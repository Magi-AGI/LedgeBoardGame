using System;
using System.Collections.Generic;
using MagiGameServer.Contracts.Rules;

namespace MagiGameServer.Modules
{
    /// Thread-safe registry of game modules keyed by stable gameId. The
    /// server's startup wires in every compiled-in module with one
    /// Register(new FooGameModule()) call; at session-open time the host
    /// looks up the module by gameId and hands it to the Session ctor.
    ///
    /// Instance-scoped rather than static-singleton so tests can spin up
    /// independent registries without cross-contaminating global state.
    /// A process-wide default can still be built on top by whoever owns
    /// the process, but the library doesn't impose one.
    public sealed class GameModuleRegistry
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, IGameModule> _byId =
            new Dictionary<string, IGameModule>(StringComparer.Ordinal);

        /// Registers a module. Throws if the module's GameId collides with
        /// an already-registered module — silent replace would mask
        /// double-registration bugs (e.g. two DLLs each wiring the same
        /// module at startup) that are otherwise invisible until a client
        /// gets an unexpected game shape.
        public void Register(IGameModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (string.IsNullOrEmpty(module.GameId))
                throw new ArgumentException("IGameModule.GameId must be non-empty", nameof(module));
            if (module.ActionType == null)
                throw new ArgumentException(
                    $"Module '{module.GameId}' returned null ActionType — the codec cannot resolve wire payloads without it",
                    nameof(module));
            if (module.StateType == null)
                throw new ArgumentException(
                    $"Module '{module.GameId}' returned null StateType",
                    nameof(module));

            lock (_gate)
            {
                if (_byId.ContainsKey(module.GameId))
                    throw new InvalidOperationException(
                        $"Module '{module.GameId}' is already registered; double-registration is rejected to surface startup wiring bugs");
                _byId.Add(module.GameId, module);
            }
        }

        /// Resolves a module by gameId. Throws KeyNotFoundException with a
        /// list of registered ids when the lookup fails so a misconfigured
        /// client request produces a diagnosable error rather than a
        /// generic "not found".
        public IGameModule Get(string gameId)
        {
            if (gameId == null) throw new ArgumentNullException(nameof(gameId));
            lock (_gate)
            {
                if (_byId.TryGetValue(gameId, out var module)) return module;
                throw new KeyNotFoundException(
                    $"No game module registered for gameId '{gameId}'. Registered: [{string.Join(", ", _byId.Keys)}]");
            }
        }

        /// Non-throwing lookup for callers that want to handle "unknown
        /// game" as a protocol error rather than an exception — the host
        /// responds to a client's join request with an ErrorEnvelope
        /// instead of crashing the connection.
        public bool TryGet(string gameId, out IGameModule module)
        {
            if (gameId == null) { module = null; return false; }
            lock (_gate)
            {
                return _byId.TryGetValue(gameId, out module);
            }
        }

        public int Count
        {
            get { lock (_gate) return _byId.Count; }
        }

        /// Snapshot of registered gameIds. Intended for diagnostics /
        /// admin surfaces, not for dispatch — host code should use Get /
        /// TryGet against the specific id it's routing.
        public IReadOnlyCollection<string> RegisteredGameIds()
        {
            lock (_gate)
            {
                return new List<string>(_byId.Keys);
            }
        }
    }
}
