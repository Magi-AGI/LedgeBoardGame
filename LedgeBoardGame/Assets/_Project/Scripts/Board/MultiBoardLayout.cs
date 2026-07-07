using System.Collections.Generic;
using UnityEngine;

namespace Magi.LedgeBoardGame.Board
{
    /// <summary>
    /// Positions BoardPresenter instances under the same parent. Supports two
    /// view modes:
    ///   - BirdsEye: grid (default; the only mode for &lt;= 4 seats).
    ///   - Comparison: one board pinned left, one opponent right, others
    ///     hidden. The left-slot policy is mode-dependent:
    ///       Local  — left = this client's seat board (network play, where
    ///                only one player drives this instance). The right slot
    ///                auto-jumps to the active player's board when it isn't
    ///                already this client's seat, so the in-progress turn is
    ///                always on screen.
    ///       Active — left = current-turn board (hot-seat practice, where
    ///                the same instance drives every seat in rotation).
    ///                Players cycle the right slot manually.
    /// </summary>
    [ExecuteAlways]
    public class MultiBoardLayout : MonoBehaviour
    {
        public enum ViewMode { BirdsEye, Comparison }
        public enum LeftSlotPolicy { Local, Active }

        [SerializeField] private List<BoardPresenter> presenters = new List<BoardPresenter>();
        [SerializeField] private int columns = 2;
        [SerializeField] private bool autoColumns = true;
        [SerializeField] private Vector2 spacing = new Vector2(1200f, 1200f);
        [SerializeField] private Vector2 startOffset = new Vector2(0f, 0f);
        [SerializeField] private ViewMode mode = ViewMode.BirdsEye;
        [SerializeField] private float comparisonSlotX = 550f;

        private int _localBoardId = -1;
        private int _activeBoardId = -1;
        private int _comparisonOpponentBoardId = -1;
        private LeftSlotPolicy _leftSlotPolicy = LeftSlotPolicy.Local;

        public ViewMode Mode => mode;
        public int LocalBoardId => _localBoardId;
        public int ActiveBoardId => _activeBoardId;
        public int CurrentOpponentBoardId => _comparisonOpponentBoardId;
        public LeftSlotPolicy Policy => _leftSlotPolicy;

        /// Fires after Refresh / opponent cycle / mode toggle / active-board
        /// change — anything that might require chrome (thumbnail strip,
        /// BoardViewHud) to re-render. Subscribe in chrome Initialize, drop
        /// the subscription in OnDestroy.
        public event System.Action LayoutChanged;
        public int ComparisonLeftBoardId =>
            _leftSlotPolicy == LeftSlotPolicy.Active && _activeBoardId >= 0
                ? _activeBoardId
                : _localBoardId;

        public void SetLeftSlotPolicy(LeftSlotPolicy policy)
        {
            if (_leftSlotPolicy == policy) return;
            _leftSlotPolicy = policy;
            EnsurePresenters();
            ResolveOpponentIfNeeded();
            if (mode == ViewMode.Comparison) Refresh();
        }

        public void SetLocalBoardId(int boardId)
        {
            _localBoardId = boardId;
            // EnsurePresenters must run before the opponent fallback — the
            // serialized presenters list is empty on a fresh scene load, so
            // the very first SetLocalBoardId call finds no opponent and the
            // right-hand slot comes up blank on the first Compare toggle.
            EnsurePresenters();
            ResolveOpponentIfNeeded();
            Refresh();
        }

        /// Track the current-turn board. Under the Local policy the right
        /// slot auto-follows the active board so a network client always
        /// sees the player whose turn it is. Under the Active policy the
        /// left slot follows instead.
        public void SetActiveBoardId(int boardId)
        {
            if (_activeBoardId == boardId) return;
            int prevLeftId = ComparisonLeftBoardId;
            _activeBoardId = boardId;
            EnsurePresenters();
            // Local policy (network play): right slot tracks the active
            // board whenever it differs from the local seat. When it's our
            // own turn, leave the right slot wherever the player parked it
            // — we don't want to blank out an opponent they were studying.
            if (_leftSlotPolicy == LeftSlotPolicy.Local
                && _activeBoardId >= 0
                && _activeBoardId != _localBoardId)
            {
                _comparisonOpponentBoardId = _activeBoardId;
            }
            // Active policy (hot-seat practice): when the rotating left slot
            // collides with whatever was on the right, swap — the just-
            // finished player moves to the right slot, so the player whose
            // turn just ended stays visible next to the new active player.
            else if (_leftSlotPolicy == LeftSlotPolicy.Active
                     && _comparisonOpponentBoardId == _activeBoardId
                     && prevLeftId >= 0
                     && prevLeftId != _activeBoardId)
            {
                _comparisonOpponentBoardId = prevLeftId;
            }
            ResolveOpponentIfNeeded();
            if (mode == ViewMode.Comparison) Refresh();
        }

