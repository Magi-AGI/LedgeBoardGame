using MagiGameServer.Contracts.Core;

namespace MagiGameServer.Contracts.Protocol
{
    /// Client-to-server: a pending action the server should attempt to apply.
    /// `PredictedStateHash` is the hash the client got after optimistically
    /// applying the action locally — if the server's post-apply hash doesn't
    /// match, the client is considered desynced on this line and must replay
    /// from the next StateEcho instead of reconciling forward. Leave zero to
    /// skip the check (non-predicting clients, e.g. bots).
    public sealed record ActionEnvelope<TAction>
    {
        public SessionId Session { get; init; }
        public SeatId Seat { get; init; }
        public ClientSeq Seq { get; init; }
        public TAction Action { get; init; }
        public long PredictedStateHash { get; init; }
    }

    /// Server-to-client: the canonical state after applying an action, already
    /// projected for the receiving seat. Clients never see cross-seat hidden
    /// information — the hidden-info guarantee is enforced server-side inside
    /// ProjectStateFor, not by the client voluntarily ignoring fields.
    /// `AckedSeq` names the client action this echo is answering; predicting
    /// clients match echoes to their pending optimistic stack via this field.
    public sealed record StateEcho<TState>
    {
        public SessionId Session { get; init; }
        public SeatId ForSeat { get; init; }
        public ClientSeq AckedSeq { get; init; }
        public TState State { get; init; }
        public long StateHash { get; init; }
        public ApplyOutcome Outcome { get; init; }
    }

    /// Server-to-client: informational error envelope for actions the server
    /// couldn't even reach the rules adapter with (malformed payload, wrong
    /// seat, no active session). Rules rejections come back as StateEcho with
    /// Outcome=Rejected so the client still gets a valid post-state; this
    /// envelope is for protocol-level failures that don't advance state.
    public sealed record ErrorEnvelope
    {
        public SessionId Session { get; init; }
        public ClientSeq AckedSeq { get; init; }
        public string Code { get; init; }
        public string Message { get; init; }
    }
}
