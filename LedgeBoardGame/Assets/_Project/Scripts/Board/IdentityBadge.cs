using Magi.LedgeBoardGame.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Magi.LedgeBoardGame.Board
{
    /// Top-left persistent label that tells the player which seat they own in
    /// Network mode. Hidden when there's no stable local-seat identity (e.g.
    /// Local hot-seat, where the "current player" rotates every turn). Paints
    /// above the board canvas but below the banner/log so it never intercepts
    /// input; the CanvasGroup is non-interactive by construction.
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasGroup))]
    public class IdentityBadge : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private Image background;

        private CanvasGroup _canvasGroup;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _canvasGroup.alpha = 1f;
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;
            EnsureVisuals();
        }

        private void EnsureVisuals()
        {
            var selfRect = (RectTransform)transform;
            selfRect.anchorMin = new Vector2(0f, 1f);
            selfRect.anchorMax = new Vector2(0f, 1f);
            selfRect.pivot = new Vector2(0f, 1f);
            selfRect.anchoredPosition = new Vector2(24f, -24f);
            selfRect.sizeDelta = new Vector2(320f, 48f);

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
                background.raycastTarget = false;
                var outline = bgGo.GetComponent<Outline>();
                outline.effectColor = LedgeUITokens.PanelEdge;
                outline.effectDistance = new Vector2(LedgeUITokens.HairlineWidth, -LedgeUITokens.HairlineWidth);
            }

            if (label == null)
            {
                var labelGo = new GameObject("Label", typeof(RectTransform));
                var labelRt = (RectTransform)labelGo.transform;
                labelRt.SetParent(transform, false);
                labelRt.anchorMin = Vector2.zero;
                labelRt.anchorMax = Vector2.one;
                labelRt.offsetMin = new Vector2(16f, 4f);
                labelRt.offsetMax = new Vector2(-16f, -4f);
                label = labelGo.AddComponent<TextMeshProUGUI>();
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.fontSize = LedgeUITokens.IdentNameSize;
                label.font = LedgeUITokens.UIFont;
                label.color = LedgeUITokens.Ink;
                label.raycastTarget = false;
            }
        }

        /// Set the badge text directly. Callers own the phrasing so the badge
        /// stays stateless about seat/player semantics.
        public void SetText(string text)
        {
            if (label == null) EnsureVisuals();
            if (label != null) label.text = text ?? string.Empty;
        }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        }
    }
}
