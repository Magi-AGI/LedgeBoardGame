using System;
using System.Collections.Generic;
using MagiGameServer.Contracts.Core;
using MagiGameServer.Contracts.Rules;

namespace MagiGameServer.Core
{
    /// Server-side host: owns the game-module registry and the live-session
    /// map. Callers register game modules once at startup (static
    /// registration per the M4 scope) and then create/resolve sessions
    /// through this type. Intentionally small — transport, auth, and
    /// matchmaking are higher layers that wrap SessionHost, not concerns
    /// baked in here.
    ///
    /// Thread-safety: not synchronised internally. The server binary (M4)
    /// is responsible for serialising Apply/Takeback per session (actor
    /// or lock-per-session) so a session can't see overlapping writes.
    /// Registration is expected to happen before any sessions are created,
    /// so the module map is effectively immutable at runtime.
    public sealed class SessionHost
    {
        private readonly Dictionary<string, IGameModule> _modules =
            new Dictionary<string, IGameModule>(StringComparer.Ordinal);
        private readonly Dictionary<SessionId, ISession> _sessions =
            new Dictionary<SessionId, ISession>();
        private readonly Func<SessionId> _sessionIdFactory;

        public SessionHost(Func<SessionId> sessionIdFactory = null)
        {
            // Default: opaque GUID string. Tests can inject a deterministic
            // counter without having to subclass anything.
            _sessionIdFactory = sessionIdFactory
                ?? (() => new SessionId(Guid.NewGuid().ToString("N")));
        }

        public IReadOnlyCollection<IGameModule> Modules => _modules.Values;
        public IReadOnlyCollection<ISession> Sessions => _sessions.Values;

        public void RegisterModule(IGameModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (string.IsNullOrEmpty(module.GameId))
                throw new ArgumentException("Module GameId must be non-empty", nameof(module));
            if (_modules.ContainsKey(module.GameId))
                throw new InvalidOperationException(
                    $"Module '{module.GameId}' is already registered");
            _modules.Add(module.GameId, module);
        }

        public bool TryGetModule(string gameId, out IGameModule module)
            => _modules.TryGetValue(gameId, out module);

        public ISession CreateSession(string gameId, GameConfig config)
        {
            if (!_modules.TryGetValue(gameId, out var module))
                throw new KeyNotFoundException($"Game module '{gameId}' is not registered");
            if (config == null) throw new ArgumentNullException(nameof(config));

            object initialState = module.CreateInitialState(config);
            var sessionId = _sessionIdFactory();
            if (_sessions.ContainsKey(sessionId))
                throw new InvalidOperationException(
                    $"SessionId factory produced duplicate id {sessionId}");

            var session = new Session(sessionId, module, initialState, config.SeatCount);
            _sessions.Add(sessionId, session);
            return session;
        }

        public ISession GetSession(SessionId id)
            => _sessions.TryGetValue(id, out var session) ? session : null;

        public bool CloseSession(SessionId id) => _sessions.Remove(id);
    }
}
