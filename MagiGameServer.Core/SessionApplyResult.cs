using System.Collections.Generic;
using MagiGameServer.Contracts.Core;
using MagiGameServer.Contracts.Protocol;

namespace MagiGameServer.Core
{
    /// Result of Session.Apply — the host dispatches `Echoes` over transport
    /// to every seat. One echo per seat, ordered by SeatId value. The
    /// acknowledgement-vs-broadcast distinction is carried in the echo
    /// itself (SubmittingSeat == ForSeat identifies the originator's own
    /// ack); the host does not branch on it. `Outcome` is the rules
    /// outcome (possibly downgraded from Applied to Desynced); `Revision`
    /// is the post-apply canonical position — equal to the pre-apply
    /// revision when Outcome=Rejected, advanced by one otherwise.
    public sealed record SessionApplyResult
    {
        public ApplyOutcome Outcome { get; init; }
        public ServerSeq Revision { get; init; }
        public IReadOnlyList<StateEcho<object>> Echoes { get; init; }
    }

    /// Result of Session.Takeback. `Response` is the envelope the requester
    /// gets back; `Echoes` is the broadcast set carrying post-rewind state
    /// to every seat when Outcome=Granted (empty for PendingConsent/Denied
    /// since nothing moved). The echoes re-use StateEcho with SubmittingSeat
    /// set to the requester and Outcome=Applied — client-side consumers
    /// distinguish "new action" from "rewind" by comparing the echo's
    /// Revision to their last-known revision, not by event type.
    public sealed record SessionTakebackResult
    {
        public TakebackResponse Response { get; init; }
        public IReadOnlyList<StateEcho<object>> Echoes { get; init; }
    }
}
