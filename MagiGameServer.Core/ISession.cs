using MagiGameServer.Contracts.Core;
using MagiGameServer.Contracts.Protocol;

namespace MagiGameServer.Core
{
    /// Runtime handle for one live match. Owns the canonical state, advances
    /// the authoritative ServerSeq revision per accepted action, and produces
    /// per-seat StateEchos for every seat on every accepted action (so
    /// transport can broadcast without re-invoking rules). Kept non-generic
    /// so the host's session map is `Dictionary&lt;SessionId, ISession&gt;` —
    /// typed state threads through IRulesAdapter.ProjectStateFor / Apply,
    /// which already handle the object/TState bridge via RulesAdapterBase.
    ///
    /// Thread-safety: implementations are not required to be internally
    /// thread-safe. The host (or a per-session actor/queue in later
    /// milestones) must serialize Apply/Takeback calls per session so a
    /// second action can't read stale state while the first is mid-apply.
    public interface ISession
    {
        SessionId Id { get; }
        string GameId { get; }
        int SeatCount { get; }
        ServerSeq CurrentRevision { get; }

        /// Applies an envelope through the rules adapter and returns the
        /// per-seat echo set plus the final outcome. When rules say Applied
        /// but the per-seat projected hash does not match
        /// envelope.PredictedStateHash, the outcome is downgraded to
        /// Desynced before echoes are built — the new state is still kept
        /// canonical, and clients must replay from the echoed state instead
        /// of reconciling forward on top of a stale prediction.
        SessionApplyResult Apply(ActionEnvelope<object> envelope);

        /// Rewinds N server-committed actions via snapshot restore. Policy
        /// (auto-grant vs opponent-consent vs flat deny) is a game-module
        /// concern that the host will plumb through later; this interface
        /// returns the module's chosen outcome plus — when Granted — a
        /// fresh per-seat echo set carrying the post-rewind state.
        SessionTakebackResult Takeback(TakebackRequest request);

        /// Flips a seat's presence in the canonical state. Delegates to
        /// IGameModule.SetSeatPresence; when the module returns the input
        /// unchanged (no-op flip or game doesn't model presence), Outcome
        /// is Rejected and Revision is unchanged — the transport should
        /// skip the broadcast. On a real change, state is snapshotted into
        /// the log and Revision advances by one, so takeback crossing a
        /// presence transition rewinds the flip along with the actions.
        /// SubmittingSeat in the echoes is the seat whose presence changed
        /// (there is no real submitter); AckedSeq is default.
        SessionApplyResult SetSeatPresence(SeatId seat, bool isConnected);
    }
}
