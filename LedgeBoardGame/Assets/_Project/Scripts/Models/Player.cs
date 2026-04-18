using System;
using System.Collections.Generic;

namespace Magi.LedgeBoardGame.Models
{
    [Serializable]
    public class Player
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int BoardId { get; set; }
        public bool IsEliminated { get; set; }
        public bool IsHuman { get; set; }

        public Player()
        {
            IsHuman = true;
        }

        public Player(int id, string name, int boardId)
        {
            Id = id;
            Name = name;
            BoardId = boardId;
            IsHuman = true;
            IsEliminated = false;
        }

        /// Canonical per-seat player construction shared by both the local
        /// GameController path and the server-side LedgeGameModule. Keeping
        /// the construction in one place is a hard correctness requirement
        /// for shadow-mode hash comparison: any divergence in Name or
        /// BoardId would land in the initial SpecGameState JSON, which
        /// would land in the hash, and every shadow submission would log
        /// a divergence from the very first action. BoardId = seat index
        /// (0-based) matches the historical construction
        /// `new Player(id, "PlayerN", boardIndex)` the scene has been
        /// running with.
        public static List<Player> BuildDefaultRoster(int seatCount)
        {
            var players = new List<Player>(seatCount);
            for (int i = 0; i < seatCount; i++)
            {
                int playerId = i + 1;
                players.Add(new Player(playerId, "Player" + playerId, i));
            }
            return players;
        }

        public override string ToString()
        {
            var status = IsEliminated ? " (Eliminated)" : "";
            return $"Player {Id}: {Name}{status}";
        }
    }
}