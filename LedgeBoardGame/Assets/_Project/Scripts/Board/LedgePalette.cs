using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Magi.LedgeBoardGame.Board
{
    /// Central color palette for the Ledge board visuals. The 12-hue wheel is the
    /// canonical palette from the design kit (design_handoff_v2/ledge-tokens.js);
    /// wedge index w corresponds to the color displayed at angle (90° − 30°·w) in the
    /// rosette, clockwise from the top.
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

        // 12 wheel colors, indexed 0..11 clockwise from top. Each entry is the OWN
        // identity color of that wedge on the board. Authoritative source:
        // design_handoff_v2/current-attempt/ledge-tokens.js.
        private static readonly Color[] SpiritByWedge =
        {
            HexToColor("40BFC0"), //  0 Ela   @  90°  cyan
            HexToColor("40C080"), //  1 Biz   @  60°  green
            HexToColor("40C040"), //  2 Yun   @  30°  green
            HexToColor("7FC040"), //  3 Jutu  @   0°  lime
            HexToColor("BDBF3F"), //  4 Glei  @ -30°  yellow
            HexToColor("C07F40"), //  5 Sace  @ -60°  orange
            HexToColor("C04040"), //  6 Rha   @ -90°  red
            HexToColor("C04080"), //  7 Dau   @-120°  pink
            HexToColor("C040BF"), //  8 Wim   @-150°  magenta
            HexToColor("8040C0"), //  9 Pfi   @ 180°  purple
            HexToColor("4040C0"), // 10 Quae  @ 150°  blue
            HexToColor("4080C0"), // 11 Vei   @ 120°  azure
        };

        private static readonly Dictionary<string, int> WedgeByLabel = new Dictionary<string, int>
        {
            { "Ela",  0 }, { "Biz",  1 }, { "Yun",  2 }, { "Jutu", 3 },
            { "Glei", 4 }, { "Sace", 5 }, { "Rha",  6 }, { "Dau",  7 },
            { "Wim",  8 }, { "Pfi",  9 }, { "Quae", 10 }, { "Vei",  11 },
        };

        /// Own identity color of the given wedge (e.g., Ela=0 → cyan).
        public static Color GetOwnColor(int wedgeIndex)
        {
            int i = ((wedgeIndex % 12) + 12) % 12;
            return SpiritByWedge[i];
        }

        /// Complement color of the given wedge (e.g., Ela=0 → Rha=red). Complement
        /// pairing wedge i ↔ wedge (i+6) mod 12 is a game mechanic, not an aesthetic.
        public static Color GetComplementColor(int wedgeIndex)
        {
            int i = ((((wedgeIndex + 6) % 12) + 12) % 12);
            return SpiritByWedge[i];
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
                return GetOwnColor(w);
            return NeutralSpaceFill;
        }

        public static Color GetFrameBaseColor(string colorLabel)
        {
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
