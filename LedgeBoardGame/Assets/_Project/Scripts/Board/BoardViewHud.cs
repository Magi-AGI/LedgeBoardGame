using Magi.LedgeBoardGame.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Magi.LedgeBoardGame.Board
{
    /// Top-right HUD for switching between bird's-eye and comparison view modes
    /// at 3-8 seats. Mirrors the kit chrome (glass panel + LedgeButton) so it
    /// reads as part of the same UI family as the TL "You" panel and BL action
    /// belt. Built procedurally so existing scenes adopt the controls without a
    /// re-save.
    public class BoardViewHud : MonoBehaviour
    {
        private MultiBoardLayout _layout;
        private LedgeButton _toggleButton;
        private RectTransform _comparisonGroup;
        private TextMeshProUGUI _opponentLabel;

        public void Initialize(MultiBoardLayout layout)
        {
            _layout = layout;
            BuildUi();
            // Keep the top-right "Board N" label in sync when the layout's
            // opponent slot changes from outside the cycler (e.g. the SEATS
            // thumb-strip calling SetOpponentBoardId). Without this the label
            // goes stale after a thumb click.
            if (_layout != null) _layout.LayoutChanged += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_layout != null) _layout.LayoutChanged -= Refresh;
        }

        public void Refresh()
        {
            if (_layout == null) return;
            bool comparison = _layout.Mode == MultiBoardLayout.ViewMode.Comparison;
            if (_toggleButton != null) _toggleButton.Text = comparison ? "Bird's-eye" : "Compare";
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
            rootRect.anchoredPosition = new Vector2(-LedgeUITokens.PanelEdgeInset, -LedgeUITokens.PanelEdgeInset);
            rootRect.sizeDelta = new Vector2(280f, 120f);
            rootRect.SetAsLastSibling();

            // Glass panel backdrop so this matches the TL/BL chrome.
            var glass = LedgeGlassPanel.Build(rootRect, "Glass");
            var gRt = glass.GetComponent<RectTransform>();
            gRt.anchorMin = Vector2.zero;
            gRt.anchorMax = Vector2.one;
            gRt.offsetMin = Vector2.zero;
            gRt.offsetMax = Vector2.zero;

            // Toggle button — Primary variant (state-changing action), pinned to
            // the top of the glass panel's content slot.
            var toggleHost = new GameObject("Toggle", typeof(RectTransform));
            var toggleRt = (RectTransform)toggleHost.transform;
            toggleRt.SetParent(glass.Content, false);
            toggleRt.anchorMin = new Vector2(0f, 1f);
            toggleRt.anchorMax = new Vector2(1f, 1f);
            toggleRt.pivot = new Vector2(0.5f, 1f);
            toggleRt.anchoredPosition = Vector2.zero;
            toggleRt.sizeDelta = new Vector2(0f, 36f);
            _toggleButton = toggleHost.AddComponent<LedgeButton>();
            _toggleButton.CurrentVariant = LedgeButton.Variant.Ghost;
            _toggleButton.Text = "Compare";
            _toggleButton.EnsureBuilt();
            _toggleButton.SetClickHandler(OnToggleClicked);

            // Comparison row — Prev | OpponentLabel | Next, beneath the toggle.
            _comparisonGroup = new GameObject("ComparisonControls", typeof(RectTransform)).GetComponent<RectTransform>();
            _comparisonGroup.SetParent(glass.Content, false);
            _comparisonGroup.anchorMin = new Vector2(0f, 1f);
            _comparisonGroup.anchorMax = new Vector2(1f, 1f);
            _comparisonGroup.pivot = new Vector2(0.5f, 1f);
            _comparisonGroup.anchoredPosition = new Vector2(0f, -44f);
            _comparisonGroup.sizeDelta = new Vector2(0f, 36f);
            var hl = _comparisonGroup.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 8f;
            hl.childAlignment = TextAnchor.MiddleCenter;
            // Control width so the prev/next buttons honour their 44px
            // LayoutElement and the label takes the flexible remainder. With it
            // false the buttons rendered at their default width and the row
            // (< Board N >) overflowed the 280px panel — clipping off the right
            // screen edge, most visibly in narrow portrait.
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = true;

            var prev = LedgeButton.Build(_comparisonGroup, "<", LedgeButton.Variant.Ghost, LedgeButton.Size.Sm,
                () => { _layout?.CycleOpponent(-1); Refresh(); });
            var prevLe = prev.gameObject.AddComponent<LayoutElement>();
            prevLe.preferredWidth = 44f;
            prevLe.minWidth = 44f;

            var labelGo = new GameObject("OpponentLabel", typeof(RectTransform));
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.SetParent(_comparisonGroup, false);
            _opponentLabel = labelGo.AddComponent<TextMeshProUGUI>();
            _opponentLabel.text = "—";
            _opponentLabel.alignment = TextAlignmentOptions.Center;
            _opponentLabel.fontSize = LedgeUITokens.IdentNameSize;
            _opponentLabel.font = LedgeUITokens.UIFont;
            _opponentLabel.color = LedgeUITokens.Ink;
            _opponentLabel.raycastTarget = false;
            var labelLe = labelGo.AddComponent<LayoutElement>();
            labelLe.flexibleWidth = 1f;
            labelLe.minWidth = 80f;

            var next = LedgeButton.Build(_comparisonGroup, ">", LedgeButton.Variant.Ghost, LedgeButton.Size.Sm,
                () => { _layout?.CycleOpponent(1); Refresh(); });
            var nextLe = next.gameObject.AddComponent<LayoutElement>();
            nextLe.preferredWidth = 44f;
            nextLe.minWidth = 44f;
        }

        private void OnToggleClicked()
        {
            if (_layout == null) return;
            _layout.ToggleViewMode();
            Refresh();
        }
    }
}
