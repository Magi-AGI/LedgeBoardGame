using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MagiGameServer.Codec;
using MagiGameServer.Contracts.Core;
using MagiGameServer.Contracts.Protocol;
using MagiGameServer.Contracts.Rules;
using MagiGameServer.Core;
using Microsoft.Extensions.Logging;

namespace MagiGameServer.Host.Session
{
    /// Per-session authoritative runtime. Every mutation of the Session
    /// (Apply, Takeback, attach, detach) flows through a single dispatcher
    /// task reading from `_work`, so Session itself never sees concurrent
    /// callers — the "host must serialize per session" contract in
    /// ISession.cs is honored by construction here, not by locks inside
    /// Session.
    ///
    /// Outbound fan-out writes per-seat frames into that seat's outgoing
    /// Channel; the socket's write loop drains the channel independently.
    /// That way a slow client only backs up its own send queue and never
    /// blocks the dispatcher or the other seats on the same session.
    public sealed class SessionRuntime : IAsyncDisposable
    {
        private readonly global::MagiGameServer.Core.Session _session;
        private readonly IGameModule _module;
        private readonly ILogger<SessionRuntime> _logger;
        private readonly Channel<IWorkItem> _work;
        // ConcurrentDictionary because the dispatcher task writes attach/
        // detach entries while per-seat send loops (running on separate
        // tasks spawned by Program.cs) read the same map to resolve their
        // outbound Channel. A plain Dictionary would be undefined behavior
        // under that access pattern even if tests don't hit it.
        private readonly ConcurrentDictionary<int, SeatConnection> _seats = new ConcurrentDictionary<int, SeatConnection>();
        // Persistent seat ownership. Entries are added on a fresh attach/
        // claim and kept across disconnects — a reattach matches the token
        // and reuses the same seat, while a new claim must skip any seat
        // that still has an owner. "No seat-reclaim timeout" is the scope:
        // once a seat is claimed it belongs to that client for the
        // session's lifetime. Mutation runs from the dispatcher thread
        // only, but ConcurrentDictionary is used for the same reason as
        // _seats — it's read from other tasks when we route reattach
        // lookups and resist future refactors that might introduce
        // additional writers.
        private readonly ConcurrentDictionary<int, string> _seatOwners = new ConcurrentDictionary<int, string>();
        // Highest ClientSeq the dispatcher has already processed for each
        // seat. Retransmits (envelope.Seq <= last) bounce with
        // "duplicate_seq" so the client's dispatcher retires its optimistic
        // entry, and a double-delivered frame can't mutate state twice.
        // Dispatcher-thread only; plain Dictionary is fine.
        private readonly Dictionary<int, long> _lastAppliedSeqBySeat = new Dictionary<int, long>();
        private readonly Task _dispatcherTask;
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();

        public SessionId Id => _session.Id;
        public string GameId => _module.GameId;
        public int SeatCount => _session.SeatCount;

