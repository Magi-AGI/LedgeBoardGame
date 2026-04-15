using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Magi.LedgeBoardGame.Models;

namespace Magi.LedgeBoardGame.Board
{
    /// Translucent stack that follows the cursor with a soft lag while a movement source is
    /// selected, visually representing the chips the player has "picked up." Hidden when no
    /// stack is in hand. GameController drives it via SetStack(light, dark) on selection +
    /// deselection; the lag is pure cosmetics so a fast cursor flick doesn't feel rigid.
    [RequireComponent(typeof(RectTransform))]
    public class InHandGhost : MonoBehaviour
    {
        [SerializeField] private float chipSize = 38f;
        [SerializeField] private float stackOffset = 4f;
        [SerializeField] private float alpha = 1f;
        [SerializeField] private float followSmoothing = 18f;

        private RectTransform _selfRect;
        private Canvas _canvas;
        private RectTransform _canvasRect;
        private readonly List<Image> _chips = new List<Image>();
        private int _lightCount;
        private int _darkCount;
        private bool _visible;
        private bool _hasCursorPos;
        private Vector2 _cursorLocal;

        private void Awake()
        {
            _selfRect = (RectTransform)transform;
            SetVisible(false);
        }

        public void SetStack(int lightCount, int darkCount)
        {
            _lightCount = Mathf.Max(0, lightCount);
            _darkCount = Mathf.Max(0, darkCount);
            int total = _lightCount + _darkCount;
            if (total == 0)
            {
                SetVisible(false);
                HideAllChips();
                return;
            }
            RebuildChips();
            SetVisible(true);
        }

        private void RebuildChips()
        {
            int total = _lightCount + _darkCount;
            while (_chips.Count < total) _chips.Add(CreateChip());
            for (int i = total; i < _chips.Count; i++)
            {
                if (_chips[i] != null) _chips[i].gameObject.SetActive(false);
            }

            float baseY = -(total - 1) * stackOffset * 0.5f;
            int idx = 0;
            for (int d = 0; d < _darkCount; d++)
            {
                Place(_chips[idx], LedgePalette.CounterDark, idx, baseY);
                idx++;
            }
            for (int l = 0; l < _lightCount; l++)
            {
                Place(_chips[idx], LedgePalette.CounterLight, idx, baseY);
                idx++;
            }
        }

        private void Place(Image img, Color color, int stackIndex, float baseY)
        {
            img.gameObject.SetActive(true);
            img.color = new Color(color.r, color.g, color.b, alpha);
            var rt = (RectTransform)img.transform;
            rt.anchoredPosition = new Vector2(0f, baseY + stackIndex * stackOffset);
            rt.sizeDelta = new Vector2(chipSize, chipSize);
            rt.SetAsLastSibling();
        }

        private Image CreateChip()
        {
            var go = new GameObject("HandChip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(transform, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(chipSize, chipSize);
            var img = go.GetComponent<Image>();
            img.sprite = LedgeSpriteFactory.Counter;
            img.raycastTarget = false;
            return img;
        }

        private void HideAllChips()
        {
            foreach (var c in _chips)
            {
                if (c != null) c.gameObject.SetActive(false);
            }
        }

        private void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            _visible = visible;
            foreach (var c in _chips)
            {
                if (c != null) c.enabled = visible;
            }
            // Snap to cursor on first reveal so there's no drift from the last hidden
            // position toward the new one.
            if (visible) _hasCursorPos = false;
        }

        private void Update()
        {
            if (!_visible) return;
            EnsureCanvasCache();
            if (_canvasRect == null) return;

            Vector2 localPoint;
            var cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            if (!TryReadPointer(out Vector2 screenPos)) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPos, cam, out localPoint))
                return;

            if (!_hasCursorPos)
            {
                _cursorLocal = localPoint;
                _hasCursorPos = true;
            }
            else
            {
                // Exponential smoothing — higher followSmoothing = tighter follow. Default
                // lands around "chip catches up in ~60ms", which feels like weight without lag.
                float t = 1f - Mathf.Exp(-followSmoothing * Time.unscaledDeltaTime);
                _cursorLocal = Vector2.Lerp(_cursorLocal, localPoint, t);
            }
            _selfRect.anchoredPosition = _cursorLocal;
        }

        private void EnsureCanvasCache()
        {
            if (_canvas != null && _canvasRect != null) return;
            _canvas = GetComponentInParent<Canvas>();
            if (_canvas != null)
            {
                var parent = _selfRect.parent as RectTransform;
                _canvasRect = parent != null ? parent : (RectTransform)_canvas.transform;
            }
        }

        // Reads the pointer position under whichever input backend is active. Project
        // is on the new Input System (com.unity.inputsystem), which makes legacy
        // UnityEngine.Input.mousePosition throw at runtime.
        private static bool TryReadPointer(out Vector2 screenPos)
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null)
            {
                screenPos = mouse.position.ReadValue();
                return true;
            }
            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.isPressed)
            {
                screenPos = touch.primaryTouch.position.ReadValue();
                return true;
            }
            screenPos = default;
            return false;
#else
            screenPos = Input.mousePosition;
            return true;
#endif
        }
    }
}
