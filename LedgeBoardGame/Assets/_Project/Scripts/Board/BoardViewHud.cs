using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Magi.LedgeBoardGame.Board
{
    /// Minimal HUD for switching between bird's-eye and comparison view modes
    /// at 3-8 seats. Auto-creates three buttons + an opponent-name label on the
    /// top-right of the canvas, bound to MultiBoardLayout. Kept as a runtime
    /// builder (no scene prefab) so existing scenes adopt the controls
    /// automatically without a re-save.
    public class BoardViewHud : MonoBehaviour
    {
        private MultiBoardLayout _layout;
        private Button _toggleButton;
        private TextMeshProUGUI _toggleLabel;
        private RectTransform _comparisonGroup;
        private TextMeshProUGUI _opponentLabel;

        public void Initialize(MultiBoardLayout layout)
        {
            _layout = layout;
            BuildUi();
            Refresh();
        }

        public void Refresh()
        {
            if (_layout == null) return;
            bool comparison = _layout.Mode == MultiBoardLayout.ViewMode.Comparison;
            if (_toggleLabel != null) _toggleLabel.text = comparison ? "Bird's-eye" : "Compare";
            if (_comparisonGroup != null) _comparisonGroup.gameObject.SetActive(comparison);
            UpdateOpponentLabel();
        }

        private void UpdateOpponentLabel()
        {
            if (_opponentLabel == null || _layout == null) return;
            int id = _layout.CurrentOpponentBoardId;
            if (id < 0) { _opponentLabel.text = "—"; return; }
            var presenters = _layout.GetComponentsInChildren<BoardPresenter>(true);
            foreach (var p in presenters)
            {
                if (p?.BoardState != null && p.BoardState.BoardId == id)
                {
                    _opponentLabel.text = $"Board {id}";
                    return;
                }
            }
            _opponentLabel.text = "—";
        }

        private void BuildUi()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            var root = new GameObject("BoardViewHud", typeof(RectTransform));
            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(canvas.transform, false);
            rootRect.anchorMin = new Vector2(1f, 1f);
            rootRect.anchorMax = new Vector2(1f, 1f);
            rootRect.pivot = new Vector2(1f, 1f);
            rootRect.anchoredPosition = new Vector2(-20f, -20f);
            rootRect.sizeDelta = new Vector2(280f, 120f);
            rootRect.SetAsLastSibling();

            _toggleButton = CreateButton(rootRect, "ToggleViewButton", "Compare", new Vector2(0f, 0f), new Vector2(260f, 40f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            _toggleLabel = _toggleButton.GetComponentInChildren<TextMeshProUGUI>();
            _toggleButton.onClick.AddListener(OnToggleClicked);

            _comparisonGroup = new GameObject("ComparisonControls", typeof(RectTransform)).GetComponent<RectTransform>();
            _comparisonGroup.SetParent(rootRect, false);
            _comparisonGroup.anchorMin = new Vector2(1f, 1f);
            _comparisonGroup.anchorMax = new Vector2(1f, 1f);
            _comparisonGroup.pivot = new Vector2(1f, 1f);
            _comparisonGroup.anchoredPosition = new Vector2(0f, -50f);
            _comparisonGroup.sizeDelta = new Vector2(260f, 40f);

            var prev = CreateButton(_comparisonGroup, "PrevOpponent", "<", new Vector2(0f, 0f), new Vector2(50f, 40f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            prev.onClick.AddListener(() => { _layout?.CycleOpponent(-1); Refresh(); });

            var next = CreateButton(_comparisonGroup, "NextOpponent", ">", new Vector2(0f, 0f), new Vector2(50f, 40f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            next.onClick.AddListener(() => { _layout?.CycleOpponent(1); Refresh(); });

            var labelGo = new GameObject("OpponentLabel", typeof(RectTransform));
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.SetParent(_comparisonGroup, false);
            labelRect.anchorMin = new Vector2(0f, 0.5f);
            labelRect.anchorMax = new Vector2(1f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.anchoredPosition = Vector2.zero;
            labelRect.sizeDelta = new Vector2(-120f, 40f);
            _opponentLabel = labelGo.AddComponent<TextMeshProUGUI>();
            _opponentLabel.text = "—";
            _opponentLabel.alignment = TextAlignmentOptions.Center;
            _opponentLabel.fontSize = 18f;
        }

        private static Button CreateButton(RectTransform parent, string name, string label, Vector2 anchoredPos, Vector2 size, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = anchorMax;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = new Color(0.12f, 0.14f, 0.18f, 0.85f);

            var textGo = new GameObject("Label", typeof(RectTransform));
            var textRt = (RectTransform)textGo.transform;
            textRt.SetParent(rt, false);
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 18f;
            return go.GetComponent<Button>();
        }

        private void OnToggleClicked()
        {
            if (_layout == null) return;
            _layout.ToggleViewMode();
            Refresh();
        }
    }
}
