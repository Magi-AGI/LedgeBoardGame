using System;
using System.Collections.Generic;

namespace Magi.LedgeBoardGame.Models
{
    /// Result of a state-based effects pass. Describes what changed so callers can
    /// narrate the outcome (banner text) without re-inspecting GameState diffs.
    /// OverflowTrims only populate for end-of-turn passes; mid-turn SBE never
    /// produces them.
    [Serializable]
    public class StateBasedEffectsResult
    {
        public List<int> NewlyEliminatedPlayerIds { get; set; }
        public bool GameEnded { get; set; }
        public int? WinnerId { get; set; }
        public List<OverflowTrim> OverflowTrims { get; set; }

        public StateBasedEffectsResult()
        {
            NewlyEliminatedPlayerIds = new List<int>();
            OverflowTrims = new List<OverflowTrim>();
        }

        public bool HasAnyEffect =>
            NewlyEliminatedPlayerIds.Count > 0 || GameEnded || OverflowTrims.Count > 0;
    }

    /// One space's worth of end-of-turn overflow trimming — which stack lost
    /// counters, which tone, and how many. Produced by GameState.EndTurn when a
    /// space on the ending player's board held more than three counters.
    [Serializable]
    public class OverflowTrim
    {
        public SpaceId Space { get; set; }
        public Tone Tone { get; set; }
        public int RemovedCount { get; set; }
    }
}
