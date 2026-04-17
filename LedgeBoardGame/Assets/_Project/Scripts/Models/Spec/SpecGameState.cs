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

        // Mid-turn action log. Runtime GameState enforces per-turn limits
        // (one Light + one Dark placement, N moves) off these lists and drives
        // the local undo stack from them. Serializing is required so a state
        // delivered mid-turn (rollback, reconcile, save/load) lands with the
        // same remaining budget the original turn had.
        public List<Move> CurrentTurnMoves { get; set; }
        public List<PlacementMove> CurrentTurnPlacements { get; set; }
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

