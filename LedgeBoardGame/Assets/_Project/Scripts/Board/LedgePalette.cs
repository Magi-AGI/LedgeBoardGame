using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Magi.LedgeBoardGame.Board
{
    /// Central color palette for the Ledge board visuals. Keeps the frame / fill / counter tints
    /// in one place so artists can tune without touching SpaceView or BoardPresenter internals.
    public static class LedgePalette
    {
        // Neutral fills for non-ledge spaces — a warm parchment for regular tiles and a slightly
        // darker tone for the center so the hub reads as special.
        public static readonly Color NeutralSpaceFill = new Color(0.93f, 0.90f, 0.83f, 1f);
        public static readonly Color CenterSpaceFill = new Color(0.82f, 0.78f, 0.68f, 1f);

        // Frame base + additive states. Hovered / Selected / ValidTarget compose additively on
        // top of the base frame color so a hovered valid-target reads as both.
        public static readonly Color FrameIdle = new Color(0.22f, 0.20f, 0.17f, 1f);
        public static readonly Color FrameHoverAdd = new Color(0.28f, 0.26f, 0.20f, 0f);
        public static readonly Color FrameSelectedAdd = new Color(0.75f, 0.55f, 0.05f, 0f);
        public static readonly Color FrameValidTargetAdd = new Color(0.25f, 0.65f, 0.28f, 0f);
        // Softer amber breathe for stacks the current player can pick up — warm enough to
        // say "come grab me" but clearly distinct from the saturated green valid-target pulse.
        public static readonly Color FrameMovableSourceAdd = new Color(0.95f, 0.72f, 0.30f, 0f);

        public static readonly Color CounterLight = new Color(0.97f, 0.95f, 0.88f, 1f);
        public static readonly Color CounterDark = new Color(0.12f, 0.11f, 0.10f, 1f);
        public static readonly Color GhostTint = new Color(1f, 1f, 1f, 0.55f);

        // 12 hex colors pulled from Ledge Wheel EN 20 (Board Game).svg arranged clockwise
        // starting at 90° (top). Ledge names come from LedgeConfigConstants.LedgeColors.
        private static readonly Dictionary<string, Color> LedgeColorsByLabel = new Dictionary<string, Color>
        {
            { "Ela",  HexToColor("C04040") }, // 90°
            { "Biz",  HexToColor("C04080") }, // 60°
            { "Yun",  HexToColor("C040BF") }, // 30°
            { "Jutu", HexToColor("8040C0") }, // 0°
            { "Glei", HexToColor("4040C0") }, // -30°
            { "Sace", HexToColor("4080C0") }, // -60°
            { "Rha",  HexToColor("40BFC0") }, // -90°
            { "Dau",  HexToColor("40C080") }, // -120°
            { "Wim",  HexToColor("40C040") }, // -150°
            { "Pfi",  HexToColor("7FC040") }, // 180°
            { "Quae", HexToColor("BDBF3F") }, // 150°
            { "Vei",  HexToColor("C07F40") }, // 120°
        };

        public static Color GetFillColor(string colorLabel)
        {
            if (string.IsNullOrEmpty(colorLabel))
                return NeutralSpaceFill;
            if (LedgeColorsByLabel.TryGetValue(colorLabel, out var c))
                return Color.Lerp(c, Color.white, 0.35f); // Pastel-ize so counters and frame read on top.
            return NeutralSpaceFill;
        }

        public static Color GetFrameBaseColor(string colorLabel)
        {
            if (string.IsNullOrEmpty(colorLabel))
                return FrameIdle;
            if (LedgeColorsByLabel.TryGetValue(colorLabel, out var c))
                return Color.Lerp(c, Color.black, 0.55f);
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
