using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Models.Network;
using Magi.LedgeBoardGame.Models.Spec;
using Magi.LedgeBoardGame.ServerModule;
using Magi.UnityTools.Net;
using MagiGameServer.Contracts.Core;
using MagiGameServer.Contracts.Protocol;
using MagiGameServer.Contracts.Rules;
using MagiGameServer.Core;
using ContractsApplyOutcome = MagiGameServer.Contracts.Core.ApplyOutcome;

namespace Magi.LedgeBoardGame.Net
{
    /// Shadow-mode driver for M6c3a. Hosts a single MagiGameServer.Core.Session
    /// in-process and mirrors the GameController's committed-action stream
    /// through it via one InProcessMagiTransport + MagiSession per seat.
    /// All transports share one InProcessSessionBus so the fanout matches
    /// the WebSocket server's per-seat echo delivery.
    ///
    /// Not authoritative: GameController's local _gameState is still the
    /// source of truth for UI. The driver only submits and listens. When a
    /// shadow submission's PredictedStateHash disagrees with what the server
    /// computed, the OnPredictionDiverged callback logs an error with enough
    /// context to diagnose. This is the M6c3a success gate — every committed
    /// action flows through the transport with zero mismatches.
    ///
    /// Lifetime: construct + StartAsync once at scene load (LedgeShadowBootstrap
    /// does this), Tick() each frame to drain inbound echoes on the main
    /// thread, DisposeAsync when the scene unloads. The driver does NOT
    /// start an HTTP host — for shadow mode the "server" is literally a
    /// Session object in the same process, no socket hop involved.
    public sealed class LedgeBoardSessionDriver : ILedgeSessionBinding, IAsyncDisposable
    {
        private readonly int _seatCount;
        private readonly LedgeRulesAdapter _adapter = new LedgeRulesAdapter();
        private readonly LedgeGameModule _module = new LedgeGameModule();
        private Session _session;
        private InProcessSessionBus<SpecGameState> _bus;
        private readonly List<IAsyncDisposable> _transports
            = new List<IAsyncDisposable>();
        private readonly List<MagiSession<SpecGameState, LedgeAction>> _sessions
            = new List<MagiSession<SpecGameState, LedgeAction>>();
        // Synchronises the AddRange publication at the end of Start with
        // the snapshot+clear in DisposeAsync, so a scene-unload racing a
        // nearly-complete connect can't land mid-resize on the list's
        // internal array. Both critical sections are O(N) in seat count
        // and never await, so the lock is held briefly.
        private readonly object _lock = new object();
        private HttpClient _hostHttp;
        private int _divergences;
        private int _matches;
        private int _disposed;

        /// Running count of submissions whose local and server hashes agreed.
        /// Exposed for inspector visibility and editor-time diagnostics —
        /// failing builds would surface as matches=0 with divergences>0.
        public int Matches => _matches;

        /// Running count of OnPredictionDiverged callbacks since StartAsync.
        /// The shadow mode's single success metric: zero divergences over a
        /// full play session means the adapter/transport/session chain is
        /// ready for authority flip in M6c3b.
        public int Divergences => _divergences;

        public bool IsReady { get; private set; }
        public int SeatCount => _seatCount;

        /// Observer surface (ILedgeSessionObserver). Raised on the Unity main
        /// thread because each per-seat MagiSession dispatches these events
        /// inside its own Tick(), which LedgeShadowBootstrap pumps from
        /// MonoBehaviour.Update. No subscription filtering here — the driver
        /// fans all seats' events through one surface and the subscriber (the
        /// GameController, typically) decides which seats matter.
        public event Action<LedgeSessionJoinInfo> OnServerJoin;
        public event Action<LedgeSessionEchoInfo> OnServerAdvance;
        public event Action<LedgeSessionEchoInfo> OnServerMatched;
        public event Action<LedgeSessionEchoInfo> OnServerDiverged;
        public event Action<LedgeSessionErrorInfo> OnServerError;
        public event Action<LedgeSessionTakebackInfo> OnServerTakeback;
        public event Action<LedgeSessionTakebackReplyInfo> OnServerTakebackReply;

