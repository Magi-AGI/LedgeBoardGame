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

        // Runtime config snapshot. Travels with state so server-mode Apply
        // can reconstruct the same GameRules the local path would use. Null
        // means "defaults" (matches the legacy no-spec local path).
        public SpecLedgeRuntimeConfig Config { get; set; }
    }

    // Wire-friendly mirror of LedgeRuntimeConfig. The runtime type is
    // ctor-initialized with get-only properties; this DTO exists so the
    // config round-trips cleanly through any serializer without depending
    // on Newtonsoft's ctor-binding heuristics.
    public class SpecLedgeRuntimeConfig
    {
        public int MinPlayers { get; set; }
        public int MaxPlayers { get; set; }
        public int PlacementMinMoves { get; set; }
        public int PlacementMaxMoves { get; set; }
        public int MovementMinMoves { get; set; }
        public int MovementMaxMoves { get; set; }
    }

    public class SpecPlayer
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int BoardId { get; set; }
        public bool IsEliminated { get; set; }
        // JIP/LIP presence — travels with SpecGameState so a mid-session
        // snapshot delivered to a reconnecting client carries the full
        // seat-occupancy picture. Default true keeps existing persisted
        // states (and tests) that predate this field behaving as if all
        // seats are present; network sessions override explicitly.
        public bool IsConnected { get; set; } = true;
    }

    public class SpecLedgeData
    {
        public List<BoardState> Boards { get; set; }
        public int? WinnerId { get; set; }
        public bool GameOver { get; set; }
    }
}