        public SessionRuntime(global::MagiGameServer.Core.Session session, IGameModule module, ILogger<SessionRuntime> logger)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _module = module ?? throw new ArgumentNullException(nameof(module));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _work = Channel.CreateUnbounded<IWorkItem>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
            });
            _dispatcherTask = Task.Run(DispatchLoopAsync);
        }

        /// Attempt to attach a seat. Returns false if the seat is already
        /// live — callers close the socket with a protocol-error code.
        /// Successful attach is visible after the dispatcher processes the
        /// work item, so the very first frame the client sees is the
        /// JoinSnapshot this method schedules.
        public async ValueTask<bool> TryAttachAsync(SeatId seat, WebSocket socket, CancellationToken ct)
        {
            if (seat.Value < 0 || seat.Value >= _session.SeatCount) return false;
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var connection = new SeatConnection(seat, socket);
            await _work.Writer.WriteAsync(new AttachWork(seat, connection, tcs), ct).ConfigureAwait(false);
            return await tcs.Task.ConfigureAwait(false);
        }

        /// Claim the lowest-numbered free seat and attach the socket to it.
        /// Returns the assigned SeatId, or null if every seat is already
        /// occupied. The scan + insert runs on the dispatcher thread, so
        /// two simultaneous claims can never land on the same seat — the
        /// single-reader channel serializes them by construction.
        public async ValueTask<SeatId?> TryClaimAndAttachAsync(WebSocket socket, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<SeatId?>(TaskCreationOptions.RunContinuationsAsynchronously);
            await _work.Writer.WriteAsync(new ClaimWork(socket, tcs), ct).ConfigureAwait(false);
            return await tcs.Task.ConfigureAwait(false);
        }

        /// Reattach a seat using a reconnect token issued on the original
        /// claim. Returns one of the ReattachOutcome values; on Success the
        /// socket is live and the caller should run the send loop. The
        /// seat/token match and liveness checks run on the dispatcher
        /// thread — same channel-serialization guarantee as the other
        /// attach paths.
        public async ValueTask<ReattachOutcome> TryReattachAsync(SeatId seat, string token, WebSocket socket, CancellationToken ct)
        {
            if (seat.Value < 0 || seat.Value >= _session.SeatCount) return ReattachOutcome.SeatOutOfRange;
            if (string.IsNullOrEmpty(token)) return ReattachOutcome.TokenMismatch;
            var tcs = new TaskCompletionSource<ReattachOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            var connection = new SeatConnection(seat, socket);
            await _work.Writer.WriteAsync(new ReattachWork(seat, token, connection, tcs), ct).ConfigureAwait(false);
            return await tcs.Task.ConfigureAwait(false);
        }

        public enum ReattachOutcome
        {
            Success,
            SeatOutOfRange,
            TokenMismatch,
            SeatAlreadyAttached,
        }

        public ValueTask SubmitAsync(SeatId seat, JsonDocument frameDoc, CancellationToken ct)
            => _work.Writer.WriteAsync(new FrameWork(seat, frameDoc), ct);

        public ValueTask DetachAsync(SeatId seat, CancellationToken ct)
            => _work.Writer.WriteAsync(new DetachWork(seat), ct);

        /// Projects the current canonical state for debug/admin reads. The
        /// read routes through the dispatcher channel so it never races an
        /// in-flight Apply mid-mutation — same single-writer invariant the
        /// attach/detach/frame paths honour. Seat defaults to 0 since every
        /// current module is perfect-info; private-info modules will want
        /// the explicit seat projection.
        public async ValueTask<SnapshotResult> SnapshotAsync(SeatId seat, CancellationToken ct)
        {
            if (seat.Value < 0 || seat.Value >= _session.SeatCount)
                throw new ArgumentOutOfRangeException(nameof(seat),
                    $"Seat {seat} out of range [0,{_session.SeatCount}) for session {Id}");
            var tcs = new TaskCompletionSource<SnapshotResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            await _work.Writer.WriteAsync(new SnapshotWork(seat, tcs), ct).ConfigureAwait(false);
            return await tcs.Task.ConfigureAwait(false);
        }

        public sealed class SnapshotResult
        {
            public object State { get; }
            public long StateHash { get; }
            public ServerSeq Revision { get; }

            public SnapshotResult(object state, long stateHash, ServerSeq revision)
            {
                State = state;
                StateHash = stateHash;
                Revision = revision;
            }
        }

        /// Drains outbound frames into the seat's WebSocket. Runs as a
        /// per-seat task so slow clients don't block the session
        /// dispatcher.
        public async Task RunSendLoopAsync(SeatId seat, CancellationToken ct)
        {
            if (!_seats.TryGetValue(seat.Value, out var conn)) return;
            using (_logger.BeginScope(SeatScope(seat)))
            {
                try
                {
                    while (await conn.Outgoing.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
                    {
                        while (conn.Outgoing.Reader.TryRead(out var bytes))
                        {
                            if (conn.Socket.State != WebSocketState.Open) return;
                            await conn.Socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct).ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (WebSocketException ex)
                {
                    _logger.LogInformation(ex, "Send loop terminated");
                }
            }
        }

        private async Task DispatchLoopAsync()
        {
            using (_logger.BeginScope(SessionScope()))
            {
                try
                {
                    while (await _work.Reader.WaitToReadAsync(_shutdown.Token).ConfigureAwait(false))
                    {
                        while (_work.Reader.TryRead(out var item))
                        {
                            DispatchOne(item);
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Dispatcher crashed");
                }
            }
        }

        private void DispatchOne(IWorkItem item)
        {
            // Attach a Seat scope for every work item that has one so the log
            // line for the handler (and anything it calls transitively) carries
            // SessionId + Seat automatically. ClaimWork has no seat yet (the
            // handler assigns one), so it runs under the session scope only.
            SeatId? seat = item switch
            {
                AttachWork a => a.Seat,
                ReattachWork r => r.Seat,
                DetachWork d => d.Seat,
                FrameWork f => f.Seat,
                SnapshotWork s => s.Seat,
                ProtocolErrorWork e => e.Seat,
                _ => (SeatId?)null,
            };
            IDisposable seatScope = seat.HasValue ? _logger.BeginScope(SeatScope(seat.Value)) : null;
            try
            {
                switch (item)
                {
                    case AttachWork a: HandleAttach(a); break;
                    case ClaimWork c: HandleClaim(c); break;
                    case ReattachWork r: HandleReattach(r); break;
                    case DetachWork d: HandleDetach(d); break;
                    case FrameWork f: HandleFrame(f); break;
                    case SnapshotWork s: HandleSnapshot(s); break;
                    case ProtocolErrorWork e: SendError(e.Seat, e.Code, e.Message, e.Acked); break;
                }
            }
            finally
            {
                seatScope?.Dispose();
            }
        }

        private Dictionary<string, object> SessionScope() => new Dictionary<string, object>
        {
            ["SessionId"] = Id.Value,
            ["GameId"] = GameId,
        };

        private Dictionary<string, object> SeatScope(SeatId seat) => new Dictionary<string, object>
        {
            ["SessionId"] = Id.Value,
            ["GameId"] = GameId,
            ["Seat"] = seat.Value,
        };

        private void HandleAttach(AttachWork work)
        {
            if (!_seats.TryAdd(work.Seat.Value, work.Connection))
            {
                work.Completion.TrySetResult(false);
                return;
            }
            _logger.LogInformation("Seat attached");

            // Seat ownership: if nobody owns this seat yet, the attacher
            // becomes the owner and gets a fresh token. If an owner already
            // exists (e.g. rapid reconnect on a seat whose prior socket
            // hasn't detached yet), we hand the same token back — reattach
            // semantics are seat-identity, not frame-identity. The client
            // stashes the token for a later no-token-lost reattach via 5c.
            string token = _seatOwners.GetOrAdd(work.Seat.Value, _ => NewReconnectToken());
            // Flip IsConnected=true in canonical state BEFORE building the
            // JoinSnapshot so the attaching seat sees its own presence set
            // in the very first frame. Other seats receive the change as an
            // echo below.
            var presenceResult = TrySetPresence(work.Seat, true);
            var snapshot = BuildJoinSnapshot(work.Seat, token);
            var frame = WrapServerFrame(ServerFrameKind.JoinSnapshot, joinSnapshot: snapshot);
            SendTo(work.Seat, frame);
            BroadcastPresenceEchoExcluding(presenceResult, work.Seat);
            work.Completion.TrySetResult(true);
        }

        private void HandleClaim(ClaimWork work)
        {
            // Linear scan over the seat range. SeatCount is bounded by
            // module.MaxSeats (8 for LedgeBoardGame today), so O(n) here
            // is strictly cheaper than maintaining a sorted free-set.
            for (int i = 0; i < _session.SeatCount; i++)
            {
                if (_seats.ContainsKey(i)) continue;
                // Skip seats that have been claimed earlier in the session
                // but are currently disconnected — they're owned, waiting
                // for their original client to reattach. "No seat-reclaim
                // timeout" is an explicit P5 constraint: once a seat is
                // claimed it stays with the claimant for the session.
                if (_seatOwners.ContainsKey(i)) continue;

                var seat = new SeatId(i);
                var connection = new SeatConnection(seat, work.Socket);
                // _seats is the source of truth the scan just consulted; a
                // concurrent external write is impossible because only the
                // dispatcher thread mutates it. TryAdd still guards against
                // future refactors that might add another writer.
                if (!_seats.TryAdd(i, connection))
                {
                    connection.Outgoing.Writer.TryComplete();
                    continue;
                }

                string token = NewReconnectToken();
                // Record ownership before the JoinSnapshot so a concurrent
                // claim on the next iteration of this scan (or a follow-up
                // work item) sees the seat as taken. In practice the
                // dispatcher is single-threaded so the ordering only
                // matters across separate work items, not within one.
                _seatOwners[i] = token;
                _logger.LogInformation("Seat claimed (seat={Seat})", seat.Value);
                var presenceResult = TrySetPresence(seat, true);
                var snapshot = BuildJoinSnapshot(seat, token);
                var frame = WrapServerFrame(ServerFrameKind.JoinSnapshot, joinSnapshot: snapshot);
                SendTo(seat, frame);
                BroadcastPresenceEchoExcluding(presenceResult, seat);
                work.Completion.TrySetResult(seat);
                return;
            }

            // Every seat taken (or owned-but-disconnected) — caller
            // closes the socket with PolicyViolation "session_full".
            work.Completion.TrySetResult(null);
        }

        private void HandleReattach(ReattachWork work)
        {
            // Token must match the owner recorded on the original claim.
            // Unknown seat = nobody ever claimed it, so it can't be
            // reattached — the client should claim fresh instead.
            if (!_seatOwners.TryGetValue(work.Seat.Value, out var owner))
            {
                work.Completion.TrySetResult(ReattachOutcome.TokenMismatch);
                return;
            }
            if (!string.Equals(owner, work.Token, StringComparison.Ordinal))
            {
                work.Completion.TrySetResult(ReattachOutcome.TokenMismatch);
                return;
            }
            // If the seat is already live, the previous socket hasn't
            // detached yet (client reconnected faster than the old
            // connection's close propagated). Report the collision so the
            // caller can 409 — the original client needs to drop its old
            // socket or wait for its close to land.
            if (!_seats.TryAdd(work.Seat.Value, work.Connection))
            {
                work.Completion.TrySetResult(ReattachOutcome.SeatAlreadyAttached);
                return;
            }

            _logger.LogInformation("Seat reattached");

            // Presence flips back to true and the snapshot echoes the
            // EXISTING token (not a new one) — a reattach must be
            // idempotent in token-identity so the client's stash stays
            // valid across multiple reconnects.
            var presenceResult = TrySetPresence(work.Seat, true);
            var snapshot = BuildJoinSnapshot(work.Seat, owner);
            var frame = WrapServerFrame(ServerFrameKind.JoinSnapshot, joinSnapshot: snapshot);
            SendTo(work.Seat, frame);
            BroadcastPresenceEchoExcluding(presenceResult, work.Seat);
            work.Completion.TrySetResult(ReattachOutcome.Success);
        }

        private void HandleSnapshot(SnapshotWork work)
        {
            try
            {
                var (projected, hash, revision) = _session.ProjectForSeat(work.Seat);
                work.Completion.TrySetResult(new SnapshotResult(projected, hash, revision));
            }
            catch (Exception ex)
            {
                work.Completion.TrySetException(ex);
            }
        }

        private void HandleDetach(DetachWork work)
        {
            if (_seats.TryRemove(work.Seat.Value, out var conn))
            {
                conn.Outgoing.Writer.TryComplete();
                _logger.LogInformation("Seat detached");
                // _seatOwners is deliberately NOT cleared — the seat stays
                // owned for the remainder of the session. A fresh claim
                // will skip it; a reattach (5c) will match the stashed
                // token and revive the seat with its original identity.
                // After removal from _seats so the fan-out skips the
                // departing socket (its outbound channel is already closed
                // by TryComplete above). Echo to every remaining seat so
                // their rosters reflect the departure.
                var presenceResult = TrySetPresence(work.Seat, false);
                BroadcastPresenceEchoExcluding(presenceResult, work.Seat);
            }
        }

        private static string NewReconnectToken() => Guid.NewGuid().ToString("N");

        private SessionApplyResult TrySetPresence(SeatId seat, bool isConnected)
        {
            try
            {
                return _session.SetSeatPresence(seat, isConnected);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SetSeatPresence threw (seat={Seat})", seat.Value);
                return new SessionApplyResult
                {
                    Outcome = ApplyOutcome.Rejected,
                    Revision = _session.CurrentRevision,
                    Echoes = Array.Empty<StateEcho<object>>(),
                };
            }
        }

        private void BroadcastPresenceEchoExcluding(SessionApplyResult result, SeatId exclude)
        {
            // Rejected == module returned the input ref (no-op). Nothing to
            // broadcast; nobody's view changed. Applied means state + revision
            // moved and the echo set is populated.
            if (result == null || result.Outcome != ApplyOutcome.Applied || result.Echoes == null) return;
            foreach (var echo in result.Echoes)
            {
                if (echo.ForSeat == exclude) continue;
                if (!_seats.ContainsKey(echo.ForSeat.Value)) continue;
                var wrapped = WrapServerFrameWithTypedEcho(echo);
                SendTo(echo.ForSeat, wrapped);
            }
        }

        private void HandleFrame(FrameWork work)
        {
            using var doc = work.Frame;
            try
            {
                var root = doc.RootElement;
                if (!root.TryGetProperty("kind", out var kindEl))
                {
                    SendError(work.Seat, "malformed_frame", "Frame missing 'kind' discriminator", default);
                    return;
                }
                var kindStr = kindEl.GetString();
                if (string.Equals(kindStr, "action", StringComparison.OrdinalIgnoreCase))
                {
                    HandleActionFrame(work.Seat, root);
                }
                else if (string.Equals(kindStr, "takeback", StringComparison.OrdinalIgnoreCase))
                {
                    HandleTakebackFrame(work.Seat, root);
                }
                else
                {
                    SendError(work.Seat, "unknown_kind", $"Unknown frame kind '{kindStr}'", default);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to dispatch frame");
                SendError(work.Seat, "frame_error", ex.Message, default);
            }
        }

        private void HandleActionFrame(SeatId seat, JsonElement root)
        {
            if (!root.TryGetProperty("action", out var payload) || payload.ValueKind == JsonValueKind.Null)
            {
                SendError(seat, "malformed_frame", "Action frame missing 'action' payload", default);
                return;
            }
            // Peek the seq before calling the codec so seat-mismatch and
            // other envelope-rejection paths can carry AckedSeq back to
            // the client. Without this, OnError can't retire the matching
            // optimistic entry in the dispatcher (SessionDispatcher only
            // clears pending predictions by error.AckedSeq).
            ClientSeq peekedSeq = TryReadSeq(payload);

            ActionEnvelope<object> envelope = EnvelopeCodec.DeserializeActionEnvelope(payload.GetRawText(), _module.ActionType);
            if (envelope == null || envelope.Seat != seat)
            {
                SendError(seat, "seat_mismatch", "Envelope seat does not match connection seat", peekedSeq);
                return;
            }

            // Per-seat ClientSeq idempotency. Anything at-or-below the last
            // seq we've already consumed is a retransmit or an
            // out-of-order stale frame — bounce it with the duplicate seq
            // as AckedSeq so the client's dispatcher retires the matching
            // optimistic entry. Applies pre-Apply so duplicates never
            // mutate state twice.
            if (_lastAppliedSeqBySeat.TryGetValue(seat.Value, out var lastSeq)
                && envelope.Seq.Value <= lastSeq)
            {
                SendError(seat, "duplicate_seq",
                    $"seq {envelope.Seq.Value} already processed (last={lastSeq})",
                    envelope.Seq);
                return;
            }

            SessionApplyResult result;
            try
            {
                result = _session.Apply(envelope);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Apply threw");
                SendError(seat, "apply_failed", ex.Message, envelope.Seq);
                return;
            }

            // Record the seq *after* a successful dispatch regardless of
            // outcome — Applied/Rejected/Desynced all consume the
            // submission and the client won't resubmit the same seq in
            // normal operation; any later frame carrying this seq is a
            // retransmit.
            _lastAppliedSeqBySeat[seat.Value] = envelope.Seq.Value;

            if (result.Outcome == ApplyOutcome.Desynced)
            {
                _logger.LogWarning("Desync at revision {Revision}", result.Revision);
            }
            else
            {
                _logger.LogInformation("Apply {Outcome} at revision {Revision}",
                    result.Outcome, result.Revision);
            }

            FanOutEchoes(result.Echoes);
        }

        private void HandleTakebackFrame(SeatId seat, JsonElement root)
        {
            if (!root.TryGetProperty("takeback", out var payload) || payload.ValueKind == JsonValueKind.Null)
            {
                SendError(seat, "malformed_frame", "Takeback frame missing 'takeback' payload", default);
                return;
            }
            ClientSeq peekedSeq = TryReadProperty(payload, "seqAtRequestTime");

            TakebackRequest request;
            try
            {
                request = JsonSerializer.Deserialize<TakebackRequest>(payload.GetRawText(), EnvelopeCodec.Options);
            }
            catch (JsonException ex)
            {
                SendError(seat, "malformed_frame", ex.Message, peekedSeq);
                return;
            }
            if (request == null || request.RequestingSeat != seat)
            {
                SendError(seat, "seat_mismatch", "Takeback request seat does not match connection seat", peekedSeq);
                return;
            }

            SessionTakebackResult result;
            try
            {
                result = _session.Takeback(request);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Takeback threw");
                SendError(seat, "takeback_failed", ex.Message, request.SeqAtRequestTime);
                return;
            }

            if (result.Response.Outcome == TakebackOutcome.Granted)
            {
                _logger.LogInformation("Takeback granted ({Steps} steps)", result.Response.StepsGranted);
                FanOutTakebackBroadcast(seat, request.SeqAtRequestTime, result);
            }
            else
            {
                _logger.LogInformation("Takeback {Outcome}", result.Response.Outcome);
                SendTo(seat, WrapServerFrame(ServerFrameKind.TakebackResponse, takebackResponse: result.Response));
            }
        }

        private void FanOutEchoes(IReadOnlyList<StateEcho<object>> echoes)
        {
            foreach (var echo in echoes)
            {
                if (!_seats.ContainsKey(echo.ForSeat.Value)) continue;
                var wrapped = WrapServerFrameWithTypedEcho(echo);
                SendTo(echo.ForSeat, wrapped);
            }
        }

        private void FanOutTakebackBroadcast(SeatId requestingSeat, ClientSeq ackedRequestSeq, SessionTakebackResult result)
        {
            foreach (var echo in result.Echoes)
            {
                if (!_seats.ContainsKey(echo.ForSeat.Value)) continue;
                object broadcast = BuildTakebackBroadcast(echo, requestingSeat, ackedRequestSeq, result.Response.StepsGranted);
                var wrapped = WrapTypedTakebackBroadcast(broadcast);
                SendTo(echo.ForSeat, wrapped);
            }
        }

        private object BuildJoinSnapshot(SeatId seat, string reconnectToken)
        {
            var (projected, hash, revision) = _session.ProjectForSeat(seat);
            var snapshotType = typeof(JoinSnapshot<>).MakeGenericType(_module.StateType);
            object snap = Activator.CreateInstance(snapshotType);
            SetProp(snap, "Session", Id);
            SetProp(snap, "ForSeat", seat);
            SetProp(snap, "Revision", revision);
            SetProp(snap, "State", projected);
            SetProp(snap, "StateHash", hash);
            SetProp(snap, "ReconnectToken", reconnectToken);
            return snap;
        }

        private object BuildTakebackBroadcast(StateEcho<object> echo, SeatId requestingSeat, ClientSeq acked, int stepsRewound)
        {
            var broadcastType = typeof(TakebackBroadcast<>).MakeGenericType(_module.StateType);
            object b = Activator.CreateInstance(broadcastType);
            SetProp(b, "Session", echo.Session);
            SetProp(b, "ForSeat", echo.ForSeat);
            SetProp(b, "RequestingSeat", requestingSeat);
            SetProp(b, "AckedRequestSeq", acked);
            SetProp(b, "RevisionAfter", echo.Revision);
            SetProp(b, "StepsRewound", stepsRewound);
            SetProp(b, "State", echo.State);
            SetProp(b, "StateHash", echo.StateHash);
            return b;
        }

        private static void SetProp(object target, string name, object value)
        {
            var p = target.GetType().GetProperty(name);
            p.SetValue(target, value);
        }

        private byte[] WrapServerFrame(ServerFrameKind kind, object joinSnapshot = null, object echo = null,
            ErrorEnvelope error = null, TakebackResponse takebackResponse = null, object takebackBroadcast = null)
        {
            // Build a ServerFrame<TState> via reflection so the TState
            // generic is resolved against the module's state type at
            // runtime, then serialize through the shared codec options.
            var frameType = typeof(ServerFrame<>).MakeGenericType(_module.StateType);
            object frame = Activator.CreateInstance(frameType);
            SetProp(frame, "Kind", kind);
            if (joinSnapshot != null) SetProp(frame, "JoinSnapshot", joinSnapshot);
            if (echo != null) SetProp(frame, "Echo", echo);
            if (error != null) SetProp(frame, "Error", error);
            if (takebackResponse != null) SetProp(frame, "TakebackResponse", takebackResponse);
            if (takebackBroadcast != null) SetProp(frame, "TakebackBroadcast", takebackBroadcast);
            return JsonSerializer.SerializeToUtf8Bytes(frame, frameType, EnvelopeCodec.Options);
        }

        private byte[] WrapServerFrameWithTypedEcho(StateEcho<object> echo)
        {
            // StateEcho<object>.State is the module's concrete state
            // type already — rebuild as StateEcho<TState> so
            // serialization emits the proper typed payload.
            var typedEchoType = typeof(StateEcho<>).MakeGenericType(_module.StateType);
            object typedEcho = Activator.CreateInstance(typedEchoType);
            SetProp(typedEcho, "Session", echo.Session);
            SetProp(typedEcho, "ForSeat", echo.ForSeat);
            SetProp(typedEcho, "SubmittingSeat", echo.SubmittingSeat);
            SetProp(typedEcho, "AckedSeq", echo.AckedSeq);
            SetProp(typedEcho, "Revision", echo.Revision);
            SetProp(typedEcho, "State", echo.State);
            SetProp(typedEcho, "StateHash", echo.StateHash);
            SetProp(typedEcho, "Outcome", echo.Outcome);
            return WrapServerFrame(ServerFrameKind.StateEcho, echo: typedEcho);
        }

        private byte[] WrapTypedTakebackBroadcast(object typedBroadcast)
            => WrapServerFrame(ServerFrameKind.TakebackBroadcast, takebackBroadcast: typedBroadcast);

        private void SendTo(SeatId seat, byte[] bytes)
        {
            if (!_seats.TryGetValue(seat.Value, out var conn)) return;
            if (!conn.Outgoing.Writer.TryWrite(bytes))
            {
                _logger.LogWarning("Failed to enqueue outbound frame (seat={Seat})", seat.Value);
            }
        }

        private void SendError(SeatId seat, string code, string message, ClientSeq acked)
        {
            var err = new ErrorEnvelope
            {
                Session = Id,
                AckedSeq = acked,
                Code = code,
                Message = message,
            };
            SendTo(seat, WrapServerFrame(ServerFrameKind.Error, error: err));
        }

        /// Emit a protocol-level error on a seat from outside the dispatcher
        /// (e.g. the transport read loop when inbound bytes don't parse as
        /// JSON at all). Routes through the work channel so the dispatcher
        /// remains the sole writer to each seat's outbound Channel — that
        /// invariant is what lets SeatConnection.Outgoing stay configured
        /// with SingleWriter=true.
        public void SendProtocolError(SeatId seat, string code, string message, ClientSeq acked)
            => _work.Writer.TryWrite(new ProtocolErrorWork(seat, code, message, acked));

        private static ClientSeq TryReadSeq(JsonElement payload) => TryReadProperty(payload, "seq");

        private static ClientSeq TryReadProperty(JsonElement payload, string propertyName)
        {
            if (payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty(propertyName, out var seqEl)
                && seqEl.ValueKind == JsonValueKind.Number
                && seqEl.TryGetInt64(out var v))
            {
                return new ClientSeq(v);
            }
            return default;
        }

        /// Graceful close: send a WebSocket CloseOutput(GoingAway, reason) to
        /// every attached seat and wait up to `perSocketTimeout` for each.
        /// Runs in parallel across seats. Doesn't dispose the runtime — the
        /// caller owns that ordering so it can await the receive/send loops
        /// in Program.cs finishing first. Idempotent under repeat calls
        /// because CloseOutputAsync on an already-closed socket is a no-op
        /// that swallows WebSocketException.
        public async Task CloseAllSocketsAsync(string reason, TimeSpan perSocketTimeout, CancellationToken ct)
        {
            var tasks = new List<Task>();
            foreach (var conn in _seats.Values)
            {
                tasks.Add(CloseOneAsync(conn.Socket, reason, perSocketTimeout, ct));
            }
            if (tasks.Count > 0)
            {
                try { await Task.WhenAll(tasks).ConfigureAwait(false); }
                catch { /* individual CloseOneAsync already swallowed per-socket errors */ }
            }
        }

        private static async Task CloseOneAsync(WebSocket socket, string reason, TimeSpan timeout, CancellationToken ct)
        {
            if (socket.State != WebSocketState.Open && socket.State != WebSocketState.CloseReceived) return;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.EndpointUnavailable, reason, cts.Token).ConfigureAwait(false);
            }
            catch { /* socket already torn down or timed out — fine, the send/receive loops will mop up */ }
        }

        public async ValueTask DisposeAsync()
        {
            _shutdown.Cancel();
            _work.Writer.TryComplete();
            try { await _dispatcherTask.ConfigureAwait(false); } catch { }
            foreach (var conn in _seats.Values) conn.Outgoing.Writer.TryComplete();
            _seats.Clear();
            _shutdown.Dispose();
        }

        private interface IWorkItem { }
        private sealed record AttachWork(SeatId Seat, SeatConnection Connection, TaskCompletionSource<bool> Completion) : IWorkItem;
        private sealed record ClaimWork(WebSocket Socket, TaskCompletionSource<SeatId?> Completion) : IWorkItem;
        private sealed record ReattachWork(SeatId Seat, string Token, SeatConnection Connection, TaskCompletionSource<ReattachOutcome> Completion) : IWorkItem;
        private sealed record DetachWork(SeatId Seat) : IWorkItem;
        private sealed record FrameWork(SeatId Seat, JsonDocument Frame) : IWorkItem;
        private sealed record SnapshotWork(SeatId Seat, TaskCompletionSource<SnapshotResult> Completion) : IWorkItem;
        private sealed record ProtocolErrorWork(SeatId Seat, string Code, string Message, ClientSeq Acked) : IWorkItem;

        private sealed class SeatConnection
        {
            public SeatId Seat { get; }
            public WebSocket Socket { get; }
            public Channel<byte[]> Outgoing { get; }

            public SeatConnection(SeatId seat, WebSocket socket)
            {
                Seat = seat;
                Socket = socket;
                Outgoing = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = true,
                });
            }
        }
    }
}
