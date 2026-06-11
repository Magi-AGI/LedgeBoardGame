using UnityEngine;
using UnityEngine.UI;

namespace Magi.LedgeBoardGame.UI
{
    /// Translucent glass panel chrome. Mirrors the kit's `Panel` primitive
    /// (kit/ledge-board-game/project/ui/ui-primitives.jsx): translucent dark
    /// fill, hairline border, soft inner top-light + outer drop shadow.
    ///
    /// Backdrop blur is deferred (see task U0.8d) — the kit's CSS uses
    /// `backdrop-filter: blur(14px) saturate(120%)`, which on URP requires a
    /// scene-grab pass. Until that lands the fill is plain translucent dark
    /// (PanelEdge over Panel) and reads acceptably against any backdrop.
    ///
    /// Construct in code via <see cref="Build"/>, or attach in Editor and
    /// the visuals self-assemble in Awake.
    [RequireComponent(typeof(RectTransform))]
    public class LedgeGlassPanel : MonoBehaviour
    {
        [Tooltip("Inner padding, in px (1600x900 reference). x=horizontal, y=vertical.")]
        public Vector2 padding = new Vector2(LedgeUITokens.PanelPadX, LedgeUITokens.PanelPadY);

        [Tooltip("Use the elevated panel surface (Panel2) instead of the default.")]
        public bool elevated = false;

        [Tooltip("Use the brighter (PanelEdge2) hairline instead of the default PanelEdge.")]
        public bool stronglyEdged = false;

        private Image _fill;
        private Image _border;
        private RectTransform _content;

        public RectTransform Content => _content != null ? _content : EnsureBuilt()._content;

        private void Awake() { EnsureBuilt(); }

        public LedgeGlassPanel EnsureBuilt()
        {
            if (_fill != null) return this;

            // ── Fill ────────────────────────────────────────────────────
            var fillGo = new GameObject("PanelFill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var fillRt = (RectTransform)fillGo.transform;
            fillRt.SetParent(transform, false);
            StretchToParent(fillRt);
            _fill = fillGo.GetComponent<Image>();
            _fill.color = elevated ? LedgeUITokens.Panel2 : LedgeUITokens.Panel;
            _fill.raycastTarget = true; // panels swallow input so the board doesn't see clicks behind them

            // ── Border (hairline) ───────────────────────────────────────
            // Implemented as a transparent Image with an Outline component so
            // we don't ship a 9-slice asset. 1px hairline at PanelEdge color.
            var borderGo = new GameObject("PanelBorder", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
            var borderRt = (RectTransform)borderGo.transform;
            borderRt.SetParent(transform, false);
            StretchToParent(borderRt);
            var borderImg = borderGo.GetComponent<Image>();
            borderImg.color = new Color(1, 1, 1, 0); // shape exists for the Outline only
            borderImg.raycastTarget = false;
            var outline = borderGo.GetComponent<Outline>();
            outline.effectColor = stronglyEdged ? LedgeUITokens.PanelEdge2 : LedgeUITokens.PanelEdge;
            outline.effectDistance = new Vector2(LedgeUITokens.HairlineWidth, -LedgeUITokens.HairlineWidth);
            _border = borderImg;

            // ── Content slot ────────────────────────────────────────────
            // Children should be added to Content, not directly to the panel.
            // Padding is enforced by anchoring the content rect inset.
            var contentGo = new GameObject("Content", typeof(RectTransform));
            var contentRt = (RectTransform)contentGo.transform;
            contentRt.SetParent(transform, false);
            contentRt.anchorMin = Vector2.zero;
            contentRt.anchorMax = Vector2.one;
            contentRt.offsetMin = new Vector2( padding.x,  padding.y);
            contentRt.offsetMax = new Vector2(-padding.x, -padding.y);
            _content = contentRt;

            return this;
        }

        public void SetPadding(float x, float y)
        {
            padding = new Vector2(x, y);
            if (_content != null)
            {
                _content.offsetMin = new Vector2(x, y);
                _content.offsetMax = new Vector2(-x, -y);
            }
        }

        /// Procedural construction shortcut. Returns the panel and exposes its
        /// content rect so callers can parent children.
        public static LedgeGlassPanel Build(Transform parent, string name = "GlassPanel",
                                            bool elevated = false, bool stronglyEdged = false,
                                            Vector2? padding = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            var panel = go.AddComponent<LedgeGlassPanel>();
            panel.elevated = elevated;
            panel.stronglyEdged = stronglyEdged;
            if (padding.HasValue) panel.padding = padding.Value;
            panel.EnsureBuilt();
            return panel;
        }

        private static void StretchToParent(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
