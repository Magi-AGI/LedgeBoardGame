using System;
using System.Collections.Generic;
using System.Linq;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Models.Network;
using Magi.LedgeBoardGame.Models.Spec;
using MagiGameServer.Contracts.Rules;

namespace Magi.LedgeBoardGame.ServerModule
{
    /// IGameModule entry point for LedgeBoardGame. Registered once at server
    /// startup under GameId "ledge-board". Session creation routes here to
    /// build the initial SpecGameState; per-seat action dispatch routes
    /// through the shared LedgeRulesAdapter (singleton, thread-safe, no
    /// per-session state).
    ///
    /// Seat counts 2–4 mirror the domain: LedgeBoardGame is designed for
    /// 2-4 players sharing a central board. Single-player is not a valid
    /// mode for this game. A UI that wants to let one human play against
    /// themselves for testing should open a session with SeatCount=2 and
    /// attach both seats from the same client — that's a session-shape
    /// concern, not a module-shape concern.
    public sealed class LedgeGameModule : IGameModule
    {
        public string GameId => "ledge-board";
        public string DisplayName => "Ledge Board Game";
        public int MinSeats => 2;
        public int MaxSeats => 4;
        public IRulesAdapter Rules { get; } = new LedgeRulesAdapter();
        public Type ActionType => typeof(LedgeAction);
        public Type StateType => typeof(SpecGameState);

        public object CreateInitialState(GameConfig config)
        {
            int seatCount = config?.SeatCount > 0 ? config.SeatCount : MinSeats;
            if (seatCount < MinSeats || seatCount > MaxSeats)
                throw new ArgumentException($"LedgeBoardGame requires {MinSeats}-{MaxSeats} seats (got {seatCount}).");

            // Build players via GameState's constructor, which also builds the
            // canonical boards + cross-board ledge edges. ToSpecState then
            // projects down to the wire shape the server will persist and
            // echo. Seed is accepted here but unused at start-of-session;
            // LedgeBoardGame's initial state is fully deterministic from the
            // seat count. Future variants (scenario seeds, starting piece
            // placement) would consume Seed/Options at this point.
            var players = BuildPlayers(seatCount);
            var gs = new GameState(players);
            return gs.ToSpecState();
        }

        private static List<Player> BuildPlayers(int seatCount)
        {
            // SeatId is 0-indexed in the session layer, PlayerId is 1-indexed
            // in LedgeBoardGame's domain. The mapping stays 1:1: seat N
            // controls player (N+1). BoardId aligns with PlayerId.
            var players = new List<Player>(seatCount);
            for (int i = 0; i < seatCount; i++)
            {
                int playerId = i + 1;
                players.Add(new Player
                {
                    Id = playerId,
                    Name = "Player " + playerId,
                    BoardId = playerId,
                    IsHuman = true,
                    IsEliminated = false,
                });
            }
            return players;
        }

        public static GameConfig DefaultConfig(int seatCount = 2, IReadOnlyDictionary<string, string> options = null)
            => new GameConfig
            {
                Seed = 0,
                SeatCount = seatCount,
                Options = options ?? new Dictionary<string, string>(),
            };
    }
}
