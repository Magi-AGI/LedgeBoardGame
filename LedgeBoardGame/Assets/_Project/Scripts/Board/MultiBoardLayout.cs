using System.Collections.Generic;
using UnityEngine;

namespace Magi.LedgeBoardGame.Board
{
    /// <summary>
    /// Positions BoardPresenter instances under the same parent. Supports two
    /// view modes:
    ///   - BirdsEye: grid (default; the only mode for &lt;= 4 seats).
    ///   - Comparison: local seat's board fixed left, one opponent right,
    ///     others hidden. Players cycle the right-side slot through the
    ///     remaining opponents. Gives the player a useful focused view at
    ///     6-8 seats where the grid becomes too dense.
    /// </summary>
    [ExecuteAlways]
    public class MultiBoardLayout : MonoBehaviour
    {
        public enum ViewMode { BirdsEye, Comparison }

        [SerializeField] private List<BoardPresenter> presenters = new List<BoardPresenter>();
        [SerializeField] private int columns = 2;
        [SerializeField] private bool autoColumns = true;
        [SerializeField] private Vector2 spacing = new Vector2(1200f, 1200f);
        [SerializeField] private Vector2 startOffset = new Vector2(0f, 0f);
        [SerializeField] private ViewMode mode = ViewMode.BirdsEye;
        [SerializeField] private float comparisonSlotX = 550f;

        private int _localBoardId = -1;
        private int _comparisonOpponentBoardId = -1;

        public ViewMode Mode => mode;
        public int LocalBoardId => _localBoardId;
        public int CurrentOpponentBoardId => _comparisonOpponentBoardId;

        public void SetLocalBoardId(int boardId)
        {
            _localBoardId = boardId;
            // EnsurePresenters must run before the opponent fallback — the
            // serialized presenters list is empty on a fresh scene load, so
            // the very first SetLocalBoardId call finds no opponent and the
            // right-hand slot comes up blank on the first Compare toggle.
            EnsurePresenters();
            if (_comparisonOpponentBoardId == _localBoardId || _comparisonOpponentBoardId < 0)
            {
                _comparisonOpponentBoardId = FindFirstOpponentBoardId();
            }
            Refresh();
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
            var ids = new List<int>();
            foreach (var p in presenters)
            {
                if (p == null || p.BoardState == null) continue;
                if (p.BoardState.BoardId == _localBoardId) continue;
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

        [ContextMenu("Refresh Layout")]
        public void Refresh()
        {
            EnsurePresenters();
            if (mode == ViewMode.Comparison && _localBoardId >= 0 && presenters.Count >= 2)
                ApplyComparisonLayout();
            else
                ApplyBirdsEyeLayout();

            var panZoom = GetComponent<MultiBoardPanZoom>();
            if (panZoom != null) panZoom.RequestFit();
        }

        private void EnsurePresenters()
        {
            if (presenters == null || presenters.Count == 0)
            {
                presenters = new List<BoardPresenter>(GetComponentsInChildren<BoardPresenter>(true));
            }
        }

        private int FindFirstOpponentBoardId()
        {
            foreach (var p in presenters)
            {
                if (p == null || p.BoardState == null) continue;
                if (p.BoardState.BoardId != _localBoardId) return p.BoardState.BoardId;
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
            foreach (var presenter in presenters)
            {
                if (presenter == null || presenter.BoardState == null) continue;
                int id = presenter.BoardState.BoardId;
                bool isLocal = id == _localBoardId;
                bool isOpponent = id == _comparisonOpponentBoardId;
                presenter.gameObject.SetActive(isLocal || isOpponent);
                if (!isLocal && !isOpponent) continue;

                var pos = new Vector2(isLocal ? -comparisonSlotX : comparisonSlotX, 0f);
                var rect = presenter.GetComponent<RectTransform>();
                if (rect != null) rect.anchoredPosition = pos;
                else presenter.transform.localPosition = pos;
            }
        }
    }
}
