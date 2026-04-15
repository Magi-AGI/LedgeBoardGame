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
        [SerializeField] private float counterSize = 34f;
        [SerializeField] private float counterStackOffset = 4f;

        [Header("Pulse")]
        [SerializeField] private float pulseFrequencyHz = 1.4f;
        [SerializeField] private float pulseMinAlpha = 0.25f;
        [SerializeField] private float pulseMaxAlpha = 0.85f;

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
        private bool _validTarget;
        private bool _movableSource;
        private bool _pulseVisible;

        private readonly List<Image> _counterImages = new List<Image>();

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

            var fillColor = LedgePalette.GetFillColor(meta.ColorLabel);
            if (meta.Type == SpaceType.Center)
                fillColor = LedgePalette.CenterSpaceFill;

            SetFillColor(fillColor);
            SetFrameBaseColor(LedgePalette.GetFrameBaseColor(meta.ColorLabel));

            _hovered = false;
            _selected = false;
            _validTarget = false;
            _movableSource = false;
            ApplyFrameVisual();
            UpdateFrameGlow(instant: true);

            UpdateTokenDisplay(stack);
        }

        /// Fades the top `topCount` of the active chips down to `alpha`. Bottom chips —
        /// including a locked chip at index 0 — keep full opacity so the origin still
        /// reads as "this chip is still planted here." Called by GameController while a
        /// movable stack is in-hand: the picked-up chips look spectral, the locked anchor
        /// does not. A subsequent UpdateTokenDisplay rewrites alphas to opaque, which is
        /// fine because phantom state is always re-applied by the selection flow.
        public void SetPhantomChips(int topCount, float alpha)
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
                var c = img.color;
                c.a = (i >= fadeStart) ? alpha : 1f;
                img.color = c;
            }
        }

        public void ClearPhantomChips()
        {
            for (int i = 0; i < _counterImages.Count; i++)
            {
                var img = _counterImages[i];
                if (img == null) continue;
                var c = img.color;
                if (c.a < 1f)
                {
                    c.a = 1f;
                    img.color = c;
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

            // Dark on bottom, light on top — reads like physical chips stacked with the lighter
            // ones sitting on the darker ones. Order doesn't matter for game rules.
            int index = 0;
            for (int d = 0; d < stack.DarkCount; d++)
                LayoutCounter(_counterImages[index++], LedgePalette.CounterDark, d, totalNeeded);
            for (int l = 0; l < stack.LightCount; l++)
                LayoutCounter(_counterImages[index++], LedgePalette.CounterLight, stack.DarkCount + l, totalNeeded);

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
            ApplyFrameVisual();
        }

        public void SetSelected(bool selected)
        {
            if (_selected == selected) return;
            _selected = selected;
            ApplyFrameVisual();
        }

        public void SetValidTarget(bool valid)
        {
            if (_validTarget == valid) return;
            _validTarget = valid;
            UpdateFrameGlow(instant: !valid);
        }

        public void SetMovableSource(bool active)
        {
            if (_movableSource == active) return;
            _movableSource = active;
            UpdateFrameGlow(instant: !active);
        }

        /// Legacy shim — BoardPresenter.HighlightValidMoves still calls this to toggle the
        /// valid-target glow. Selection highlight is routed via SetSelected.
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

        /// Legacy shim — accepts a color hint but the frame-state model ignores it. Kept so
        /// older call sites compile; BoardPresenter can migrate to SetSelected / SetValidTarget.
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
            if (frameGlowImage == null) return;

            if (_validTarget)
            {
                float t = Mathf.Sin(Time.unscaledTime * pulseFrequencyHz * 2f * Mathf.PI) * 0.5f + 0.5f;
                float a = Mathf.Lerp(pulseMinAlpha, pulseMaxAlpha, t);
                var baseCol = LedgePalette.FrameValidTargetAdd;
                frameGlowImage.color = new Color(baseCol.r, baseCol.g, baseCol.b, a);
            }
            else if (_movableSource)
            {
                float t = Mathf.Sin(Time.unscaledTime * sourceBreatheHz * 2f * Mathf.PI) * 0.5f + 0.5f;
                float a = Mathf.Lerp(sourceMinAlpha, sourceMaxAlpha, t);
                var baseCol = LedgePalette.FrameMovableSourceAdd;
                frameGlowImage.color = new Color(baseCol.r, baseCol.g, baseCol.b, a);
            }
        }

        private void EnsureVisuals()
        {
            var selfRect = (RectTransform)transform;
            if (selfRect.sizeDelta.sqrMagnitude < 0.01f)
                selfRect.sizeDelta = new Vector2(60f, 60f);

            if (fillImage == null)
            {
                fillImage = CreateChildImage("Fill", sibling: 0, stretch: true);
                fillImage.color = LedgePalette.NeutralSpaceFill;
            }
            if (fillImage.sprite == null)
                fillImage.sprite = LedgeSpriteFactory.Disc;
            fillImage.raycastTarget = true;

            if (frameImage == null)
            {
                frameImage = GetComponent<Image>();
                if (frameImage == null)
                    frameImage = CreateChildImage("Frame", sibling: 1, stretch: true);
            }
            // Always replace the frame sprite with the generated ring — the existing prefab
            // uses Unity's default white UISprite which we want swapped out for the circular frame.
            frameImage.sprite = LedgeSpriteFactory.Ring;
            frameImage.raycastTarget = true;
            frameImage.color = _frameBaseColor;

            if (frameGlowImage == null)
            {
                // Ring-shaped glow so the pulse concentrates at the frame edge rather than
                // washing over the entire fill.
                frameGlowImage = CreateChildImage("FrameGlow", sibling: 2, stretch: true);
                frameGlowImage.sprite = LedgeSpriteFactory.Ring;
                frameGlowImage.color = new Color(LedgePalette.FrameValidTargetAdd.r, LedgePalette.FrameValidTargetAdd.g, LedgePalette.FrameValidTargetAdd.b, 0f);
                frameGlowImage.raycastTarget = false;
                var glowRect = (RectTransform)frameGlowImage.transform;
                // Slightly larger than the frame so the glow visibly bleeds outward.
                var selfSize = ((RectTransform)transform).sizeDelta;
                glowRect.sizeDelta = selfSize * 1.18f;
            }

            if (countersRoot == null)
            {
                var go = new GameObject("Counters", typeof(RectTransform));
                countersRoot = (RectTransform)go.transform;
                countersRoot.SetParent(transform, false);
                countersRoot.anchorMin = new Vector2(0.5f, 0.5f);
                countersRoot.anchorMax = new Vector2(0.5f, 0.5f);
                countersRoot.pivot = new Vector2(0.5f, 0.5f);
                countersRoot.anchoredPosition = Vector2.zero;
                countersRoot.sizeDelta = selfRect.sizeDelta;
            }
            countersRoot.SetAsLastSibling();
        }

        private Image CreateChildImage(string name, int sibling, bool stretch)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.SetSiblingIndex(sibling);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = stretch ? ((RectTransform)transform).sizeDelta : new Vector2(32f, 32f);
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
            return img;
        }

        private void LayoutCounter(Image img, Color color, int indexInStack, int totalInStack)
        {
            if (img == null) return;
            img.gameObject.SetActive(true);
            img.color = color;
            // Stack from the bottom up: first chip sits lowest, each subsequent chip rises.
            float baseY = -(totalInStack - 1) * counterStackOffset * 0.5f;
            float y = baseY + indexInStack * counterStackOffset;
            var rect = (RectTransform)img.transform;
            rect.anchoredPosition = new Vector2(0f, y);
            rect.SetAsLastSibling();
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
            if (_validTarget)
            {
                if (instant)
                {
                    var baseCol = LedgePalette.FrameValidTargetAdd;
                    frameGlowImage.color = new Color(baseCol.r, baseCol.g, baseCol.b, pulseMinAlpha);
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

        private static Color AddColor(Color baseColor, Color additive)
        {
            // Add additive's rgb scaled by its own alpha so callers tune intensity via the alpha channel.
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
