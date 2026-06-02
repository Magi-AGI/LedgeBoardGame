using TMPro;
using UnityEngine;

namespace Magi.LedgeBoardGame.UI
{
    /// Design tokens mirrored from the visual language kit
    /// (kit/ledge-board-game/project/ui/UI Exploration.html). Every UI
    /// surface in-game (panels, banners, buttons, log entries) reads from
    /// this static class so a kit revision is a one-file edit on the C# side.
    ///
    /// Values are drop-ins for the kit's CSS variables: `--canvas`, `--ink`,
    /// `--panel`, `--accent`, etc. See <see cref="Color"/> ctor (r,g,b,a in
    /// 0..1); the source hex/rgba comments preserve the kit's authoring form.
    public static class LedgeUITokens
    {
        // ── Canvas / surface ─────────────────────────────────────────────
        public static readonly Color Canvas    = Hex("0A0D1B");                      // page background
        public static readonly Color Canvas2   = Hex("06080F");                      // recessed (board container)
        public static readonly Color Panel     = Rgba(20,  24, 44, 0.72f);           // glass panel fill
        public static readonly Color Panel2    = Rgba(28,  33, 58, 0.78f);           // elevated panel
        public static readonly Color PanelEdge  = Rgba(220, 226, 250, 0.10f);        // hairline rule
        public static readonly Color PanelEdge2 = Rgba(220, 226, 250, 0.18f);        // secondary border / divider

        // ── Ink ──────────────────────────────────────────────────────────
        public static readonly Color Ink       = Hex("E2E4EE");                      // primary text
        public static readonly Color InkFaint  = Rgba(226, 228, 238, 0.62f);         // body / secondary
        public static readonly Color InkDim    = Rgba(226, 228, 238, 0.40f);         // section labels
        public static readonly Color InkMute   = Rgba(226, 228, 238, 0.22f);         // quaternary
        public static readonly Color Rule      = Rgba(226, 228, 238, 0.10f);

        // ── Accent ───────────────────────────────────────────────────────
        public static readonly Color Accent     = Hex("F2B84D"); // warm halo: your-turn / active / primary action
        public static readonly Color AccentCool = Hex("8FB4FF"); // cool: opponent / inactive

        // ── Action-state tints (from in-game frame overlays, kept aligned) ─
        public static readonly Color StateValid    = Rgba(64, 165, 71, 0.42f);
        public static readonly Color StateMovable  = Rgba(242, 184, 77, 0.55f);
        public static readonly Color StateSelected = Rgba(191, 140, 13, 0.55f);
        public static readonly Color StateHover    = Rgba(71,  66, 51, 0.35f);

        // ── Spacing scale (px in 1600x900 reference) ─────────────────────
        public const float PanelPadX     = 20f;
        public const float PanelPadY     = 18f;
        public const float PanelGap      = 14f;        // between stacked panels
        public const float PanelEdgeInset= 28f;        // distance from frame edge
        public const float PanelRadius   = 10f;        // border-radius
        public const float ButtonRadius  = 4f;
        public const float HairlineWidth = 1f;

        // ── Type scale ───────────────────────────────────────────────────
        // Letter-spacing is handled per-call (TMP uses character spacing in
        // arbitrary units); these are size targets matching the kit.
        public const float SectionLabelSize = 9.5f;    // mono, 0.22em tracking, uppercase
        public const float BodySize         = 12.5f;
        public const float IdentNameSize    = 15f;
        public const float TurnBannerSize   = 32f;     // Fraunces italic
        public const float ButtonMdSize     = 12f;
        public const float ButtonSmSize     = 11f;
        public const float ButtonLgSize     = 13f;

        // ── Fonts ────────────────────────────────────────────────────────
        // Resolved lazily from Resources so the project compiles before the
        // Fraunces / Space Grotesk / JetBrains Mono TMP_FontAssets are
        // imported. Until then the resolvers fall through to LiberationSans.
        // Place TMP_FontAssets at:
        //   Resources/Fonts/Fraunces SDF
        //   Resources/Fonts/SpaceGrotesk SDF
        //   Resources/Fonts/JetBrainsMono SDF
        // …or override at runtime by setting the static properties below.
        public static TMP_FontAsset DisplayFont
        {
            get => _display ?? (_display = LoadOrFallback("Fonts/Fraunces SDF"));
            set => _display = value;
        }
        public static TMP_FontAsset UIFont
        {
            get => _ui ?? (_ui = LoadOrFallback("Fonts/SpaceGrotesk SDF"));
            set => _ui = value;
        }
        public static TMP_FontAsset MonoFont
        {
            get => _mono ?? (_mono = LoadOrFallback("Fonts/JetBrainsMono SDF"));
            set => _mono = value;
        }

        private static TMP_FontAsset _display;
        private static TMP_FontAsset _ui;
        private static TMP_FontAsset _mono;

        private static TMP_FontAsset LoadOrFallback(string resourcePath)
        {
            var f = Resources.Load<TMP_FontAsset>(resourcePath);
            if (f != null) return f;
            return TMP_Settings.defaultFontAsset
                ?? Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF")
                ?? Resources.Load<TMP_FontAsset>("LiberationSans SDF");
        }

        // ── Helpers ──────────────────────────────────────────────────────
        private static Color Hex(string hex)
        {
            // Accepts "RRGGBB" or "RRGGBBAA".
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }

        private static Color Rgba(int r, int g, int b, float a)
        {
            return new Color(r / 255f, g / 255f, b / 255f, a);
        }
    }
}
