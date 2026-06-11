using System;
using System.Collections.Generic;
using MagiGameServer.Contracts.Core;

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

        /// Concrete action payload type for this module (e.g. CounterAction,
        /// LedgeAction). The wire codec uses this to deserialize incoming
        /// ActionEnvelope payloads into the right shape: the server
        /// receives JSON, resolves the session's module, and asks
        /// JsonSerializer.Deserialize(json, typeof(ActionEnvelope&lt;&gt;).MakeGenericType(module.ActionType)).
        /// Non-generic IRulesAdapter deliberately keeps this off its own
        /// surface — rules code doesn't care about wire typing, but the
        /// module is the natural single source of truth for what "an
        /// action for this game" concretely is.
        Type ActionType { get; }

        /// Concrete canonical state type for this module. The server
        /// serializes StateEcho.State using the runtime type of the
        /// instance (which System.Text.Json handles by default), so this
        /// is primarily advisory on the server side. Clients use it to
        /// parameterise SessionDispatcher&lt;TState, TAction&gt; — pairing
        /// a gameId with a module tells the client which concrete TState
        /// to expect from echoes.
        Type StateType { get; }

        /// Applies a seat presence change (connect / disconnect) to `state`.
        /// Called by the server transport when a WebSocket attaches or
        /// detaches — outside the normal action pipeline, so the module is
        /// the single point of truth for how presence is represented in its
        /// state type (LedgeBoardGame flips Players[seat].IsConnected; a
        /// sibling game might maintain a separate presence set).
        ///
        /// Must not mutate the input `state`. When the change is a no-op
        /// (presence already matches, or this game doesn't model presence),
        /// return the input reference and the host will skip the revision
        /// bump + broadcast. Otherwise return a fresh state with the flip
        /// applied — the host snapshots this into the session's log so
        /// takeback rewinds after a presence change behave sensibly.
        object SetSeatPresence(object state, SeatId seat, bool isConnected);
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
