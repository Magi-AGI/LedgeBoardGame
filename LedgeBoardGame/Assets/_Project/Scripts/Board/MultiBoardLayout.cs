using System.Collections.Generic;
using UnityEngine;

namespace Magi.LedgeBoardGame.Board
{
    /// <summary>
    /// Simple utility to lay out multiple BoardPresenter instances in a grid.
    /// Attach to a parent RectTransform that owns board presenters.
    /// </summary>
    [ExecuteAlways]
    public class MultiBoardLayout : MonoBehaviour
    {
        [SerializeField] private List<BoardPresenter> presenters = new List<BoardPresenter>();
        [SerializeField] private int columns = 2;
        [SerializeField] private Vector2 spacing = new Vector2(800f, 800f);
        [SerializeField] private Vector2 startOffset = new Vector2(0f, 0f);

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
            if (presenters == null || presenters.Count == 0)
            {
                presenters = new List<BoardPresenter>(GetComponentsInChildren<BoardPresenter>(true));
            }

            int cols = Mathf.Max(1, columns);
            int count = presenters.Count;
            int usedCols = Mathf.Min(cols, count);
            int usedRows = Mathf.CeilToInt(count / (float)cols);
            float xCenterShift = (usedCols - 1) * spacing.x * 0.5f;
            float yCenterShift = (usedRows - 1) * spacing.y * 0.5f;

            for (int i = 0; i < count; i++)
            {
                var presenter = presenters[i];
                if (presenter == null) continue;

                int row = i / cols;
                int col = i % cols;

                var pos = new Vector2(
                    startOffset.x + col * spacing.x - xCenterShift,
                    startOffset.y - row * spacing.y + yCenterShift);

                var rect = presenter.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.anchoredPosition = pos;
                }
                else
                {
                    presenter.transform.localPosition = pos;
                }
            }
        }
    }
}
