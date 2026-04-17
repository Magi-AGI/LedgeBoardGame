using MagiGameServer.Contracts.Core;

namespace MagiGameServer.Contracts.Protocol
{
    /// Client-to-server: a request to rewind one or more already-applied actions.
    /// Distinct from local mid-turn undo, which operates on pre-submit client
    /// buffers only — any action that has been echoed back by the server is
    /// server-committed and can only be rolled back via this path. Policy
    /// (auto-grant same-turn, require opponent consent, flat reject) is the
    /// server's call per-game, not the client's.
    public sealed record TakebackRequest
    {
        public SessionId Session { get; init; }
        public SeatId RequestingSeat { get; init; }
        public ClientSeq SeqAtRequestTime { get; init; }

        /// How far back the requester wants to rewind. `1` = revoke most recent
        /// own action, `N` = revoke the last N own actions. The server may
        /// truncate or reject — the returned response is authoritative on what
        /// actually got rewound.
        public int StepsRequested { get; init; }

        /// Free-form reason the requester wants to surface to the opponent
        /// (misclick, rules misunderstanding). Optional; server may show to
        /// peers for consent prompts.
        public string Reason { get; init; }
    }

    /// Server-to-client: outcome of a takeback request. When Outcome=Granted,
    /// the server has already rewound canonical state and sent a fresh
    /// StateEcho to every affected seat carrying the post-takeback state; this
    /// envelope is the notification that the rewind happened, not itself the
    /// rewound state.
    public sealed record TakebackResponse
    {
        public SessionId Session { get; init; }
        public ClientSeq AckedRequestSeq { get; init; }
        public TakebackOutcome Outcome { get; init; }
        public int StepsGranted { get; init; }
        public string Message { get; init; }
    }

    public enum TakebackOutcome
    {
        /// Server rewound the requested (or truncated) number of steps.
        Granted,
        /// Opponent consent required but not yet received — clients should
        /// expect a follow-up Granted/Denied once the opponent responds.
        PendingConsent,
        /// Server policy or opponent rejected the request.
        Denied
    }
}
