using System;
using System.Collections.Generic;

namespace Magi.LedgeBoardGame.Models
{
    public enum Tone
    {
        Light,
        Dark
    }

    public enum SpaceType
    {
        Center,
        InnerBridge,
        InnerWall,
        Ring2,
        Ring3,
        OuterAdded
    }

    public enum GamePhase
    {
        Placement,
        Movement
    }

    public enum MoveResult
    {
        Lock,
        Stack,
        Clear
    }

    /// The twelve colours of the Ledge Wheel, in canonical ring order.
    ///
    /// Lives here, in the shared models file, so the board game and the TCG read
    /// ONE definition. Magi.LedgeTCG.asmdef already references
    /// Magi.LedgeBoardGame, so nothing needed rewiring, and folding the types
    /// into an existing file means no new Unity asset or .meta is required.
    ///
    /// Order matches Board/LedgePalette.cs, which draws the wheel from
    /// design_handoff_v2/current-attempt/ledge-tokens.js: wedge w sits at angle
    /// (90 - 30*w) degrees, clockwise from the top. Keeping one ordering means
    /// the visuals and the rules cannot drift apart on what "adjacent" means.
    ///
    /// Ring index is the enum value, so opposition is (i + 6) % 12 - which
    /// LedgePalette already documents as "a game mechanic, not an aesthetic".
    /// Those six pairs are the wheel's axes:
    ///   Social Ela/Rha - Nature Biz/Dau - Physical Yun/Wim
    ///   Tech Jutu/Pfi  - Mental Glei/Quae - Magic Sace/Vei
    public enum LedgeColor
    {
        Ela = 0,
        Biz = 1,
        Yun = 2,
        Jutu = 3,
        Glei = 4,
        Sace = 5,
        Rha = 6,
        Dau = 7,
        Wim = 8,
        Pfi = 9,
        Quae = 10,
        Vei = 11
    }

    /// How two colours stand to each other on the wheel. Exhaustive: every
    /// ordered pair of colours is exactly one of these.
    ///
    /// Same, Adjacent and Opposite correspond to the wheel's 30 significant
    /// groupings - 12 monocolours, 12 adjacent pairs, 6 opposite pairs.
    public enum LedgeColorRelationship
    {
        Same,
        Adjacent,
        Opposite,
        Unrelated
    }

    /// Pure wheel geometry. No Unity types, no game state - just the fixed
    /// structure of the twelve colours, so it can be unit-tested in isolation
    /// and reused by any rules layer.
    public static class LedgeColorWheel
    {
        public const int Count = 12;

        /// Half a turn around the ring: the distance between a colour and its
        /// opposite.
        private const int HalfTurn = Count / 2;

        /// Classifies the relationship between two colours. Symmetric.
        public static LedgeColorRelationship Relate(LedgeColor a, LedgeColor b)
        {
            switch (RingDistance(a, b))
            {
                case 0: return LedgeColorRelationship.Same;
                case 1: return LedgeColorRelationship.Adjacent;
                case HalfTurn: return LedgeColorRelationship.Opposite;
                default: return LedgeColorRelationship.Unrelated;
            }
        }

        /// The colour directly across the wheel - the other half of this
        /// colour's axis. Involutive: Opposite(Opposite(c)) == c.
        public static LedgeColor Opposite(LedgeColor colour)
        {
            return (LedgeColor)(((int)colour + HalfTurn) % Count);
        }

        /// Shortest number of steps around the ring between two colours, 0..6.
        public static int RingDistance(LedgeColor a, LedgeColor b)
        {
            int diff = Math.Abs((int)a - (int)b);
            return Math.Min(diff, Count - diff);
        }

        /// True when any colour in a stands in relationship to any colour in b.
        ///
        /// This - not Strongest - is what target matching should ask, because
        /// matching cares about a SPECIFIC relationship: a dark action looks
        /// for Opposite, a light action for Same. Strongest would hide an
        /// Opposite behind a Same that happens to outrank it.
        public static bool HasRelationship(
            IReadOnlyList<LedgeColor> a,
            IReadOnlyList<LedgeColor> b,
            LedgeColorRelationship relationship)
        {
            if (a == null) throw new ArgumentNullException("a");
            if (b == null) throw new ArgumentNullException("b");

            for (int i = 0; i < a.Count; i++)
                for (int j = 0; j < b.Count; j++)
                    if (Relate(a[i], b[j]) == relationship) return true;

            return false;
        }

        /// The strongest relationship between two colour sets, for cards that
        /// carry more than one colour.
        ///
        /// Precedence: Same > Opposite > Adjacent > Unrelated. So {Rha,Dau} vs
        /// {Ela,Biz} is Opposite, because Rha/Ela and Dau/Biz are both axes.
        ///
        /// Precedence only matters when a pair of sets exhibits more than one
        /// relationship at once, and it is a display/summary convention rather
        /// than a matching rule - see HasRelationship. Empty sets are Unrelated
        /// rather than an error, since a colourless card is simply unconnected.
        public static LedgeColorRelationship Strongest(
            IReadOnlyList<LedgeColor> a, IReadOnlyList<LedgeColor> b)
        {
            if (a == null) throw new ArgumentNullException("a");
            if (b == null) throw new ArgumentNullException("b");

            var best = LedgeColorRelationship.Unrelated;
            for (int i = 0; i < a.Count; i++)
                for (int j = 0; j < b.Count; j++)
                {
                    var relationship = Relate(a[i], b[j]);
                    if (Rank(relationship) > Rank(best)) best = relationship;
                }

            return best;
        }

        private static int Rank(LedgeColorRelationship relationship)
        {
            switch (relationship)
            {
                case LedgeColorRelationship.Same: return 3;
                case LedgeColorRelationship.Opposite: return 2;
                case LedgeColorRelationship.Adjacent: return 1;
                default: return 0;
            }
        }
    }
}
