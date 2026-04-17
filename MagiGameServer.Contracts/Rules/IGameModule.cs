using System.Collections.Generic;

namespace MagiGameServer.Contracts.Rules
{
    /// Registers a game with the server. The server's module registry maps
    /// `GameId` → module, picks a module per session creation, and delegates
    /// rules/state work to it. Static registration (M4 scope) means each module
    /// is compiled into the server binary and registered at startup via a
    /// single call site; dynamic plugin loading (post-M6) would register
    /// modules discovered at runtime.
    public interface IGameModule
    {
        /// Stable, human-readable game identifier. Used by clients to request
        /// a session of a specific game ("ledge-board", "ledge-tcg",
        /// "ledge-rpg", "inkling"). Must be unique across all registered
        /// modules in a server instance.
        string GameId { get; }

        /// Display metadata — surfaced by matchmaking/lobby UIs. Keep
        /// game-shape-neutral; transport-specific UI lives in the client.
        string DisplayName { get; }
        int MinSeats { get; }
        int MaxSeats { get; }

        /// Rules adapter for this game. Returned singleton-style — the adapter
        /// must be thread-safe and stateless; per-session state lives in the
        /// session's canonical state object, not in the adapter.
        IRulesAdapter Rules { get; }

        /// Builds the starting state for a new session. `config` carries
        /// per-session options (seed, player count, variant flags). The returned
        /// object is the first canonical state the server will persist and hash.
        object CreateInitialState(GameConfig config);
    }

    /// Per-session configuration passed to CreateInitialState. Kept as a
    /// dictionary-backed bag deliberately: games vary in what they need
    /// (seed, player count, scenario id, rule variants) and pinning a schema
    /// here would force contract churn every time a game adds a flag. The
    /// schema for each game's config lives in that game's module, not here.
    public sealed class GameConfig
    {
        public long Seed { get; init; }
        public int SeatCount { get; init; }
        public IReadOnlyDictionary<string, string> Options { get; init; }
    }
}
