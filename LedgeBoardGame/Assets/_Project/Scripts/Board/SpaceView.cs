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

        // CP064 replacement (Claude Design "silhouette-matched keyline rim +
        // one-shot silhouette shockwave"): all three visitor layers reuse the
        // tile's own fillImage sprite/rotation, so they are automatically
        // correct on every silhouette including the bespoke bridge/wall polys
        // — never a generic hex ring. See SetVisitorOverlay/TickVisitorRim.
        [Header("Visitor Rim (CP064 replacement)")]
        [SerializeField] private float visitorKeylineScale = 1.045f;
        [SerializeField] private float visitorRimScale = 1.115f;
        [SerializeField] private float visitorWaveEndScale = 1.55f;
        [SerializeField] private float visitorWaveDuration = 0.30f;
        [SerializeField] private float visitorRimAlphaMin = 0.55f;
        [SerializeField] private float visitorRimAlphaMax = 0.85f;
        [SerializeField] private float visitorRimPulsePeriodSeconds = 2.4f;
        [SerializeField] private float visitorKeylineAlpha = 0.95f;
        [SerializeField] private float visitorWaveStartAlpha = 0.95f;

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
                hoverLabelTMP.textWrappingMode = TextWrappingModes.Normal;
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

        // ── Visitor overlay (CP064 replacement — Claude Design "silhouette-
        // matched keyline rim + one-shot silhouette shockwave", verdict
        // approve_with_replacement) ─────────────────────────────────────
        // Superseded: the previous flat-tint + diffuse frame-glow-based halo
        // (CP054/CP064 Option A) read as a soft blob that washed out against
        // the board's already-saturated wedge fills, and a generic hex ring
        // would mis-fit the bespoke 12-vertex bridge / 6-vertex wall polygons.
        // The replacement stacks three copies of the tile's own fillImage
        // sprite (so it is automatically correct for every silhouette),
        // entirely behind the opaque tile face, so only a crisp outboard band
        // of each copy is ever visible:
        //   VisitorWave (farthest)  — one-shot entry shockwave, then hidden.
        //   VisitorRim              — accent-colored steady band, alpha-breathes.
        //   VisitorKeyline          — thin near-black band that separates the
        //                             accent rim from the wedge fill color so
        //                             it never reads as a wash/tint.
        // Sibling order behind Fill/Frame/FrameGlow is set once at creation
        // (see EnsureVisitorRimLayers) and is the whole trick: if these ever
        // land in front of the tile face, the implementation is wrong.
        private Image _visitorWaveImage;
        private Image _visitorRimImage;
        private Image _visitorKeylineImage;
        private Color _visitorAccentColor;
        private bool _visitorActive;
        private bool _visitorWaveActive;
        private float _visitorWaveElapsed;

        // Board background near-black family (#0F0D0A) — the keyline's only
        // job is to separate the accent rim from the wedge fill, never to add
        // its own hue.
        private static readonly Color VisitorKeylineColor = new Color(0.0588f, 0.0510f, 0.0392f, 1f);

        public void SetVisitorOverlay(Color accent, float alpha = 0.5f)
        {
            // `alpha` is retained only so BoardPresenter/GameController callers
            // don't need to change; the replacement primitive has no flat fill
            // tint to apply it to (Design: "do not fill/tint the tile face").
            EnsureVisitorRimLayers();
            _visitorAccentColor = accent;
            _visitorActive = true;

            if (_visitorRimImage != null)
            {
                // Assign the rim's accent color immediately rather than
                // waiting for the next Update()/TickVisitorRim() tick — the
                // reviewer fix packet flagged that the first rendered frame
                // could otherwise show a stale/transparent rim for one frame.
                _visitorRimImage.color = new Color(accent.r, accent.g, accent.b, ComputeSteadyRimAlpha());
                _visitorRimImage.rectTransform.localScale = Vector3.one * visitorRimScale;
                _visitorRimImage.gameObject.SetActive(true);
            }
            if (_visitorKeylineImage != null)
            {
                _visitorKeylineImage.color = new Color(VisitorKeylineColor.r, VisitorKeylineColor.g, VisitorKeylineColor.b, visitorKeylineAlpha);
                _visitorKeylineImage.rectTransform.localScale = Vector3.one * visitorKeylineScale;
                _visitorKeylineImage.gameObject.SetActive(true);
            }
            if (_visitorWaveImage != null)
            {
                // Restart from t=0 rather than stacking if a repeated call
                // lands on the same tile without an intervening clear.
                _visitorWaveActive = true;
                _visitorWaveElapsed = 0f;
                _visitorWaveImage.rectTransform.localScale = Vector3.one;
                _visitorWaveImage.color = new Color(accent.r, accent.g, accent.b, visitorWaveStartAlpha);
                _visitorWaveImage.gameObject.SetActive(true);
            }
        }

        public void ClearVisitorOverlay()
        {
            _visitorActive = false;
            _visitorWaveActive = false;
            _visitorWaveElapsed = 0f;
            // Reset every layer to a fully idle state — disabled, scale 1,
            // alpha 0 — not just disabled-with-stale-color. Reviewer fix
            // packet flagged that a disabled-but-still-colored rim/keyline
            // is visually harmless while inactive but violates the design
            // reset criterion and risks a stale first frame on reactivation.
            if (_visitorRimImage != null)
            {
                _visitorRimImage.gameObject.SetActive(false);
                _visitorRimImage.rectTransform.localScale = Vector3.one;
                var c = _visitorRimImage.color;
                c.a = 0f;
                _visitorRimImage.color = c;
            }
            if (_visitorKeylineImage != null)
            {
                _visitorKeylineImage.gameObject.SetActive(false);
                _visitorKeylineImage.rectTransform.localScale = Vector3.one;
                var c = _visitorKeylineImage.color;
                c.a = 0f;
                _visitorKeylineImage.color = c;
            }
            if (_visitorWaveImage != null)
            {
                _visitorWaveImage.gameObject.SetActive(false);
                _visitorWaveImage.rectTransform.localScale = Vector3.one;
                var c = _visitorWaveImage.color;
                c.a = 0f;
                _visitorWaveImage.color = c;
            }
        }

        // Creation order matters: SetSiblingIndex(0) then (1) then (2), in
        // Wave/Rim/Keyline order, pushes each new layer in front of the last
        // while shifting Fill/Frame/FrameGlow later — the net result is
        // Wave(0), Rim(1), Keyline(2), Fill(3+), regardless of how many
        // siblings shapeRoot already had. This runs once, lazily, on first
        // visitor use per tile.
        private void EnsureVisitorRimLayers()
        {
            if (_visitorWaveImage != null && _visitorRimImage != null && _visitorKeylineImage != null) return;
            if (fillImage == null || shapeRoot == null) return;

            if (_visitorWaveImage == null)
            {
                _visitorWaveImage = CreateVisitorSilhouetteChild("VisitorWave");
                _visitorWaveImage.transform.SetSiblingIndex(0);
            }
            if (_visitorRimImage == null)
            {
                _visitorRimImage = CreateVisitorSilhouetteChild("VisitorRim");
                _visitorRimImage.transform.SetSiblingIndex(1);
            }
            if (_visitorKeylineImage == null)
            {
                _visitorKeylineImage = CreateVisitorSilhouetteChild("VisitorKeyline");
                _visitorKeylineImage.transform.SetSiblingIndex(2);
            }
        }

        // Reuses fillImage's own sprite so the overlay always matches the
        // tile's real silhouette — hex, bespoke bridge 12-gon, or bespoke wall
        // hexagon — with no per-shape special-casing. Uniform scale only (no
        // independent x/y, no position offset): any asymmetry breaks the
        // constant-band illusion on the bespoke polys.
        private Image CreateVisitorSilhouetteChild(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(shapeRoot, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;

            var img = go.GetComponent<Image>();
            img.sprite = fillImage.sprite;
            img.raycastTarget = false;
            img.color = new Color(0f, 0f, 0f, 0f);
            go.SetActive(false);

            ApplyVisitorSilhouetteTransform(rt);
            return img;
        }

        private void ApplyVisitorSilhouetteTransform(RectTransform rt)
        {
            if (fillImage == null) return;
            rt.sizeDelta = ((RectTransform)transform).sizeDelta;
            rt.localRotation = fillImage.transform.localRotation;
        }

        /// Called after ApplyShapeAndFill assigns a (possibly new) fillImage
        /// sprite/rotation so the visitor layers stay in sync with the tile's
        /// current shape instead of freezing on whatever shape was active when
        /// the visitor layers were first created (pooled/reused SpaceViews).
        private void RefreshVisitorSilhouetteLayers()
        {
            if (fillImage == null) return;
            if (_visitorWaveImage != null)
            {
                _visitorWaveImage.sprite = fillImage.sprite;
                ApplyVisitorSilhouetteTransform(_visitorWaveImage.rectTransform);
            }
            if (_visitorRimImage != null)
            {
                _visitorRimImage.sprite = fillImage.sprite;
                ApplyVisitorSilhouetteTransform(_visitorRimImage.rectTransform);
            }
            if (_visitorKeylineImage != null)
            {
                _visitorKeylineImage.sprite = fillImage.sprite;
                ApplyVisitorSilhouetteTransform(_visitorKeylineImage.rectTransform);
            }

            // Reassert draw order (Wave, Rim, Keyline, then Fill/Frame/
            // FrameGlow) in case a future pooling/prefab path reordered
            // shapeRoot's children — cheap no-op in the common case where
            // order was never disturbed.
            if (_visitorWaveImage != null) _visitorWaveImage.transform.SetSiblingIndex(0);
            if (_visitorRimImage != null) _visitorRimImage.transform.SetSiblingIndex(1);
            if (_visitorKeylineImage != null) _visitorKeylineImage.transform.SetSiblingIndex(2);
        }

        /// Drives the steady accent-rim alpha breathe (pulses alpha only,
        /// never scale — a size-pulsing tile reads as a hover/selection
        /// affordance) and, while active, the one-shot entry wave: a hard-
        /// edged copy of the tile's own silhouette that flings outward
        /// (easeOutCubic scale) and dissolves (held solid for the first 15%,
        /// then easeOutQuad alpha to 0) over visitorWaveDuration seconds.
        // Shared by SetVisitorOverlay (immediate first-frame color) and
        // TickVisitorRim (every subsequent frame) so the rim never shows a
        // stale/default alpha before its first Update() tick.
        private float ComputeSteadyRimAlpha()
        {
            float period = Mathf.Max(0.01f, visitorRimPulsePeriodSeconds);
            float hz = 1f / period;
            float t = Mathf.Sin(Time.unscaledTime * hz * 2f * Mathf.PI) * 0.5f + 0.5f;
            return Mathf.Lerp(visitorRimAlphaMin, visitorRimAlphaMax, t);
        }

        // Single frames can spike well past a normal frame time — an editor
        // hitch, a test-harness delay between activating the overlay and its
        // first tick, or a paused-then-resumed window. Uncapped, that single
        // spike could advance _visitorWaveElapsed past visitorWaveDuration in
        // one step and the one-shot wave would never be observed active.
        // Capping the per-frame step preserves the intended ~300ms duration
        // under normal frame rates while guaranteeing the wave is visible
        // (active, partway through its scale/alpha animation) for at least
        // one real frame even after an anomalously long frame.
        private const float VisitorWaveMaxFrameStep = 0.05f;

        private void TickVisitorRim()
        {
            if (!_visitorActive) return;

            if (_visitorRimImage != null)
            {
                float alpha = ComputeSteadyRimAlpha();
                _visitorRimImage.color = new Color(_visitorAccentColor.r, _visitorAccentColor.g, _visitorAccentColor.b, alpha);
            }

            if (_visitorWaveActive && _visitorWaveImage != null)
            {
                _visitorWaveElapsed += Mathf.Min(Time.unscaledDeltaTime, VisitorWaveMaxFrameStep);
                float waveT = Mathf.Clamp01(_visitorWaveElapsed / Mathf.Max(0.01f, visitorWaveDuration));

                float scaleEase = 1f - Mathf.Pow(1f - waveT, 3f);
                float scale = Mathf.Lerp(1f, visitorWaveEndScale, scaleEase);

                const float holdFraction = 0.15f;
                float alpha;
                if (waveT <= holdFraction)
                {
                    alpha = visitorWaveStartAlpha;
                }
                else
                {
                    float fadeT = (waveT - holdFraction) / (1f - holdFraction);
                    float fadeEase = 1f - (1f - fadeT) * (1f - fadeT);
                    alpha = Mathf.Lerp(visitorWaveStartAlpha, 0f, fadeEase);
                }

                _visitorWaveImage.rectTransform.localScale = Vector3.one * scale;
                _visitorWaveImage.color = new Color(_visitorAccentColor.r, _visitorAccentColor.g, _visitorAccentColor.b, alpha);

                if (waveT >= 1f)
                {
                    _visitorWaveActive = false;
                    _visitorWaveImage.gameObject.SetActive(false);
                    _visitorWaveImage.rectTransform.localScale = Vector3.one;
                }
            }
        }

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
            TickVisitorRim();

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
                    // to the wedge-aligned rotation. The 180° flip means the sprite-local +Y end
                    // (named `outerColor` by the sprite factory) lands on world-inner. To get
                    // world-outer = own / world-inner = complement (matching Ring 2), we pass
                    // own as the sprite's *inner* arg and complement as the sprite's *outer* arg.
                    rotZ = 180f - 30f * meta.WedgeIndex;
                    var ownColor = LedgePalette.GetOwnColor(meta.WedgeIndex);
                    var complementColor = LedgePalette.GetComplementColor(meta.WedgeIndex);
                    fillSprite = LedgeSpriteFactory.GetBridgeFill(outerColor: complementColor, innerColor: ownColor);
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
                    // Split: world-outer = own, world-inner = complement. BuildHexSplitFill
                    // computes dy as (Size-1-fy)-cx, which is y-down in sprite-local space —
                    // so the baked +normal at `splitNormal` points to world-(-splitNormal) after
                    // rendering. rotZ = splitNormal + wedgeAngle = 2·wedgeAngle = 180° - 60°·w
                    // cancels the flip and aligns sideA (own) with the wedge's outer radial.
                    var own = LedgePalette.GetOwnColor(meta.WedgeIndex);
                    var complement = LedgePalette.GetComplementColor(meta.WedgeIndex);
                    float splitNormal = LedgePalette.GetWedgeAngleDeg(meta.WedgeIndex);
                    fillSprite = LedgeSpriteFactory.GetHexSplitFill(own, complement, splitNormal);
                    rotZ = 180f - 60f * meta.WedgeIndex;
                    frameSprite = LedgeSpriteFactory.HexFrame;
                    glowSprite = LedgeSpriteFactory.HexFrameGlow;
                    break;
                }

                case SpaceType.Ring3:
                {
                    bool hasLabel = !string.IsNullOrEmpty(meta.ColorLabel);
                    if (hasLabel)
                    {
                        // Ring3 vertex ledge: solid own color.
                        fillSprite = LedgeSpriteFactory.GetHexFill(LedgePalette.GetOwnColor(meta.WedgeIndex));
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
                    // Outer-axis ledge: solid own color.
                    fillSprite = LedgeSpriteFactory.GetHexFill(LedgePalette.GetOwnColor(meta.WedgeIndex));
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

            RefreshVisitorSilhouetteLayers();
        }

        // Per-space rotZ for Ring3-off hexes (ids 25..36). Drives the on-screen
        // direction of the colour split. Within each (ccw, cw) sector pair the
        // cw rotation differs from the ccw by ±180° (same line, opposite sides).
        //
        // Values (2026-05-05) were derived by hand-tuning each tile's rotation
        // in the Unity editor against the canonical reference wheel image, then
        // folding the per-tile root rotation back into this table so the
        // SpaceView root transforms can stay at identity. Compared to the
        // pre-2026-05-05 table the per-sector pair rotates by 60° (even-k pair
        // CW 60°, odd-k pair CCW 60°) — which is one hex-edge of rotation.
        private static readonly float[] Ring3OffRotZ = new float[]
        {
               0f, -180f, // 25 (k=0 ccw), 26 (k=0 cw)
             -60f,  120f, // 27 (k=1 ccw), 28 (k=1 cw)
             120f,  -60f, // 29 (k=2 ccw), 30 (k=2 cw)
              60f, -120f, // 31 (k=3 ccw), 32 (k=3 cw)
            -120f,   60f, // 33 (k=4 ccw), 34 (k=4 cw)
             180f,    0f, // 35 (k=5 ccw), 36 (k=5 cw)
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

            var primary = LedgePalette.GetOwnColor(primaryWedgeIndex);
            var partner = LedgePalette.GetOwnColor(partnerWedge);

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
