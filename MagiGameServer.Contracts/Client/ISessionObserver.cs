using System;
using MagiGameServer.Contracts.Protocol;

namespace MagiGameServer.Contracts.Client
{
    /// Client-side inbound surface for server-authored events. Everything the
    /// server sends to a seat — join snapshots, state echoes (own and
    /// remote), takeback outcomes, protocol errors — arrives here, and every
    /// event is already projected for this observer's seat. No event on this
    /// interface carries cross-seat hidden information.
    ///
    /// Why separate from IActionSubmitter:
    /// - Passive consumers (spectators, replays, late joiners) observe
    ///   without submitting.
    /// - Dispatcher routing is non-trivial and belongs in one place: the
    ///   transport layer classifies each inbound echo into exactly one of
    ///   OnStateAdvanced / OnPredictionMatched / OnPredictionDiverged by
    ///   comparing `SubmittingSeat` + `AckedSeq` + `StateHash` against the
    ///   observer's own optimistic stack.
    /// - Generics are asymmetric: observers are parameterised on TState
    ///   (because they receive state), submitters on TAction (because they
    ///   send actions). Forcing a single interface would bind both generics
    ///   at every call site for no benefit.
    ///
    /// Dispatcher routing rules (implemented in the M5a transport layer,
    /// documented here so the contract is explicit about what each event
    /// means):
    /// - `OnSessionJoined`: fired exactly once per session, when the server
    ///   accepts the join and sends the initial JoinSnapshot. Use this to
    ///   seed the local optimistic view.
    /// - `OnStateAdvanced`: fired for any StateEcho whose `SubmittingSeat`
    ///   is not this observer's seat (a remote player's action), OR whose
    ///   SubmittingSeat is ours but the echo was not linked to a live
    ///   optimistic prediction (e.g. non-predicting client, reconciled bot,
    ///   catching up after reconnect).
    /// - `OnPredictionMatched`: fired for a StateEcho whose
    ///   `SubmittingSeat == ForSeat` AND there is a matching entry in this
    ///   observer's optimistic stack AND `Outcome == Applied` AND the echo's
    ///   `StateHash` equals the optimistic entry's predicted hash. The UI
    ///   typically does nothing visible here — the optimistic view already
    ///   shows the correct state; the dispatcher just retires the stack
    ///   entry.
    /// - `OnPredictionDiverged`: fired for the same seat/stack match as
    ///   above, but `Outcome` is Rejected/Desynced OR the hashes disagree.
    ///   The client must snap its optimistic view to the echoed `State`.
    ///   The three events OnStateAdvanced/OnPredictionMatched/
    ///   OnPredictionDiverged are mutually exclusive — exactly one fires
    ///   per StateEcho the observer receives.
    /// - `OnTakebackBroadcast`: fired for every seat (including the
    ///   requester) when a takeback was granted and canonical state was
    ///   rewound. Receivers should replace local state with `State` and
    ///   drop any pending optimistic entries with ClientSeq >
    ///   AckedRequestSeq (the requester) or simply reset their optimistic
    ///   stack (non-requesters).
    /// - `OnTakebackReply`: fired only for Denied/PendingConsent outcomes.
    ///   Granted outcomes arrive as OnTakebackBroadcast instead, because
    ///   the broadcast carries the post-rewind state and the reply would
    ///   be redundant.
    /// - `OnError`: protocol-level failures that never reached the rules
    ///   adapter (malformed envelope, unknown session, out-of-range seat).
    ///   The optimistic stack entry tagged with `AckedSeq` must be undone
    ///   locally — no state echo is coming for it.
    public interface ISessionObserver<TState>
    {
        /// Fired once when the server accepts this observer into a session
        /// and delivers the initial snapshot of state as this seat sees it.
        /// Subsequent state is delivered via the OnStateAdvanced /
        /// OnPrediction* / OnTakebackBroadcast events.
        event Action<JoinSnapshot<TState>> OnSessionJoined;

        /// Fired when canonical state advanced for a reason unrelated to
        /// this observer's pending predictions (remote action, or own
        /// action on a non-predicting path). The client should render
        /// `State` as-is.
        event Action<StateEcho<TState>> OnStateAdvanced;

        /// Fired when the server acknowledged one of this observer's own
        /// submissions and the server's post-projection hash matched the
        /// optimistic prediction. The optimistic stack entry is retired;
        /// no visible state change is needed.
        event Action<StateEcho<TState>> OnPredictionMatched;

        /// Fired when the server acknowledged one of this observer's own
        /// submissions but the outcome or hash disagrees with what the
        /// client predicted (rules rejected, desync detected, or hashes
        /// differ despite Applied). The client must snap to `State`.
        event Action<StateEcho<TState>> OnPredictionDiverged;

        /// Fired on every seat when a takeback was granted, carrying the
        /// post-rewind state already projected for `ForSeat`. The `Revision`
        /// moves backward on this event — it is the only ISessionObserver
        /// event where that happens, and dispatchers should treat it as a
        /// branch cut rather than a forward advance.
        event Action<TakebackBroadcast<TState>> OnTakebackBroadcast;

        /// Fired only for Denied / PendingConsent outcomes. Granted
        /// takebacks arrive as OnTakebackBroadcast instead, because that
        /// event already carries the authoritative post-rewind state.
        event Action<TakebackResponse> OnTakebackReply;

        /// Fired when the server rejected a protocol envelope before rules
        /// ran (malformed payload, unknown session, out-of-range seat). The
        /// client must undo the corresponding optimistic entry locally —
        /// no StateEcho is coming.
        event Action<ErrorEnvelope> OnError;
    }
}