        private void ResolveOpponentIfNeeded()
        {
            int leftId = ComparisonLeftBoardId;
            if (_comparisonOpponentBoardId == leftId || _comparisonOpponentBoardId < 0)
            {
                _comparisonOpponentBoardId = FindFirstNonLeftBoardId(leftId);
            }
        }

        public void SetViewMode(ViewMode newMode)
        {
            if (mode == newMode) return;
            mode = newMode;
            Refresh();
        }

        public void ToggleViewMode()
        {
            SetViewMode(mode == ViewMode.BirdsEye ? ViewMode.Comparison : ViewMode.BirdsEye);
        }

        /// dir = +1 next, -1 previous; wraps through the non-local, non-null
        /// presenters in their registered order. Eliminated seats are included
        /// — their boards are still playable terrain.
        public void CycleOpponent(int dir)
        {
            EnsurePresenters();
            int leftId = ComparisonLeftBoardId;
            var ids = new List<int>();
            foreach (var p in presenters)
            {
                if (p == null || p.BoardState == null) continue;
                if (p.BoardState.BoardId == leftId) continue;
                ids.Add(p.BoardState.BoardId);
            }
            if (ids.Count == 0) return;

            int idx = ids.IndexOf(_comparisonOpponentBoardId);
            if (idx < 0) idx = 0;
            else idx = (idx + dir + ids.Count) % ids.Count;
            _comparisonOpponentBoardId = ids[idx];
            if (mode == ViewMode.Comparison) Refresh();
        }

        private void OnEnable()
        {
            Refresh();
        }

        private void OnValidate()
        {
            Refresh();
        }

        /// Force opponent slot to a specific board (thumbnail strip
        /// direct-jump). No-op if the target is the left slot — left and
        /// right can't be the same board. Refresh + LayoutChanged fire
        /// only when the value actually changes.
        public void SetOpponentBoardId(int boardId)
        {
            if (boardId == ComparisonLeftBoardId) return;
            if (boardId == _comparisonOpponentBoardId) return;
            _comparisonOpponentBoardId = boardId;
            if (mode == ViewMode.Comparison) Refresh();
            else LayoutChanged?.Invoke();
        }

        [ContextMenu("Refresh Layout")]
        public void Refresh()
        {
            EnsurePresenters();
            if (mode == ViewMode.Comparison && ComparisonLeftBoardId >= 0 && presenters.Count >= 2)
                ApplyComparisonLayout();
            else
                ApplyBirdsEyeLayout();

            var panZoom = GetComponent<MultiBoardPanZoom>();
            if (panZoom != null) panZoom.RequestFit();

            LayoutChanged?.Invoke();
        }

        private void EnsurePresenters()
        {
            if (presenters == null || presenters.Count == 0)
            {
                presenters = new List<BoardPresenter>(GetComponentsInChildren<BoardPresenter>(true));
            }
        }

        private int FindFirstNonLeftBoardId(int leftId)
        {
            foreach (var p in presenters)
            {
                if (p == null || p.BoardState == null) continue;
                if (p.BoardState.BoardId != leftId) return p.BoardState.BoardId;
            }
            return -1;
        }

        private void ApplyBirdsEyeLayout()
        {
            int count = presenters.Count;
            // Default screens are 16:9, so we bias toward width: one row up to
            // 4 seats, two rows up to 8. Avoids the tall 2-column stack that
            // pushed later boards below the viewport at 6-8 players.
            int cols = autoColumns
                ? (count <= 4 ? Mathf.Max(1, count) : Mathf.CeilToInt(count / 2f))
                : Mathf.Max(1, columns);
            int usedCols = Mathf.Min(cols, count);
            int usedRows = Mathf.CeilToInt(count / (float)cols);
            float xCenterShift = (usedCols - 1) * spacing.x * 0.5f;
            float yCenterShift = (usedRows - 1) * spacing.y * 0.5f;

            for (int i = 0; i < count; i++)
            {
                var presenter = presenters[i];
                if (presenter == null) continue;
                presenter.gameObject.SetActive(true);

                int row = i / cols;
                int col = i % cols;

                var pos = new Vector2(
                    startOffset.x + col * spacing.x - xCenterShift,
                    startOffset.y - row * spacing.y + yCenterShift);

                var rect = presenter.GetComponent<RectTransform>();
                if (rect != null) rect.anchoredPosition = pos;
                else presenter.transform.localPosition = pos;
            }
        }

        private void ApplyComparisonLayout()
        {
            int leftId = ComparisonLeftBoardId;
            foreach (var presenter in presenters)
            {
                if (presenter == null || presenter.BoardState == null) continue;
                int id = presenter.BoardState.BoardId;
                bool isLeft = id == leftId;
                bool isOpponent = id == _comparisonOpponentBoardId;
                presenter.gameObject.SetActive(isLeft || isOpponent);
                if (!isLeft && !isOpponent) continue;

                var pos = new Vector2(isLeft ? -comparisonSlotX : comparisonSlotX, 0f);
                var rect = presenter.GetComponent<RectTransform>();
                if (rect != null) rect.anchoredPosition = pos;
                else presenter.transform.localPosition = pos;
            }
        }
    }
}
