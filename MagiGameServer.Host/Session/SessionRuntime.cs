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

        public ValueTask SubmitAsync(SeatId seat, JsonDocument frameDoc, CancellationToken ct)
            => _work.Writer.WriteAsync(new FrameWork(seat, frameDoc), ct);

        public ValueTask DetachAsync(SeatId seat, CancellationToken ct)
            => _work.Writer.WriteAsync(new DetachWork(seat), ct);

        /// Drains outbound frames into the seat's WebSocket. Runs as a
        /// per-seat task so slow clients don't block the session
        /// dispatcher.
        public async Task RunSendLoopAsync(SeatId seat, CancellationToken ct)
        {
            if (!_seats.TryGetValue(seat.Value, out var conn)) return;
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
                _logger.LogInformation(ex, "Send loop terminated for session {Session} seat {Seat}", Id, seat);
            }
        }

        private async Task DispatchLoopAsync()
        {
            try
            {
                while (await _work.Reader.WaitToReadAsync(_shutdown.Token).ConfigureAwait(false))
                {
                    while (_work.Reader.TryRead(out var item))
                    {
                        switch (item)
                        {
                            case AttachWork a: HandleAttach(a); break;
                            case DetachWork d: HandleDetach(d); break;
                            case FrameWork f: HandleFrame(f); break;
                            case ProtocolErrorWork e: SendError(e.Seat, e.Code, e.Message, e.Acked); break;
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dispatcher crashed for session {Session}", Id);
            }
        }

        private void HandleAttach(AttachWork work)
        {
            if (!_seats.TryAdd(work.Seat.Value, work.Connection))
            {
                work.Completion.TrySetResult(false);
                return;
            }
            _logger.LogInformation("Seat {Seat} attached to session {Session}", work.Seat, Id);

            var snapshot = BuildJoinSnapshot(work.Seat);
            var frame = WrapServerFrame(ServerFrameKind.JoinSnapshot, joinSnapshot: snapshot);
            SendTo(work.Seat, frame);
            work.Completion.TrySetResult(true);
        }

        private void HandleDetach(DetachWork work)
        {
            if (_seats.TryRemove(work.Seat.Value, out var conn))
            {
                conn.Outgoing.Writer.TryComplete();
                _logger.LogInformation("Seat {Seat} detached from session {Session}", work.Seat, Id);
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
                _logger.LogWarning(ex, "Failed to dispatch frame on session {Session} seat {Seat}", Id, work.Seat);
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

            SessionApplyResult result;
            try
            {
                result = _session.Apply(envelope);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Apply threw on session {Session}", Id);
                SendError(seat, "apply_failed", ex.Message, envelope.Seq);
                return;
            }

            if (result.Outcome == ApplyOutcome.Desynced)
            {
                _logger.LogWarning("Desync on session {Session} seat {Seat} revision {Revision}",
                    Id, seat, result.Revision);
            }
            else
            {
                _logger.LogInformation("Apply {Outcome} on session {Session} seat {Seat} revision {Revision}",
                    result.Outcome, Id, seat, result.Revision);
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
                _logger.LogWarning(ex, "Takeback threw on session {Session}", Id);
                SendError(seat, "takeback_failed", ex.Message, request.SeqAtRequestTime);
                return;
            }

            if (result.Response.Outcome == TakebackOutcome.Granted)
            {
                _logger.LogInformation("Takeback granted on session {Session} for seat {Seat} ({Steps} steps)",
                    Id, seat, result.Response.StepsGranted);
                FanOutTakebackBroadcast(seat, request.SeqAtRequestTime, result);
            }
            else
            {
                _logger.LogInformation("Takeback {Outcome} on session {Session} for seat {Seat}",
                    result.Response.Outcome, Id, seat);
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

        private object BuildJoinSnapshot(SeatId seat)
        {
            var (projected, hash, revision) = _session.ProjectForSeat(seat);
            var snapshotType = typeof(JoinSnapshot<>).MakeGenericType(_module.StateType);
            object snap = Activator.CreateInstance(snapshotType);
            SetProp(snap, "Session", Id);
            SetProp(snap, "ForSeat", seat);
            SetProp(snap, "Revision", revision);
            SetProp(snap, "State", projected);
            SetProp(snap, "StateHash", hash);
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
                _logger.LogWarning("Failed to enqueue outbound frame for session {Session} seat {Seat}", Id, seat);
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
        private sealed record DetachWork(SeatId Seat) : IWorkItem;
        private sealed record FrameWork(SeatId Seat, JsonDocument Frame) : IWorkItem;
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
