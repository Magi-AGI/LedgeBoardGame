using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Magi.LedgeBoardGame.Models;

namespace Magi.LedgeBoardGame.Board
{
    public class SpaceView : MonoBehaviour,
        IPointerClickHandler,
        IPointerEnterHandler,
        IPointerExitHandler
    {
        [Header("Frame / Fill")]
        [SerializeField] private Image frameImage;
        [SerializeField] private Image fillImage;
        [SerializeField] private Image frameGlowImage;
        [SerializeField] private RectTransform shapeRoot;
        [SerializeField] private RectTransform countersRoot;

        [Header("Token Display (TMP, legacy — hidden by default now)")]
        [SerializeField] private TextMeshProUGUI lightCountTMP;
        [SerializeField] private TextMeshProUGUI darkCountTMP;

        [Header("Token Display (Legacy Text fallback)")]
        [SerializeField] private Text lightCountText;
        [SerializeField] private Text darkCountText;

        [Header("Indicators (legacy)")]
        [SerializeField] private GameObject lockIndicator;
        [SerializeField] private GameObject highlightEffect;
        [SerializeField] private Image highlightImage;

        [Header("Counter Layout")]
        [SerializeField] private float counterSize = 60f;
        [SerializeField] private float counterStackOffset = 5f;

        [Header("Hover Label")]
        [SerializeField] private TextMeshProUGUI hoverLabelTMP;
        [SerializeField] private float hoverLabelFontSize = 14f;
        [SerializeField] private Color hoverLabelColor = Color.white;
        [SerializeField] private Color hoverLabelBgColor = new Color(0f, 0f, 0f, 0.72f);
        [SerializeField] private Vector2 hoverLabelPadding = new Vector2(4f, 3f);
        [SerializeField] private float hoverLabelFadeSeconds = 0.25f;
        [SerializeField] private float hoverLabelShowDelaySeconds = 1.25f;
        private RectTransform _hoverLabelRoot;
        private CanvasGroup _hoverLabelGroup;
        private Image _hoverLabelBg;
        private float _hoverLabelAlpha;
        private float _hoverEnterTime = -1f;
        private string _spaceLabel;

        [Header("Pulse")]
        [SerializeField] private float pulseFrequencyHz = 0.9f;
        [SerializeField] private float pulseMinAlpha = 0.25f;
        [SerializeField] private float pulseMaxAlpha = 0.85f;
        // Inner-ring fills (Center/Wall/Bridge) are grey, so full white/black pulses
        // there read as harsh white-outs or black-outs. Scale the pulse alpha down
        // on those spaces so the tone still reads without overwhelming the fill.
        [SerializeField, Range(0f, 1f)] private float innerRingPulseDamping = 0.5f;
        // Hop-delay between pulse peaks on adjacent rings. Peaks travel outward from
        // the origin (source during movement, core during placement) so the highlight
        // reads as a ripple rather than a synchronized flash.
        [SerializeField] private float radialPhasePerHop = 0.15f;
        // Soft start/stop on selection so target-picking doesn't flash. Delay matches
        // the hover-label's armed-reveal pattern (shorter here — half the hover delay —
        // so the pulse appears quickly enough to feel connected to the pick-up, but
        // not so fast it's jarring). The fade itself is a short ramp.
        [SerializeField] private float validTargetFadeInDelay = 0.6f;
        [SerializeField] private float validTargetFadeDuration = 0.35f;

        [Header("Movable-source breathe")]
        [SerializeField] private float sourceBreatheHz = 0.7f;
        [SerializeField] private float sourceMinAlpha = 0.15f;
        [SerializeField] private float sourceMaxAlpha = 0.55f;

        [Header("Events")]
        [SerializeField] private UnityEngine.Events.UnityEvent<SpaceView> onClicked;

        private int _spaceId;
        private SpaceMeta _metadata;
        private Color _frameBaseColor = LedgePalette.FrameIdle;
        private bool _hovered;
        private bool _selected;
        private float _validTargetIntensity;
        private Tone? _validTargetTone;
        private int _validTargetHopsFromOrigin;
        private float _validTargetEnvelope;
        private float _validTargetEnvelopeTarget;
        private float _validTargetFadeDelayRemaining;
        private bool _movableSource;
        private bool _pulseVisible;

        private readonly List<Image> _counterImages = new List<Image>();
        private readonly List<Image> _counterRims = new List<Image>();

        public int SpaceId => _spaceId;
        public SpaceMeta Metadata => _metadata;

        private void Awake()
        {
            EnsureVisuals();
            HideLegacyTextCounters();
            ApplyFrameVisual();
            UpdateFrameGlow(instant: true);
        }

        public void SetData(int id, SpaceMeta meta, TokenStack stack)
        {
            _spaceId = id;
            _metadata = meta;

            EnsureVisuals();
            HideLegacyTextCounters();

            ApplyShapeAndFill(id, meta);
            SetFrameBaseColor(LedgePalette.FrameIdle);

            _hovered = false;
            _selected = false;
            _validTargetIntensity = 0f;
            _movableSource = false;
            ApplyFrameVisual();
            UpdateFrameGlow(instant: true);

            UpdateTokenDisplay(stack);
        }

        /// Fades the top `topCount` of the active counters down to `alpha`. Bottom counters —
        /// including a locked counter at index 0 — keep full opacity so the origin still
        /// reads as "this counter is still planted here."
        public void SetPhantomCounters(int topCount, float alpha)
        {
            if (_counterImages.Count == 0 || topCount <= 0) return;
            int totalActive = 0;
            for (int i = 0; i < _counterImages.Count; i++)
            {
                if (_counterImages[i] != null && _counterImages[i].gameObject.activeSelf)
                    totalActive++;
            }
            if (totalActive == 0) return;

            int clamped = Mathf.Clamp(topCount, 0, totalActive);
            int fadeStart = totalActive - clamped;
            for (int i = 0; i < totalActive; i++)
            {
                var img = _counterImages[i];
                if (img == null) continue;
                float a = (i >= fadeStart) ? alpha : 1f;
                var c = img.color;
                c.a = a;
                img.color = c;

                if (i < _counterRims.Count)
                {
                    var rim = _counterRims[i];
                    if (rim != null)
                    {
                        var rc = rim.color;
                        rc.a = a;
                        rim.color = rc;
                    }
                }
            }
        }

        public void ClearPhantomCounters()
        {
            for (int i = 0; i < _counterImages.Count; i++)
            {
                var img = _counterImages[i];
                if (img != null)
                {
                    var c = img.color;
                    if (c.a < 1f)
                    {
                        c.a = 1f;
                        img.color = c;
                    }
                }
                if (i < _counterRims.Count)
                {
                    var rim = _counterRims[i];
                    if (rim == null) continue;
                    var rc = rim.color;
                    if (rc.a < 1f)
                    {
                        rc.a = 1f;
                        rim.color = rc;
                    }
                }
            }
        }

        public void UpdateTokenDisplay(TokenStack stack)
        {
            EnsureVisuals();

            int totalNeeded = stack.LightCount + stack.DarkCount;
            while (_counterImages.Count < totalNeeded)
            {
                _counterImages.Add(CreateCounterImage());
            }
            for (int i = totalNeeded; i < _counterImages.Count; i++)
            {
                if (_counterImages[i] != null)
                    _counterImages[i].gameObject.SetActive(false);
            }

            int index = 0;
            for (int d = 0; d < stack.DarkCount; d++)
                LayoutCounter(index++, LedgePalette.CounterDark, d, totalNeeded);
            for (int l = 0; l < stack.LightCount; l++)
                LayoutCounter(index++, LedgePalette.CounterLight, stack.DarkCount + l, totalNeeded);

            if (lockIndicator != null)
            {
                var isLocked = stack.IsLocked(Tone.Light) || stack.IsLocked(Tone.Dark);
                lockIndicator.SetActive(isLocked);
            }
        }

        public void SetFillColor(Color color)
        {
            if (fillImage != null)
                fillImage.color = color;
        }

        public void SetFrameBaseColor(Color color)
        {
            _frameBaseColor = color;
            ApplyFrameVisual();
        }

        public void SetHovered(bool hovered)
        {
            if (_hovered == hovered) return;
            _hovered = hovered;
            if (hovered)
                _hoverEnterTime = Time.unscaledTime;
            else
                _hoverEnterTime = -1f;
            ApplyFrameVisual();
            ApplyHoverLabel();
        }

        public void SetSpaceLabel(string label)
        {
            _spaceLabel = label;
            if (hoverLabelTMP != null)
                hoverLabelTMP.text = label ?? string.Empty;
            ApplyHoverLabel();
        }

        private void ApplyHoverLabel()
        {
            EnsureHoverLabel();
            if (_hoverLabelRoot == null) return;
            bool show = _hovered && !string.IsNullOrEmpty(_spaceLabel);
            if (show)
                _hoverLabelRoot.SetAsLastSibling();
        }

        private float HoverLabelTargetAlpha
        {
            get
            {
                if (!_hovered || string.IsNullOrEmpty(_spaceLabel))
                    return 0f;
                // Arm a delay on hover-enter: the label only begins fading in after
                // `hoverLabelShowDelaySeconds`. Leaving before the delay elapses means
                // target never goes above 0, so the label never appears — no flash.
                if (hoverLabelShowDelaySeconds > 0f
                    && Time.unscaledTime - _hoverEnterTime < hoverLabelShowDelaySeconds)
                    return 0f;
                return 1f;
            }
        }

        private void EnsureHoverLabel()
        {
            if (_hoverLabelRoot != null && _hoverLabelGroup != null && hoverLabelTMP != null) return;

            if (hoverLabelTMP == null)
            {
                // The TMP is its own root: we run ContentSizeFitter on it (height only) so
                // it auto-sizes to the wrapped text. No backdrop — fade alpha via CanvasGroup.
                var tmpGo = new GameObject("HoverLabel", typeof(RectTransform));
                var tmpRect = (RectTransform)tmpGo.transform;
                tmpRect.SetParent(transform, false);
                tmpRect.anchorMin = new Vector2(0.5f, 0.5f);
                tmpRect.anchorMax = new Vector2(0.5f, 0.5f);
                tmpRect.pivot = new Vector2(0.5f, 0.5f);
                tmpRect.anchoredPosition = Vector2.zero;

                hoverLabelTMP = tmpGo.AddComponent<TextMeshProUGUI>();
                hoverLabelTMP.alignment = TextAlignmentOptions.Center;
                hoverLabelTMP.enableWordWrapping = true;
                hoverLabelTMP.fontSize = hoverLabelFontSize;
                hoverLabelTMP.color = hoverLabelColor;
                hoverLabelTMP.raycastTarget = false;
                hoverLabelTMP.text = _spaceLabel ?? string.Empty;

                // Wrap cap well inside the hex so two-word names break onto two lines
                // and the backdrop stays proportionally narrow.
                var selfRect = (RectTransform)transform;
                float cap = Mathf.Max(40f, selfRect.sizeDelta.x * 0.4f);
                tmpRect.sizeDelta = new Vector2(cap, 0f);

                var fitter = tmpGo.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                // Backdrop plate: sibling-first under the TMP so text renders on top.
                // Stretches to the TMP's rect and extends outward by `hoverLabelPadding`.
                var bgGo = new GameObject("Bg", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var bgRect = (RectTransform)bgGo.transform;
                bgRect.SetParent(tmpRect, false);
                bgRect.anchorMin = Vector2.zero;
                bgRect.anchorMax = Vector2.one;
                bgRect.offsetMin = new Vector2(-hoverLabelPadding.x, -hoverLabelPadding.y);
                bgRect.offsetMax = new Vector2(hoverLabelPadding.x, hoverLabelPadding.y);
                bgRect.SetAsFirstSibling();
                _hoverLabelBg = bgGo.GetComponent<Image>();
                _hoverLabelBg.color = hoverLabelBgColor;
                _hoverLabelBg.raycastTarget = false;

                _hoverLabelGroup = tmpGo.AddComponent<CanvasGroup>();
                _hoverLabelGroup.alpha = 0f;
                _hoverLabelGroup.interactable = false;
                _hoverLabelGroup.blocksRaycasts = false;

                _hoverLabelRoot = tmpRect;
                _hoverLabelAlpha = 0f;
            }
            else if (_hoverLabelRoot == null)
            {
                _hoverLabelRoot = hoverLabelTMP.rectTransform;
                _hoverLabelGroup = hoverLabelTMP.GetComponent<CanvasGroup>();
                if (_hoverLabelGroup == null)
                    _hoverLabelGroup = hoverLabelTMP.gameObject.AddComponent<CanvasGroup>();
                _hoverLabelAlpha = _hoverLabelGroup.alpha;
            }
        }

        public void SetSelected(bool selected)
        {
            if (_selected == selected) return;
            _selected = selected;
            ApplyFrameVisual();
        }

        public void SetValidTarget(bool valid)
        {
            SetValidTargetIntensity(valid ? 1f : 0f, null, 0);
        }

        /// Multi-hop reach highlight. `intensity` scales pulse alpha (distant reaches
        /// read fainter than neighbors); `tone` picks the pulse color (white for Light,
        /// near-black for Dark, null reverts to the legacy green); `hopsFromOrigin`
        /// phase-shifts the pulse so peaks travel outward from the selected source (or
        /// the core during placement), making the highlight read as a ripple. A value
        /// of 0 clears the pulse via a soft fade-out; a fresh activation starts after
        /// `validTargetFadeInDelay` so target-picking doesn't flash on every click.
        public void SetValidTargetIntensity(float intensity, Tone? tone = null, int hopsFromOrigin = 0)
        {
            intensity = Mathf.Clamp01(intensity);
            bool wasActive = _validTargetIntensity > 0f;
            bool willBeActive = intensity > 0f;

            _validTargetIntensity = intensity;
            _validTargetTone = tone;
            _validTargetHopsFromOrigin = Mathf.Max(0, hopsFromOrigin);

            if (willBeActive && !wasActive)
            {
                // Fresh activation: fade in with delay; mid-fade re-activation skips the
                // delay so a quick deselect/re-select doesn't strand the glow at partial
                // visibility while the delay ticks down.
                _validTargetEnvelopeTarget = 1f;
                _validTargetFadeDelayRemaining = (_validTargetEnvelope <= 0f) ? validTargetFadeInDelay : 0f;
            }
            else if (!willBeActive && wasActive)
            {
                _validTargetEnvelopeTarget = 0f;
                _validTargetFadeDelayRemaining = 0f;
            }

            UpdateFrameGlow(instant: !willBeActive && !wasActive);
        }

        public void SetMovableSource(bool active)
        {
            if (_movableSource == active) return;
            _movableSource = active;
            UpdateFrameGlow(instant: !active);
        }

        public void SetHighlight(bool active)
        {
            SetValidTarget(active);
            if (highlightEffect != null)
                highlightEffect.SetActive(false);
            if (highlightImage != null)
            {
                var c = highlightImage.color;
                c.a = 0f;
                highlightImage.color = c;
            }
        }

        public void SetHighlightColor(Color _) { }

        public void OnPointerClick(PointerEventData eventData)
        {
            onClicked?.Invoke(this);
            SpaceClickedEvent.Raise(this);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            SetHovered(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            SetHovered(false);
        }

        public void RegisterClickListener(UnityEngine.Events.UnityAction<SpaceView> handler)
        {
            onClicked ??= new UnityEngine.Events.UnityEvent<SpaceView>();
            onClicked.AddListener(handler);
        }

        public void UnregisterClickListener(UnityEngine.Events.UnityAction<SpaceView> handler)
        {
            if (onClicked == null) return;
            onClicked.RemoveListener(handler);
        }

        private void Update()
        {
            TickHoverLabelFade();

            if (frameGlowImage == null) return;

            TickValidTargetEnvelope();

            if (_validTargetEnvelope > 0f)
            {
                // Phase shift by hop distance produces a wave that visibly travels
                // outward from the origin space rather than all targets flashing in
                // sync — the farther a space is, the later its pulse peak arrives.
                float phaseOffset = _validTargetHopsFromOrigin * radialPhasePerHop;
                float t = Mathf.Sin((Time.unscaledTime - phaseOffset) * pulseFrequencyHz * 2f * Mathf.PI) * 0.5f + 0.5f;
                float a = Mathf.Lerp(pulseMinAlpha, pulseMaxAlpha, t) * _validTargetIntensity * _validTargetEnvelope * GetPulseDampingForMeta();
                var baseCol = GetValidTargetPulseColor(_validTargetTone);
                frameGlowImage.color = new Color(baseCol.r, baseCol.g, baseCol.b, a);
            }
            else if (_movableSource)
            {
                float t = Mathf.Sin(Time.unscaledTime * sourceBreatheHz * 2f * Mathf.PI) * 0.5f + 0.5f;
                float a = Mathf.Lerp(sourceMinAlpha, sourceMaxAlpha, t);
                var baseCol = LedgePalette.FrameMovableSourceAdd;
                frameGlowImage.color = new Color(baseCol.r, baseCol.g, baseCol.b, a);
            }
            else
            {
                // Envelope finished fading out and no movable-source breathe is active —
                // clear any residual alpha so the next state starts from zero.
                var c = frameGlowImage.color;
                if (c.a > 0f)
                {
                    c.a = 0f;
                    frameGlowImage.color = c;
                }
            }
        }

        private void TickValidTargetEnvelope()
        {
            if (Mathf.Approximately(_validTargetEnvelope, _validTargetEnvelopeTarget))
                return;
            float dt = Time.unscaledDeltaTime;
            if (_validTargetEnvelopeTarget > _validTargetEnvelope)
            {
                if (_validTargetFadeDelayRemaining > 0f)
                {
                    _validTargetFadeDelayRemaining -= dt;
                    return;
                }
                float step = (validTargetFadeDuration <= 0f) ? 1f : dt / validTargetFadeDuration;
                _validTargetEnvelope = Mathf.Min(_validTargetEnvelopeTarget, _validTargetEnvelope + step);
            }
            else
            {
                float step = (validTargetFadeDuration <= 0f) ? 1f : dt / validTargetFadeDuration;
                _validTargetEnvelope = Mathf.Max(_validTargetEnvelopeTarget, _validTargetEnvelope - step);
            }
        }

        private static Color GetValidTargetPulseColor(Tone? tone)
        {
            if (tone == Tone.Light) return LedgePalette.CounterLight;
            if (tone == Tone.Dark) return LedgePalette.CounterDark;
            return LedgePalette.FrameValidTargetAdd;
        }

        private float GetPulseDampingForMeta()
        {
            switch (_metadata.Type)
            {
                case SpaceType.Center:
                case SpaceType.InnerBridge:
                case SpaceType.InnerWall:
                    return innerRingPulseDamping;
                default:
                    return 1f;
            }
        }

        private void TickHoverLabelFade()
        {
            if (_hoverLabelGroup == null) return;
            float target = HoverLabelTargetAlpha;
            if (Mathf.Approximately(_hoverLabelAlpha, target)) return;
            float step = (hoverLabelFadeSeconds <= 0f) ? 1f : Time.unscaledDeltaTime / hoverLabelFadeSeconds;
            _hoverLabelAlpha = Mathf.MoveTowards(_hoverLabelAlpha, target, step);
            _hoverLabelGroup.alpha = _hoverLabelAlpha;
        }

        private void EnsureVisuals()
        {
            var selfRect = (RectTransform)transform;
            if (selfRect.sizeDelta.sqrMagnitude < 0.01f)
                selfRect.sizeDelta = new Vector2(60f, 60f);

            if (shapeRoot == null)
            {
                var go = new GameObject("ShapeRoot", typeof(RectTransform));
                shapeRoot = (RectTransform)go.transform;
                shapeRoot.SetParent(transform, false);
                shapeRoot.anchorMin = new Vector2(0.5f, 0.5f);
                shapeRoot.anchorMax = new Vector2(0.5f, 0.5f);
                shapeRoot.pivot = new Vector2(0.5f, 0.5f);
                shapeRoot.anchoredPosition = Vector2.zero;
                shapeRoot.SetAsFirstSibling();
            }
            shapeRoot.sizeDelta = selfRect.sizeDelta;

            // Legacy root Image (if present on prefab): disable it so stray white hexes don't render
            // underneath our generated sprites.
            var rootImg = GetComponent<Image>();
            if (rootImg != null && rootImg != frameImage && rootImg != fillImage)
                rootImg.enabled = false;

            if (fillImage == null)
            {
                fillImage = CreateShapeChild("Fill", sibling: 0);
                fillImage.color = LedgePalette.NeutralSpaceFill;
            }
            fillImage.raycastTarget = true;
            ((RectTransform)fillImage.transform).sizeDelta = selfRect.sizeDelta;

            if (frameImage == null)
            {
                frameImage = CreateShapeChild("Frame", sibling: 1);
            }
            frameImage.raycastTarget = false;
            frameImage.color = _frameBaseColor;
            ((RectTransform)frameImage.transform).sizeDelta = selfRect.sizeDelta;

            if (frameGlowImage == null)
            {
                frameGlowImage = CreateShapeChild("FrameGlow", sibling: 2);
                frameGlowImage.color = new Color(
                    LedgePalette.FrameValidTargetAdd.r,
                    LedgePalette.FrameValidTargetAdd.g,
                    LedgePalette.FrameValidTargetAdd.b, 0f);
                frameGlowImage.raycastTarget = false;
            }
            ((RectTransform)frameGlowImage.transform).sizeDelta = selfRect.sizeDelta * 1.18f;

            if (countersRoot == null)
            {
                var go = new GameObject("Counters", typeof(RectTransform));
                countersRoot = (RectTransform)go.transform;
                countersRoot.SetParent(transform, false);
                countersRoot.anchorMin = new Vector2(0.5f, 0.5f);
                countersRoot.anchorMax = new Vector2(0.5f, 0.5f);
                countersRoot.pivot = new Vector2(0.5f, 0.5f);
                countersRoot.anchoredPosition = Vector2.zero;
            }
            countersRoot.sizeDelta = selfRect.sizeDelta;
            countersRoot.SetAsLastSibling();
        }

        private Image CreateShapeChild(string name, int sibling)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(shapeRoot, false);
            rect.SetSiblingIndex(sibling);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = ((RectTransform)transform).sizeDelta;
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            return img;
        }

        private Image CreateCounterImage()
        {
            var go = new GameObject($"Counter_{_counterImages.Count}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(countersRoot, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(counterSize, counterSize);
            var img = go.GetComponent<Image>();
            img.sprite = LedgeSpriteFactory.Counter;
            img.raycastTarget = false;
            img.color = Color.white;

            var rimGo = new GameObject("Rim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rimRect = (RectTransform)rimGo.transform;
            rimRect.SetParent(rect, false);
            rimRect.anchorMin = new Vector2(0.5f, 0.5f);
            rimRect.anchorMax = new Vector2(0.5f, 0.5f);
            rimRect.pivot = new Vector2(0.5f, 0.5f);
            rimRect.anchoredPosition = Vector2.zero;
            rimRect.sizeDelta = new Vector2(counterSize, counterSize);
            var rimImg = rimGo.GetComponent<Image>();
            rimImg.sprite = LedgeSpriteFactory.CounterRim;
            rimImg.raycastTarget = false;
            rimImg.color = Color.white;
            _counterRims.Add(rimImg);

            return img;
        }

        private void LayoutCounter(int counterIndex, Color color, int indexInStack, int totalInStack)
        {
            if (counterIndex < 0 || counterIndex >= _counterImages.Count) return;
            var img = _counterImages[counterIndex];
            if (img == null) return;
            img.gameObject.SetActive(true);
            img.color = color;
            float baseY = -(totalInStack - 1) * counterStackOffset * 0.5f;
            float y = baseY + indexInStack * counterStackOffset;
            var rect = (RectTransform)img.transform;
            rect.anchoredPosition = new Vector2(0f, y);
            rect.SetAsLastSibling();

            if (counterIndex < _counterRims.Count)
            {
                var rim = _counterRims[counterIndex];
                if (rim != null)
                {
                    rim.gameObject.SetActive(true);
                    // Opposite tone for contrast: dark counters get a light rim, light counters get a dark rim.
                    bool isDark = ApproxEqual(color, LedgePalette.CounterDark);
                    var rimColor = isDark ? LedgePalette.CounterLight : LedgePalette.CounterDark;
                    rimColor.a = img.color.a;
                    rim.color = rimColor;
                }
            }
        }

        private static bool ApproxEqual(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.01f
                && Mathf.Abs(a.g - b.g) < 0.01f
                && Mathf.Abs(a.b - b.b) < 0.01f;
        }

        private void ApplyFrameVisual()
        {
            if (frameImage == null) return;
            var c = _frameBaseColor;
            if (_hovered) c = AddColor(c, LedgePalette.FrameHoverAdd);
            if (_selected) c = AddColor(c, LedgePalette.FrameSelectedAdd);
            frameImage.color = c;
        }

        private void UpdateFrameGlow(bool instant)
        {
            if (frameGlowImage == null) return;
            if (_validTargetIntensity > 0f)
            {
                if (instant)
                {
                    var baseCol = LedgePalette.FrameValidTargetAdd;
                    frameGlowImage.color = new Color(baseCol.r, baseCol.g, baseCol.b, pulseMinAlpha * _validTargetIntensity);
                }
            }
            else if (_movableSource)
            {
                if (instant)
                {
                    var baseCol = LedgePalette.FrameMovableSourceAdd;
                    frameGlowImage.color = new Color(baseCol.r, baseCol.g, baseCol.b, sourceMinAlpha);
                }
            }
            else
            {
                var c = frameGlowImage.color;
                c.a = 0f;
                frameGlowImage.color = c;
            }
        }

        /// Shape + color dispatch. Called once per SetData. Assigns sprites for fill/frame/glow,
        /// picks the right color rule per space type, and rotates non-hex shapes to align with
        /// their wedge axis.
        private void ApplyShapeAndFill(int id, SpaceMeta meta)
        {
            float rotZ = 0f;
            Sprite fillSprite;
            Sprite frameSprite;
            Sprite glowSprite;

            switch (meta.Type)
            {
                case SpaceType.InnerBridge:
                {
                    // Wedge is always even (outer axis). The authored bridge sprite is drawn
                    // flipped relative to the rosette's convention, so we tessellate by adding 180°
                    // to the wedge-aligned rotation.
                    rotZ = 180f - 30f * meta.WedgeIndex;
                    var outerColor = LedgePalette.GetSpiritColor(meta.WedgeIndex);
                    var innerColor = LedgePalette.GetOppositeSpiritColor(meta.WedgeIndex);
                    fillSprite = LedgeSpriteFactory.GetBridgeFill(outerColor, innerColor);
                    frameSprite = LedgeSpriteFactory.BridgeFrame;
                    glowSprite = LedgeSpriteFactory.BridgeFrameGlow;
                    break;
                }

                case SpaceType.InnerWall:
                {
                    // Same authored-flip correction as InnerBridge.
                    rotZ = 180f - 30f * meta.WedgeIndex;
                    fillSprite = LedgeSpriteFactory.GetWallFill();
                    frameSprite = LedgeSpriteFactory.WallFrame;
                    glowSprite = LedgeSpriteFactory.WallFrameGlow;
                    break;
                }

                case SpaceType.Center:
                {
                    fillSprite = LedgeSpriteFactory.GetHexFill(LedgePalette.CenterSpaceFill);
                    frameSprite = LedgeSpriteFactory.HexFrame;
                    glowSprite = LedgeSpriteFactory.HexFrameGlow;
                    break;
                }

                case SpaceType.Ring2:
                {
                    var color = LedgePalette.GetSpiritColor(meta.WedgeIndex);
                    fillSprite = LedgeSpriteFactory.GetHexFill(color);
                    frameSprite = LedgeSpriteFactory.HexFrame;
                    glowSprite = LedgeSpriteFactory.HexFrameGlow;
                    break;
                }

                case SpaceType.Ring3:
                {
                    bool hasLabel = !string.IsNullOrEmpty(meta.ColorLabel);
                    if (hasLabel)
                    {
                        // Ring3 vertex ledge: inner-half = own spirit, outer-half = opposite.
                        fillSprite = BuildLedgeSplit(meta.WedgeIndex);
                        rotZ = 180f - 60f * meta.WedgeIndex;
                    }
                    else
                    {
                        fillSprite = BuildRing3OffSplit(id, meta.WedgeIndex);
                        rotZ = GetRing3OffRotation(id);
                    }
                    frameSprite = LedgeSpriteFactory.HexFrame;
                    glowSprite = LedgeSpriteFactory.HexFrameGlow;
                    break;
                }

                case SpaceType.OuterAdded:
                {
                    // Outer-axis ledge: same rule as Ring3 vertex.
                    fillSprite = BuildLedgeSplit(meta.WedgeIndex);
                    rotZ = 180f - 60f * meta.WedgeIndex;
                    frameSprite = LedgeSpriteFactory.HexFrame;
                    glowSprite = LedgeSpriteFactory.HexFrameGlow;
                    break;
                }

                default:
                {
                    fillSprite = LedgeSpriteFactory.GetHexFill(LedgePalette.NeutralSpaceFill);
                    frameSprite = LedgeSpriteFactory.HexFrame;
                    glowSprite = LedgeSpriteFactory.HexFrameGlow;
                    break;
                }
            }

            if (fillImage != null)
            {
                fillImage.sprite = fillSprite;
                fillImage.color = Color.white;
                fillImage.transform.localRotation = Quaternion.Euler(0f, 0f, rotZ);
                // Only painted (opaque) fill pixels should catch clicks — otherwise the bridge
                // rect's empty corners steal clicks from neighbors, and the wall rect overlaps
                // its bridges. Requires the fill texture to be readable (see LedgeSpriteFactory.Finalize).
                fillImage.alphaHitTestMinimumThreshold = 0.5f;
            }
            if (frameImage != null)
            {
                frameImage.sprite = frameSprite;
                frameImage.transform.localRotation = Quaternion.Euler(0f, 0f, rotZ);
            }
            if (frameGlowImage != null)
            {
                frameGlowImage.sprite = glowSprite;
                frameGlowImage.transform.localRotation = Quaternion.Euler(0f, 0f, rotZ);
            }
        }

        private static Sprite BuildLedgeSplit(int wedgeIndex)
        {
            var own = LedgePalette.GetSpiritColor(wedgeIndex);
            var opp = LedgePalette.GetOppositeSpiritColor(wedgeIndex);
            // Ring3-vertex and OuterAdded spaces are drawn corner-to-corner: the split line
            // runs from one pointy-top vertex to the opposite vertex of the baked hex, then
            // the whole fill rotates with rotZ to line up with the hex frame.
            float splitNormal = LedgePalette.GetWedgeAngleDeg(wedgeIndex);
            return LedgeSpriteFactory.GetHexSplitFill(opp, own, splitNormal);
        }

        // Per-space rotZ for Ring3-off hexes (ids 25..36). Baked to match the ledge-color
        // arrow semantics authored by hand in the scene. Within each (ccw, cw) sector pair,
        // the cw rotation equals the ccw rotation plus 180°.
        private static readonly float[] Ring3OffRotZ = new float[]
        {
             60f, -120f,  // 25 (k=0 ccw), 26 (k=0 cw)
            -120f,   60f, // 27 (k=1 ccw), 28 (k=1 cw)
             180f,    0f, // 29 (k=2 ccw), 30 (k=2 cw)
               0f, -180f, // 31 (k=3 ccw), 32 (k=3 cw)
             -60f,  120f, // 33 (k=4 ccw), 34 (k=4 cw)
             120f,  -60f, // 35 (k=5 ccw), 36 (k=5 cw)
        };

        private static float GetRing3OffRotation(int spaceId)
        {
            int idx = spaceId - 25;
            if (idx < 0 || idx >= Ring3OffRotZ.Length) return 0f;
            return Ring3OffRotZ[idx];
        }

        private static Sprite BuildRing3OffSplit(int id, int primaryWedgeIndex)
        {
            int offset = id - 25;
            int k = offset / 2;
            bool isCcw = (offset % 2) == 0;
            int partnerWedge = 2 * k + 1;

            var primary = LedgePalette.GetSpiritColor(primaryWedgeIndex);
            var partner = LedgePalette.GetSpiritColor(partnerWedge);

            // Ring3-off splits are midpoint-to-midpoint: splitNormal points along a vertex
            // direction of the unrotated hex (30°, 90°, 150°, ...), so the dividing line
            // passes through two opposite edge midpoints (0°, 60°, 120°, ...).
            //   ccwOff k=0 (Space_25): splitNormal 210° → line TL→BR, primary on lower-left.
            //   cwOff  k=0 (Space_26): splitNormal  90° → horizontal line, partner on top.
            // The pattern rotates by -60° per sector k so successive sectors mirror the rosette's
            // 60° rotational step. cwOff mirrors ccwOff across the sector's vertex axis.
            float splitNormal = isCcw ? (210f - 60f * k) : (90f - 60f * k);

            Color sideA = isCcw ? primary : partner;
            Color sideB = isCcw ? partner : primary;
            return LedgeSpriteFactory.GetHexSplitFill(sideA, sideB, splitNormal);
        }

        private static Color AddColor(Color baseColor, Color additive)
        {
            float a = additive.a > 0f ? additive.a : 1f;
            return new Color(
                Mathf.Clamp01(baseColor.r + additive.r * a),
                Mathf.Clamp01(baseColor.g + additive.g * a),
                Mathf.Clamp01(baseColor.b + additive.b * a),
                baseColor.a);
        }

        private void HideLegacyTextCounters()
        {
            if (lightCountTMP != null) lightCountTMP.gameObject.SetActive(false);
            if (darkCountTMP != null) darkCountTMP.gameObject.SetActive(false);
            if (lightCountText != null) lightCountText.gameObject.SetActive(false);
            if (darkCountText != null) darkCountText.gameObject.SetActive(false);
        }
    }
}
