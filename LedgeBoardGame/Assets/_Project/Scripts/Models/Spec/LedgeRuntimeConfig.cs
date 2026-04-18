namespace Magi.LedgeBoardGame.Models.Spec
{
    public class LedgeRuntimeConfig
    {
        public int MinPlayers { get; }
        public int MaxPlayers { get; }

        public int PlacementMinMoves { get; }
        public int PlacementMaxMoves { get; }
        public int MovementMinMoves { get; }
        public int MovementMaxMoves { get; }

        public LedgeRuntimeConfig(
            int minPlayers,
            int maxPlayers,
            int placementMinMoves,
            int placementMaxMoves,
            int movementMinMoves,
            int movementMaxMoves)
        {
            MinPlayers = minPlayers;
            MaxPlayers = maxPlayers;
            PlacementMinMoves = placementMinMoves;
            PlacementMaxMoves = placementMaxMoves;
            MovementMinMoves = movementMinMoves;
            MovementMaxMoves = movementMaxMoves;
        }

        public SpecLedgeRuntimeConfig ToSpec()
            => new SpecLedgeRuntimeConfig
            {
                MinPlayers = MinPlayers,
                MaxPlayers = MaxPlayers,
                PlacementMinMoves = PlacementMinMoves,
                PlacementMaxMoves = PlacementMaxMoves,
                MovementMinMoves = MovementMinMoves,
                MovementMaxMoves = MovementMaxMoves,
            };

        public static LedgeRuntimeConfig FromSpec(SpecLedgeRuntimeConfig spec)
        {
            if (spec == null) return null;
            return new LedgeRuntimeConfig(
                spec.MinPlayers,
                spec.MaxPlayers,
                spec.PlacementMinMoves,
                spec.PlacementMaxMoves,
                spec.MovementMinMoves,
                spec.MovementMaxMoves);
        }

        public static LedgeRuntimeConfig FromSpec(LedgeGameSpec spec)
        {
            if (spec == null || spec.Config == null)
            {
                return new LedgeRuntimeConfig(2, 4, 2, 2, 0, 999);
            }

            var minPlayers = spec.Config.MinPlayers > 0 ? spec.Config.MinPlayers : 2;
            var maxPlayers = spec.Config.MaxPlayers >= minPlayers ? spec.Config.MaxPlayers : 4;

            var placementPhase = GetPhaseByName(spec, "placement");
            var movementPhase = GetPhaseByName(spec, "movement");

            int placementMin = placementPhase?.Turn?.MinMoves ?? 2;
            int placementMax = placementPhase?.Turn?.MaxMoves ?? 2;
            int movementMin = movementPhase?.Turn?.MinMoves ?? 0;
            int movementMax = movementPhase?.Turn?.MaxMoves ?? 999;

            return new LedgeRuntimeConfig(minPlayers, maxPlayers, placementMin, placementMax, movementMin, movementMax);
        }

        private static LedgePhaseSpec GetPhaseByName(LedgeGameSpec spec, string name)
        {
            if (spec.Phases == null)
                return null;

            foreach (var phase in spec.Phases)
            {
                if (string.Equals(phase.Name, name, System.StringComparison.OrdinalIgnoreCase))
                {
                    return phase;
                }
            }

            return null;
        }
    }
}
