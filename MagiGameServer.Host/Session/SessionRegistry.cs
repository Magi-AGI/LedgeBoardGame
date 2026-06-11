using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
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

        // Crockford's base32, sans look-alikes (I, L, O, U). 32 symbols keeps
        // the code shareable by voice without "was that a 1 or an l?"
        // ambiguity. Lowercase-only on the wire so the session code is
        // case-insensitive at the match layer (clients canonicalise before
        // comparing).
        private const string Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";

        public bool TryAdd(SessionId id, SessionRuntime runtime) => _sessions.TryAdd(id, runtime);

        public bool TryGet(SessionId id, out SessionRuntime runtime) => _sessions.TryGetValue(id, out runtime);

        /// Graceful shutdown pass for SIGINT/SIGTERM. Sends a GoingAway
        /// close to every attached seat in parallel across all live
        /// sessions, then disposes each runtime. Bounded by `drainTimeout`
        /// so a stuck socket can't hang the host. Fresh calls to
        /// `TryAdd`/`TryGet` are fine during shutdown (ASP.NET stops new
        /// connections at the upgrade layer); this method is the
        /// dispatcher-side half of the close handshake.
        public async Task ShutdownAllAsync(TimeSpan drainTimeout, CancellationToken ct)
        {
            var closeTasks = new List<Task>();
            foreach (var runtime in _sessions.Values)
            {
                closeTasks.Add(runtime.CloseAllSocketsAsync("server_shutdown", drainTimeout, ct));
            }
            if (closeTasks.Count > 0)
            {
                try { await Task.WhenAll(closeTasks).ConfigureAwait(false); }
                catch { /* per-socket errors already swallowed */ }
            }
            foreach (var runtime in _sessions.Values)
            {
                try { await runtime.DisposeAsync().ConfigureAwait(false); }
                catch { /* runtime dispose swallows dispatcher crashes already */ }
            }
            _sessions.Clear();
        }

        /// Allocates a short, URL-safe, human-shareable session id. 8
        /// characters of the 32-symbol alphabet give 32^8 ≈ 10^12 codes —
        /// plenty for concurrent lobbies against a single host while still
        /// being typable over voice. Re-rolls on the extremely unlikely
        /// event of a collision with a live session.
        public SessionId NewId()
        {
            const int length = 8;
            Span<byte> buf = stackalloc byte[length];
            for (int attempt = 0; attempt < 8; attempt++)
            {
                RandomNumberGenerator.Fill(buf);
                var chars = new char[length];
                for (int i = 0; i < length; i++)
                {
                    chars[i] = Alphabet[buf[i] & 0x1F];
                }
                var id = new SessionId(new string(chars));
                if (!_sessions.ContainsKey(id)) return id;
            }
            // Vanishingly unlikely collision loop wore out — fall through to
            // the legacy 32-hex id so the host never hard-fails at session
            // open. A log at the caller site would be nice but SessionRegistry
            // intentionally has no logger injected.
            return new SessionId(Guid.NewGuid().ToString("n"));
        }
    }
}
