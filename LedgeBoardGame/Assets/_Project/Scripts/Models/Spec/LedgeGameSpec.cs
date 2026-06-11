using System.Collections.Generic;

namespace Magi.LedgeBoardGame.Models.Spec
{
    public class LedgeGameSpec
    {
        public string SchemaVersion { get; set; }
        public string BasedOn { get; set; }
        public string SpecId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        public LedgeConfig Config { get; set; }
        public List<LedgePhaseSpec> Phases { get; set; }
        public Dictionary<string, LedgeMoveSpec> Moves { get; set; }
        public LedgeEndIfSpec EndIf { get; set; }
    }

    public class LedgeConfig
    {
        public int MinPlayers { get; set; }
        public int MaxPlayers { get; set; }
        public LedgeBoardConfig Board { get; set; }
    }

    public class LedgeBoardConfig
    {
        public int Spaces { get; set; }
        public List<LedgeRingConfig> Rings { get; set; }
        public List<string> LedgeColors { get; set; }
    }

    public class LedgeRingConfig
    {
        public string Name { get; set; }
        public List<int> SpaceIds { get; set; }
    }

    public class LedgePhaseSpec
    {
        public string Name { get; set; }
        public List<string> Moves { get; set; }
        public LedgeTurnConstraints Turn { get; set; }
        public string Description { get; set; }
    }

    public class LedgeTurnConstraints
    {
        public int? MinMoves { get; set; }
        public int? MaxMoves { get; set; }
    }

    public class LedgeMoveSpec
    {
        public List<LedgeMoveParameter> Parameters { get; set; }
        public List<string> AllowedPhases { get; set; }
        public List<string> Constraints { get; set; }
        public List<string> Effects { get; set; }
    }

    public class LedgeMoveParameter
    {
        public string Name { get; set; }
        public string Type { get; set; }
    }

    public class LedgeEndIfSpec
    {
        public string Type { get; set; }
        public Dictionary<string, object> Details { get; set; }
    }
}