        public LedgeBoardSessionDriver(int seatCount)
        {
            var tempModule = new LedgeGameModule();
            if (seatCount < tempModule.MinSeats || seatCount > tempModule.MaxSeats)
                throw new ArgumentOutOfRangeException(nameof(seatCount),
                    $"LedgeBoardSessionDriver requires {tempModule.MinSeats}-{tempModule.MaxSeats} seats (got {seatCount})");
            _seatCount = seatCount;
        }

        public async Task StartAsync(string ledgeSpecJson, CancellationToken ct)
        {
            if (IsReady) throw new InvalidOperationException("StartAsync already completed");

            var options = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(ledgeSpecJson))
                options[LedgeGameModule.SpecJsonOptionKey] = ledgeSpecJson;
            var config = new GameConfig
            {
                Seed = 0,
                SeatCount = _seatCount,
                Options = options,
            };
            var initialState = _module.CreateInitialState(config);
            // Bypass SessionHost — we only need one session and the host's
            // module registry adds no value in-process. Calling the Session
            // ctor directly also avoids a downcast from ISession to Session
            // at the bus boundary.
            var sessionId = new SessionId(Guid.NewGuid().ToString("N"));
            _session = new Session(sessionId, _module, initialState, _seatCount);
            _bus = new InProcessSessionBus<SpecGameState>(_session);

            var magiConfig = new MagiSessionConfig
            {
                BaseUri = null,
                GameId = _module.GameId,
                SeatCount = _seatCount,
                Seed = 0,
                Options = options,
            };

            // Build per-seat state in locals and publish atomically at the
            // end. Keeps DisposeAsync (which reads _sessions/_transports on
            // the main thread) out of the async Add path and lets the
            // awaits use ConfigureAwait(false) without racing. The
            // per-iteration ct + _disposed guards let a dispose that lands
            // mid-start abort cleanly and tear down only what was built.
            var localTransports = new List<IAsyncDisposable>(_seatCount);
            var localSessions = new List<MagiSession<SpecGameState, LedgeAction>>(_seatCount);
            try
            {
                for (int i = 0; i < _seatCount; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (Volatile.Read(ref _disposed) != 0) throw new OperationCanceledException();
                    int seatIndex = i;
                    var transport = new InProcessMagiTransport<SpecGameState, LedgeAction>(_bus);
                    var session = new MagiSession<SpecGameState, LedgeAction>(transport);
                    WireSeatSession(seatIndex, session);
                    localTransports.Add(transport);
                    localSessions.Add(session);
                    await session.ConnectAsync(magiConfig, new SeatId(i), ct).ConfigureAwait(false);
                    if (Volatile.Read(ref _disposed) != 0) throw new OperationCanceledException();
                }
            }
            catch
            {
                foreach (var s in localSessions)
                {
                    try { await s.DisposeAsync().ConfigureAwait(false); } catch { }
                }
                throw;
            }

            // Publish under the lock, re-checking _disposed so a scene
            // unload that wins the race here still drops these cleanly.
            bool published;
            lock (_lock)
            {
                published = Volatile.Read(ref _disposed) == 0;
                if (published)
                {
                    _transports.AddRange(localTransports);
                    _sessions.AddRange(localSessions);
                }
            }
            if (!published)
            {
                foreach (var s in localSessions)
                {
                    try { await s.DisposeAsync().ConfigureAwait(false); } catch { }
                }
                throw new OperationCanceledException();
            }
            IsReady = true;
        }

