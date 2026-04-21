using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MagiGameServer.Host.Session
{
    /// Drains live sessions on SIGINT/SIGTERM. StopAsync fires once the
    /// host is shutting down — we send a WebSocket GoingAway to every
    /// attached seat, bounded by a short drain window, then dispose each
    /// per-session runtime so the dispatcher loop terminates before the
    /// Kestrel server shuts down its listening sockets.
    ///
    /// Separate hosted service (rather than inline Lifetime.Register) so
    /// StopAsync gets a real async path — Lifetime.Register callbacks are
    /// synchronous and would force a blocking Wait on the drain.
    public sealed class SessionShutdownHostedService : IHostedService
    {
        private readonly SessionRegistry _registry;
        private readonly ILogger<SessionShutdownHostedService> _logger;
        // Short: the SIGTERM path is supposed to be snappy. Any seat that
        // can't acknowledge a Close inside this window is already gone
        // and the runtime's DisposeAsync will tear the channel down.
        private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(3);

        public SessionShutdownHostedService(SessionRegistry registry, ILogger<SessionShutdownHostedService> logger)
        {
            _registry = registry;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Draining live sessions for shutdown (timeout {Timeout}s)", (int)DrainTimeout.TotalSeconds);
            try
            {
                await _registry.ShutdownAllAsync(DrainTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Session drain encountered errors; host will continue shutting down");
            }
        }
    }
}
