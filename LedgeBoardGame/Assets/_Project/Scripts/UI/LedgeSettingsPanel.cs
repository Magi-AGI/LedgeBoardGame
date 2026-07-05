using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Magi.LedgeBoardGame.UI
{
    /// Settings screen. Built from
    /// `kit/ledge-board-game/project/ui/frame-settings.jsx` → SettingsFrame:
    /// Fraunces italic title + mono section label + 2×2 grid of glass panels
    /// (Audio · Motion · Accessibility · Account). Each row is label/hint +
    /// a control (Toggle / Slider / Segmented / Button).
    ///
    /// Kit-faithful chrome only: clicking a Toggle / dragging a Slider /
    /// picking a Segmented option updates the visual state but does NOT
    /// persist or feed any system. The persistence layer (PlayerPrefs or a
    /// settings ScriptableObject) and the actually-wired toggles (reduced-
    /// motion → LedgeDreamCanvas, audio mute, colorblind aid, etc.) land
    /// in a follow-up pass — at that point each control's onChange callback
    /// becomes the integration point.
    ///
    /// Controls (Toggle, Slider, Segmented) are defined inline rather than
    /// promoted to shared components yet — they have a single consumer here.
    /// Promote when Player Setup or another surface needs them too (v4
    /// handoff §4 lists this as a deferred promotion).
    public class LedgeSettingsPanel : MonoBehaviour
    {
        private GameObject _overlayGo;

        // Responsive refs (see ApplyResponsiveLayout). Cached from BuildUi so we
        // can switch the 2×2 landscape grid to a single-column portrait layout
        // when the canvas is too narrow to fit the 1000-wide frame.
        private RectTransform _canvasRt;
        private RectTransform _contentRt;
        private RectTransform _gridRt;
        private GridLayoutGroup _gridLayout;
        private float _lastCanvasWidth = -1f;

        // Landscape frame constants (the accepted cp017 layout).
        private const float FrameW = 1000f;
        private const float FrameH = 700f;
        private const float GridPadTop = 140f; // header (title + sublabel) reserve
        private const float GridPadSide = 40f;
        private const float GridPadBottom = 40f;

        private Action _onClose;
        // Account sub-routes — fired by the Account panel's Edit/Change
        // buttons. Setting either lets a caller plug Settings → Setup as
        // the kit's F1 decision specifies. Null = button stays a stub log.
        private Action _onEditProfile;

        // ── Stub state (kit-faithful, not persisted) ───────────────────────
        private float _audioMaster = 0.72f;
        private float _audioMusic = 0.40f;
        private float _audioSfx = 0.85f;
        private bool _motionRipple = true;
        private bool _motionReduced = false;
        private bool _motionStarfield = true;
        private string _a11yContrast = "Standard";
        private bool _a11yColorblind = false;
        private string _a11yTextSize = "M";

        private void Awake() => EnsureBuilt();

        public void EnsureBuilt()
        {
            if (_overlayGo != null) return;
            BuildUi();
            HideInternal();
        }

        // ── Public API ─────────────────────────────────────────────────────

        public void Show(Action onClose = null, Action onEditProfile = null)
        {
            EnsureBuilt();
            _onClose = onClose;
            _onEditProfile = onEditProfile;
            _overlayGo.SetActive(true);
            _overlayGo.transform.SetAsLastSibling();
            ApplyResponsiveLayout();
        }

        // Re-fit if the canvas width changes while Settings is open (e.g. a
        // device rotation / window resize). Cheap early-out on unchanged width.
        private void Update()
        {
            if (!IsShowing || _canvasRt == null) return;
            if (!Mathf.Approximately(_canvasRt.rect.width, _lastCanvasWidth))
                ApplyResponsiveLayout();
        }

        public void Hide() => HideInternal();

        public bool IsShowing => _overlayGo != null && _overlayGo.activeSelf;

        [ContextMenu("Show Settings (Test)")]
        private void ShowTestPreview()
        {
            Show(() => UnityEngine.Debug.Log("[settings] Closed (test)"));
        }

        // ── Internals ─────────────────────────────────────────────────────

        private void HideInternal()
        {
            if (_overlayGo != null) _overlayGo.SetActive(false);
        }

        private void FireAndClose()
        {
            HideInternal();
            try { _onClose?.Invoke(); }
            catch (Exception ex) { UnityEngine.Debug.LogError($"[settings] close callback threw: {ex}"); }
        }

        // ── UI construction ───────────────────────────────────────────────

        private void BuildUi()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            _overlayGo = new GameObject("SettingsOverlay", typeof(RectTransform));
            var overlayRt = (RectTransform)_overlayGo.transform;
            overlayRt.SetParent(canvas.transform, false);
            overlayRt.anchorMin = Vector2.zero; overlayRt.anchorMax = Vector2.one;
            overlayRt.offsetMin = Vector2.zero; overlayRt.offsetMax = Vector2.zero;
            overlayRt.SetAsLastSibling();

            // Solid backdrop — settings is a full takeover, not a floating modal.
            var bgGo = new GameObject("Backdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var bgRt = (RectTransform)bgGo.transform;
            bgRt.SetParent(overlayRt, false);
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
            var bgImg = bgGo.GetComponent<Image>();
            bgImg.color = LedgeUITokens.Canvas;
            bgImg.raycastTarget = true;

            // Content frame — 1000×700 inset on the 1600×900 canvas (kit's
            // native size for this surface). Centered.
            var contentGo = new GameObject("Content", typeof(RectTransform));
            var contentRt = (RectTransform)contentGo.transform;
            contentRt.SetParent(overlayRt, false);
            contentRt.anchorMin = new Vector2(0.5f, 0.5f);
            contentRt.anchorMax = new Vector2(0.5f, 0.5f);
            contentRt.pivot = new Vector2(0.5f, 0.5f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(FrameW, FrameH);
            _contentRt = contentRt;
            _canvasRt = canvas.transform as RectTransform;

            // Header: Fraunces italic "Settings" + SectionLabel breadcrumb.
            var titleGo = MakeText(contentRt, "Title", LedgeUITokens.DisplayFont, 40f, LedgeUITokens.Ink, "Settings").rectTransform;
            var titleTmp = titleGo.GetComponent<TMP_Text>();
            titleTmp.fontStyle = FontStyles.Italic;
            titleTmp.alignment = TextAlignmentOptions.TopLeft;
            titleGo.anchorMin = new Vector2(0f, 1f);
            titleGo.anchorMax = new Vector2(1f, 1f);
            titleGo.pivot = new Vector2(0f, 1f);
            titleGo.anchoredPosition = new Vector2(40f, -40f);
            titleGo.sizeDelta = new Vector2(-80f, 48f);

            var sublabel = MakeText(contentRt, "Sublabel", LedgeUITokens.MonoFont,
                LedgeUITokens.SectionLabelSize, LedgeUITokens.InkDim,
                "AUDIO · MOTION · ACCESSIBILITY · ACCOUNT");
            sublabel.fontStyle = FontStyles.UpperCase;
            sublabel.characterSpacing = 22f;
            sublabel.alignment = TextAlignmentOptions.TopLeft;
            var subRt = sublabel.rectTransform;
            subRt.anchorMin = new Vector2(0f, 1f);
            subRt.anchorMax = new Vector2(1f, 1f);
            subRt.pivot = new Vector2(0f, 1f);
            subRt.anchoredPosition = new Vector2(40f, -94f);
            subRt.sizeDelta = new Vector2(-80f, 14f);

            // Done button — top-right. The kit doesn't show one (the screen
            // is meant to compose into a larger nav flow), but as a free-
            // standing overlay we need an explicit exit.
            var doneBtn = LedgeButton.Build(contentRt, "Done", LedgeButton.Variant.Ghost, LedgeButton.Size.Md, FireAndClose);
            var doneRt = doneBtn.GetComponent<RectTransform>();
            doneRt.anchorMin = new Vector2(1f, 1f);
            doneRt.anchorMax = new Vector2(1f, 1f);
            doneRt.pivot = new Vector2(1f, 1f);
            doneRt.anchoredPosition = new Vector2(-40f, -42f);
            doneRt.sizeDelta = new Vector2(100f, 36f);

            // 2×2 grid of glass panels.
            var gridGo = new GameObject("Grid", typeof(RectTransform));
            var gridRt = (RectTransform)gridGo.transform;
            gridRt.SetParent(contentRt, false);
            gridRt.anchorMin = new Vector2(0f, 0f);
            gridRt.anchorMax = new Vector2(1f, 1f);
            gridRt.pivot = new Vector2(0.5f, 0.5f);
            gridRt.offsetMin = new Vector2(GridPadSide, GridPadBottom);
            gridRt.offsetMax = new Vector2(-GridPadSide, -GridPadTop);

            var gl = gridGo.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(450f, 240f);
            gl.spacing = new Vector2(20f, 20f);
            gl.childAlignment = TextAnchor.UpperLeft;
            gl.startCorner = GridLayoutGroup.Corner.UpperLeft;
            gl.startAxis = GridLayoutGroup.Axis.Horizontal;
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 2;
            _gridRt = gridRt;
            _gridLayout = gl;

            BuildAudioPanel(gridRt);
            BuildMotionPanel(gridRt);
            BuildAccessibilityPanel(gridRt);
            BuildAccountPanel(gridRt);

            // Pick landscape vs. narrow-portrait layout based on canvas width.
            ApplyResponsiveLayout();
        }

        // ── Responsive layout ─────────────────────────────────────────────
        // Landscape/16:9 keeps the accepted cp017 2×2 grid (1000×700 frame,
        // 450×240 cells). When the canvas is too narrow to fit the 1000-wide
        // frame with margins (e.g. 720×1280 portrait → ~900 ref-units wide), we
        // switch to a single-column layout: the frame is clamped to the canvas
        // width with side margins, the grid becomes 1 column, and each card
        // spans the full grid width. The frame grows taller (4 stacked cards)
        // but stays within portrait height. Called after build, on Show(), and
        // on canvas-width change while visible.
        private void ApplyResponsiveLayout()
        {
            if (_contentRt == null || _gridRt == null || _gridLayout == null) return;
            if (_canvasRt == null) _canvasRt = _overlayGo != null ? _overlayGo.transform.parent as RectTransform : null;
            if (_canvasRt == null) return;

            float canvasW = _canvasRt.rect.width;
            float canvasH = _canvasRt.rect.height;
            _lastCanvasWidth = canvasW;

            const float SideMargin = 32f;
            // Narrow when the landscape frame + a small margin can't fit.
            bool narrow = canvasW < FrameW + 2f * SideMargin;

            if (!narrow)
            {
                // Landscape / accepted layout.
                _contentRt.sizeDelta = new Vector2(FrameW, FrameH);
                _gridRt.offsetMin = new Vector2(GridPadSide, GridPadBottom);
                _gridRt.offsetMax = new Vector2(-GridPadSide, -GridPadTop);
                _gridLayout.constraintCount = 2;
                _gridLayout.cellSize = new Vector2(450f, 240f);
                return;
            }

            // Narrow / portrait: single column, frame clamped to canvas width.
            float contentW = Mathf.Min(FrameW, canvasW - 2f * SideMargin);
            float gridW = contentW - 2f * GridPadSide;
            const float cellH = 240f;
            float spacing = _gridLayout.spacing.y;
            // Four stacked cards + inter-card spacing + header/bottom reserves.
            float contentH = GridPadTop + (4f * cellH + 3f * spacing) + GridPadBottom;
            // Never exceed the canvas height (leave a small top/bottom margin).
            contentH = Mathf.Min(contentH, canvasH - 2f * SideMargin);

            _contentRt.sizeDelta = new Vector2(contentW, contentH);
            _gridRt.offsetMin = new Vector2(GridPadSide, GridPadBottom);
            _gridRt.offsetMax = new Vector2(-GridPadSide, -GridPadTop);
            _gridLayout.constraintCount = 1;
            _gridLayout.cellSize = new Vector2(gridW, cellH);
        }

        private void BuildAudioPanel(Transform parent)
        {
            var col = BuildPanel(parent, "Audio");
            BuildSliderRow(col, "Master volume", null, _audioMaster, v => _audioMaster = v);
            BuildSliderRow(col, "Music", null, _audioMusic, v => _audioMusic = v);
            BuildSliderRow(col, "Sound effects", null, _audioSfx, v => _audioSfx = v);
        }

        private void BuildMotionPanel(Transform parent)
        {
            var col = BuildPanel(parent, "Motion");
            BuildToggleRow(col, "Board ripple & breathe", "Valid-target and source pulses", _motionRipple, v => _motionRipple = v);
            BuildToggleRow(col, "Reduced motion", "Calmer transitions", _motionReduced, v => _motionReduced = v);
            BuildToggleRow(col, "Backdrop starfield", null, _motionStarfield, v => _motionStarfield = v);
        }

        private void BuildAccessibilityPanel(Transform parent)
        {
            var col = BuildPanel(parent, "Accessibility");
            BuildSegmentedRow(col, "Counter contrast", null, new[] { "Standard", "High" }, _a11yContrast, v => _a11yContrast = v);
            BuildToggleRow(col, "Colorblind aid", "Tone glyphs on counters", _a11yColorblind, v => _a11yColorblind = v);
            BuildSegmentedRow(col, "UI text size", null, new[] { "S", "M", "L" }, _a11yTextSize, v => _a11yTextSize = v);
        }

        private void BuildAccountPanel(Transform parent)
        {
            var col = BuildPanel(parent, "Account");
            // F1: Display-name and Board-skin both route into the Setup
            // screen (the kit's "Settings → Account → Setup" sub-route).
            // If no onEditProfile is wired by the caller, both fall back
            // to a stub log so the buttons stay clickable.
            BuildButtonRow(col, "Display name", "Aurelia", "Edit",
                () => FireEditProfileOrStub("Edit display name"));
            BuildButtonRow(col, "Board skin", "Nightfall", "Change",
                () => FireEditProfileOrStub("Change skin"));
            BuildButtonRow(col, "Sign out", null, "Sign out",
                () => UnityEngine.Debug.Log("[settings] Sign out (stub)"));
        }

        private void FireEditProfileOrStub(string action)
        {
            if (_onEditProfile != null)
            {
                HideInternal();
                try { _onEditProfile.Invoke(); }
                catch (Exception ex) { UnityEngine.Debug.LogError($"[settings] onEditProfile threw: {ex}"); }
            }
            else
            {
                UnityEngine.Debug.Log($"[settings] {action} (stub — no editor wired)");
            }
        }

        // ── Panel + Row primitives ────────────────────────────────────────

        private static RectTransform BuildPanel(Transform parent, string title)
        {
            var hostGo = new GameObject($"Panel_{title}", typeof(RectTransform));
            var hostRt = (RectTransform)hostGo.transform;
            hostRt.SetParent(parent, false);

            var glass = LedgeGlassPanel.Build(hostRt, "Glass",
                padding: new Vector2(22f, 8f));
            var gRt = glass.GetComponent<RectTransform>();
            gRt.anchorMin = Vector2.zero; gRt.anchorMax = Vector2.one;
            gRt.offsetMin = Vector2.zero; gRt.offsetMax = Vector2.zero;

            var content = new GameObject("Col", typeof(RectTransform));
            var contentRt = (RectTransform)content.transform;
            contentRt.SetParent(glass.Content, false);
            contentRt.anchorMin = Vector2.zero; contentRt.anchorMax = Vector2.one;
            contentRt.offsetMin = Vector2.zero; contentRt.offsetMax = Vector2.zero;
            var vl = content.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 0f;
            vl.childAlignment = TextAnchor.UpperLeft;
            vl.childControlWidth = true;
            // Control height so each row's declared LayoutElement height (50px)
            // sizes it. With it false the rows kept their default rect height
            // and overflowed the 240px card — later rows/hints spilled below
            // the panel (e.g. "Tone glyphs on counters", "Nightfall").
            vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            var sectionLabel = MakeText(contentRt, "SectionLabel", LedgeUITokens.MonoFont,
                LedgeUITokens.SectionLabelSize, LedgeUITokens.InkDim, title.ToUpperInvariant());
            sectionLabel.fontStyle = FontStyles.UpperCase;
            sectionLabel.characterSpacing = 22f;
            sectionLabel.alignment = TextAlignmentOptions.TopLeft;
            AddLayoutHeight(sectionLabel.gameObject, 22f);

            return contentRt;
        }

        // Builds the label/hint left side + a per-row hairline rule + a slot
        // for the control. Returns the slot RectTransform so callers can drop
        // their control into it.
        private static RectTransform BuildRowShell(Transform parent, string label, string hint)
        {
            // Fixed-height band. The panel's VerticalLayoutGroup reads this
            // height (LayoutElement) to stack rows; everything INSIDE the row
            // is explicitly anchored — no nested HorizontalLayoutGroup /
            // VerticalLayoutGroup — so TMP label/hint rects can't resolve to a
            // bad default height and overlap the next row (the cp016 failure).
            const float RowHeight = 58f;
            // Right area reserved for the control slot, so the label/hint never
            // run under the control.
            const float ControlReserve = 196f;

            var rowGo = new GameObject($"Row_{label}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rowRt = (RectTransform)rowGo.transform;
            rowRt.SetParent(parent, false);
            AddLayoutHeight(rowGo, RowHeight);

            var img = rowGo.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);
            img.raycastTarget = false;

            // Top hairline rule.
            var rule = new GameObject("Rule", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var ruleRt = (RectTransform)rule.transform;
            ruleRt.SetParent(rowRt, false);
            ruleRt.anchorMin = new Vector2(0f, 1f);
            ruleRt.anchorMax = new Vector2(1f, 1f);
            ruleRt.pivot = new Vector2(0.5f, 1f);
            ruleRt.anchoredPosition = Vector2.zero;
            ruleRt.sizeDelta = new Vector2(0f, 1f);
            var ruleImg = rule.GetComponent<Image>();
            ruleImg.color = LedgeUITokens.Rule;
            ruleImg.raycastTarget = false;

            bool hasHint = !string.IsNullOrEmpty(hint);

            // Label — stretched from the left to ControlReserve short of the
            // right edge, single line.
            var labelText = MakeText(rowRt, "Label", LedgeUITokens.UIFont, 14f, LedgeUITokens.Ink, label);
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.textWrappingMode = TextWrappingModes.NoWrap;
            var lrt = labelText.rectTransform;
            lrt.anchorMin = new Vector2(0f, 1f);
            lrt.anchorMax = new Vector2(1f, 1f);
            lrt.pivot = new Vector2(0f, 1f);

            if (hasHint)
            {
                // Label y[-30,-10] (20 tall) + hint y[-48,-32] (16 tall): a 38px
                // block centred in the 58px band.
                lrt.offsetMin = new Vector2(0f, -30f);
                lrt.offsetMax = new Vector2(-ControlReserve, -10f);

                var hintText = MakeText(rowRt, "Hint", LedgeUITokens.UIFont, 12f, LedgeUITokens.InkDim, hint);
                hintText.alignment = TextAlignmentOptions.MidlineLeft;
                hintText.textWrappingMode = TextWrappingModes.NoWrap;
                var hrt = hintText.rectTransform;
                hrt.anchorMin = new Vector2(0f, 1f);
                hrt.anchorMax = new Vector2(1f, 1f);
                hrt.pivot = new Vector2(0f, 1f);
                hrt.offsetMin = new Vector2(0f, -48f);
                hrt.offsetMax = new Vector2(-ControlReserve, -32f);
            }
            else
            {
                // Single label vertically centred in the band.
                lrt.anchorMin = new Vector2(0f, 0.5f);
                lrt.anchorMax = new Vector2(1f, 0.5f);
                lrt.pivot = new Vector2(0f, 0.5f);
                lrt.offsetMin = new Vector2(0f, -11f);
                lrt.offsetMax = new Vector2(-ControlReserve, 11f);
            }

            // Control slot — fixed 180×44, anchored centre-right. Control
            // builders parent here and anchor to its right edge.
            var slotGo = new GameObject("ControlSlot", typeof(RectTransform));
            var slotRt = (RectTransform)slotGo.transform;
            slotRt.SetParent(rowRt, false);
            slotRt.anchorMin = new Vector2(1f, 0.5f);
            slotRt.anchorMax = new Vector2(1f, 0.5f);
            slotRt.pivot = new Vector2(1f, 0.5f);
            slotRt.anchoredPosition = Vector2.zero;
            slotRt.sizeDelta = new Vector2(180f, 44f);
            return slotRt;
        }

        // ── Toggle / Slider / Segmented controls ──────────────────────────

        private static void BuildToggleRow(Transform parent, string label, string hint, bool initial, Action<bool> onChange)
        {
            var slot = BuildRowShell(parent, label, hint);
            BuildToggle(slot, initial, onChange);
        }

        private static void BuildSliderRow(Transform parent, string label, string hint, float initial, Action<float> onChange)
        {
            var slot = BuildRowShell(parent, label, hint);
            BuildKitSlider(slot, initial, onChange);
        }

        private static void BuildSegmentedRow(Transform parent, string label, string hint, string[] options, string initial, Action<string> onChange)
        {
            var slot = BuildRowShell(parent, label, hint);
            BuildSegmented(slot, options, initial, onChange);
        }

        private static void BuildButtonRow(Transform parent, string label, string hint, string buttonText, Action onClick)
        {
            var slot = BuildRowShell(parent, label, hint);
            // LedgeButton.Build takes UnityAction; System.Action wraps via a
            // small adapter lambda so onClick? null still no-ops cleanly.
            UnityEngine.Events.UnityAction wrapped = onClick != null ? () => onClick() : (UnityEngine.Events.UnityAction)null;
            var btn = LedgeButton.Build(slot, buttonText, LedgeButton.Variant.Ghost, LedgeButton.Size.Sm, wrapped);
            var rt = (RectTransform)btn.transform;
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(90f, 28f);
        }

        private static void BuildToggle(Transform slot, bool initial, Action<bool> onChange)
        {
            var go = new GameObject("Toggle",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                typeof(Outline), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(slot, false);
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(44f, 24f);

            var img = go.GetComponent<Image>();
            var outline = go.GetComponent<Outline>();
            outline.effectDistance = new Vector2(LedgeUITokens.HairlineWidth, -LedgeUITokens.HairlineWidth);

            // Knob — child Image animated between left=2 and left=22.
            var knobGo = new GameObject("Knob", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var knobRt = (RectTransform)knobGo.transform;
            knobRt.SetParent(rt, false);
            knobRt.anchorMin = new Vector2(0f, 0.5f);
            knobRt.anchorMax = new Vector2(0f, 0.5f);
            knobRt.pivot = new Vector2(0.5f, 0.5f);
            knobRt.sizeDelta = new Vector2(18f, 18f);
            var knobImg = knobGo.GetComponent<Image>();
            knobImg.sprite = GetDiscSprite();
            knobImg.raycastTarget = false;

            bool state = initial;
            Action apply = () =>
            {
                if (state)
                {
                    img.color = new Color(LedgeUITokens.Accent.r, LedgeUITokens.Accent.g, LedgeUITokens.Accent.b, 0.22f);
                    outline.effectColor = new Color(LedgeUITokens.Accent.r, LedgeUITokens.Accent.g, LedgeUITokens.Accent.b, 0.50f);
                    knobImg.color = LedgeUITokens.Accent;
                    knobRt.anchoredPosition = new Vector2(31f, 0f); // right = width - knobHalf - 2px = 44-9-2-2 ≈ 31
                }
                else
                {
                    img.color = new Color(8f / 255f, 10f / 255f, 22f / 255f, 0.70f);
                    outline.effectColor = LedgeUITokens.PanelEdge2;
                    knobImg.color = LedgeUITokens.InkDim;
                    knobRt.anchoredPosition = new Vector2(11f, 0f); // left = knobHalf + 2px = 9+2 = 11
                }
            };
            apply();

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                state = !state;
                apply();
                try { onChange?.Invoke(state); }
                catch (Exception ex) { UnityEngine.Debug.LogError($"[settings:toggle] onChange threw: {ex}"); }
            });
        }

        private static void BuildKitSlider(Transform slot, float initial, Action<float> onChange)
        {
            var go = new GameObject("Slider", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(slot, false);
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(160f, 14f);

            var bgGo = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var bgRt = (RectTransform)bgGo.transform;
            bgRt.SetParent(rt, false);
            bgRt.anchorMin = new Vector2(0f, 0.5f);
            bgRt.anchorMax = new Vector2(1f, 0.5f);
            bgRt.pivot = new Vector2(0.5f, 0.5f);
            bgRt.sizeDelta = new Vector2(0f, 4f);
            var bgImg = bgGo.GetComponent<Image>();
            bgImg.color = new Color(8f / 255f, 10f / 255f, 22f / 255f, 0.70f);

            var fillAreaGo = new GameObject("FillArea", typeof(RectTransform));
            var faRt = (RectTransform)fillAreaGo.transform;
            faRt.SetParent(rt, false);
            faRt.anchorMin = new Vector2(0f, 0.5f);
            faRt.anchorMax = new Vector2(1f, 0.5f);
            faRt.pivot = new Vector2(0.5f, 0.5f);
            faRt.sizeDelta = new Vector2(0f, 4f);

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var fillRt = (RectTransform)fillGo.transform;
            fillRt.SetParent(faRt, false);
            fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero; fillRt.offsetMax = Vector2.zero;
            var fillImg = fillGo.GetComponent<Image>();
            fillImg.color = new Color(LedgeUITokens.Accent.r, LedgeUITokens.Accent.g, LedgeUITokens.Accent.b, 0.55f);

            var handleAreaGo = new GameObject("HandleArea", typeof(RectTransform));
            var haRt = (RectTransform)handleAreaGo.transform;
            haRt.SetParent(rt, false);
            haRt.anchorMin = new Vector2(0f, 0.5f);
            haRt.anchorMax = new Vector2(1f, 0.5f);
            haRt.pivot = new Vector2(0.5f, 0.5f);
            haRt.sizeDelta = new Vector2(0f, 14f);

            var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var hRt = (RectTransform)handleGo.transform;
            hRt.SetParent(haRt, false);
            hRt.sizeDelta = new Vector2(14f, 14f);
            var hImg = handleGo.GetComponent<Image>();
            hImg.sprite = GetDiscSprite();
            hImg.color = LedgeUITokens.Ink;

            var slider = go.AddComponent<Slider>();
            slider.fillRect = fillRt;
            slider.handleRect = hRt;
            slider.targetGraphic = hImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = Mathf.Clamp01(initial);
            slider.onValueChanged.AddListener(v =>
            {
                try { onChange?.Invoke(v); }
                catch (Exception ex) { UnityEngine.Debug.LogError($"[settings:slider] onChange threw: {ex}"); }
            });
        }

        private static void BuildSegmented(Transform slot, string[] options, string initial, Action<string> onChange)
        {
            var go = new GameObject("Segmented", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(slot, false);
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(180f, 30f);

            var bg = go.GetComponent<Image>();
            bg.color = new Color(8f / 255f, 10f / 255f, 22f / 255f, 0.60f);
            bg.raycastTarget = false;

            var hl = go.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(3, 3, 3, 3);
            hl.spacing = 4f;
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = true;
            hl.childForceExpandHeight = true;

            var optionImages = new List<Image>(options.Length);
            var optionLabels = new List<TMP_Text>(options.Length);

            string state = initial;

            void Apply()
            {
                for (int i = 0; i < options.Length; i++)
                {
                    bool active = options[i] == state;
                    if (i < optionImages.Count && optionImages[i] != null)
                        optionImages[i].color = active
                            ? new Color(LedgeUITokens.Accent.r, LedgeUITokens.Accent.g, LedgeUITokens.Accent.b, 0.15f)
                            : new Color(0f, 0f, 0f, 0f);
                    if (i < optionLabels.Count && optionLabels[i] != null)
                        optionLabels[i].color = active ? LedgeUITokens.Accent : LedgeUITokens.InkDim;
                }
            }

            for (int i = 0; i < options.Length; i++)
            {
                int capturedI = i;
                string captured = options[i];
                var optGo = new GameObject($"Option_{captured}",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                optGo.transform.SetParent(rt, false);
                var optImg = optGo.GetComponent<Image>();
                optImg.color = new Color(0f, 0f, 0f, 0f);
                optionImages.Add(optImg);

                var optBtn = optGo.GetComponent<Button>();
                optBtn.targetGraphic = optImg;
                optBtn.onClick.AddListener(() =>
                {
                    if (state == captured) return;
                    state = captured;
                    Apply();
                    try { onChange?.Invoke(state); }
                    catch (Exception ex) { UnityEngine.Debug.LogError($"[settings:segmented] onChange threw: {ex}"); }
                });

                var optTextGo = new GameObject("Label", typeof(RectTransform));
                optTextGo.transform.SetParent(optGo.transform, false);
                var optTextRt = (RectTransform)optTextGo.transform;
                optTextRt.anchorMin = Vector2.zero; optTextRt.anchorMax = Vector2.one;
                optTextRt.offsetMin = Vector2.zero; optTextRt.offsetMax = Vector2.zero;
                var optLabel = optTextGo.AddComponent<TextMeshProUGUI>();
                optLabel.font = LedgeUITokens.UIFont;
                optLabel.fontSize = 12f;
                optLabel.fontStyle = FontStyles.Bold;
                optLabel.color = LedgeUITokens.InkDim;
                optLabel.text = captured;
                optLabel.alignment = TextAlignmentOptions.Center;
                optLabel.raycastTarget = false;
                optionLabels.Add(optLabel);
            }

            Apply();
        }

        // ── Build helpers ─────────────────────────────────────────────────

        private static TMP_Text MakeText(Transform parent, string name, TMP_FontAsset font,
                                         float size, Color color, string text)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSize = size;
            t.color = color;
            t.text = text;
            t.raycastTarget = false;
            return t;
        }

        private static void AddLayoutHeight(GameObject go, float height)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
        }

        // Cached AA disc sprite for slider handle + toggle knob. Lazily
        // generated; matches the small-radius round affordance the kit calls
        // for without dragging in YouPanel's now-private circle generator.
        private static Sprite _discSprite;
        private static Sprite GetDiscSprite()
        {
            if (_discSprite != null) return _discSprite;
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color32[N * N];
            float c = (N - 1) * 0.5f;
            float r = c;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = x - c, dy = y - c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(r - d);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            _discSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            _discSprite.hideFlags = HideFlags.HideAndDontSave;
            return _discSprite;
        }
    }
}
