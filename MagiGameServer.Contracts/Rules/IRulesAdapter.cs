using MagiGameServer.Contracts.Core;

namespace MagiGameServer.Contracts.Rules
{
    /// Per-game rules bridge. The server stays game-agnostic: it holds a session,
    /// a canonical state, and an IRulesAdapter. Each game (LedgeBoardGame,
    /// LedgeTCG, LedgeRPG, Inkling) implements this once and registers via
    /// IGameModule. The generic form pins the state/action types; the non-generic
    /// shape exists so the server's session registry can hold heterogeneous
    /// modules without the whole server becoming generic-viral.
    public interface IRulesAdapter
    {
        /// Deterministically advances state. The returned new state must be
        /// produced only from (state, action) — no wall clock, no unseeded PRNG,
        /// no ordered-dictionary-from-unordered-hash iteration. Any randomness
        /// routes through a seeded PRNG seeded per session (M3 scope). Called
        /// server-side with canonical state; clients running optimistic
        /// prediction call the same function on their projected state for their
        /// own submissions (projected state must be self-contained for that seat's
        /// own moves).
        ApplyOutcome Apply(object state, object action, out object newState);

        /// Stable, collision-resistant hash over the passed state value. Equal
        /// state values yield equal hashes regardless of whether the input is
        /// canonical or already-projected — clients hash their projected
        /// post-apply state to compute PredictedStateHash, and the server
        /// computes the matching value via GetStateHashForSeat below. Long
        /// (not int) per the determinism mandate — birthday collisions on int at
        /// session length are realistic, on long are not.
        long GetStateHash(object state);

        /// Hash of state as `seat` is allowed to see it. Equivalent to
        /// GetStateHash(ProjectStateFor(state, seat)); the server calls this
        /// against canonical state + the submitting seat to compare against the
        /// client's PredictedStateHash. Hidden-info games (LedgeTCG, LedgeRPG)
        /// require this path because clients never see canonical state and
        /// cannot compute a canonical hash directly — any contract that only
        /// exposed GetStateHash(canonical) would silently break for those games.
        long GetStateHashForSeat(object state, SeatId seat);

        /// Projects canonical state onto the view that `seat` is allowed to see.
        /// LedgeBoardGame is a perfect-info game so this is the identity function;
        /// LedgeTCG hides opponent hand/deck, LedgeRPG hides unexplored tiles and
        /// opponent positions. Server calls this before sending state echoes so
        /// canonical state never leaves the trust boundary.
        object ProjectStateFor(object state, SeatId seat);
    }

    /// Strongly-typed counterpart. Game modules typically inherit from the
    /// RulesAdapterBase below rather than implementing this interface directly,
    /// because the non-generic object-shape methods are not auto-delegated — an
    /// implementer of this interface alone would still have to hand-write casts
    /// between TState/object and TAction/object. Keeping both shapes means the
    /// session registry can be `Dictionary&lt;string, IGameModule&gt;` (one line
    /// of infra) while each adapter stays type-safe internally.
    public interface IRulesAdapter<TState, TAction> : IRulesAdapter
        where TState : class
        where TAction : class
    {
        ApplyOutcome Apply(TState state, TAction action, out TState newState);
        long GetStateHash(TState state);
        long GetStateHashForSeat(TState state, SeatId seat);
        TState ProjectStateFor(TState state, SeatId seat);
    }

    /// Convenience base class that bridges the object-shape (non-generic) and
    /// typed (generic) surfaces so game modules only implement the three
    /// typed primitives. GetStateHashForSeat has a virtual default of
    /// GetStateHash(ProjectStateFor(state, seat)) — override only if you can
    /// hash the projection more cheaply than materializing it.
    public abstract class RulesAdapterBase<TState, TAction> : IRulesAdapter<TState, TAction>
        where TState : class
        where TAction : class
    {
        public abstract ApplyOutcome Apply(TState state, TAction action, out TState newState);
        public abstract long GetStateHash(TState state);
        public abstract TState ProjectStateFor(TState state, SeatId seat);

        public virtual long GetStateHashForSeat(TState state, SeatId seat)
            => GetStateHash(ProjectStateFor(state, seat));

        ApplyOutcome IRulesAdapter.Apply(object state, object action, out object newState)
        {
            var outcome = Apply((TState)state, (TAction)action, out TState typed);
            newState = typed;
            return outcome;
        }

        long IRulesAdapter.GetStateHash(object state) => GetStateHash((TState)state);
        long IRulesAdapter.GetStateHashForSeat(object state, SeatId seat) => GetStateHashForSeat((TState)state, seat);
        object IRulesAdapter.ProjectStateFor(object state, SeatId seat) => ProjectStateFor((TState)state, seat);
    }
}
