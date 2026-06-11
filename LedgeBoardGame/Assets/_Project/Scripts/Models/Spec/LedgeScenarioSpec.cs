using System.Collections.Generic;

namespace Magi.LedgeBoardGame.Models.Spec
{
    public class LedgeScenario
    {
        public string Name { get; set; }
        public LedgeScenarioInitial Initial { get; set; }
        public List<LedgeScenarioMove> Moves { get; set; }
        public LedgeScenarioExpected Expected { get; set; }
    }

    public class LedgeScenarioInitial
    {
        public List<SpecPlayer> Players { get; set; }
        public SpecCtx Ctx { get; set; }
    }

    public class LedgeScenarioMove
    {
        public string Move { get; set; }
        public LedgeScenarioMoveArgs Args { get; set; }
    }

    public class LedgeScenarioMoveArgs
    {
        public int BoardId { get; set; }
        public int? SpaceId { get; set; }
        public int? FromBoardId { get; set; }
        public int? FromSpaceId { get; set; }
        public int? ToBoardId { get; set; }
        public int? ToSpaceId { get; set; }
        public string Tone { get; set; }
    }

    public class LedgeScenarioExpected
    {
        public string Phase { get; set; }
        public string CurrentPlayer { get; set; }
        public int? TurnNumber { get; set; }
    }
}
