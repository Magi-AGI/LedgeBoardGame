using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Magi.LedgeBoardGame.Board
{
    /// Scroll + zoom the multi-board container so 6-8 player sessions are navigable.
    /// Boards are spaced at ~1000px in a fixed 1600x900 Canvas viewport, which
    /// overflows past 4 seats. Zooming via mouse wheel and panning via middle-mouse
    /// drag gives players reach without a full ScrollRect rewire. Auto-fit on enable
    /// chooses an initial scale that frames the current board count.
    [RequireComponent(typeof(RectTransform))]
    public class MultiBoardPanZoom : MonoBehaviour
    {
        [SerializeField] private float minScale = 0.2f;
        [SerializeField] private float maxScale = 1.5f;
        [SerializeField] private float zoomStep = 0.1f;
        [SerializeField] private float fitPadding = 100f;
        [SerializeField] private KeyCode recenterKey = KeyCode.F;

        private RectTransform _rect;
        private RectTransform _viewportRect;
        private MultiBoardLayout _layout;
        private bool _dragging;
        private Vector2 _lastPointer;
        private int _lastBoardCount = -1;
        // Per-board hover-scoped zoom/pan. When the pointer is over a
        // specific board's rect, mouse-wheel zoom and middle/right drag
        // operate on that board alone instead of the whole container —
        // lets players inspect one opponent without shifting everybody
        // else. Drops back to container-scope when no board is hovered
        // (e.g. margin drag for a quick recenter).
        private readonly Dictionary<BoardPresenter, float> _boardScale = new Dictionary<BoardPresenter, float>();
        private readonly Dictionary<BoardPresenter, Vector2> _boardPanOffset = new Dictionary<BoardPresenter, Vector2>();
        private BoardPresenter _activeDragBoard;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _layout = GetComponent<MultiBoardLayout>();
            var parent = transform.parent as RectTransform;
            _viewportRect = parent != null ? parent : _rect;
        }

        private void OnEnable()
        {
            _lastBoardCount = -1;
        }

        private void Update()
        {
            var presenters = _layout != null ? _layout.GetComponentsInChildren<BoardPresenter>(false) : null;
            int count = presenters != null ? presenters.Length : 0;
            bool locked = _layout != null && _layout.Mode == MultiBoardLayout.ViewMode.Comparison;

            if (count != _lastBoardCount && count > 0)
            {
                _lastBoardCount = count;
                FitToViewport(presenters);
            }

            // Comparison view keeps its canonical fit — suppress zoom and
            // hotkey-refit so players can't drift off the side-by-side
            // frame. Pan stays available either way: in playtest Lake
            // reported being able to zoom but not slide the board around,
            // so the minimum useful inspection gesture is drag-to-pan
            // regardless of mode.
            HandlePan(presenters);
            if (locked) return;

            HandleZoom(presenters);
            HandleHotkeys(presenters);
        }

        /// Force a refit — called after a view-mode change when the set of
        /// visible boards changes but the total presenter count hasn't.
        /// Also clears per-board hover state so a fresh layout starts at
        /// uniform scale and zero offset.
        public void RequestFit()
        {
            _lastBoardCount = -1;
            ResetPerBoardState();
        }

        private void HandleZoom(BoardPresenter[] presenters)
        {
            if (!TryReadScroll(out float scrollY) || Mathf.Approximately(scrollY, 0f)) return;
            if (!TryReadPointer(out var pointer)) return;

            var hovered = FindHoveredBoard(presenters, pointer);
            if (hovered != null)
            {
                ZoomBoard(hovered, scrollY);
                return;
            }

            float current = _rect.localScale.x;
            float next = Mathf.Clamp(current + Mathf.Sign(scrollY) * zoomStep, minScale, maxScale);
            if (Mathf.Approximately(next, current)) return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewportRect, pointer, null, out var viewportLocal))
                return;

            var before = _rect.anchoredPosition;
            var offset = before - viewportLocal;
            float ratio = next / current;
            _rect.anchoredPosition = viewportLocal + offset * ratio;
            _rect.localScale = new Vector3(next, next, 1f);
        }

        private void ZoomBoard(BoardPresenter board, float scrollY)
        {
            var rt = board.GetComponent<RectTransform>();
            if (rt == null) return;
            float current = rt.localScale.x;
            if (current <= 0f) current = 1f;
            float next = Mathf.Clamp(current + Mathf.Sign(scrollY) * zoomStep, minScale, maxScale);
            if (Mathf.Approximately(next, current)) return;
            rt.localScale = new Vector3(next, next, 1f);
            _boardScale[board] = next;
        }

        private void HandlePan(BoardPresenter[] presenters)
        {
            bool pressed = TryReadMiddleOrRightButton(out bool justPressed, out bool justReleased);
            if (!TryReadPointer(out var pointer)) return;

            if (justPressed)
            {
                _dragging = true;
                _lastPointer = pointer;
                // Capture which board (if any) the press started on —
                // the drag stays scoped to that board for its duration
                // even if the pointer wanders into another board's rect
                // partway through, which would otherwise cause a visual
                // jump as different boards start shifting.
                _activeDragBoard = FindHoveredBoard(presenters, pointer);
            }
            else if (justReleased)
            {
                _dragging = false;
                _activeDragBoard = null;
            }

            if (_dragging && pressed)
            {
                var delta = pointer - _lastPointer;
                if (_activeDragBoard != null)
                {
                    var rt = _activeDragBoard.GetComponent<RectTransform>();
                    if (rt != null) rt.anchoredPosition += delta;
                    if (_boardPanOffset.TryGetValue(_activeDragBoard, out var acc)) _boardPanOffset[_activeDragBoard] = acc + delta;
                    else _boardPanOffset[_activeDragBoard] = delta;
                }
                else
                {
                    _rect.anchoredPosition += delta;
                }
                _lastPointer = pointer;
            }
        }

        private static BoardPresenter FindHoveredBoard(BoardPresenter[] presenters, Vector2 screenPoint)
        {
            if (presenters == null) return null;
            // Reverse iterate so boards drawn on top (later in the sibling
            // order) win hover ties — matches the natural UI stacking.
            for (int i = presenters.Length - 1; i >= 0; i--)
            {
                var p = presenters[i];
                if (p == null || !p.isActiveAndEnabled) continue;
                var rt = p.GetComponent<RectTransform>();
                if (rt == null) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(rt, screenPoint, null))
                    return p;
            }
            return null;
        }

        private void ResetPerBoardState()
        {
            _boardScale.Clear();
            _boardPanOffset.Clear();
            _activeDragBoard = null;
        }

        private void HandleHotkeys(BoardPresenter[] presenters)
        {
            if (TryReadKey(recenterKey))
            {
                FitToViewport(presenters);
            }
        }

        public void FitToViewport(IList<BoardPresenter> presenters)
        {
            if (presenters == null || presenters.Count == 0 || _viewportRect == null) return;

            float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
            int sampled = 0;
            foreach (var p in presenters)
            {
                if (p == null) continue;
                var rt = p.GetComponent<RectTransform>();
                if (rt == null) continue;
                var pos = rt.anchoredPosition;
                var size = rt.rect.size;
                float half = Mathf.Max(size.x, size.y) * 0.5f;
                if (half <= 0f) half = 400f;
                minX = Mathf.Min(minX, pos.x - half);
                maxX = Mathf.Max(maxX, pos.x + half);
                minY = Mathf.Min(minY, pos.y - half);
                maxY = Mathf.Max(maxY, pos.y + half);
                sampled++;
            }
            if (sampled == 0) return;

            float contentW = Mathf.Max(1f, maxX - minX) + fitPadding * 2f;
            float contentH = Mathf.Max(1f, maxY - minY) + fitPadding * 2f;
            var viewSize = _viewportRect.rect.size;
            float scale = Mathf.Min(viewSize.x / contentW, viewSize.y / contentH);
            scale = Mathf.Clamp(scale, minScale, maxScale);

            _rect.localScale = new Vector3(scale, scale, 1f);
            _rect.anchoredPosition = Vector2.zero;
            // Any per-board zoom/pan residue is now inconsistent with the
            // fresh container fit — reset so boards start uniform.
            foreach (var p in presenters)
            {
                if (p == null) continue;
                var rt = p.GetComponent<RectTransform>();
                if (rt != null) rt.localScale = Vector3.one;
            }
            ResetPerBoardState();
        }

