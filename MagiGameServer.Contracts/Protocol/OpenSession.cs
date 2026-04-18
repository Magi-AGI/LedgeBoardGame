using System.Collections.Generic;
using MagiGameServer.Contracts.Core;

namespace MagiGameServer.Contracts.Protocol
{
    /// Client-to-server (HTTP): create a new session and return its SessionId.
    /// Kept off the WebSocket attach path so the WS URL carries only session +
    /// seat routing concerns. Create-only parameters (gameId, seatCount, seed,
    /// options) live here so there's one canonical place a module gets
    /// instantiated, and so the attach path has nothing to improvise.
    public sealed record OpenSessionRequest
    {
        public string GameId { get; init; }
        public int SeatCount { get; init; }
        public long Seed { get; init; }
        public IReadOnlyDictionary<string, string> Options { get; init; }
    }

    public sealed record OpenSessionResponse
    {
        public SessionId Session { get; init; }
        public string GameId { get; init; }
        public int SeatCount { get; init; }
    }
}
