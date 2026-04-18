using System;
using System.Collections.Generic;
using MagiGameServer.Contracts.Client;
using MagiGameServer.Contracts.Core;
using MagiGameServer.Contracts.Protocol;

namespace MagiGameServer.Client
{
    /// Client-side routing core. Holds the optimistic prediction stack for
    /// one seat in one session and classifies every inbound server envelope
    /// into exactly one ISessionObserver event. The dispatcher is the single
    /// place where "which callback does this echo fire?" is decided — the
    /// transport layer below (WebSocket, in-process, mock) only needs to
    /// deserialize envelopes and forward them into Ingest(...); the game
    /// layer above only needs to subscribe to the seven observer events.
    ///
    /// No transport, no Unity. Pure netstandard2.1 so every client platform
    /// (Unity via MagiSession, headless bot, integration tests, replay
    /// tooling) shares the same routing logic.
    ///
    /// Outbound side: Submit / SubmitTakeback construct envelopes and raise
    /// OutgoingAction / OutgoingTakeback. The transport subscribes to those
    /// events and does whatever it needs to deliver them (serialize + send
    /// over socket, hand to an in-process Session, queue for replay).
    /// Keeping outbound as events rather than an abstract transport
    /// interface keeps the dispatcher's dependencies minimal — a test can
    /// subscribe a lambda, production can subscribe a WebSocket writer,
    /// neither path touches the other.
    public sealed class SessionDispatcher<TState, TAction> : IActionSubmitter<TAction>, ISessionObserver<TState>
    {
        private readonly SessionId _session;
        private readonly SeatId _ownSeat;
        private readonly List<PendingPrediction> _pending = new List<PendingPrediction>();
        private long _nextClientSeq = 1;

        public SessionDispatcher(SessionId session, SeatId ownSeat)
        {
            _session = session;
            _ownSeat = ownSeat;
        }

        public SessionId Session => _session;
        public SeatId OwnSeat => _ownSeat;

        /// Count of pending optimistic predictions awaiting reconciliation.
        /// Exposed for tests and diagnostics only — game code should react
        /// to observer events rather than polling this.
        public int PendingCount => _pending.Count;

        // ------------------------------------------------------------------
        // Outbound surface (IActionSubmitter)
        // ------------------------------------------------------------------

        /// Raised after Submit stamps and wraps the action into an envelope.
        /// The transport subscribes and delivers over the wire (or to an
        /// in-process Session). Not part of IActionSubmitter because the
        /// submitter caller doesn't need to see the envelope — it already
        /// knows what it submitted — but the transport does.
        public event Action<ActionEnvelope<TAction>> OutgoingAction;

        /// Raised after SubmitTakeback builds a TakebackRequest. Same
        /// transport-facing contract as OutgoingAction.
        public event Action<TakebackRequest> OutgoingTakeback;

        public void Submit(TAction action, long predictedStateHash)
        {
            var seq = new ClientSeq(_nextClientSeq++);
            if (predictedStateHash != 0)
            {
                // Only track optimistic entries when the caller actually
                // predicted. Non-predicting callers (bots, headless) pass 0
                // and their echoes route to OnStateAdvanced — the same path
                // remote actions take. This is exactly the reconnect /
                // non-predicting catch-up behaviour documented on
                // ISessionObserver.
                _pending.Add(new PendingPrediction(seq, predictedStateHash));
            }

            var envelope = new ActionEnvelope<TAction>
            {
                Session = _session,
                Seat = _ownSeat,
                Seq = seq,
                Action = action,
                PredictedStateHash = predictedStateHash,
            };
            OutgoingAction?.Invoke(envelope);
        }

        public void SubmitTakeback(int stepsRequested, string reason)
        {
            var seq = new ClientSeq(_nextClientSeq++);
            var request = new TakebackRequest
            {
                Session = _session,
                RequestingSeat = _ownSeat,
                SeqAtRequestTime = seq,
                StepsRequested = stepsRequested,
                Reason = reason,
            };
            OutgoingTakeback?.Invoke(request);
        }

        // ------------------------------------------------------------------
        // Inbound surface (ISessionObserver)
        // ------------------------------------------------------------------

        public event Action<JoinSnapshot<TState>> OnSessionJoined;
        public event Action<StateEcho<TState>> OnStateAdvanced;
        public event Action<StateEcho<TState>> OnPredictionMatched;
        public event Action<StateEcho<TState>> OnPredictionDiverged;
        public event Action<TakebackBroadcast<TState>> OnTakebackBroadcast;
        public event Action<TakebackResponse> OnTakebackReply;
        public event Action<ErrorEnvelope> OnError;

        /// Transport entry point for the initial snapshot handed to this
        /// seat at session join. Fires OnSessionJoined. No routing logic
        /// needed — JoinSnapshot has no SubmittingSeat / AckedSeq, so it
        /// can't be confused with the prediction-match path.
        public void Ingest(JoinSnapshot<TState> snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            GuardAddressing(snapshot.Session, snapshot.ForSeat, nameof(snapshot));
            OnSessionJoined?.Invoke(snapshot);
        }