        /// Host-backed variant of StartAsync. Opens one server session via
        /// HTTP POST against the launcher, then attaches N WebSockets to
        /// that single SessionId so every seat sees the same authoritative
        /// timeline — mirrors what StartAsync does in-process, but the
        /// "server" is a real ASP.NET binary running at baseUri.
        ///
        /// Caller must ensure the MagiGameServer.Host launcher (with
        /// LedgeGameModule registered) is already listening at baseUri
        /// before invoking this. StartAsync (in-process) and
        /// StartHostBackedAsync are mutually exclusive — either one runs
        /// per driver instance, never both.
        public async Task StartHostBackedAsync(string baseUri, string ledgeSpecJson, CancellationToken ct)
        {
            if (IsReady) throw new InvalidOperationException("StartAsync/StartHostBackedAsync already completed");
            if (string.IsNullOrEmpty(baseUri)) throw new ArgumentException("baseUri required", nameof(baseUri));

            var options = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(ledgeSpecJson))
                options[LedgeGameModule.SpecJsonOptionKey] = ledgeSpecJson;
            var magiConfig = new MagiSessionConfig
            {
                BaseUri = baseUri,
                GameId = _module.GameId,
                SeatCount = _seatCount,
                Seed = 0,
                Options = options,
            };

            _hostHttp = new HttpClient();

            // Seat 0's transport POSTs /session/open and learns the
            // SessionId. Subsequent seats get the pre-seeded ctor so they
            // skip the POST and attach to the same session. All N sockets
            // land on the same SessionRuntime on the host side.
            //
            // ConnectAsync over a real WebSocket parks continuations on a
            // pool thread; building transports/sessions in locals and
            // publishing at the end keeps DisposeAsync (main thread) out
            // of the async Add path. The per-iteration ct + _disposed
            // guards let a mid-start dispose abort cleanly, and the catch
            // block tears down whatever was built so nothing leaks.
            var localTransports = new List<IAsyncDisposable>(_seatCount);
            var localSessions = new List<MagiSession<SpecGameState, LedgeAction>>(_seatCount);
            SessionId sharedSession = default;
            try
            {
                for (int i = 0; i < _seatCount; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (Volatile.Read(ref _disposed) != 0) throw new OperationCanceledException();
                    int seatIndex = i;
                    WebSocketMagiTransport<SpecGameState, LedgeAction> transport = (i == 0)
                        ? new WebSocketMagiTransport<SpecGameState, LedgeAction>(_hostHttp)
                        : new WebSocketMagiTransport<SpecGameState, LedgeAction>(_hostHttp, baseUri, sharedSession);
                    var session = new MagiSession<SpecGameState, LedgeAction>(transport);
                    WireSeatSession(seatIndex, session);
                    localTransports.Add(transport);
                    localSessions.Add(session);
                    await session.ConnectAsync(magiConfig, new SeatId(i), ct).ConfigureAwait(false);
                    if (Volatile.Read(ref _disposed) != 0) throw new OperationCanceledException();
                    if (i == 0) sharedSession = session.Session;
                }
            }
            catch
            {
                foreach (var s in localSessions)
                {
                    try { await s.DisposeAsync().ConfigureAwait(false); } catch { }
                }
                throw;
            }

