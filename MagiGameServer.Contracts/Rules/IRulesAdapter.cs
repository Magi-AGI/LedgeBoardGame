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
        /// Deterministically advances the canonical state. The returned new state
        /// must be produced only from (state, action) — no wall clock, no unseeded
        /// PRNG, no ordered-dictionary-from-unordered-hash iteration. Any randomness
        /// routes through ISeededRandom seeded per session (M3 scope).
        ApplyOutcome Apply(object state, object action, out object newState);

        /// Stable, collision-resistant hash over canonical state. Used for desync
        /// detection: client sends its predicted post-apply hash with every action,
        /// and the server compares against its own hash of the applied state. Long
        /// (not int) per the determinism mandate — birthday collisions on int at
        /// session length are realistic, on long are not.
        long GetStateHash(object state);

        /// Projects canonical state onto the view that `seat` is allowed to see.
        /// LedgeBoardGame is a perfect-info game so this is the identity function;
        /// LedgeTCG hides opponent hand/deck, LedgeRPG hides unexplored tiles and
        /// opponent positions. Server calls this before sending state echoes so
        /// canonical state never leaves the trust boundary.
        object ProjectStateFor(object state, SeatId seat);
    }

    /// Strongly-typed counterpart. Game modules should implement this form; the
    /// non-generic interface delegates through. Keeping both shapes means the
    /// session registry can be `Dictionary<string, IGameModule>` (one line of
    /// infra) while each adapter stays type-safe internally.
    public interface IRulesAdapter<TState, TAction> : IRulesAdapter
        where TState : class
        where TAction : class
    {
        ApplyOutcome Apply(TState state, TAction action, out TState newState);
        long GetStateHash(TState state);
        TState ProjectStateFor(TState state, SeatId seat);
    }
}
