using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MagiGameServer.Codec;
using MagiGameServer.Contracts.Core;
using MagiGameServer.Contracts.Protocol;
using MagiGameServer.Contracts.Rules;
using MagiGameServer.Host.Session;
using MagiGameServer.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MagiGameServer.Host
{
    // Non-static so WebApplicationFactory<Program> in tests can use it as a
    // type argument; constructors never run, all members stay static.
    public class Program
    {
        private Program() { }
        public static void Main(string[] args) => CreateApp(args).Run();

        /// Factory so tests can construct the same pipeline through
        /// WebApplicationFactory without duplicating endpoint wiring.
        public static WebApplication CreateApp(string[] args, Action<GameModuleRegistry> registerModules = null)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddSingleton<GameModuleRegistry>(_ =>
            {
                var registry = new GameModuleRegistry();
                registerModules?.Invoke(registry);
                return registry;
            });
            builder.Services.AddSingleton<SessionRegistry>();
            // Align the minimal-API default JSON pipeline with the
            // codec's converters so HTTP bodies and WebSocket frames
            // agree on the wire shape of SessionId / SeatId / enums.
            builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o =>
                EnvelopeCodec.ConfigureOptions(o.SerializerOptions));

            var app = builder.Build();
            app.UseWebSockets();
            MapEndpoints(app);
            return app;
        }

        private static void MapEndpoints(WebApplication app)
        {
            app.MapGet("/healthz", (GameModuleRegistry modules) =>
            {
                var ids = modules.RegisteredGameIds();
                return Results.Ok(new { status = "ok", modules = ids });
            });

            app.MapPost("/session/open", async (HttpContext ctx, GameModuleRegistry modules, SessionRegistry registry, ILogger<SessionRegistry> logger) =>
            {
                OpenSessionRequest req;
                try
                {
                    req = await JsonSerializer.DeserializeAsync<OpenSessionRequest>(ctx.Request.Body, EnvelopeCodec.Options).ConfigureAwait(false);
                }
                catch (JsonException ex)
                {
                    return Results.BadRequest(new { code = "malformed_body", message = ex.Message });
                }
                if (req == null || string.IsNullOrEmpty(req.GameId))
                {
                    return Results.BadRequest(new { code = "missing_game_id", message = "gameId is required" });
                }
                if (!modules.TryGet(req.GameId, out var module))
                {
                    return Results.BadRequest(new { code = "unknown_game", message = $"No module registered for '{req.GameId}'" });
                }
                if (req.SeatCount < module.MinSeats || req.SeatCount > module.MaxSeats)
                {
                    return Results.BadRequest(new { code = "seat_count_out_of_range",
                        message = $"SeatCount {req.SeatCount} outside [{module.MinSeats},{module.MaxSeats}] for '{module.GameId}'" });
                }

                var config = new GameConfig
                {
                    Seed = req.Seed,
                    SeatCount = req.SeatCount,
                    Options = req.Options ?? new Dictionary<string, string>(),
                };
                object initialState;
                try
                {
                    initialState = module.CreateInitialState(config);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Module {GameId} failed to create initial state", module.GameId);
                    return Results.BadRequest(new { code = "initial_state_failed", message = ex.Message });
                }

                var id = registry.NewId();
                var session = new global::MagiGameServer.Core.Session(id, module, initialState, req.SeatCount);
                var runtimeLogger = ctx.RequestServices.GetRequiredService<ILogger<SessionRuntime>>();
                var runtime = new SessionRuntime(session, module, runtimeLogger);
                if (!registry.TryAdd(id, runtime))
                {
                    // ID collision is statistically impossible but guard anyway
                    await runtime.DisposeAsync().ConfigureAwait(false);
                    return Results.StatusCode(500);
                }

                logger.LogInformation("Opened session {Session} for game {GameId} seats={Seats}", id, module.GameId, req.SeatCount);
                return Results.Ok(new OpenSessionResponse
                {
                    Session = id,
                    GameId = module.GameId,
                    SeatCount = req.SeatCount,
                });
            });

            // Read-only snapshot endpoint for debug / admin / M5b driver tests.
            // No authentication — scope is localhost-only / trusted-network use.
            // ?seat=N projects for a specific seat; default 0 is equivalent to
            // canonical state for every perfect-info module today. Routed
            // through the runtime's dispatcher queue so the read never races
            // an in-flight Apply.
            app.MapGet("/session/{sessionId}/state", async (HttpContext ctx, string sessionId, int? seat, SessionRegistry registry) =>
            {
                if (!registry.TryGet(new SessionId(sessionId), out var runtime))
                {
                    return Results.NotFound(new { code = "unknown_session", message = $"No session {sessionId}" });
                }
                int seatValue = seat ?? 0;
                if (seatValue < 0 || seatValue >= runtime.SeatCount)
                {
                    return Results.BadRequest(new { code = "seat_out_of_range",
                        message = $"seat {seatValue} outside [0,{runtime.SeatCount})" });
                }
                var snap = await runtime.SnapshotAsync(new SeatId(seatValue), ctx.RequestAborted).ConfigureAwait(false);
                return Results.Ok(new
                {
                    session = sessionId,
                    gameId = runtime.GameId,
                    seatCount = runtime.SeatCount,
                    seat = seatValue,
                    revision = snap.Revision.Value,
                    stateHash = snap.StateHash,
                    state = snap.State,
                });
            });

            app.MapGet("/session/{sessionId}", async (HttpContext ctx, string sessionId, SessionRegistry registry, ILogger<SessionRuntime> logger) =>
            {
                if (!ctx.WebSockets.IsWebSocketRequest)
                {
                    ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }
                if (!registry.TryGet(new SessionId(sessionId), out var runtime))
                {
                    ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
                // Two attach shapes:
                //   * ?seat=N   — caller picks the seat explicitly (legacy /
                //                 LedgeTCG / reconnect). Out-of-range → 400,
                //                 duplicate → PolicyViolation.
                //   * no query  — server claims the lowest free seat. Full
                //                 session → PolicyViolation session_full.
                // The claim path is authoritative when both are absent or
                // the seat query doesn't parse, so a client that passes an
                // empty ?seat= falls through to claim rather than 400.
                int seatIndex = 0;
                bool hasExplicitSeat = ctx.Request.Query.TryGetValue("seat", out var seatRaw)
                                       && int.TryParse(seatRaw, out seatIndex);
                if (hasExplicitSeat && (seatIndex < 0 || seatIndex >= runtime.SeatCount))
                {
                    ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }
                // ?reattach=<token> routes through the token-matching path.
                // Without ?seat=N the server can't know which seat to
                // restore — reject at 400 rather than let it fall into
                // claim and silently hand out a new seat.
                bool hasReattach = ctx.Request.Query.TryGetValue("reattach", out var reattachRaw)
                                   && !string.IsNullOrEmpty(reattachRaw.ToString());
                if (hasReattach && !hasExplicitSeat)
                {
                    ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                var socket = await ctx.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
                SeatId seat;
                if (hasReattach)
                {
                    seat = new SeatId(seatIndex);
                    var outcome = await runtime.TryReattachAsync(seat, reattachRaw.ToString(), socket, ctx.RequestAborted).ConfigureAwait(false);
                    if (outcome != SessionRuntime.ReattachOutcome.Success)
                    {
                        var reason = outcome switch
                        {
                            SessionRuntime.ReattachOutcome.TokenMismatch => "token_mismatch",
                            SessionRuntime.ReattachOutcome.SeatAlreadyAttached => "seat_already_attached",
                            _ => "reattach_failed",
                        };
                        logger.LogInformation("Rejected reattach on session {Session} seat {Seat}: {Reason}", runtime.Id, seat, reason);
                        await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, reason, CancellationToken.None).ConfigureAwait(false);
                        return;
                    }
                }
                else if (hasExplicitSeat)
                {
                    seat = new SeatId(seatIndex);
                    var attached = await runtime.TryAttachAsync(seat, socket, ctx.RequestAborted).ConfigureAwait(false);
                    if (!attached)
                    {
                        logger.LogInformation("Rejected duplicate attach on session {Session} seat {Seat}", runtime.Id, seat);
                        await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "seat_already_attached", CancellationToken.None).ConfigureAwait(false);
                        return;
                    }
                }
                else
                {
                    var claimed = await runtime.TryClaimAndAttachAsync(socket, ctx.RequestAborted).ConfigureAwait(false);
                    if (claimed == null)
                    {
                        logger.LogInformation("Rejected claim on session {Session} — session_full", runtime.Id);
                        await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "session_full", CancellationToken.None).ConfigureAwait(false);
                        return;
                    }
                    seat = claimed.Value;
                }

                logger.LogInformation("Socket accepted for session {Session} seat {Seat}", runtime.Id, seat);
                var sendTask = Task.Run(() => runtime.RunSendLoopAsync(seat, ctx.RequestAborted));
                try
                {
                    await RunReceiveLoopAsync(runtime, seat, socket, ctx.RequestAborted).ConfigureAwait(false);
                }
                finally
                {
                    await runtime.DetachAsync(seat, CancellationToken.None).ConfigureAwait(false);
                    try { await sendTask.ConfigureAwait(false); } catch { }
                    if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                    {
                        try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).ConfigureAwait(false); } catch { }
                    }
                }
            });
        }

        private static async Task RunReceiveLoopAsync(SessionRuntime runtime, SeatId seat, WebSocket socket, CancellationToken ct)
        {
            var buffer = new byte[8192];
            var accumulated = new List<byte>();
            while (socket.State == WebSocketState.Open)
            {
                accumulated.Clear();
                WebSocketReceiveResult result;
                do
                {
                    try
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (WebSocketException) { return; }
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    accumulated.AddRange(new ArraySegment<byte>(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                if (accumulated.Count == 0) continue;
                var bytes = accumulated.ToArray();
                JsonDocument doc;
                try
                {
                    doc = JsonDocument.Parse(bytes);
                }
                catch (JsonException ex)
                {
                    // Malformed bytes never reach the dispatcher, but the
                    // contracts say OnError is the client's cue to retire
                    // a matching optimistic entry — we emit one here
                    // (with AckedSeq=0 since we couldn't peek a seq out
                    // of unparseable bytes) rather than silently dropping
                    // the frame.
                    runtime.SendProtocolError(seat, "malformed_frame", ex.Message, default);
                    continue;
                }
                await runtime.SubmitAsync(seat, doc, ct).ConfigureAwait(false);
            }
        }
    }
}
