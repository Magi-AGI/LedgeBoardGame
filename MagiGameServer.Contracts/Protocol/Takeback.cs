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
        public SeatId RequestingSeat { get; init; }
        public ClientSeq AckedRequestSeq { get; init; }
        public TakebackOutcome Outcome { get; init; }
        public int StepsGranted { get; init; }

        /// Canonical timeline position after the rewind. Only meaningful when
        /// Outcome=Granted; for PendingConsent/Denied the revision did not
        /// move. Non-requesting seats watching the broadcast should reconcile
        /// their own state toward whichever StateEcho carries this revision.
        public ServerSeq RevisionAfter { get; init; }

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

    /// Server-to-client: broadcast delivered to every seat (including the
    /// requester) when a takeback was granted and canonical state was
    /// rewound. Carries the post-rewind state already projected for
    /// `ForSeat`, so receivers don't need to correlate this envelope with
    /// a separate StateEcho — unlike ordinary actions, a granted takeback
    /// does not also produce per-seat StateEchoes on the wire.
    ///
    /// `RevisionAfter` is lower than the revision each recipient was
    /// holding before the broadcast; this is the one server-authored event
    /// where the timeline moves backward, and dispatchers should treat it
    /// as a branch cut: drop any pending optimistic stack entries whose
    /// ClientSeq > `AckedRequestSeq` on the requesting seat, and reset the
    /// optimistic stack entirely on non-requesting seats.
    ///
    /// Denied / PendingConsent outcomes do not produce a broadcast —
    /// they arrive as TakebackResponse on OnTakebackReply instead.
    public sealed record TakebackBroadcast<TState>
    {
        public SessionId Session { get; init; }
        public SeatId ForSeat { get; init; }
        public SeatId RequestingSeat { get; init; }
        public ClientSeq AckedRequestSeq { get; init; }
        public ServerSeq RevisionAfter { get; init; }
        public int StepsRewound { get; init; }
        public TState State { get; init; }
        public long StateHash { get; init; }
    }
}
