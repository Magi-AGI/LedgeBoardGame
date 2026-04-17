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
        /// Submits an action. Non-blocking. The submitter is responsible for
        /// stamping `Seq` and `PredictedStateHash`; callers pass just the
        /// action payload and let the submitter wrap it.
        void Submit(TAction action, long predictedStateHash);

        /// Fired when the server echoed back a matching applied state. `Seq`
        /// names which submit is being acked so callers can match to their
        /// optimistic stack.
        event Action<StateEcho<TState>> OnActionApplied;

        /// Fired when the server's post-state diverges from the client's
        /// predicted state. The client should replace its optimistic state with
        /// the echoed `State` rather than trying to apply further optimistic
        /// actions on top of the stale prediction.
        event Action<StateEcho<TState>> OnActionReconciled;

        /// Fired when the server rejected the protocol envelope entirely
        /// (malformed, wrong seat, etc). Separate from rule rejections, which
        /// come back as an Applied echo with Outcome=Rejected.
        event Action<ErrorEnvelope> OnError;
    }
}