            // Publish under the lock, re-checking _disposed so a scene
            // unload that wins the race here still drops these cleanly.
            bool published;
            lock (_lock)
            {
                published = Volatile.Read(ref _disposed) == 0;
                if (published)
                {
                    _transports.AddRange(localTransports);
                    _sessions.AddRange(localSessions);
                }
            }
            if (!published)
            {
                foreach (var s in localSessions)
                {
                    try { await s.DisposeAsync().ConfigureAwait(false); } catch { }
                }
                throw new OperationCanceledException();
            }
            IsReady = true;
        }

        private void WireSeatSession(int seatIndex, MagiSession<SpecGameState, LedgeAction> session)
        {
            session.OnSessionJoined += snap => RaiseJoin(seatIndex, snap);
            session.OnStateAdvanced += echo => RaiseAdvance(seatIndex, echo);
            session.OnPredictionMatched += echo => RaiseMatched(seatIndex, echo);
            session.OnPredictionDiverged += echo => RaiseDiverged(seatIndex, echo);
            session.OnError += err => RaiseError(seatIndex, err);
            session.OnTakebackBroadcast += bcast => RaiseTakeback(seatIndex, bcast);
            session.OnTakebackReply += reply => RaiseTakebackReply(seatIndex, reply);
            session.OnTransportError += ex => UnityEngine.Debug.LogError(
                $"[shadow] transport error seat={seatIndex}: {ex}");
        }

        /// Drains inbound echoes on the calling thread. Must be called from
        /// the Unity main thread (LedgeShadowBootstrap pumps this from
        /// Update). Returns total frames routed across all seats, mostly as
        /// a diagnostic hook — a Tick returning zero for several consecutive
        /// frames during active play means the echo loop is stuck.
        public int Tick()
        {
            if (!IsReady) return 0;
            int drained = 0;
            for (int i = 0; i < _sessions.Count; i++)
                drained += _sessions[i].Tick();
            return drained;
        }

        public void ShadowPlace(int seatIndex, SpecGameState localPostApplyState, SpaceId target, Tone tone)
            => SubmitShadow(seatIndex, localPostApplyState, LedgeAction.PlaceToken(target, tone));

        public void ShadowMove(int seatIndex, SpecGameState localPostApplyState, SpaceId from, SpaceId to, Tone tone)
            => SubmitShadow(seatIndex, localPostApplyState, LedgeAction.MoveToken(from, to, tone));

        public void ShadowEndTurn(int seatIndex, SpecGameState localPostApplyState)
            => SubmitShadow(seatIndex, localPostApplyState, LedgeAction.EndTurn());

        public bool SubmitPlace(int seatIndex, SpaceId target, Tone tone)
            => SubmitAuthoritative(seatIndex, LedgeAction.PlaceToken(target, tone));

        public bool SubmitMove(int seatIndex, SpaceId from, SpaceId to, Tone tone)
            => SubmitAuthoritative(seatIndex, LedgeAction.MoveToken(from, to, tone));

        public bool SubmitEndTurn(int seatIndex)
            => SubmitAuthoritative(seatIndex, LedgeAction.EndTurn());

        public bool SubmitTakeback(int seatIndex, int stepsRequested, string reason)
        {
            if (!ValidateSeat(seatIndex, "takeback")) return false;
            if (stepsRequested < 1)
            {
                UnityEngine.Debug.LogError(
                    $"[takeback] submit rejected: stepsRequested={stepsRequested} must be >= 1");
                return false;
            }
            _sessions[seatIndex].SubmitTakeback(stepsRequested, reason);
            return true;
        }

        private void SubmitShadow(int seatIndex, SpecGameState localPostApplyState, LedgeAction action)
        {
            if (!ValidateSeat(seatIndex, "shadow")) return;
            if (localPostApplyState == null)
            {
                UnityEngine.Debug.LogError($"[shadow] submit rejected: seat {seatIndex} local post-apply state was null");
                return;
            }
            // Hashing the local post-apply state is exactly what the server
            // will re-compute on its own side (identity projection for
            // LedgeBoardGame). Passing it as PredictedStateHash flips the
            // echo onto the OnPredictionMatched/OnPredictionDiverged
            // branches rather than the non-predicting OnStateAdvanced path.
            long localHash = _adapter.GetStateHash(localPostApplyState);
            _sessions[seatIndex].Submit(action, localHash);
        }

        private bool SubmitAuthoritative(int seatIndex, LedgeAction action)
        {
            if (!ValidateSeat(seatIndex, "submit")) return false;
            // predictedHash=0 is the non-predicting path — submit-only mode
            // has no local mutation, so there is no post-apply state to hash
            // and the echo routes through OnStateAdvanced. The controller
            // then calls ApplyServerState to pick up the canonical state.
            _sessions[seatIndex].Submit(action, 0L);
            return true;
        }

        private bool ValidateSeat(int seatIndex, string label)
        {
            if (!IsReady || Volatile.Read(ref _disposed) != 0) return false;
            if (seatIndex < 0 || seatIndex >= _seatCount)
            {
                UnityEngine.Debug.LogError($"[{label}] submit rejected: seat {seatIndex} out of range [0,{_seatCount})");
                return false;
            }
            return true;
        }

        private void RaiseJoin(int seatIndex, JoinSnapshot<SpecGameState> snap)
        {
            var info = new LedgeSessionJoinInfo(
                forSeatIndex: snap.ForSeat.Value,
                revision: snap.Revision.Value,
                serverHash: snap.StateHash,
                state: snap.State);
            try { OnServerJoin?.Invoke(info); }
            catch (Exception ex) { UnityEngine.Debug.LogError($"[shadow] OnServerJoin subscriber threw seat={seatIndex}: {ex}"); }
        }

        private void RaiseAdvance(int seatIndex, StateEcho<SpecGameState> echo)
        {
            // Non-predicting path — the dispatcher routes here when the local
            // submission did not include a PredictedStateHash. Shadow mode
            // always submits with a hash, so in M6c3b-1 this branch only fires
            // for replayed joins or background server-pushed state changes
            // (none today, but the wire is kept honest for M6c3b-3).
            var info = BuildEchoInfo(echo);
            try { OnServerAdvance?.Invoke(info); }
            catch (Exception ex) { UnityEngine.Debug.LogError($"[shadow] OnServerAdvance subscriber threw seat={seatIndex}: {ex}"); }
        }

        private void RaiseMatched(int seatIndex, StateEcho<SpecGameState> echo)
        {
            Interlocked.Increment(ref _matches);
            var info = BuildEchoInfo(echo);
            try { OnServerMatched?.Invoke(info); }
            catch (Exception ex) { UnityEngine.Debug.LogError($"[shadow] OnServerMatched subscriber threw seat={seatIndex}: {ex}"); }
        }

        private void RaiseDiverged(int seatIndex, StateEcho<SpecGameState> echo)
        {
            Interlocked.Increment(ref _divergences);
            // Echo carries the canonical server state under Outcome=Desynced
            // (rules accepted but hash differed) or Outcome=Rejected (rules
            // refused the action). Log both explicitly so the reader can
            // tell whether the divergence is "server saw a different world"
            // vs "server disagrees with what the action should do."
            var localAction = seatIndex >= 0 && seatIndex < _sessions.Count
                ? $" submittingSeat={echo.SubmittingSeat.Value}"
                : string.Empty;
            UnityEngine.Debug.LogError(
                $"[shadow] divergence seat={seatIndex} outcome={echo.Outcome} " +
                $"seq={echo.AckedSeq.Value} revision={echo.Revision.Value} " +
                $"serverHash=0x{echo.StateHash:X16}{localAction}");

            var info = BuildEchoInfo(echo);
            try { OnServerDiverged?.Invoke(info); }
            catch (Exception ex) { UnityEngine.Debug.LogError($"[shadow] OnServerDiverged subscriber threw seat={seatIndex}: {ex}"); }
        }

        private void RaiseError(int seatIndex, ErrorEnvelope err)
        {
            Interlocked.Increment(ref _divergences);
            UnityEngine.Debug.LogError(
                $"[shadow] server error seat={seatIndex} seq={err.AckedSeq.Value} " +
                $"code={err.Code} message={err.Message}");

            var info = new LedgeSessionErrorInfo(
                subscribingSeatIndex: seatIndex,
                ackedSeq: err.AckedSeq.Value,
                code: err.Code,
                message: err.Message);
            try { OnServerError?.Invoke(info); }
            catch (Exception ex) { UnityEngine.Debug.LogError($"[shadow] OnServerError subscriber threw seat={seatIndex}: {ex}"); }
        }

        private void RaiseTakeback(int seatIndex, TakebackBroadcast<SpecGameState> bcast)
        {
            UnityEngine.Debug.Log(
                $"[takeback] broadcast seat={seatIndex} " +
                $"requestingSeat={bcast.RequestingSeat.Value} forSeat={bcast.ForSeat.Value} " +
                $"stepsRewound={bcast.StepsRewound} revisionAfter={bcast.RevisionAfter.Value} " +
                $"hash=0x{bcast.StateHash:X16}");

            var info = new LedgeSessionTakebackInfo(
                requestingSeatIndex: bcast.RequestingSeat.Value,
                forSeatIndex: bcast.ForSeat.Value,
                ackedRequestSeq: bcast.AckedRequestSeq.Value,
                revisionAfter: bcast.RevisionAfter.Value,
                serverHash: bcast.StateHash,
                stepsRewound: bcast.StepsRewound,
                state: bcast.State);
            try { OnServerTakeback?.Invoke(info); }
            catch (Exception ex) { UnityEngine.Debug.LogError($"[takeback] OnServerTakeback subscriber threw seat={seatIndex}: {ex}"); }
        }

        private void RaiseTakebackReply(int seatIndex, TakebackResponse reply)
        {
            UnityEngine.Debug.Log(
                $"[takeback] reply seat={seatIndex} " +
                $"requestingSeat={reply.RequestingSeat.Value} outcome={reply.Outcome} " +
                $"stepsGranted={reply.StepsGranted} seq={reply.AckedRequestSeq.Value} " +
                $"message={reply.Message}");

            var info = new LedgeSessionTakebackReplyInfo(
                subscribingSeatIndex: seatIndex,
                requestingSeatIndex: reply.RequestingSeat.Value,
                ackedRequestSeq: reply.AckedRequestSeq.Value,
                outcome: TranslateTakebackOutcome(reply.Outcome),
                stepsGranted: reply.StepsGranted,
                message: reply.Message);
            try { OnServerTakebackReply?.Invoke(info); }
            catch (Exception ex) { UnityEngine.Debug.LogError($"[takeback] OnServerTakebackReply subscriber threw seat={seatIndex}: {ex}"); }
        }

        private static LedgeTakebackOutcome TranslateTakebackOutcome(TakebackOutcome outcome)
        {
            switch (outcome)
            {
                case TakebackOutcome.Granted: return LedgeTakebackOutcome.Granted;
                case TakebackOutcome.PendingConsent: return LedgeTakebackOutcome.PendingConsent;
                case TakebackOutcome.Denied: return LedgeTakebackOutcome.Denied;
                default: return LedgeTakebackOutcome.Denied;
            }
        }

        private static LedgeSessionEchoInfo BuildEchoInfo(StateEcho<SpecGameState> echo)
        {
            return new LedgeSessionEchoInfo(
                submittingSeatIndex: echo.SubmittingSeat.Value,
                forSeatIndex: echo.ForSeat.Value,
                ackedSeq: echo.AckedSeq.Value,
                revision: echo.Revision.Value,
                serverHash: echo.StateHash,
                outcome: TranslateOutcome(echo.Outcome),
                state: echo.State);
        }

        private static LedgeSessionOutcome TranslateOutcome(ContractsApplyOutcome outcome)
        {
            switch (outcome)
            {
                case ContractsApplyOutcome.Applied: return LedgeSessionOutcome.Applied;
                case ContractsApplyOutcome.Rejected: return LedgeSessionOutcome.Rejected;
                case ContractsApplyOutcome.Desynced: return LedgeSessionOutcome.Desynced;
                default: return LedgeSessionOutcome.Applied;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            IsReady = false;
            // Snapshot under the lock, then dispose outside it — can't hold
            // a lock across await. The Start path's publish step observes
            // _disposed under the same lock, so a concurrent AddRange can
            // only either (a) finish before our snapshot, making its
            // sessions visible here, or (b) see _disposed=1 and dispose
            // its locals itself.
            MagiSession<SpecGameState, LedgeAction>[] snapshot;
            lock (_lock)
            {
                snapshot = _sessions.ToArray();
                _sessions.Clear();
                _transports.Clear();
            }
            for (int i = 0; i < snapshot.Length; i++)
            {
                try { await snapshot[i].DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { UnityEngine.Debug.LogError($"[shadow] dispose seat {i}: {ex}"); }
            }
            if (_hostHttp != null)
            {
                try { _hostHttp.Dispose(); }
                catch (Exception ex) { UnityEngine.Debug.LogError($"[shadow] dispose http client: {ex}"); }
                _hostHttp = null;
            }
        }
    }
}
