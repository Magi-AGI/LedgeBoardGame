using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Magi.LedgeBoardGame.UI
{
    /// Player setup screen. Built from
    /// `kit/ledge-board-game/project/ui/frame-setup.jsx` → SetupFrame:
    /// two-column layout with an Identity panel on the left (display name +
    /// "how others see you" preview) and a Board-skin grid on the right
    /// (3×2 SkinSwatch cells + Back / Ready actions).
    ///
    /// Identity model (handoff v4 §0): players do NOT own a wheel color —
    /// identity is name + board-skin signature. The skin grid is the
    /// cosmetic pick. Real art is deferred; the six example skins below
    /// (Nightfall / Ember / Verdant / Amethyst / Ashen / Tide) ship as
    /// procedural radial gradients + an accent dot, the same data the kit
    /// uses for its preview. Replace with texture lookups when the skin
    /// catalog lands.
    public class LedgeSetupPanel : MonoBehaviour
    {
        public readonly struct SkinDef
        {
            public readonly string Id;
            public readonly string Name;
            public readonly Color SkyInner;
            public readonly Color SkyOuter;
            public readonly Color Accent;

            public SkinDef(string id, string name, Color skyInner, Color skyOuter, Color accent)
            {
                Id = id; Name = name; SkyInner = skyInner; SkyOuter = skyOuter; Accent = accent;
            }
        }

        /// All 14 skins from kit `frame-setup.jsx` (F5 design pass).
        /// Each entry mirrors {id, sky inner+outer, accent} verbatim.
        public static readonly SkinDef[] DefaultSkins =
        {
            new SkinDef("nightfall", "Nightfall",
                Hex("1B2755"), Hex("04050C"), Hex("8FB4FF")),
            new SkinDef("ember",     "Ember",
                Hex("3A1B16"), Hex("0C0503"), Hex("F2A24D")),
            new SkinDef("verdant",   "Verdant",
                Hex("143020"), Hex("03100A"), Hex("6FD89A")),
            new SkinDef("amethyst",  "Amethyst",
                Hex("2A1644"), Hex("0A0518"), Hex("C49AF0")),
            new SkinDef("ashen",     "Ashen",
                Hex("24262C"), Hex("08090C"), Hex("C8CCD6")),
            new SkinDef("tide",      "Tide",
                Hex("103040"), Hex("03101A"), Hex("5FC8E0")),
            new SkinDef("rosewood",  "Rosewood",
                Hex("3A1428"), Hex("0E040A"), Hex("E88FB4")),
            new SkinDef("gilded",    "Gilded",
                Hex("2E2410"), Hex("0C0902"), Hex("E8C66A")),
            new SkinDef("glacier",   "Glacier",
                Hex("16303A"), Hex("040E12"), Hex("A8E0E8")),
            new SkinDef("obsidian",  "Obsidian",
                Hex("16181E"), Hex("030305"), Hex("7A8294")),
            new SkinDef("orchard",   "Orchard",
                Hex("2A2E12"), Hex("090B02"), Hex("C2D86F")),
            new SkinDef("dusk",      "Dusk",
                Hex("34203A"), Hex("0C0512"), Hex("B98FD8")),
            new SkinDef("coral",     "Coral",
                Hex("3A1C18"), Hex("0E0503"), Hex("F0987A")),
            new SkinDef("mistral",   "Mistral",
                Hex("1E2838"), Hex("05090E"), Hex("9FB0C8")),
        };

        private GameObject _overlayGo;
        private TMP_InputField _nameField;
        private TMP_Text _previewLabel;
        private RectTransform _previewChipBg;
        private Image _previewChipSky;
        private Image _previewChipAccent;
        private TMP_Text _skinCountLabel;

        private readonly List<SkinSwatchHandles> _swatches = new List<SkinSwatchHandles>();

        private Action<string, string> _onReady;
        private Action _onBack;

        private string _selectedSkinId;
        private string _displayName = "";

        private struct SkinSwatchHandles
        {
            public string Id;
            public Image ContainerBg;
            public Outline ContainerOutline;
            public TMP_Text NameLabel;
            public TMP_Text ChosenLabel;
        }

        private void Awake() => EnsureBuilt();

        public void EnsureBuilt()
        {
            if (_overlayGo != null) return;
            BuildUi();
            HideInternal();
        }

        // ── Public API ─────────────────────────────────────────────────────

        /// Open the setup screen with an optional initial name + skin id.
        /// Ready fires onReady(name, skinId); Back fires onBack.
        public void Show(string initialName, string initialSkinId,
                         Action<string, string> onReady, Action onBack = null)
        {
            EnsureBuilt();
            _onReady = onReady;
            _onBack = onBack;
            _displayName = initialName ?? "";
            if (_nameField != null) _nameField.SetTextWithoutNotify(_displayName);
            _selectedSkinId = !string.IsNullOrEmpty(initialSkinId) && SkinExists(initialSkinId)
                ? initialSkinId
                : DefaultSkins[0].Id;
            RefreshSelection();
            _overlayGo.SetActive(true);
            _overlayGo.transform.SetAsLastSibling();
        }

        public void Hide() => HideInternal();

        public bool IsShowing => _overlayGo != null && _overlayGo.activeSelf;

        [ContextMenu("Show Setup (Test)")]
        private void ShowTestPreview()
        {
            Show("Aurelia", "nightfall",
                (n, s) => UnityEngine.Debug.Log($"[setup] Ready name={n} skin={s} (test)"),
                () => UnityEngine.Debug.Log("[setup] Back (test)"));
        }

        // ── Internals ─────────────────────────────────────────────────────

        private bool SkinExists(string id)
        {
            for (int i = 0; i < DefaultSkins.Length; i++)
                if (DefaultSkins[i].Id == id) return true;
            return false;
        }

        private SkinDef ResolveSkin(string id)
        {
            for (int i = 0; i < DefaultSkins.Length; i++)
                if (DefaultSkins[i].Id == id) return DefaultSkins[i];
            return DefaultSkins[0];
        }

        private void HideInternal()
        {
            if (_overlayGo != null) _overlayGo.SetActive(false);
        }

        private void FireReady()
        {
            HideInternal();
            try { _onReady?.Invoke(_displayName, _selectedSkinId); }
            catch (Exception ex) { UnityEngine.Debug.LogError($"[setup] onReady threw: {ex}"); }
        }

        private void FireBack()
        {
            HideInternal();
            try { _onBack?.Invoke(); }
            catch (Exception ex) { UnityEngine.Debug.LogError($"[setup] onBack threw: {ex}"); }
        }

        private void RefreshSelection()
        {
            // Swatch tint pass.
            foreach (var s in _swatches)
            {
                bool active = s.Id == _selectedSkinId;
                if (s.ContainerBg != null)
                    s.ContainerBg.color = active
                        ? new Color(LedgeUITokens.Accent.r, LedgeUITokens.Accent.g, LedgeUITokens.Accent.b, 0.10f)
                        : new Color(0f, 0f, 0f, 0f);
                if (s.ContainerOutline != null)
                    s.ContainerOutline.effectColor = active
                        ? new Color(LedgeUITokens.Accent.r, LedgeUITokens.Accent.g, LedgeUITokens.Accent.b, 0.50f)
                        : LedgeUITokens.PanelEdge;
                if (s.NameLabel != null)
                    s.NameLabel.color = active ? LedgeUITokens.Accent : LedgeUITokens.Ink;
                if (s.ChosenLabel != null)
                    s.ChosenLabel.gameObject.SetActive(active);
            }

            // Preview chip + label.
            var skin = ResolveSkin(_selectedSkinId);
            if (_previewChipSky != null)
                _previewChipSky.sprite = GetRadialGradientSprite(skin.SkyInner, skin.SkyOuter);
            if (_previewChipAccent != null) _previewChipAccent.color = skin.Accent;
            if (_previewLabel != null)
                _previewLabel.text = $"{(_displayName.Length == 0 ? "—" : _displayName)} · {skin.Name}";
        }

        // ── UI construction ───────────────────────────────────────────────

        private void BuildUi()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            _overlayGo = new GameObject("SetupOverlay", typeof(RectTransform));
            var overlayRt = (RectTransform)_overlayGo.transform;
            overlayRt.SetParent(canvas.transform, false);
            overlayRt.anchorMin = Vector2.zero; overlayRt.anchorMax = Vector2.one;
            overlayRt.offsetMin = Vector2.zero; overlayRt.offsetMax = Vector2.zero;
            overlayRt.SetAsLastSibling();

            // Solid canvas backdrop. Kit shows a faint board through a 60%
            // scrim — we omit the live board for the same reason as title/
            // tutorial and let the dream-canvas atmosphere read through any
            // existing sibling backdrop.
            var bgGo = new GameObject("Backdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var bgRt = (RectTransform)bgGo.transform;
            bgRt.SetParent(overlayRt, false);
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
            var bgImg = bgGo.GetComponent<Image>();
            // Fully opaque, matching the Settings and Tutorial full-takeover
            // backdrops. At 0.95 the returning-player title (Ledge wordmark +
            // nav) bled through when Setup opened over it (e.g. the Settings →
            // Account sub-route); opaque removes the bleed while keeping the
            // same dark dream-canvas tone.
            bgImg.color = LedgeUITokens.Canvas;
            bgImg.raycastTarget = true;

            // Centered two-column grid host. The kit's grid is 340 + 28 + 520
            // = 888 wide, with content vertically centered.
            var gridGo = new GameObject("Grid", typeof(RectTransform));
            var gridRt = (RectTransform)gridGo.transform;
            gridRt.SetParent(overlayRt, false);
            gridRt.anchorMin = new Vector2(0.5f, 0.5f);
            gridRt.anchorMax = new Vector2(0.5f, 0.5f);
            gridRt.pivot = new Vector2(0.5f, 0.5f);
            gridRt.anchoredPosition = Vector2.zero;
            gridRt.sizeDelta = new Vector2(888f, 600f);

            var hl = gridGo.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 28f;
            hl.childAlignment = TextAnchor.UpperLeft;
            // Control both axes so the panels' LayoutElement sizes (340x460 /
            // 520x540) actually apply. With these false the group ignored the
            // LayoutElements and both panels collapsed to their default ~100x100
            // rect, which cascaded into every child (BOARD SKIN wrapping one
            // letter per line, skin grid clipped, copy overlapping the name field).
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = false;

            BuildIdentityPanel(gridRt);
            BuildSkinPanel(gridRt);
        }

        private void BuildIdentityPanel(Transform parent)
        {
            var hostGo = new GameObject("IdentityPanel", typeof(RectTransform), typeof(LayoutElement));
            hostGo.transform.SetParent(parent, false);
            var le = hostGo.GetComponent<LayoutElement>();
            le.preferredWidth = 340f; le.minWidth = 340f;
            le.preferredHeight = 460f; le.minHeight = 460f;

            var glass = LedgeGlassPanel.Build(hostGo.transform, "Glass",
                padding: new Vector2(30f, 32f));
            var gRt = glass.GetComponent<RectTransform>();
            gRt.anchorMin = Vector2.zero; gRt.anchorMax = Vector2.one;
            gRt.offsetMin = Vector2.zero; gRt.offsetMax = Vector2.zero;

            var col = new GameObject("Col", typeof(RectTransform));
            var colRt = (RectTransform)col.transform;
            colRt.SetParent(glass.Content, false);
            colRt.anchorMin = Vector2.zero; colRt.anchorMax = Vector2.one;
            colRt.offsetMin = Vector2.zero; colRt.offsetMax = Vector2.zero;
            var vl = col.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 0f;
            vl.childAlignment = TextAnchor.UpperLeft;
            vl.childControlWidth = true;
            // Control height so each row's declared LayoutElement height sizes it;
            // with it false the rows kept their default rect height and the copy
            // overlapped the display-name label/field.
            vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            var section = MakeText(colRt, "Section", LedgeUITokens.MonoFont,
                LedgeUITokens.SectionLabelSize, LedgeUITokens.InkDim, "YOUR IDENTITY");
            section.fontStyle = FontStyles.UpperCase;
            section.characterSpacing = 22f;
            AddLayoutHeight(section.gameObject, 14f);
            AddSpacer(colRt, 16f);

            var copy = MakeText(colRt, "Copy", LedgeUITokens.DisplayFont, 28f, LedgeUITokens.Ink,
                "You don't own a color — your board is your signature.");
            copy.fontStyle = FontStyles.Italic;
            copy.alignment = TextAlignmentOptions.TopLeft;
            copy.textWrappingMode = TextWrappingModes.Normal;
            AddLayoutHeight(copy.gameObject, 96f);
            AddSpacer(colRt, 22f);

            var nameSection = MakeText(colRt, "NameSection", LedgeUITokens.MonoFont,
                LedgeUITokens.SectionLabelSize, LedgeUITokens.InkDim, "DISPLAY NAME");
            nameSection.fontStyle = FontStyles.UpperCase;
            nameSection.characterSpacing = 22f;
            AddLayoutHeight(nameSection.gameObject, 14f);
            AddSpacer(colRt, 7f);

            _nameField = BuildInputField(colRt, _displayName);
            _nameField.onValueChanged.AddListener(v =>
            {
                _displayName = v ?? "";
                if (_previewLabel != null)
                {
                    var skin = ResolveSkin(_selectedSkinId);
                    _previewLabel.text = $"{(_displayName.Length == 0 ? "—" : _displayName)} · {skin.Name}";
                }
            });
            AddSpacer(colRt, 24f);

            BuildPreviewBlock(colRt);
        }

        private void BuildPreviewBlock(Transform parent)
        {
            var rowGo = new GameObject("Preview",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                typeof(Outline), typeof(HorizontalLayoutGroup));
            rowGo.transform.SetParent(parent, false);
            AddLayoutHeight(rowGo, 76f);

            var bg = rowGo.GetComponent<Image>();
            bg.color = new Color(8f / 255f, 10f / 255f, 22f / 255f, 0.40f);
            bg.raycastTarget = false;
            var outline = rowGo.GetComponent<Outline>();
            outline.effectColor = LedgeUITokens.PanelEdge;
            outline.effectDistance = new Vector2(LedgeUITokens.HairlineWidth, -LedgeUITokens.HairlineWidth);

            var hl = rowGo.GetComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(16, 16, 14, 14);
            hl.spacing = 12f;
            hl.childAlignment = TextAnchor.MiddleLeft;
            // Control both axes so the chip (40x44) and the text column size
            // from their layout data instead of collapsing to default rects.
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = false;

            // Skin chip (40×44, kit dims): radial gradient bg + small accent
            // hex hint approximated as a colored disc (Unity UI can't draw
            // arbitrary polygons; the kit's hex motif degrades to a dot here).
            var chipGo = new GameObject("Chip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(LayoutElement));
            chipGo.transform.SetParent(rowGo.transform, false);
            _previewChipBg = (RectTransform)chipGo.transform;
            _previewChipSky = chipGo.GetComponent<Image>();
            _previewChipSky.color = Color.white;
            _previewChipSky.raycastTarget = false;
            var chipOutline = chipGo.GetComponent<Outline>();
            chipOutline.effectColor = LedgeUITokens.PanelEdge2;
            chipOutline.effectDistance = new Vector2(LedgeUITokens.HairlineWidth, -LedgeUITokens.HairlineWidth);
            var chipLe = chipGo.GetComponent<LayoutElement>();
            chipLe.minWidth = 40f; chipLe.preferredWidth = 40f;
            chipLe.minHeight = 44f; chipLe.preferredHeight = 44f;

            // Accent dot (centered)
            var dotGo = new GameObject("AccentDot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            dotGo.transform.SetParent(chipGo.transform, false);
            var dotRt = (RectTransform)dotGo.transform;
            dotRt.anchorMin = new Vector2(0.5f, 0.5f);
            dotRt.anchorMax = new Vector2(0.5f, 0.5f);
            dotRt.pivot = new Vector2(0.5f, 0.5f);
            dotRt.sizeDelta = new Vector2(16f, 16f);
            _previewChipAccent = dotGo.GetComponent<Image>();
            _previewChipAccent.sprite = GetDiscSprite();
            _previewChipAccent.color = Color.white;
            _previewChipAccent.raycastTarget = false;

            // Right side: label + "HOW OTHERS SEE YOU" caption.
            var textColGo = new GameObject("TextCol", typeof(RectTransform), typeof(VerticalLayoutGroup));
            textColGo.transform.SetParent(rowGo.transform, false);
            var textVl = textColGo.GetComponent<VerticalLayoutGroup>();
            textVl.spacing = 3f;
            textVl.childAlignment = TextAnchor.MiddleLeft;
            textVl.childControlWidth = true;
            // Control height so the label + caption stack by their declared
            // heights rather than overlapping at a default rect height.
            textVl.childControlHeight = true;
            textVl.childForceExpandWidth = true;
            textVl.childForceExpandHeight = false;
            // Fill the row's remaining width beside the chip.
            var textColLe = textColGo.AddComponent<LayoutElement>();
            textColLe.flexibleWidth = 1f;

            _previewLabel = MakeText(textColGo.transform, "Label", LedgeUITokens.UIFont, 13f, LedgeUITokens.Ink, "—");
            _previewLabel.fontStyle = FontStyles.Bold;
            _previewLabel.alignment = TextAlignmentOptions.MidlineLeft;
            AddLayoutHeight(_previewLabel.gameObject, 18f);

            var caption = MakeText(textColGo.transform, "Caption", LedgeUITokens.MonoFont, 9.5f, LedgeUITokens.InkDim, "HOW OTHERS SEE YOU");
            caption.fontStyle = FontStyles.UpperCase;
            caption.characterSpacing = 16f;
            caption.alignment = TextAlignmentOptions.MidlineLeft;
            AddLayoutHeight(caption.gameObject, 12f);
        }

        private void BuildSkinPanel(Transform parent)
        {
            var hostGo = new GameObject("SkinPanel", typeof(RectTransform), typeof(LayoutElement));
            hostGo.transform.SetParent(parent, false);
            var le = hostGo.GetComponent<LayoutElement>();
            le.preferredWidth = 520f; le.minWidth = 520f;
            // Taller than the identity panel so the kit's 14-skin grid has
            // breathing room. Excess vertical is absorbed by the ScrollRect
            // we wrap around the grid below.
            le.preferredHeight = 540f; le.minHeight = 540f;

            var glass = LedgeGlassPanel.Build(hostGo.transform, "Glass",
                padding: new Vector2(26f, 24f));
            var gRt = glass.GetComponent<RectTransform>();
            gRt.anchorMin = Vector2.zero; gRt.anchorMax = Vector2.one;
            gRt.offsetMin = Vector2.zero; gRt.offsetMax = Vector2.zero;

            var col = new GameObject("Col", typeof(RectTransform));
            var colRt = (RectTransform)col.transform;
            colRt.SetParent(glass.Content, false);
            colRt.anchorMin = Vector2.zero; colRt.anchorMax = Vector2.one;
            colRt.offsetMin = Vector2.zero; colRt.offsetMax = Vector2.zero;
            var vl = col.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 18f;
            vl.childAlignment = TextAnchor.UpperLeft;
            vl.childControlWidth = true;
            // Control height so the header / scroll / actions rows size from
            // their declared heights instead of collapsing.
            vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            // Header row: SectionLabel + "N OF M" mono.
            var headerGo = new GameObject("Header",
                typeof(RectTransform), typeof(HorizontalLayoutGroup));
            headerGo.transform.SetParent(colRt, false);
            var headerHl = headerGo.GetComponent<HorizontalLayoutGroup>();
            headerHl.spacing = 8f;
            headerHl.childAlignment = TextAnchor.LowerLeft;
            headerHl.childControlWidth = true;
            // Control height so the label doesn't keep a default rect height
            // (which is what made "BOARD SKIN" stack vertically once its width
            // also collapsed); the row height stays pinned by AddLayoutHeight.
            headerHl.childControlHeight = true;
            headerHl.childForceExpandWidth = true;
            headerHl.childForceExpandHeight = false;
            AddLayoutHeight(headerGo, 14f);

            var headerLabel = MakeText(headerGo.transform, "Section", LedgeUITokens.MonoFont,
                LedgeUITokens.SectionLabelSize, LedgeUITokens.InkDim, "BOARD SKIN");
            headerLabel.fontStyle = FontStyles.UpperCase;
            headerLabel.characterSpacing = 22f;
            headerLabel.alignment = TextAlignmentOptions.BottomLeft;
            var headerLabelLe = headerLabel.gameObject.AddComponent<LayoutElement>();
            headerLabelLe.flexibleWidth = 1f;

            // F5 caption: the kit now ships all 14 skins so this reads as
            // a static "14 SKINS" rather than the old "N OF 14" rollout
            // counter.
            _skinCountLabel = MakeText(headerGo.transform, "Count", LedgeUITokens.MonoFont, 9.5f,
                LedgeUITokens.InkMute, "14 SKINS");
            _skinCountLabel.fontStyle = FontStyles.UpperCase;
            _skinCountLabel.characterSpacing = 14f;
            _skinCountLabel.alignment = TextAlignmentOptions.BottomRight;
            var countLe = _skinCountLabel.gameObject.AddComponent<LayoutElement>();
            countLe.preferredWidth = 70f;
            countLe.minWidth = 70f;

            // Scrollable 3-col grid. With 14 swatches at ~145×130 the
            // content runs ~5 rows tall (~720); the viewport is bounded so
            // the panel stays compact and the user scrolls through the
            // remaining skins.
            var scrollGo = new GameObject("ScrollHost",
                typeof(RectTransform), typeof(ScrollRect), typeof(Image));
            scrollGo.transform.SetParent(colRt, false);
            var scrollImg = scrollGo.GetComponent<Image>();
            scrollImg.color = new Color(0f, 0f, 0f, 0f);
            scrollImg.raycastTarget = true; // captures wheel events
            AddLayoutHeight(scrollGo, 360f);

            var viewportGo = new GameObject("Viewport",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var viewportRt = (RectTransform)viewportGo.transform;
            viewportRt.anchorMin = Vector2.zero; viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = Vector2.zero; viewportRt.offsetMax = Vector2.zero;
            var viewportImg = viewportGo.GetComponent<Image>();
            viewportImg.color = new Color(0f, 0f, 0f, 0f);
            viewportImg.raycastTarget = true;

            var gridGo = new GameObject("SkinGrid",
                typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            gridGo.transform.SetParent(viewportGo.transform, false);
            var gridRt = (RectTransform)gridGo.transform;
            gridRt.anchorMin = new Vector2(0f, 1f);
            gridRt.anchorMax = new Vector2(1f, 1f);
            gridRt.pivot = new Vector2(0.5f, 1f);
            gridRt.anchoredPosition = Vector2.zero;
            gridRt.sizeDelta = Vector2.zero;

            var grid = gridGo.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(145f, 130f);
            grid.spacing = new Vector2(14f, 14f);
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;

            var fitter = gridGo.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.content = gridRt;
            scroll.viewport = viewportRt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            _swatches.Clear();
            for (int i = 0; i < DefaultSkins.Length; i++)
            {
                _swatches.Add(BuildSwatch(gridGo.transform, DefaultSkins[i]));
            }

            // Actions row right-aligned.
            var actionsGo = new GameObject("Actions",
                typeof(RectTransform), typeof(HorizontalLayoutGroup));
            actionsGo.transform.SetParent(colRt, false);
            var actionsHl = actionsGo.GetComponent<HorizontalLayoutGroup>();
            actionsHl.spacing = 12f;
            actionsHl.childAlignment = TextAnchor.MiddleRight;
            // Control width so the buttons' preferred widths apply (with it off
            // they collapsed to a default rect); keep force-expand-height off so
            // the buttons hold their ~48px height and this row doesn't claim the
            // column's vertical slack and balloon the buttons.
            actionsHl.childControlWidth = true;
            actionsHl.childControlHeight = true;
            actionsHl.childForceExpandWidth = false;
            actionsHl.childForceExpandHeight = false;
            AddLayoutHeight(actionsGo, 52f);

            var backBtn = LedgeButton.Build(actionsGo.transform, "Back", LedgeButton.Variant.Ghost, LedgeButton.Size.Lg,
                FireBack);
            var backLe = backBtn.gameObject.AddComponent<LayoutElement>();
            backLe.preferredWidth = 110f; backLe.minWidth = 90f;
            backLe.preferredHeight = 48f; backLe.minHeight = 44f;

            var readyBtn = LedgeButton.Build(actionsGo.transform, "Ready", LedgeButton.Variant.Primary, LedgeButton.Size.Lg,
                FireReady);
            var readyLe = readyBtn.gameObject.AddComponent<LayoutElement>();
            readyLe.preferredWidth = 130f; readyLe.minWidth = 110f;
            readyLe.preferredHeight = 48f; readyLe.minHeight = 44f;
        }

        private SkinSwatchHandles BuildSwatch(Transform parent, SkinDef skin)
        {
            var containerGo = new GameObject($"Swatch_{skin.Id}",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                typeof(Outline), typeof(Button));
            containerGo.transform.SetParent(parent, false);

            var containerBg = containerGo.GetComponent<Image>();
            containerBg.color = new Color(0f, 0f, 0f, 0f);
            var containerOutline = containerGo.GetComponent<Outline>();
            containerOutline.effectColor = LedgeUITokens.PanelEdge;
            containerOutline.effectDistance = new Vector2(LedgeUITokens.HairlineWidth, -LedgeUITokens.HairlineWidth);

            // Sky preview area (96 tall × full width, top-anchored).
            var skyGo = new GameObject("Sky", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
            skyGo.transform.SetParent(containerGo.transform, false);
            var skyRt = (RectTransform)skyGo.transform;
            skyRt.anchorMin = new Vector2(0f, 1f);
            skyRt.anchorMax = new Vector2(1f, 1f);
            skyRt.pivot = new Vector2(0.5f, 1f);
            skyRt.offsetMin = new Vector2(4f, -4f - 96f);
            skyRt.offsetMax = new Vector2(-4f, -4f);
            var skyImg = skyGo.GetComponent<Image>();
            skyImg.sprite = GetRadialGradientSprite(skin.SkyInner, skin.SkyOuter);
            skyImg.color = Color.white;
            skyImg.raycastTarget = false;
            var skyOutline = skyGo.GetComponent<Outline>();
            skyOutline.effectColor = LedgeUITokens.PanelEdge2;
            skyOutline.effectDistance = new Vector2(LedgeUITokens.HairlineWidth, -LedgeUITokens.HairlineWidth);

            // Accent hex hint approximated as a centered disc.
            var dotGo = new GameObject("AccentDot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            dotGo.transform.SetParent(skyGo.transform, false);
            var dotRt = (RectTransform)dotGo.transform;
            dotRt.anchorMin = new Vector2(0.5f, 0.5f);
            dotRt.anchorMax = new Vector2(0.5f, 0.5f);
            dotRt.pivot = new Vector2(0.5f, 0.5f);
            dotRt.sizeDelta = new Vector2(34f, 34f);
            var dotImg = dotGo.GetComponent<Image>();
            dotImg.sprite = GetDiscSprite();
            dotImg.color = new Color(skin.Accent.r, skin.Accent.g, skin.Accent.b, 0.85f);
            dotImg.raycastTarget = false;

            // Bottom name + chosen caption row.
            var bottomGo = new GameObject("Bottom", typeof(RectTransform));
            bottomGo.transform.SetParent(containerGo.transform, false);
            var bottomRt = (RectTransform)bottomGo.transform;
            bottomRt.anchorMin = new Vector2(0f, 0f);
            bottomRt.anchorMax = new Vector2(1f, 0f);
            bottomRt.pivot = new Vector2(0.5f, 0f);
            bottomRt.offsetMin = new Vector2(8f, 6f);
            bottomRt.offsetMax = new Vector2(-8f, 6f + 24f);

            var bottomHl = bottomGo.AddComponent<HorizontalLayoutGroup>();
            bottomHl.spacing = 6f;
            bottomHl.childAlignment = TextAnchor.MiddleLeft;
            bottomHl.childControlWidth = true;
            bottomHl.childControlHeight = false;
            bottomHl.childForceExpandWidth = false;
            bottomHl.childForceExpandHeight = false;

            var nameLabel = MakeText(bottomRt, "Name", LedgeUITokens.UIFont, 12f, LedgeUITokens.Ink, skin.Name);
            nameLabel.fontStyle = FontStyles.Bold;
            nameLabel.alignment = TextAlignmentOptions.MidlineLeft;
            var nameLe = nameLabel.gameObject.AddComponent<LayoutElement>();
            nameLe.flexibleWidth = 1f;

            var chosenLabel = MakeText(bottomRt, "Chosen", LedgeUITokens.MonoFont, 8f, LedgeUITokens.Accent, "CHOSEN");
            chosenLabel.fontStyle = FontStyles.UpperCase;
            chosenLabel.characterSpacing = 16f;
            chosenLabel.alignment = TextAlignmentOptions.MidlineRight;
            var chosenLe = chosenLabel.gameObject.AddComponent<LayoutElement>();
            chosenLe.preferredWidth = 50f;
            chosenLe.minWidth = 50f;
            chosenLabel.gameObject.SetActive(false);

            // Click → select.
            var btn = containerGo.GetComponent<Button>();
            btn.targetGraphic = containerBg;
            string capturedId = skin.Id;
            btn.onClick.AddListener(() =>
            {
                if (_selectedSkinId == capturedId) return;
                _selectedSkinId = capturedId;
                RefreshSelection();
            });

            return new SkinSwatchHandles
            {
                Id = skin.Id,
                ContainerBg = containerBg,
                ContainerOutline = containerOutline,
                NameLabel = nameLabel,
                ChosenLabel = chosenLabel,
            };
        }

        private static TMP_InputField BuildInputField(Transform parent, string initial)
        {
            var go = new GameObject("NameField",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
            go.transform.SetParent(parent, false);
            var bg = go.GetComponent<Image>();
            bg.color = new Color(8f / 255f, 10f / 255f, 22f / 255f, 0.55f);
            var outline = go.GetComponent<Outline>();
            outline.effectColor = LedgeUITokens.PanelEdge2;
            outline.effectDistance = new Vector2(LedgeUITokens.HairlineWidth, -LedgeUITokens.HairlineWidth);
            AddLayoutHeight(go, 42f);

            var textArea = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            var taRt = (RectTransform)textArea.transform;
            taRt.SetParent(go.transform, false);
            taRt.anchorMin = Vector2.zero; taRt.anchorMax = Vector2.one;
            taRt.offsetMin = new Vector2(14f, 4f);
            taRt.offsetMax = new Vector2(-14f, -4f);

            var phGo = new GameObject("Placeholder", typeof(RectTransform));
            var phRt = (RectTransform)phGo.transform;
            phRt.SetParent(taRt, false);
            phRt.anchorMin = Vector2.zero; phRt.anchorMax = Vector2.one;
            phRt.offsetMin = Vector2.zero; phRt.offsetMax = Vector2.zero;
            var ph = phGo.AddComponent<TextMeshProUGUI>();
            ph.text = "Aurelia";
            ph.font = LedgeUITokens.UIFont;
            ph.fontSize = 16f;
            ph.color = LedgeUITokens.InkMute;
            ph.fontStyle = FontStyles.Italic;
            ph.alignment = TextAlignmentOptions.MidlineLeft;
            ph.raycastTarget = false;

            var txGo = new GameObject("Text", typeof(RectTransform));
            var txRt = (RectTransform)txGo.transform;
            txRt.SetParent(taRt, false);
            txRt.anchorMin = Vector2.zero; txRt.anchorMax = Vector2.one;
            txRt.offsetMin = Vector2.zero; txRt.offsetMax = Vector2.zero;
            var txt = txGo.AddComponent<TextMeshProUGUI>();
            txt.font = LedgeUITokens.UIFont;
            txt.fontSize = 16f;
            txt.color = LedgeUITokens.Ink;
            txt.alignment = TextAlignmentOptions.MidlineLeft;
            txt.raycastTarget = false;

            var input = go.AddComponent<TMP_InputField>();
            input.targetGraphic = bg;
            input.textViewport = taRt;
            input.textComponent = txt;
            input.placeholder = ph;
            input.fontAsset = LedgeUITokens.UIFont;
            input.pointSize = 16f;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.text = initial ?? "";
            return input;
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

        private static void AddSpacer(Transform parent, float height)
        {
            var go = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var le = go.GetComponent<LayoutElement>();
            le.minHeight = height; le.preferredHeight = height;
        }

        private static Color Hex(string h)
        {
            ColorUtility.TryParseHtmlString("#" + h, out var c);
            return c;
        }

        // ── Sprite generators ─────────────────────────────────────────────

        private static readonly Dictionary<(Color, Color), Sprite> _radialCache = new Dictionary<(Color, Color), Sprite>();
        private static Sprite GetRadialGradientSprite(Color inner, Color outer)
        {
            var key = (inner, outer);
            if (_radialCache.TryGetValue(key, out var s) && s != null) return s;
            const int N = 128;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color32[N * N];
            // Kit specifies the radial focus at 50% 40% — slightly above center.
            float cx = (N - 1) * 0.5f;
            float cy = (N - 1) * 0.40f;
            float maxR = Mathf.Sqrt(cx * cx + (N - 1 - cy) * (N - 1 - cy));
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float t = Mathf.Clamp01(d / maxR);
                    Color c = Color.Lerp(inner, outer, t);
                    px[y * N + x] = c;
                }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            var sprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            _radialCache[key] = sprite;
            return sprite;
        }

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
