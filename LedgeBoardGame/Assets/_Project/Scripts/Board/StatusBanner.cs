using System.Collections;
using System.Collections.Generic;
using Magi.LedgeBoardGame.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Magi.LedgeBoardGame.Board
{
    /// Bottom-anchored narration strip that fades in, holds, and fades out. Used to
    /// announce both player-driven transitions ("Player 1 ended turn") and state-based
    /// effects ("Player 2 eliminated"). Messages queue serially so a burst of events
    /// reads in the order they occurred instead of overwriting each other.
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasGroup))]
    public class StatusBanner : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private Image background;
        [SerializeField] private float fadeInDuration = 0.3f;
        [SerializeField] private float holdDuration = 2.0f;
        [SerializeField] private float fadeOutDuration = 0.5f;

        private CanvasGroup _canvasGroup;
        private readonly Queue<string> _pending = new Queue<string>();
        private bool _playing;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;
            EnsureVisuals();
        }

        private void EnsureVisuals()
        {
            var selfRect = (RectTransform)transform;
            // Top-center transient toast. Slides down (well, fades in) for
            // dramatic moments ("Player 2 eliminated") without competing with
            // the persistent TL "You" panel or the TR view toggle.
            selfRect.anchorMin = new Vector2(0.5f, 1f);
            selfRect.anchorMax = new Vector2(0.5f, 1f);
            selfRect.pivot = new Vector2(0.5f, 1f);
            selfRect.anchoredPosition = new Vector2(0f, -LedgeUITokens.PanelEdgeInset);
            selfRect.sizeDelta = new Vector2(560f, 72f);

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
                labelRt.offsetMin = new Vector2(24f, 8f);
                labelRt.offsetMax = new Vector2(-24f, -8f);
                label = labelGo.AddComponent<TextMeshProUGUI>();
                label.alignment = TextAlignmentOptions.Center;
                label.fontSize = LedgeUITokens.TurnBannerSize;
                // Display font (Fraunces italic) for the theatrical "Your turn" feel
                // when the asset is present; falls back to UI font otherwise.
                label.font = LedgeUITokens.DisplayFont;
                label.fontStyle = FontStyles.Italic;
                label.color = LedgeUITokens.Ink;
                label.raycastTarget = false;
            }
        }

        /// Append a message to the narration queue. Safe to call repeatedly; each
        /// message will run its full fade-in/hold/fade-out cycle before the next starts.
        public void Enqueue(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            _pending.Enqueue(message);
            if (!_playing) StartCoroutine(PlayQueue());
        }

        private IEnumerator PlayQueue()
        {
            _playing = true;
            while (_pending.Count > 0)
            {
                var msg = _pending.Dequeue();
                label.text = msg;
                yield return FadeTo(1f, fadeInDuration);
                yield return new WaitForSecondsRealtime(holdDuration);
                yield return FadeTo(0f, fadeOutDuration);
            }
            _playing = false;
        }

        private IEnumerator FadeTo(float target, float duration)
        {
            if (duration <= 0f)
            {
                _canvasGroup.alpha = target;
                yield break;
            }
            float start = _canvasGroup.alpha;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                _canvasGroup.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
            _canvasGroup.alpha = target;
        }
    }
}
