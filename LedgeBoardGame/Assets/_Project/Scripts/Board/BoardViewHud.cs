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
        /// Panel footprint, in canvas reference units. Public because the
        /// top-band chrome has to be placed around it — see
        /// StatusBanner.ResolveToastAnchoredPosition.
        public const float HudWidth = 280f;
        public const float HudHeight = 144f;

        // ── Section-label readability (CP074) ────────────────────────────
        // The kit's caps-caption recipe — SectionLabelSize 9.5, uppercase,
        // 0.22em tracking, InkDim — is authored in canvas reference units
        // against 1920x1080. The CanvasScaler (ScaleWithScreenSize, match 0.5)
        // then scales the whole canvas by screen size, so those units are not a
        // physical size: at 720x1280 the factor is ~0.67, and a 9.5-unit caption
        // renders at ~6.3 physical px. LiberationSans SDF — which MonoFont
        // actually resolves to, since the project ships no JetBrainsMono asset —
        // has a cap line at 59/86 of em, so the capitals stand ~4.3px tall and
        // their stems fall under half a pixel. That is what Design has been
        // reading as broken glyphs on "BOARD VIEW": not a missing glyph (the
        // fallback face carries every character in the string) but strokes too
        // thin to survive SDF sampling, under an already-dim 40% ink.
        //
        // Fix holds the caption's *physical* size instead of its unit size, so
        // the label renders the same on a phone as it does in the accepted
        // landscape frame.
        private const float SectionLabelRowHeight = 14f;
        // Ceiling on the grow-up. TMP's TopLeft alignment pins the ascender line
        // to the rect top and LiberationSans SDF ascends 0.905em, so the 14-unit
        // row holds a hair over 15 units of type before the caps start clipping;
        // an extremely narrow window would otherwise ask for more than that.
        private const float SectionLabelMaxSize = 15f;
        // Tracking earns its keep at display sizes and works against this one:
        // 0.22em between 4px capitals reads as loose debris rather than a word.
        // 0.14em is the value the You panel's turn-meta caption already uses, so
        // this stays inside the family rather than inventing a number.
        private const float SectionLabelTracking = 14f;

        private MultiBoardLayout _layout;
        private LedgeButton _toggleButton;
        private RectTransform _comparisonGroup;
        private TextMeshProUGUI _opponentLabel;
        private TextMeshProUGUI _sectionLabel;
        private Canvas _canvas;
        private float _lastCanvasScaleFactor = -1f;
        private RectTransform _root;

        /// The top-right glass panel this component builds under the canvas. Note
        /// this is NOT the component's own transform: BoardViewHud lives on a
        /// bare host GameObject and builds its UI as a canvas sibling. Null until
        /// Initialize has run.
        public RectTransform Root => _root;

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
            _canvas = canvas;

            var root = new GameObject("BoardViewHud", typeof(RectTransform));
            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(canvas.transform, false);
            rootRect.anchorMin = new Vector2(1f, 1f);
            rootRect.anchorMax = new Vector2(1f, 1f);
            rootRect.pivot = new Vector2(1f, 1f);
            rootRect.anchoredPosition = new Vector2(-LedgeUITokens.PanelEdgeInset, -LedgeUITokens.PanelEdgeInset);
            rootRect.sizeDelta = new Vector2(HudWidth, HudHeight);
            rootRect.SetAsLastSibling();
            _root = rootRect;

            // Glass panel backdrop so this matches the TL/BL chrome.
            var glass = LedgeGlassPanel.Build(rootRect, "Glass");
            var gRt = glass.GetComponent<RectTransform>();
            gRt.anchorMin = Vector2.zero;
            gRt.anchorMax = Vector2.one;
            gRt.offsetMin = Vector2.zero;
            gRt.offsetMax = Vector2.zero;

            // Section label "BOARD VIEW" at the top — matches the mono
            // section-caption convention used by the other chrome panels
            // so this reads as part of the same panel family. Size and tracking
            // are CP074's: see SectionLabelMaxSize / ApplySectionLabelScale.
            var labelHostGo = new GameObject("SectionLabel", typeof(RectTransform));
            var sectionRt = (RectTransform)labelHostGo.transform;
            sectionRt.SetParent(glass.Content, false);
            sectionRt.anchorMin = new Vector2(0f, 1f);
            sectionRt.anchorMax = new Vector2(1f, 1f);
            sectionRt.pivot = new Vector2(0f, 1f);
            sectionRt.anchoredPosition = Vector2.zero;
            sectionRt.sizeDelta = new Vector2(0f, SectionLabelRowHeight);
            _sectionLabel = labelHostGo.AddComponent<TextMeshProUGUI>();
            _sectionLabel.text = "BOARD VIEW";
            _sectionLabel.font = LedgeUITokens.MonoFont;
            _sectionLabel.color = LedgeUITokens.InkDim;
            _sectionLabel.fontStyle = FontStyles.UpperCase;
            _sectionLabel.characterSpacing = SectionLabelTracking;
            _sectionLabel.alignment = TextAlignmentOptions.TopLeft;
            _sectionLabel.raycastTarget = false;
            ApplySectionLabelScale();

            // Toggle button — Ghost variant, pinned beneath the section label.
            var toggleHost = new GameObject("Toggle", typeof(RectTransform));
            var toggleRt = (RectTransform)toggleHost.transform;
            toggleRt.SetParent(glass.Content, false);
            toggleRt.anchorMin = new Vector2(0f, 1f);
            toggleRt.anchorMax = new Vector2(1f, 1f);
            toggleRt.pivot = new Vector2(0.5f, 1f);
            toggleRt.anchoredPosition = new Vector2(0f, -20f);
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
            _comparisonGroup.anchoredPosition = new Vector2(0f, -64f);
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

        /// Caps-caption size in canvas units that renders at the authored
        /// physical size under a given <c>Canvas.scaleFactor</c>.
        ///
        /// The canvas scale factor is physical-pixels-per-canvas-unit, so
        /// dividing by it converts "9.5 px on screen" into canvas units. Clamped
        /// at both ends: never below the authored size, so the accepted
        /// landscape frame (factor 1 at the 1920-wide reference, and every
        /// factor above it) is pixel-identical to before CP074; and never above
        /// what the caption row can hold, so a very narrow window degrades to a
        /// smaller-than-ideal caption rather than a clipped one.
        public static float ResolveSectionLabelSize(float canvasScaleFactor)
        {
            if (canvasScaleFactor <= 0f) return LedgeUITokens.SectionLabelSize;
            return Mathf.Clamp(LedgeUITokens.SectionLabelSize / canvasScaleFactor,
                               LedgeUITokens.SectionLabelSize, SectionLabelMaxSize);
        }

        // Re-fit when the canvas scale changes (window resize, device rotation).
        // One float compare a frame; the caption is the only thing here sized in
        // physical rather than reference units.
        private void Update()
        {
            if (_sectionLabel == null || _canvas == null) return;
            if (Mathf.Approximately(_canvas.scaleFactor, _lastCanvasScaleFactor)) return;
            ApplySectionLabelScale();
        }

        private void ApplySectionLabelScale()
        {
            if (_sectionLabel == null) return;
            float factor = _canvas != null ? _canvas.scaleFactor : 1f;
            _lastCanvasScaleFactor = factor;
            _sectionLabel.fontSize = ResolveSectionLabelSize(factor);
        }

        private void OnToggleClicked()
        {
            if (_layout == null) return;
            _layout.ToggleViewMode();
            Refresh();
        }
    }
}
