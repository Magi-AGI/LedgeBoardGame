namespace Magi.LedgeBoardGame.Models
{
    /// Computes the human-readable name for a space using the wheel-color naming
    /// convention. Pure function; the only dependency is the canonical wheel-color
    /// list in LedgeConfigConstants.
    ///
    /// Conventions:
    ///   Center      → "[Player] Core" (or "Core" if no player given)
    ///   InnerBridge → "[WedgeColor] Bridge"
    ///   InnerWall   → "[WedgeColor] Wall"
    ///   Ring2       → "Inner [WedgeColor]"
    ///   Ring3-off   → "[CCW color] [CW color]"
    ///   Ring3-vertex / OuterAdded (ledges) → "[ColorLabel] Ledge"
    public static class SpaceNamer
    {
        public static string Name(int spaceId, SpaceMeta meta, string playerName = null)
        {
            switch (meta.Type)
            {
                case SpaceType.Center:
                    return string.IsNullOrEmpty(playerName) ? "Core" : $"{playerName} Core";

                case SpaceType.InnerBridge:
                    return $"{LabelOf(meta.WedgeIndex)} Bridge";

                case SpaceType.InnerWall:
                    return $"{LabelOf(meta.WedgeIndex)} Wall";

                case SpaceType.Ring2:
                    return $"Inner {LabelOf(meta.WedgeIndex)}";

                case SpaceType.Ring3:
                    if (!string.IsNullOrEmpty(meta.ColorLabel))
                        return $"{meta.ColorLabel} Ledge";
                    // Ring3-off (ids 25-36): the WedgeIndex stored is the ADJACENT outer-axis
                    // (even) wedge — CCW for slot 0 of each pair, CW for slot 1. Pair the
                    // stored wedge with the vertex axis it flanks to get the wheel-order names.
                    int slot = spaceId - 25;
                    bool isCcwOff = (slot & 1) == 0;
                    int wedge = meta.WedgeIndex;
                    int ccwWedge = isCcwOff ? wedge : (wedge - 1 + 12) % 12;
                    int cwWedge = isCcwOff ? (wedge + 1) % 12 : wedge;
                    return $"{LabelOf(ccwWedge)} {LabelOf(cwWedge)}";

                case SpaceType.OuterAdded:
                    return $"{meta.ColorLabel} Ledge";

                default:
                    return $"Space {spaceId}";
            }
        }

        public static string LabelOf(int wedgeIndex)
        {
            int i = ((wedgeIndex % 12) + 12) % 12;
            return LedgeConfigConstants.LedgeColors[i];
        }
    }
}
