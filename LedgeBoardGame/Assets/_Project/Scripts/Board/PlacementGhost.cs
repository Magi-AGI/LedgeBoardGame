using UnityEngine;
using UnityEngine.UI;
using Magi.LedgeBoardGame.Models;

namespace Magi.LedgeBoardGame.Board
{
    /// Translucent counter that follows the cursor during GamePhase.Placement so the player
    /// knows which tone they're about to drop. Hidden in Movement phase and when the game is over.
    [RequireComponent(typeof(RectTransform))]
    public class PlacementGhost : MonoBehaviour
    {
        [SerializeField] private Image counterImage;
        [SerializeField] private float counterSize = 44f;
        [SerializeField] private float alpha = 0.55f;
        [SerializeField] private float followSmoothing = 18f;

        private RectTransform _selfRect;
        private RectTransform _parentCanvasRect;
        private Canvas _canvas;
        private bool _visible;
        private bool _hasCursorPos;
        private Vector2 _cursorLocal;

        private void Awake()
        {
            _selfRect = (RectTransform)transform;
            EnsureVisuals();
            SetVisible(false);
        }

        private void EnsureVisuals()
        {
            if (counterImage == null)
            {
                var go = new GameObject("GhostCounter", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var rect = (RectTransform)go.transform;
                rect.SetParent(transform, false);
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(counterSize, counterSize);
                counterImage = go.GetComponent<Image>();
                counterImage.raycastTarget = false;
            }
            counterImage.sprite = LedgeSpriteFactory.Counter;
        }

        public void SetTone(Tone tone)
        {
            var baseColor = tone == Tone.Light ? LedgePalette.CounterLight : LedgePalette.CounterDark;
            baseColor.a = alpha;
            counterImage.color = baseColor;
        }

        public void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            _visible = visible;
            if (counterImage != null)
                counterImage.enabled = visible;
            // Reset smoothing state so the ghost snaps to the cursor on reveal instead of
            // sliding from its last-hidden position.
            if (visible) _hasCursorPos = false;
        }

        /// Routed from GameController after every state change so the ghost reflects current phase.
        public void Refresh(GameState state)
        {
            if (state == null || state.GameOver || state.CurrentPhase != GamePhase.Placement)
            {
                SetVisible(false);
                return;
            }

            if (state.HasPlacedLight && state.HasPlacedDark)
            {
                SetVisible(false);
                return;
            }

            SetTone(state.HasPlacedLight ? Tone.Dark : Tone.Light);
            SetVisible(true);
        }

        private void Update()
        {
            if (!_visible) return;
            EnsureCanvasCache();
            if (_parentCanvasRect == null) return;

            Vector2 localPoint;
            var cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_parentCanvasRect, Input.mousePosition, cam, out localPoint))
                return;

            if (!_hasCursorPos)
            {
                _cursorLocal = localPoint;
                _hasCursorPos = true;
            }
            else
            {
                // Exponential smoothing gives the chip a touch of inertia, suggesting weight.
                float t = 1f - Mathf.Exp(-followSmoothing * Time.unscaledDeltaTime);
                _cursorLocal = Vector2.Lerp(_cursorLocal, localPoint, t);
            }
            _selfRect.anchoredPosition = _cursorLocal;
        }

        private void EnsureCanvasCache()
        {
            if (_parentCanvasRect != null && _canvas != null) return;
            _canvas = GetComponentInParent<Canvas>();
            if (_canvas != null)
            {
                _parentCanvasRect = (RectTransform)_canvas.transform;
                var selfParent = _selfRect.parent as RectTransform;
                if (selfParent != null) _parentCanvasRect = selfParent;
            }
        }
    }
}
