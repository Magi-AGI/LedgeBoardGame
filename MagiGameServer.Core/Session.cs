using System;
using System.Collections.Generic;
using MagiGameServer.Contracts.Core;
using MagiGameServer.Contracts.Protocol;
using MagiGameServer.Contracts.Rules;

namespace MagiGameServer.Core
{
    /// Default ISession implementation. Stores canonical state as `object`
    /// and delegates rules work through the non-generic IRulesAdapter, which
    /// RulesAdapterBase already bridges to the typed surface — so the host
    /// never has to know TState/TAction. Takeback rewinds via snapshot
    /// restore from the action log (O(1) rewind, bounded by log size).
    public sealed class Session : ISession
    {
        private readonly IGameModule _module;
        private readonly IRulesAdapter _rules;
        private readonly object _initialState;
        private readonly List<LogEntry> _log = new List<LogEntry>();

        private object _state;
        private ServerSeq _revision;

        public SessionId Id { get; }
        public int SeatCount { get; }
        public string GameId => _module.GameId;
        public ServerSeq CurrentRevision => _revision;

        public Session(SessionId id, IGameModule module, object initialState, int seatCount)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (initialState == null) throw new ArgumentNullException(nameof(initialState));
            if (seatCount < module.MinSeats || seatCount > module.MaxSeats)
                throw new ArgumentOutOfRangeException(nameof(seatCount),
                    $"SeatCount {seatCount} outside [{module.MinSeats},{module.MaxSeats}] for {module.GameId}");

            Id = id;
            _module = module;
            _rules = module.Rules;
            // Snapshot on intake so the caller's reference into initialState
            // can mutate afterwards without affecting us, and _initialState
            // stays independent from the live _state across the session's
            // lifetime. For immutable adapters SnapshotState is identity.
            _initialState = _rules.SnapshotState(initialState);
            _state = _rules.SnapshotState(initialState);
            SeatCount = seatCount;
            _revision = new ServerSeq(0);
        }

        public SessionApplyResult Apply(ActionEnvelope<object> envelope)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            if (envelope.Session != Id)
                throw new ArgumentException(
                    $"Envelope session {envelope.Session} does not match session {Id}", nameof(envelope));
            if (envelope.Seat.Value < 0 || envelope.Seat.Value >= SeatCount)
                throw new ArgumentException(
                    $"Envelope seat {envelope.Seat} out of range [0,{SeatCount}) for session {Id}",
                    nameof(envelope));

            var outcome = _rules.Apply(_state, envelope.Action, out object nextState);

            // Desync check: only when rules accepted the action and the client
            // sent a non-zero predicted hash. Clients that opt out of
            // prediction (bots, headless) send 0 and skip this path entirely.
            if (outcome == ApplyOutcome.Applied && envelope.PredictedStateHash != 0)
            {
                long serverHashForSubmitter = _rules.GetStateHashForSeat(nextState, envelope.Seat);
                if (serverHashForSubmitter != envelope.PredictedStateHash)
                {
                    outcome = ApplyOutcome.Desynced;
                }
            }

            object stateAfter;
            ServerSeq revisionAfter;
            if (outcome == ApplyOutcome.Applied || outcome == ApplyOutcome.Desynced)
            {
                _state = nextState;
                _revision = _revision.Next();
                // Snapshot before logging so the log entry is independent of
                // future mutations to _state. For mutable-state adapters this
                // is the critical invariant; immutable adapters no-op.
                _log.Add(new LogEntry(envelope, _rules.SnapshotState(_state), _revision));
                stateAfter = _state;
                revisionAfter = _revision;
            }
            else
            {
                // Rejected — state and revision do not move.
                stateAfter = _state;
                revisionAfter = _revision;
            }

