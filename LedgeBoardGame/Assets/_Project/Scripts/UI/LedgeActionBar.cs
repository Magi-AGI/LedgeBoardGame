using UnityEngine;
using UnityEngine.UI;

namespace Magi.LedgeBoardGame.UI
{
    /// Bottom-right action belt. Hosts the player's available actions
    /// (End Turn, Takeback, and any future move/place/complement buttons)
    /// in a horizontal row inside a glass panel. Lives in the BR corner
    /// the way most strategy games park their commit affordance — leaves
    /// the BL free for the move history / chat strip.
    ///
    /// The bar can adopt existing scene-assigned Buttons via <see cref="Adopt"/>
    /// — it reparents them under the bar, applies LedgeButton chrome, and
    /// arranges them in a HorizontalLayoutGroup. This preserves all existing
    /// onClick wiring without touching the scene assets.
    [RequireComponent(typeof(RectTransform))]
    public class LedgeActionBar : MonoBehaviour
    {
        private LedgeGlassPanel _panel;
        private RectTransform _row;

        private void Awake() => EnsureBuilt();

        public void EnsureBuilt()
        {
            if (_panel != null) return;

            var rt = (RectTransform)transform;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot     = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-LedgeUITokens.PanelEdgeInset, LedgeUITokens.PanelEdgeInset);
            rt.sizeDelta = new Vector2(360f, 64f);

            _panel = LedgeGlassPanel.Build(transform, "Glass");
            var pRt = _panel.GetComponent<RectTransform>();
            pRt.anchorMin = Vector2.zero;
            pRt.anchorMax = Vector2.one;
            pRt.offsetMin = Vector2.zero;
            pRt.offsetMax = Vector2.zero;

            // Horizontal row inside the glass panel's content slot.
            var rowGo = new GameObject("Row", typeof(RectTransform));
            _row = (RectTransform)rowGo.transform;
            _row.SetParent(_panel.Content, false);
            _row.anchorMin = Vector2.zero;
            _row.anchorMax = Vector2.one;
            _row.offsetMin = Vector2.zero;
            _row.offsetMax = Vector2.zero;
            var hl = rowGo.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 8f;
            hl.childAlignment = TextAnchor.MiddleRight;
            hl.childControlWidth = false;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = true;
        }

        /// Reparent an existing Button under the action bar and apply
        /// LedgeButton chrome. The button keeps its onClick wiring, just
        /// gets a new visual treatment + position. Pass <c>null</c> to skip
        /// (e.g. if the scene didn't wire a takeback button).
        public void Adopt(Button button, LedgeButton.Variant variant, LedgeButton.Size size = LedgeButton.Size.Md, float minWidth = 110f)
        {
            EnsureBuilt();
            if (button == null) return;

            // Reparent under the row.
            button.transform.SetParent(_row, false);

            // Reset rect so HorizontalLayoutGroup can position it.
            var brt = (RectTransform)button.transform;
            brt.anchorMin = new Vector2(0.5f, 0.5f);
            brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.pivot     = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = Vector2.zero;
            brt.sizeDelta = new Vector2(minWidth, 36f);

            // Add a LayoutElement so HorizontalLayoutGroup respects the min width.
            var le = button.gameObject.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
            le.minWidth = minWidth;
            le.preferredWidth = minWidth;

            LedgeButton.ApplyTo(button, variant, size);
        }
    }
}
