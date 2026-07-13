using System.Collections.Generic;
using Magi.LedgeBoardGame.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Magi.LedgeBoardGame.Board
{
    /// Persistent bottom-center log panel that records every event (routine AND
    /// critical). Sits directly above the StatusBanner so the two pieces of chrome
    /// stack cleanly without competing for real estate, and is shallow enough to
    /// stay clear of the ledge boards occupying the middle of the canvas. Comes
    /// with its own scrollbar so players can review earlier moves.
    [RequireComponent(typeof(RectTransform))]
    public class StatusLog : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private Image background;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private Scrollbar verticalScrollbar;
        [Tooltip("Max entries retained in history; older ones roll off even with scrollback.")]
        [SerializeField] private int maxLines = 200;

        private readonly Queue<string> _lines = new Queue<string>();
        private bool _pendingScrollToBottom;

        private void Awake() => EnsureVisuals();

        private void EnsureVisuals()
        {
            var selfRect = (RectTransform)transform;
            // Bottom-left: paired with future chat strip. Action bar lives BR
            // (the conventional "end turn" corner most strategy games use), so
            // the log/chat takes BL. Narrower than the BC stretch so it stays
            // out of the player's own board on the left half of the canvas.
            selfRect.anchorMin = new Vector2(0f, 0f);
            selfRect.anchorMax = new Vector2(0f, 0f);
            selfRect.pivot = new Vector2(0f, 0f);
            selfRect.anchoredPosition = new Vector2(Magi.LedgeBoardGame.UI.LedgeUITokens.PanelEdgeInset, Magi.LedgeBoardGame.UI.LedgeUITokens.PanelEdgeInset);
            selfRect.sizeDelta = new Vector2(420f, 180f);

            if (background == null)
            {
                var bgGo = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
                var bgRt = (RectTransform)bgGo.transform;
                bgRt.SetParent(transform, false);
                bgRt.anchorMin = Vector2.zero;
                bgRt.anchorMax = Vector2.one;
                bgRt.offsetMin = Vector2.zero;
                bgRt.offsetMax = Vector2.zero;
                background = bgGo.GetComponent<Image>();
                background.color = LedgeUITokens.Panel;
                background.raycastTarget = true;
                var outline = bgGo.GetComponent<Outline>();
                outline.effectColor = LedgeUITokens.PanelEdge;
                outline.effectDistance = new Vector2(LedgeUITokens.HairlineWidth, -LedgeUITokens.HairlineWidth);
            }

            if (scrollRect == null)
            {
                BuildScrollView();
            }
        }

        private void BuildScrollView()
        {
            // Viewport — masked region that clips the content.
            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(RectMask2D));
            var viewportRt = (RectTransform)viewportGo.transform;
            viewportRt.SetParent(transform, false);
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            // Leave a gutter on the right for the scrollbar.
            viewportRt.offsetMin = new Vector2(12f, 12f);
            viewportRt.offsetMax = new Vector2(-28f, -12f);
            var viewportImg = viewportGo.GetComponent<Image>();
            // Need an image for the mask to target, but transparent so the outer
            // background shows through.
            viewportImg.color = new Color(0f, 0f, 0f, 0f);
            viewportImg.raycastTarget = false;

            // Content IS the TMP_Text. A separate RT wrapper with ContentSizeFitter
            // wouldn't grow without a LayoutGroup to aggregate child sizes — so
            // we skip the wrapper and let the text's own preferredHeight drive
            // ContentSizeFitter directly.
            var labelGo = new GameObject("Content", typeof(RectTransform));
            var contentRt = (RectTransform)labelGo.transform;
            contentRt.SetParent(viewportRt, false);
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, 0f);
            label = labelGo.AddComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.TopLeft;
            label.fontSize = LedgeUITokens.BodySize;
            label.font = LedgeUITokens.UIFont;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.color = LedgeUITokens.InkFaint;
            label.raycastTarget = false;
            label.enableAutoSizing = false;
            var labelFitter = labelGo.AddComponent<ContentSizeFitter>();
            labelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            labelFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            // Scrollbar — vertical strip along the right edge.
            var scrollbarGo = new GameObject("Scrollbar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var scrollbarRt = (RectTransform)scrollbarGo.transform;
            scrollbarRt.SetParent(transform, false);
            scrollbarRt.anchorMin = new Vector2(1f, 0f);
            scrollbarRt.anchorMax = new Vector2(1f, 1f);
            scrollbarRt.pivot = new Vector2(1f, 0.5f);
            scrollbarRt.anchoredPosition = new Vector2(-8f, 0f);
            scrollbarRt.sizeDelta = new Vector2(12f, -16f);
            var scrollbarBg = scrollbarGo.GetComponent<Image>();
            scrollbarBg.color = LedgeUITokens.Rule;
            scrollbarBg.raycastTarget = true;

            var slidingArea = new GameObject("SlidingArea", typeof(RectTransform));
            var slidingRt = (RectTransform)slidingArea.transform;
            slidingRt.SetParent(scrollbarRt, false);
            slidingRt.anchorMin = Vector2.zero;
            slidingRt.anchorMax = Vector2.one;
            slidingRt.offsetMin = new Vector2(2f, 2f);
            slidingRt.offsetMax = new Vector2(-2f, -2f);

            var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var handleRt = (RectTransform)handleGo.transform;
            handleRt.SetParent(slidingRt, false);
            handleRt.anchorMin = Vector2.zero;
            handleRt.anchorMax = Vector2.one;
            handleRt.offsetMin = Vector2.zero;
            handleRt.offsetMax = Vector2.zero;
            var handleImg = handleGo.GetComponent<Image>();
            handleImg.color = LedgeUITokens.InkDim;
            handleImg.raycastTarget = true;

            verticalScrollbar = scrollbarGo.AddComponent<Scrollbar>();
            verticalScrollbar.handleRect = handleRt;
            verticalScrollbar.targetGraphic = handleImg;
            verticalScrollbar.direction = Scrollbar.Direction.BottomToTop;

            scrollRect = gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 24f;
            scrollRect.viewport = viewportRt;
            scrollRect.content = contentRt;
            scrollRect.verticalScrollbar = verticalScrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        }

        public void Append(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            _lines.Enqueue(message);
            while (_lines.Count > maxLines) _lines.Dequeue();
            if (label != null) label.text = string.Join("\n", _lines);
            // Snap to newest after layout settles (next LateUpdate). Doing it in
            // the same frame would run before ContentSizeFitter recalculates.
            _pendingScrollToBottom = true;
        }

        private void LateUpdate()
        {
            if (!_pendingScrollToBottom) return;
            _pendingScrollToBottom = false;
            if (scrollRect == null) return;
            // ForceUpdateCanvases so ContentSizeFitter's new height is reflected
            // before we scroll — otherwise we'd snap to a stale content size.
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }

        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
        }

        /// Returns the most recent N lines in chronological order (oldest
        /// first within the returned slice; equivalent to slicing the
        /// Queue's tail). Used by the end-of-game recap to surface the last
        /// few decisive moves under "How it turned" without classifying
        /// narrative intent — a drop-in upgrade path when a real classifier
        /// ships.
        public System.Collections.Generic.IReadOnlyList<string> GetRecentLines(int count)
        {
            if (count <= 0 || _lines.Count == 0)
                return System.Array.Empty<string>();
            int take = System.Math.Min(count, _lines.Count);
            var result = new System.Collections.Generic.List<string>(take);
            int skip = _lines.Count - take;
            int i = 0;
            foreach (var line in _lines)
            {
                if (i++ < skip) continue;
                result.Add(line);
            }
            return result;
        }

        public void Clear()
        {
            _lines.Clear();
            if (label != null) label.text = string.Empty;
        }
    }
}
