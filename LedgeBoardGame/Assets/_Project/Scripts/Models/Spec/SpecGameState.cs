using System.Collections.Generic;

namespace Magi.LedgeBoardGame.Models.Spec
{
    public class SpecCtx
    {
        public string CurrentPlayer { get; set; }
        public string Phase { get; set; }
        public int TurnNumber { get; set; }
    }

    public class SpecGameState
    {
        public List<SpecPlayer> Players { get; set; }
        public SpecCtx Ctx { get; set; }
        public SpecLedgeData Data { get; set; }
    }

    public class SpecPlayer
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int BoardId { get; set; }
        public bool IsEliminated { get; set; }
    }

    public class SpecLedgeData
    {
        public List<BoardState> Boards { get; set; }
        public int? WinnerId { get; set; }
        public bool GameOver { get; set; }
    }
}