            var echoes = BuildEchoes(envelope.Seat, envelope.Seq, stateAfter, outcome, revisionAfter);
            return new SessionApplyResult
            {
                Outcome = outcome,
                Revision = revisionAfter,
                Echoes = echoes,
            };
        }

        public SessionTakebackResult Takeback(TakebackRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.Session != Id)
                throw new ArgumentException(
                    $"Request session {request.Session} does not match session {Id}", nameof(request));
            if (request.RequestingSeat.Value < 0 || request.RequestingSeat.Value >= SeatCount)
                throw new ArgumentException(
                    $"RequestingSeat {request.RequestingSeat} out of range [0,{SeatCount}) for session {Id}",
                    nameof(request));

            int stepsAvailable = _log.Count;
            int stepsToRewind = Math.Min(Math.Max(0, request.StepsRequested), stepsAvailable);

            if (stepsToRewind == 0)
            {
                // Nothing to rewind — tell the requester plainly and don't
                // broadcast. Module-level policies (opponent consent, same-
                // turn limits) will reshape this path later.
                return new SessionTakebackResult
                {
                    Response = new TakebackResponse
                    {
                        Session = Id,
                        RequestingSeat = request.RequestingSeat,
                        AckedRequestSeq = request.SeqAtRequestTime,
                        Outcome = TakebackOutcome.Denied,
                        StepsGranted = 0,
                        RevisionAfter = _revision,
                        Message = "No actions available to rewind",
                    },
                    Echoes = Array.Empty<StateEcho<object>>(),
                };
            }

            int targetIdx = _log.Count - stepsToRewind - 1;
            object restoredState = targetIdx < 0 ? _initialState : _log[targetIdx].PostState;
            ServerSeq restoredRevision = targetIdx < 0 ? new ServerSeq(0) : _log[targetIdx].Revision;

            _log.RemoveRange(_log.Count - stepsToRewind, stepsToRewind);
            // Snapshot on restore so subsequent Apply mutations don't write
            // through to the log entry (or _initialState) we just borrowed
            // from. Without this, the first Apply after a rewind would
            // silently mutate our own historical snapshot.
            _state = _rules.SnapshotState(restoredState);
            _revision = restoredRevision;

            // Echoes carry Outcome=Applied because from the recipient's point
            // of view this is a canonical advance to a new Revision — even
            // though it's logically a rewind, the state delivery semantics
            // are identical. Clients distinguish "new action" from "rewind"
            // by comparing Revision to their last-known (rewind produces a
            // LOWER revision than the receiver's local hint).
            var echoes = BuildEchoes(
                submittingSeat: request.RequestingSeat,
                ackedSeq: request.SeqAtRequestTime,
                state: _state,
                outcome: ApplyOutcome.Applied,
                revision: _revision);

            return new SessionTakebackResult
            {
                Response = new TakebackResponse
                {
                    Session = Id,
                    RequestingSeat = request.RequestingSeat,
                    AckedRequestSeq = request.SeqAtRequestTime,
                    Outcome = TakebackOutcome.Granted,
                    StepsGranted = stepsToRewind,
                    RevisionAfter = _revision,
                    Message = null,
                },
                Echoes = echoes,
            };
        }

        private StateEcho<object>[] BuildEchoes(
            SeatId submittingSeat,
            ClientSeq ackedSeq,
            object state,
            ApplyOutcome outcome,
            ServerSeq revision)
        {
            var echoes = new StateEcho<object>[SeatCount];
            for (int i = 0; i < SeatCount; i++)
            {
                var seat = new SeatId(i);
                object projected = _rules.ProjectStateFor(state, seat);
                long hash = _rules.GetStateHash(projected);
                echoes[i] = new StateEcho<object>
                {
                    Session = Id,
                    ForSeat = seat,
                    SubmittingSeat = submittingSeat,
                    AckedSeq = ackedSeq,
                    Revision = revision,
                    State = projected,
                    StateHash = hash,
                    Outcome = outcome,
                };
            }
            return echoes;
        }

        // Snapshot entry for O(1) takeback rewind. Holds the full post-apply
        // state reference; for games with large states this is the simplest
        // correct approach for M3, and reverse-diff / replay optimisations
        // can slot in later without changing the public surface.
        private sealed class LogEntry
        {
            public ActionEnvelope<object> Envelope { get; }
            public object PostState { get; }
            public ServerSeq Revision { get; }

            public LogEntry(ActionEnvelope<object> envelope, object postState, ServerSeq revision)
            {
                Envelope = envelope;
                PostState = postState;
                Revision = revision;
            }
        }
    }
}