#if ENABLE_INPUT_SYSTEM
        private static bool TryReadPointer(out Vector2 pos)
        {
            var mouse = Mouse.current;
            if (mouse != null) { pos = mouse.position.ReadValue(); return true; }
            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.isPressed) { pos = touch.primaryTouch.position.ReadValue(); return true; }
            pos = default;
            return false;
        }

        private static bool TryReadScroll(out float y)
        {
            var mouse = Mouse.current;
            if (mouse != null) { y = mouse.scroll.ReadValue().y; return true; }
            y = 0f;
            return false;
        }

        private bool TryReadMiddleOrRightButton(out bool justPressed, out bool justReleased)
        {
            justPressed = false;
            justReleased = false;
            var mouse = Mouse.current;
            if (mouse == null) return false;
            bool mid = mouse.middleButton.isPressed;
            bool right = mouse.rightButton.isPressed;
            bool midDown = mouse.middleButton.wasPressedThisFrame;
            bool rightDown = mouse.rightButton.wasPressedThisFrame;
            bool midUp = mouse.middleButton.wasReleasedThisFrame;
            bool rightUp = mouse.rightButton.wasReleasedThisFrame;
            justPressed = midDown || rightDown;
            justReleased = midUp || rightUp;
            return mid || right;
        }

        private static bool TryReadKey(KeyCode key)
        {
            var kb = Keyboard.current;
            if (kb == null) return false;
            switch (key)
            {
                case KeyCode.F: return kb.fKey.wasPressedThisFrame;
                case KeyCode.R: return kb.rKey.wasPressedThisFrame;
                case KeyCode.Space: return kb.spaceKey.wasPressedThisFrame;
                default: return false;
            }
        }
#else
        private static bool TryReadPointer(out Vector2 pos) { pos = Input.mousePosition; return true; }
        private static bool TryReadScroll(out float y) { y = Input.mouseScrollDelta.y; return true; }
        private bool TryReadMiddleOrRightButton(out bool justPressed, out bool justReleased)
        {
            justPressed = Input.GetMouseButtonDown(2) || Input.GetMouseButtonDown(1);
            justReleased = Input.GetMouseButtonUp(2) || Input.GetMouseButtonUp(1);
            return Input.GetMouseButton(2) || Input.GetMouseButton(1);
        }
        private static bool TryReadKey(KeyCode key) { return Input.GetKeyDown(key); }
#endif
    }
}
