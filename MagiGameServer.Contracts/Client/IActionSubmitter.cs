using System;
using MagiGameServer.Contracts.Core;
using MagiGameServer.Contracts.Protocol;

namespace MagiGameServer.Contracts.Client
{
    /// Client-side fire-and-echo gateway. The submitting code never awaits a
    /// server response — Submit returns immediately so the UI can paint the
    /// optimistic result in the same frame. The two events below fire on the
    /// server's echo: OnActionApplied for happy-path commits, OnActionReconciled
    /// when the server's post-state disagreed with the client's optimistic
    /// guess and the client must snap to the echoed canonical state.
    ///
    /// Separating "applied" from "reconciled" matters for UX: an applied echo
    /// confirms what the player already sees, so the UI usually does nothing;
    /// a reconciled echo means the player is about to see a visible snap and
    /// the client can choose to soften it (brief highlight, easing the snap,
    /// etc). Collapsing both into a single "echo received" callback would
    /// blur that boundary.
    public interface IActionSubmitter<TAction, TState>
    {
        /// Submits an action. Non-blocking. The submitter stamps `Seq` and
        /// wraps the payload into an ActionEnvelope; the caller provides the
        /// action plus the hash of its own post-apply projected state (the
        /// submitter cannot compute that hash because it doesn't run the rules
        /// — only the caller, which just applied the action optimistically,
        /// has the post-state in hand). Predicting callers compute this via
        /// adapter.GetStateHash(postApplyProjectedState); non-predicting
        /// callers (e.g. bots with no optimistic layer) pass 0 to skip the
        /// desync check.
        void Submit(TAction action, long predictedStateHash);

        /// Fired when the server echoed back a matching applied state —
        /// Outcome=Applied and the server's post-projection hash matched the
        /// client's PredictedStateHash. `AckedSeq` + `SubmittingSeat` name
        /// which submit is being acked so callers can match to their
        /// optimistic stack.
        event Action<StateEcho<TState>> OnActionApplied;

        /// Fired when the server's post-state diverges from the client's
        /// optimistic view and the client must snap to the echoed `State`.
        /// This covers three cases: (1) Outcome=Rejected — the rules refused
        /// the action, so the server's post-state equals the pre-action state
        /// while the client already applied optimistically; (2) Outcome=Desynced
        /// — the server applied but the post-projection hashes disagreed;
        /// (3) broadcast receipt of an earlier takeback that rewound an action
        /// the client had already rendered. In every case, the client should
        /// replace its optimistic state with the echoed `State` rather than
        /// trying to apply further optimistic actions on top of the stale
        /// prediction.
        event Action<StateEcho<TState>> OnActionReconciled;

        /// Fired when the server rejected the protocol envelope entirely
        /// (malformed, wrong seat, no active session). Distinct from rule
        /// rejections, which come back via OnActionReconciled with
        /// Outcome=Rejected and a valid post-state attached. Protocol errors
        /// don't carry a post-state because the server never reached the rules
        /// adapter, so the client's optimistic prediction is the only state
        /// it has — it must undo locally rather than snap to an echo.
        event Action<ErrorEnvelope> OnError;
    }
}
