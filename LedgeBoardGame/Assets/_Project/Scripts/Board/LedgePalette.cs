using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Magi.LedgeBoardGame.Board
{
    /// Central color palette for the Ledge board visuals. Spirit colors are the vibrant
    /// 12-hue wheel pulled from the authoritative SVG; they render flat (no pastelize) so
    /// the wheel reads with its intended saturation.
    public static class LedgePalette
    {
        // Neutral fallbacks for spaces that aren't on the color wheel.
        public static readonly Color NeutralSpaceFill = new Color(0.93f, 0.90f, 0.83f, 1f);
        public static readonly Color CenterSpaceFill = new Color(1f, 1f, 1f, 1f);
        public static readonly Color WallGray = new Color(0.55f, 0.55f, 0.55f, 1f);

        // Frame base + additive states. Hovered / Selected / ValidTarget compose additively
        // on top of the base frame color so a hovered valid-target reads as both.
        public static readonly Color FrameIdle = new Color(0.10f, 0.09f, 0.08f, 1f);
        public static readonly Color FrameHoverAdd = new Color(0.28f, 0.26f, 0.20f, 0f);
        public static readonly Color FrameSelectedAdd = new Color(0.75f, 0.55f, 0.05f, 0f);
        public static readonly Color FrameValidTargetAdd = new Color(0.25f, 0.65f, 0.28f, 0f);
        public static readonly Color FrameMovableSourceAdd = new Color(0.95f, 0.72f, 0.30f, 0f);

        public static readonly Color CounterLight = new Color(0.97f, 0.95f, 0.88f, 1f);
        public static readonly Color CounterDark = new Color(0.12f, 0.11f, 0.10f, 1f);
        public static readonly Color GhostTint = new Color(1f, 1f, 1f, 0.55f);

        // 12 hex colors pulled from Ledge Wheel EN 20 (Board Game).svg arranged clockwise
        // starting at 90° (top). Wedge index 0..11 corresponds to angle = 90 - 30*index.
        private static readonly Color[] SpiritByWedge =
        {
            HexToColor("C04040"), // 0  Ela   @ 90°
            HexToColor("C04080"), // 1  Biz   @ 60°
            HexToColor("C040BF"), // 2  Yun   @ 30°
            HexToColor("8040C0"), // 3  Jutu  @ 0°
            HexToColor("4040C0"), // 4  Glei  @ -30°
            HexToColor("4080C0"), // 5  Sace  @ -60°
            HexToColor("40BFC0"), // 6  Rha   @ -90°
            HexToColor("40C080"), // 7  Dau   @ -120°
            HexToColor("40C040"), // 8  Wim   @ -150°
            HexToColor("7FC040"), // 9  Pfi   @ 180°
            HexToColor("BDBF3F"), // 10 Quae  @ 150°
            HexToColor("C07F40"), // 11 Vei   @ 120°
        };

        private static readonly Dictionary<string, int> WedgeByLabel = new Dictionary<string, int>
        {
            { "Ela",  0 }, { "Biz",  1 }, { "Yun",  2 }, { "Jutu", 3 },
            { "Glei", 4 }, { "Sace", 5 }, { "Rha",  6 }, { "Dau",  7 },
            { "Wim",  8 }, { "Pfi",  9 }, { "Quae", 10 }, { "Vei",  11 },
        };

        /// Returns the fill color for the hex at a given wedge. The visual scheme has
        /// each wedge-hex rendered in its *opposite* wedge's spirit color (across the
        /// wheel center), so labels stay canonical while the fill pairs its complement.
        public static Color GetSpiritColor(int wedgeIndex)
        {
            int i = ((((wedgeIndex + 6) % 12) + 12) % 12);
            return SpiritByWedge[i];
        }

        /// Returns the complement of `GetSpiritColor(wedgeIndex)` — i.e., the original
        /// "own" spirit color of that wedge. Used for the inner half of bridges and
        /// ledges, which pair outer/inner from opposite sides of the wheel.
        public static Color GetOppositeSpiritColor(int wedgeIndex)
        {
            return GetSpiritColor(wedgeIndex + 6);
        }

        /// Wedge angle in degrees (math convention: CCW from +X). Wedge 0 = 90°.
        public static float GetWedgeAngleDeg(int wedgeIndex)
        {
            return 90f - 30f * wedgeIndex;
        }

        public static bool TryGetWedgeByLabel(string label, out int wedge)
        {
            if (!string.IsNullOrEmpty(label) && WedgeByLabel.TryGetValue(label, out wedge))
                return true;
            wedge = -1;
            return false;
        }

        public static Color GetFillColor(string colorLabel)
        {
            if (TryGetWedgeByLabel(colorLabel, out var w))
                return GetSpiritColor(w);
            return NeutralSpaceFill;
        }

        public static Color GetFrameBaseColor(string colorLabel)
        {
            // Frames are uniformly dark now — the wheel's spirit colors carry the ID, not the frames.
            return FrameIdle;
        }

        private static Color HexToColor(string hex)
        {
            byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }
    }
}