        /// Transport entry point for a per-seat StateEcho. Routes into
        /// exactly one of OnStateAdvanced / OnPredictionMatched /
        /// OnPredictionDiverged per the rules documented on
        /// ISessionObserver.
        public void Ingest(StateEcho<TState> echo)
        {
            if (echo == null) throw new ArgumentNullException(nameof(echo));
            GuardAddressing(echo.Session, echo.ForSeat, nameof(echo));

            // Not our submission → remote advance. Optimistic stack is
            // untouched; remote players can't ack our entries.
            if (echo.SubmittingSeat != _ownSeat)
            {
                OnStateAdvanced?.Invoke(echo);
                return;
            }

            // Our submission. Look for a matching pending entry. If there
            // isn't one, this is the non-predicting / reconnect path:
            // fire OnStateAdvanced, don't treat it as a prediction event.
            int idx = FindPendingIndex(echo.AckedSeq);
            if (idx < 0)
            {
                OnStateAdvanced?.Invoke(echo);
                return;
            }

            var pending = _pending[idx];
            _pending.RemoveAt(idx);

            bool matched =
                echo.Outcome == ApplyOutcome.Applied
                && echo.StateHash == pending.PredictedHash;

            if (matched)
            {
                OnPredictionMatched?.Invoke(echo);
            }
            else
            {
                // Covers Rejected, Desynced, or Applied-but-hash-diverged.
                // The client must snap to echo.State; the optimistic entry
                // has already been retired above.
                OnPredictionDiverged?.Invoke(echo);
            }
        }

        /// Transport entry point for a granted takeback. Clears the
        /// appropriate portion of the optimistic stack (branch cut rule
        /// from ISessionObserver) and fires OnTakebackBroadcast.
        public void Ingest(TakebackBroadcast<TState> broadcast)
        {
            if (broadcast == null) throw new ArgumentNullException(nameof(broadcast));
            GuardAddressing(broadcast.Session, broadcast.ForSeat, nameof(broadcast));

            if (broadcast.RequestingSeat == _ownSeat)
            {
                // We requested the rewind. Drop predictions submitted
                // after our request — they're stale. Predictions submitted
                // before the request stay (they were part of the timeline
                // we asked to rewind from, but the server's rewind doesn't
                // invalidate an optimistic entry whose echo may still be
                // in flight behind the broadcast). In practice, by the
                // time a takeback broadcast arrives, the transport has
                // already delivered echoes for earlier seqs; the branch
                // cut is primarily a safety valve for aggressive clients.
                _pending.RemoveAll(p => p.Seq > broadcast.AckedRequestSeq);
            }
            else
            {
                // A remote player got a rewind granted. Our optimistic
                // view is built on top of canonical state that just moved
                // backward under us; any pending predictions are now
                // relative to a vanished timeline. Clear them and let the
                // game layer rebuild from broadcast.State.
                _pending.Clear();
            }

            OnTakebackBroadcast?.Invoke(broadcast);
        }

        /// Transport entry point for Denied / PendingConsent takeback
        /// outcomes. Granted outcomes arrive as TakebackBroadcast, not as
        /// TakebackResponse — this method is a no-op dispatcher for the
        /// non-granted paths only. Fires OnTakebackReply.
        public void Ingest(TakebackResponse response)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            // TakebackResponse has no ForSeat — it's delivered only to the
            // requester, so RequestingSeat is the addressee we must match.
            GuardAddressing(response.Session, response.RequestingSeat, nameof(response));
            OnTakebackReply?.Invoke(response);
        }

        /// Transport entry point for protocol-level errors (server rejected
        /// the envelope before rules ran). Clears the matching optimistic
        /// entry so the game layer's undo-locally path isn't left with a
        /// ghost prediction that will never be reconciled. Fires OnError.
        public void Ingest(ErrorEnvelope error)
        {
            if (error == null) throw new ArgumentNullException(nameof(error));
            // ErrorEnvelope carries no seat field — the server only emits
            // it to the submitter whose envelope was rejected. Session
            // match is the strongest guard available, and it's enough to
            // keep a foreign error with a colliding ClientSeq from
            // retiring the wrong optimistic entry.
            if (error.Session != _session)
                throw new ArgumentException(
                    $"ErrorEnvelope for session {error.Session} delivered to dispatcher bound to {_session}",
                    nameof(error));
            int idx = FindPendingIndex(error.AckedSeq);
            if (idx >= 0) _pending.RemoveAt(idx);
            OnError?.Invoke(error);
        }

        // Session + addressee-seat guard shared by every inbound envelope
        // that carries a ForSeat-ish field. A transport misroute here is
        // not a noop: a wrong-seat StateEcho / JoinSnapshot /
        // TakebackBroadcast would surface another seat's projected state
        // to this client, breaking the hidden-info invariant the server
        // enforces in ProjectStateFor. The dispatcher is the last line of
        // defence before the game layer sees projected state, so it fails
        // loud rather than silently routing.
        private void GuardAddressing(SessionId session, SeatId addressee, string paramName)
        {
            if (session != _session)
                throw new ArgumentException(
                    $"Envelope addressed to session {session} delivered to dispatcher bound to {_session}",
                    paramName);
            if (addressee != _ownSeat)
                throw new ArgumentException(
                    $"Envelope addressed to {addressee} delivered to dispatcher bound to {_ownSeat}",
                    paramName);
        }

        private int FindPendingIndex(ClientSeq seq)
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].Seq == seq) return i;
            }
            return -1;
        }

        // Optimistic stack entry. Value-typed: ClientSeq + predicted hash is
        // all the dispatcher needs to decide matched vs. diverged on echo.
        private readonly struct PendingPrediction
        {
            public ClientSeq Seq { get; }
            public long PredictedHash { get; }

            public PendingPrediction(ClientSeq seq, long predictedHash)
            {
                Seq = seq;
                PredictedHash = predictedHash;
            }
        }
    }
}
