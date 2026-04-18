using System;
using System.Collections.Concurrent;
using MagiGameServer.Contracts.Core;

namespace MagiGameServer.Host.Session
{
    /// In-process map from SessionId to its per-session runtime. Thread-safe
    /// for concurrent `Open` and `TryGet` calls — attachments to an
    /// existing runtime happen inside SessionRuntime itself, which
    /// serializes them through its dispatcher channel. M4b has no
    /// persistence and no horizontal sharding; both are out of scope.
    public sealed class SessionRegistry
    {
        private readonly ConcurrentDictionary<SessionId, SessionRuntime> _sessions = new ConcurrentDictionary<SessionId, SessionRuntime>();

        public bool TryAdd(SessionId id, SessionRuntime runtime) => _sessions.TryAdd(id, runtime);

        public bool TryGet(SessionId id, out SessionRuntime runtime) => _sessions.TryGetValue(id, out runtime);

        public SessionId NewId() => new SessionId(Guid.NewGuid().ToString("n"));
    }
}
