using System;

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

        public override string ToString()
        {
            var status = IsEliminated ? " (Eliminated)" : "";
            return $"Player {Id}: {Name}{status}";
        }
    }
}