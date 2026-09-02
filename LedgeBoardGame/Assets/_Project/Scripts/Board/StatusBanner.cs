using System.Collections;
using System.Collections.Generic;
using Magi.LedgeBoardGame.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Magi.LedgeBoardGame.Board
{
    /// Centered narration toast that fades in, holds, and fades out. Used to
    /// announce both player-driven transitions ("Player 1 ended turn") and state-based
    /// effects ("Player 2 eliminated"). Messages queue serially so a burst of events
    /// reads in the order they occurred instead of overwriting each other.
    ///
    /// Its vertical slot is resolved from canvas geometry rather than fixed —
    /// see ResolveToastAnchoredPosition.
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasGroup))]
    public class StatusBanner : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private Image background;
        [SerializeField] private float fadeInDuration = 0.3f;
        [SerializeField] private float holdDuration = 2.0f;
        [SerializeField] private float fadeOutDuration = 0.5f;

        /// The toast's footprint. Public so the layout contract can reason about
        /// the rect the toast will occupy without standing up a canvas.
        public const float ToastWidth = 560f;
        public const float ToastHeight = 72f;

        private CanvasGroup _canvasGroup;
        private readonly Queue<string> _pending = new Queue<string>();
        private bool _playing;

        // Responsive refs (see ApplyResponsiveLayout). The two top-corner chrome
        // panels are what the toast has to share the top band with.
        private RectTransform _selfRect;
        private RectTransform _canvasRect;
        private LedgeYouPanel _youPanel;
        private BoardViewHud _viewHud;
        private float _lastCanvasWidth = -1f;
        private float _lastLeftWidth = -1f;
        private float _lastLeftHeight = -1f;
        private float _lastRightWidth = -1f;
        private float _lastRightHeight = -1f;

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
            _selfRect = selfRect;
            // Top-center transient toast. Slides down (well, fades in) for
            // dramatic moments ("Player 2 eliminated"). The y offset is not a
            // constant: on narrow canvases the top band is already spoken for by
            // the TL "You" panel and the TR view toggle, so ApplyResponsiveLayout
            // drops the toast clear of them. See ResolveToastAnchoredPosition.
            selfRect.anchorMin = new Vector2(0.5f, 1f);
            selfRect.anchorMax = new Vector2(0.5f, 1f);
            selfRect.pivot = new Vector2(0.5f, 1f);
            selfRect.anchoredPosition = new Vector2(0f, -LedgeUITokens.PanelEdgeInset);
            selfRect.sizeDelta = new Vector2(ToastWidth, ToastHeight);

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

            ApplyResponsiveLayout();
        }

        // ── Responsive placement ─────────────────────────────────────────
        // CP073. The toast used to sit at a fixed top-center inset, which is
        // clear of the corner chrome at the 1920-unit reference width but not on
        // a phone: a 720x1280 screen scales (reference 1920x1080, match 0.5) to
        // roughly 1080 canvas units, where the centered 560-wide toast spans
        // 260..820 and lands on both the 360-wide You panel (28..388) and the
        // 280-wide Board View HUD (772..1052) at the same top inset. Design read
        // it as the toast's translucent box sitting over the "Place Light"
        // headline and the TURN 1 eyebrow.
        //
        // Fix moves the toast, not the panels: the You panel is persistent and
        // load-bearing (it is the "what do I do now" surface), while the toast is
        // transient, so the transient thing yields.

        /// Where the toast sits, given the canvas width and the measured
        /// footprints of the two top-corner chrome panels (TL You panel, TR Board
        /// View HUD). Pass a width of 0 for a corner panel that is absent.
        ///
        /// Wide layouts keep the accepted top-center inset placement. When the
        /// centered toast cannot clear a corner panel by a PanelGap, it drops
        /// below the deeper of the panels it collides with, again one PanelGap
        /// clear — so the toast stays centered and styled exactly as before and
        /// only its vertical slot changes.
        ///
        /// The breakpoint is geometry, not a resolution string: it falls out of
        /// PanelEdgeInset/PanelGap, ToastWidth and the measured panel sizes, so
        /// retuning a token or resizing a panel moves the breakpoint with it.
        /// (At today's numbers the You panel binds first, at ~1364 canvas units.)
        public static Vector2 ResolveToastAnchoredPosition(
            float canvasWidth,
            float topLeftWidth, float topLeftHeight,
            float topRightWidth, float topRightHeight)
        {
            const float inset = LedgeUITokens.PanelEdgeInset;
            const float gap = LedgeUITokens.PanelGap;
            var topCenter = new Vector2(0f, -inset);
            if (canvasWidth <= 0f) return topCenter;

            float half = canvasWidth * 0.5f;
            float toastLeft = half - ToastWidth * 0.5f;
            float toastRight = half + ToastWidth * 0.5f;

            // Depth of the top band the toast actually runs into. Only the panels
            // it overlaps horizontally count, so a canvas wide enough for one
            // corner but not the other clears just what it has to.
            float clearance = 0f;
            if (topLeftWidth > 0f && toastLeft < inset + topLeftWidth + gap)
                clearance = Mathf.Max(clearance, topLeftHeight);
            if (topRightWidth > 0f && toastRight > canvasWidth - inset - topRightWidth - gap)
                clearance = Mathf.Max(clearance, topRightHeight);

            if (clearance <= 0f) return topCenter;
            return new Vector2(0f, -(inset + clearance + gap));
        }

        // Re-fit when the canvas resizes (window resize / device rotation) or a
        // corner panel changes footprint (the You panel's compact toggle). A few
        // rect reads a frame, with an early-out on unchanged geometry.
        private void Update()
        {
            ResolveChromeRefs();
            if (_canvasRect == null || _selfRect == null) return;

            float canvasWidth = _canvasRect.rect.width;
            Measure(YouPanelRect, out float lw, out float lh);
            Measure(ViewHudRect, out float rw, out float rh);
            if (Mathf.Approximately(canvasWidth, _lastCanvasWidth)
                && Mathf.Approximately(lw, _lastLeftWidth)
                && Mathf.Approximately(lh, _lastLeftHeight)
                && Mathf.Approximately(rw, _lastRightWidth)
                && Mathf.Approximately(rh, _lastRightHeight)) return;

            ApplyLayout(canvasWidth, lw, lh, rw, rh);
        }

        private void ApplyResponsiveLayout()
        {
            ResolveChromeRefs();
            if (_selfRect == null) return;
            float canvasWidth = _canvasRect != null ? _canvasRect.rect.width : 0f;
            Measure(YouPanelRect, out float lw, out float lh);
            Measure(ViewHudRect, out float rw, out float rh);
            ApplyLayout(canvasWidth, lw, lh, rw, rh);
        }

        private void ApplyLayout(float canvasWidth, float lw, float lh, float rw, float rh)
        {
            _lastCanvasWidth = canvasWidth;
            _lastLeftWidth = lw;
            _lastLeftHeight = lh;
            _lastRightWidth = rw;
            _lastRightHeight = rh;
            _selfRect.anchoredPosition =
                ResolveToastAnchoredPosition(canvasWidth, lw, lh, rw, rh);
        }

        // The TR Board View HUD is built after the banner (GameController runs
        // EnsureStatusBanner well before EnsureBoardViewHud), so refs resolve
        // lazily and keep being retried until found rather than cached at Awake.
        private void ResolveChromeRefs()
        {
            if (_selfRect == null) _selfRect = transform as RectTransform;
            if (_canvasRect == null)
            {
                var canvas = GetComponentInParent<Canvas>();
                if (canvas != null) _canvasRect = canvas.transform as RectTransform;
            }
            if (_canvasRect == null) return;
            if (_youPanel == null) _youPanel = _canvasRect.GetComponentInChildren<LedgeYouPanel>(true);
            if (_viewHud == null) _viewHud = _canvasRect.GetComponentInChildren<BoardViewHud>(true);
        }

        private RectTransform YouPanelRect =>
            _youPanel != null ? _youPanel.transform as RectTransform : null;

        private RectTransform ViewHudRect => _viewHud != null ? _viewHud.Root : null;

        // A hidden panel is not an obstacle, so it measures as absent.
        private static void Measure(RectTransform rt, out float width, out float height)
        {
            if (rt == null || !rt.gameObject.activeInHierarchy)
            {
                width = 0f;
                height = 0f;
                return;
            }
            var r = rt.rect;
            width = r.width;
            height = r.height;
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
                // Re-fit before fading in rather than only on canvas resize: the
                // corner chrome can appear or change size (view HUD build, You
                // panel compact toggle) between toasts, and a toast that is about
                // to be visible is exactly when the placement has to be right.
                ApplyResponsiveLayout();
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
