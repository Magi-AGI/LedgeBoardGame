namespace MagiGameServer.Contracts.Client
{
    /// Client-side outbound gateway for fire-and-echo. The submitting code
    /// never awaits a server response — Submit returns immediately so the UI
    /// can paint the optimistic result in the same frame. Inbound server
    /// echoes are delivered separately via ISessionObserver, keeping outbound
    /// intent ("tell the server") cleanly split from inbound reconciliation
    /// ("react to the server"). The two are not combined on one interface
    /// because observation is legitimately passive — spectators, replays, and
    /// late joiners consume observer events without ever submitting — and
    /// because broadcast routing cares about which seat submitted an echoed
    /// action, a detail submitters don't participate in.
    ///
    /// Not parameterised on TState: submitters only produce TAction payloads
    /// and protocol metadata, never state. Echoes and snapshots — which do
    /// carry TState — live on ISessionObserver<TState>. Keeping the two
    /// interfaces asymmetric in their generics is intentional and prevents
    /// callers that only want to send actions from dragging a TState type
    /// parameter through their call sites.
    public interface IActionSubmitter<TAction>
    {
        /// Submits an action. Non-blocking. The submitter stamps `Seq` and
        /// wraps the payload into an ActionEnvelope; the caller provides the
        /// action plus the hash of its own post-apply projected state (the
        /// submitter cannot compute that hash because it doesn't run the
        /// rules — only the caller, which just applied the action
        /// optimistically, has the post-state in hand). Predicting callers
        /// compute this via adapter.GetStateHash(postApplyProjectedState);
        /// non-predicting callers (e.g. bots with no optimistic layer) pass 0
        /// to skip the desync check.
        void Submit(TAction action, long predictedStateHash);

        /// Requests a rewind of one or more already-echoed actions. Mirrors
        /// Submit's non-blocking semantics — the server's decision arrives
        /// asynchronously on ISessionObserver as OnTakebackReply (for
        /// Denied/PendingConsent) or OnTakebackBroadcast (for Granted, fired
        /// to every seat including the requester). `stepsRequested` is a
        /// hint; the server may grant fewer or deny outright per module
        /// policy, and the observer event is authoritative on what actually
        /// rewound. `reason` is free-form and may be surfaced to opponents
        /// in consent prompts.
        void SubmitTakeback(int stepsRequested, string reason);
    }
}
