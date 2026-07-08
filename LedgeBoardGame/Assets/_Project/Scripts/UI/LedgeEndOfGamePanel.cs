using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Magi.LedgeBoardGame.UI
{
    /// End-of-game theatrical takeover. Built from
    /// `kit/ledge-board-game/project/ui/frame-end.jsx` → EndFrame: the final
    /// board pushed off the right edge with a heavy horizontal gradient scrim
    /// on the left, a giant Fraunces italic "{winner} prevails." block, a
    /// skin-chip identity row with a one-line outcome summary, an optional
    /// "How it turned" recap panel, and a Rematch / Back-to-lobby / View
    /// replay action row.
    ///
    /// Spawned eagerly under the gameplay Canvas (hidden until shown) so the
    /// `result.GameEnded` branch in NarrateStateBasedEffects can call Show()
    /// without canvas resolution at the hot path.
    public class LedgeEndOfGamePanel : MonoBehaviour
    {
        private GameObject _overlayGo;
        private TMP_Text _sectionLabel;
        private TMP_Text _winnerNameLabel;
        private TMP_Text _winnerVerbLabel;
        private TMP_Text _summaryLabel;
        private GameObject _recapPanelGo;

        // Recap layout metrics. Kept explicit (not ContentSizeFitter-derived)
        // so 0/1/2/3+ row counts all resolve to a predictable panel height and
        // the action row always sits below the glass, never on top of a row.
        private const float RecapRowHeight = 32f;
        private const float RecapGlassPadY = 18f;   // LedgeGlassPanel padding.y below
        private const float RecapSectionLabelHeight = 18f;
        private const float RecapLabelRowsGap = 6f;
        private const float RecapAllowance = 6f;
        private TMP_Text _recapSectionLabel;
        private RectTransform _recapRows;
        private LedgeButton _rematchBtn;
        private LedgeButton _backBtn;
        private LedgeButton _replayBtn;

        private Action _onRematch;
        private Action _onBackToLobby;
        private Action _onViewReplay;

        private static Sprite _gradientScrimSprite;

        private void Awake() => EnsureBuilt();

        public void EnsureBuilt()
        {
            if (_overlayGo != null) return;
            BuildUi();
            HideInternal();
        }

        // ── Public API ─────────────────────────────────────────────────────

        /// Display the end-of-game takeover.
        /// <param name="winnerName">Winning player's display name. Falls back
        ///   to "Stalemate" if null/empty.</param>
        /// <param name="turnCount">Final turn number for the "Game over · N
        ///   turns" section label.</param>
        /// <param name="summaryLine">One-line outcome summary, kit-style
        ///   ("Claimed 4 Cores · held the Ela channel"). Hidden if empty.</param>
        /// <param name="recap">Optional last-N decisive moves to render in
        ///   the "How it turned" panel. Each row gets a numbered prefix and
        ///   "Name text" line. Pass null or empty to hide the panel.</param>
        public void Show(
            string winnerName,
            int turnCount,
            string summaryLine,
            IReadOnlyList<RecapEntry> recap,
            Action onRematch,
            Action onBackToLobby,
            Action onViewReplay = null)
        {
            EnsureBuilt();
            bool hasWinner = !string.IsNullOrWhiteSpace(winnerName);
            _sectionLabel.text = $"GAME OVER · {turnCount} TURNS";
            _winnerNameLabel.text = hasWinner ? winnerName.Trim() : "Stalemate";
            _winnerVerbLabel.text = hasWinner ? "prevails." : "—";

            if (string.IsNullOrEmpty(summaryLine))
            {
                _summaryLabel.gameObject.SetActive(false);
            }
            else
            {
                _summaryLabel.gameObject.SetActive(true);
                _summaryLabel.text = summaryLine;
            }

            PopulateRecap(recap);

            _onRematch = onRematch;
            _onBackToLobby = onBackToLobby;
            _onViewReplay = onViewReplay;

            // Hide the View-replay button entirely when no callback is
            // supplied — a button that does nothing reads worse than one
            // that isn't there.
            if (_replayBtn != null) _replayBtn.gameObject.SetActive(onViewReplay != null);

            _overlayGo.SetActive(true);
        }

        public void Hide() => HideInternal();

        [ContextMenu("Show End-of-Game (Test)")]
        private void ShowTestPreview()
        {
            Show(
                winnerName: "Aurelia",
                turnCount: 42,
                summaryLine: "Claimed 4 Cores · held the Ela channel",
                recap: new List<RecapEntry>
                {
                    new RecapEntry(31, "Aurelia",  "crossed the Ela Ledge and seized Vesperin's Core."),
                    new RecapEntry(28, "Vesperin", "over-extended into the Wim wall — no retreat."),
                    new RecapEntry(24, "Aurelia",  "set the complement trap on Rha / Ela."),
                },
                onRematch: () => UnityEngine.Debug.Log("[end-of-game] Rematch (test)"),
                onBackToLobby: () => UnityEngine.Debug.Log("[end-of-game] Back to lobby (test)"),
                onViewReplay: () => UnityEngine.Debug.Log("[end-of-game] View replay (test)"));
        }

        /// One row in the "How it turned" recap. <paramref name="text"/>
        /// is a complete sentence describing the action (kit-style narrative
        /// voice, not the raw "P1 moved L from 3 to 5" log format).
        public readonly struct RecapEntry
        {
            public readonly int TurnNumber;
            public readonly string Name;
            public readonly string Text;
            public RecapEntry(int turnNumber, string name, string text)
            {
                TurnNumber = turnNumber;
                Name = name ?? "";
                Text = text ?? "";
            }
        }

        // ── Internals ─────────────────────────────────────────────────────

        private void HideInternal()
        {
            if (_overlayGo != null) _overlayGo.SetActive(false);
        }

        private void FireAndHide(Action callback)
        {
            HideInternal();
            try { callback?.Invoke(); }
            catch (Exception ex) { UnityEngine.Debug.LogError($"[end-of-game] callback threw: {ex}"); }
        }

        private void PopulateRecap(IReadOnlyList<RecapEntry> recap)
        {
            bool hasRecap = recap != null && recap.Count > 0;
            _recapPanelGo.SetActive(hasRecap);
            if (!hasRecap) return;

            // Clear stale rows.
            for (int i = _recapRows.childCount - 1; i >= 0; i--)
            {
                var child = _recapRows.GetChild(i);
                if (child != null) DestroyImmediate(child.gameObject);
            }

            for (int i = 0; i < recap.Count; i++)
            {
                BuildRecapRow(_recapRows, recap[i], current: i == 0);
            }

            SetRecapPanelHeight(recap.Count);
        }

        /// Deterministic recap sizing. Three things were wrong:
        ///   1. The host's ContentSizeFitter can't derive a preferred height
        ///      from the stretch-anchored LedgeGlassPanel content chain.
        ///   2. The Content/Rows VerticalLayoutGroups had childControlHeight
        ///      = false, so every AddLayoutHeight LayoutElement was ignored
        ///      and children fell back to their default ~100px rects — which
        ///      is what pushed rows 2-3 through the glass and under the
        ///      action row.
        ///   3. The rows container itself had no preferred height feeding the
        ///      Content layout.
        /// So: the layout groups now control child height, and both the rows
        /// container and the host get explicit LayoutElement + sizeDelta.y.
        private void SetRecapPanelHeight(int rowCount)
        {
            if (_recapPanelGo == null) return;
            rowCount = Mathf.Max(0, rowCount);
            float rowsHeight = RecapRowHeight * rowCount;
            float height = RecapGlassPadY * 2f
                           + RecapSectionLabelHeight
                           + RecapLabelRowsGap
                           + rowsHeight
                           + RecapAllowance;

            if (_recapRows != null)
            {
                var rowsLe = _recapRows.GetComponent<LayoutElement>();
                if (rowsLe == null) rowsLe = _recapRows.gameObject.AddComponent<LayoutElement>();
                rowsLe.minHeight = rowsHeight;
                rowsLe.preferredHeight = rowsHeight;
                _recapRows.sizeDelta = new Vector2(_recapRows.sizeDelta.x, rowsHeight);
            }

            var le = _recapPanelGo.GetComponent<LayoutElement>();
            if (le == null) le = _recapPanelGo.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;

            var rt = (RectTransform)_recapPanelGo.transform;
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
        }

        // ── UI construction ───────────────────────────────────────────────

        private void BuildUi()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            _overlayGo = new GameObject("EndOfGameOverlay", typeof(RectTransform));
            var overlayRt = (RectTransform)_overlayGo.transform;
            overlayRt.SetParent(canvas.transform, false);
            overlayRt.anchorMin = Vector2.zero; overlayRt.anchorMax = Vector2.one;
            overlayRt.offsetMin = Vector2.zero; overlayRt.offsetMax = Vector2.zero;
            overlayRt.SetAsLastSibling();

            // Horizontal gradient scrim: opaque on the left for legibility,
            // transparent on the right so the final board reads through.
            var scrimGo = new GameObject("Scrim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var scrimRt = (RectTransform)scrimGo.transform;
            scrimRt.SetParent(overlayRt, false);
            scrimRt.anchorMin = Vector2.zero; scrimRt.anchorMax = Vector2.one;
            scrimRt.offsetMin = Vector2.zero; scrimRt.offsetMax = Vector2.zero;
            var scrimImg = scrimGo.GetComponent<Image>();
            scrimImg.sprite = GetGradientScrimSprite();
            scrimImg.color = Color.white;
            scrimImg.raycastTarget = true;
            scrimImg.type = Image.Type.Simple;

            // Left column: 560 wide at x=80, vertically centered.
            var colGo = new GameObject("Col", typeof(RectTransform));
            var colRt = (RectTransform)colGo.transform;
            colRt.SetParent(overlayRt, false);
            colRt.anchorMin = new Vector2(0f, 0.5f);
            colRt.anchorMax = new Vector2(0f, 0.5f);
            colRt.pivot = new Vector2(0f, 0.5f);
            colRt.anchoredPosition = new Vector2(80f, 0f);
            colRt.sizeDelta = new Vector2(560f, 620f);

            var vl = colGo.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 28f;
            vl.childAlignment = TextAnchor.MiddleLeft;
            vl.childControlWidth = true;
            vl.childControlHeight = false;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            // Section label "GAME OVER · N TURNS"
            _sectionLabel = MakeText(colRt, "SectionLabel", LedgeUITokens.MonoFont,
                LedgeUITokens.SectionLabelSize, LedgeUITokens.InkDim, "GAME OVER · 0 TURNS");
            _sectionLabel.fontStyle = FontStyles.UpperCase;
            _sectionLabel.characterSpacing = 22f;
            _sectionLabel.alignment = TextAlignmentOptions.TopLeft;
            AddLayoutHeight(_sectionLabel.gameObject, 14f);

            BuildWinnerBlock(colRt);
            BuildIdentityRow(colRt);
            BuildRecapPanel(colRt);
            BuildActionRow(colRt);
        }

        private void BuildWinnerBlock(Transform parent)
        {
            var blockGo = new GameObject("WinnerBlock", typeof(RectTransform));
            var blockRt = (RectTransform)blockGo.transform;
            blockRt.SetParent(parent, false);
            var vl = blockGo.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 0f;
            vl.childAlignment = TextAnchor.UpperLeft;
            vl.childControlWidth = true;
            vl.childControlHeight = false;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            _winnerNameLabel = MakeText(blockRt, "WinnerName", LedgeUITokens.DisplayFont, 72f, LedgeUITokens.Ink, "");
            _winnerNameLabel.fontStyle = FontStyles.Italic;
            _winnerNameLabel.alignment = TextAlignmentOptions.TopLeft;
            _winnerNameLabel.characterSpacing = -2f; // ~-0.02em
            AddLayoutHeight(_winnerNameLabel.gameObject, 78f);

            _winnerVerbLabel = MakeText(blockRt, "WinnerVerb", LedgeUITokens.DisplayFont, 72f, LedgeUITokens.Accent, "");
            _winnerVerbLabel.fontStyle = FontStyles.Italic;
            _winnerVerbLabel.alignment = TextAlignmentOptions.TopLeft;
            _winnerVerbLabel.characterSpacing = -2f;
            // Accent glow approximation: 4-direction Outline copy at low alpha
            // gives a soft halo close to the kit's `0 0 30px` text-shadow.
            // UnityEngine.UI has no real blur; this is the cheap reasonable
            // facsimile.
            var glow = _winnerVerbLabel.gameObject.AddComponent<Outline>();
            glow.effectColor = new Color(LedgeUITokens.Accent.r, LedgeUITokens.Accent.g, LedgeUITokens.Accent.b, 0.40f);
            glow.effectDistance = new Vector2(2f, -2f);
            glow.useGraphicAlpha = true;
            AddLayoutHeight(_winnerVerbLabel.gameObject, 78f);
        }

        private void BuildIdentityRow(Transform parent)
        {
            var rowGo = new GameObject("Identity", typeof(RectTransform));
            var rowRt = (RectTransform)rowGo.transform;
            rowRt.SetParent(parent, false);
            var hl = rowGo.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 10f;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = false;
            hl.childControlHeight = false;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = false;
            AddLayoutHeight(rowGo, 22f);

            // Skin chip placeholder (matches LedgeYouPanel / nameplate fallback).
            var chipGo = new GameObject("SkinChip",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                typeof(Outline), typeof(LayoutElement));
            chipGo.transform.SetParent(rowRt, false);
            var chipImg = chipGo.GetComponent<Image>();
            chipImg.color = new Color(0.10f, 0.12f, 0.22f, 1f);
            chipImg.raycastTarget = false;
            var chipOutline = chipGo.GetComponent<Outline>();
            chipOutline.effectColor = LedgeUITokens.PanelEdge2;
            chipOutline.effectDistance = new Vector2(LedgeUITokens.HairlineWidth, -LedgeUITokens.HairlineWidth);
            var chipLe = chipGo.GetComponent<LayoutElement>();
            chipLe.minWidth = 18f; chipLe.preferredWidth = 18f;
            chipLe.minHeight = 18f; chipLe.preferredHeight = 18f;

            _summaryLabel = MakeText(rowRt, "Summary", LedgeUITokens.UIFont, 14f, LedgeUITokens.InkFaint, "");
            _summaryLabel.alignment = TextAlignmentOptions.MidlineLeft;
            var sumLe = _summaryLabel.gameObject.AddComponent<LayoutElement>();
            sumLe.flexibleWidth = 1f;
        }

        private void BuildRecapPanel(Transform parent)
        {
            var panelHostGo = new GameObject("RecapPanel", typeof(RectTransform));
            var panelHostRt = (RectTransform)panelHostGo.transform;
            panelHostRt.SetParent(parent, false);
            panelHostRt.sizeDelta = new Vector2(460f, 0f);
            _recapPanelGo = panelHostGo;

            var glass = LedgeGlassPanel.Build(panelHostRt, "Glass",
                padding: new Vector2(22f, RecapGlassPadY));
            var gRt = glass.GetComponent<RectTransform>();
            gRt.anchorMin = Vector2.zero; gRt.anchorMax = Vector2.one;
            gRt.offsetMin = Vector2.zero; gRt.offsetMax = Vector2.zero;

            var fitter = panelHostGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var content = new GameObject("Content", typeof(RectTransform));
            var contentRt = (RectTransform)content.transform;
            contentRt.SetParent(glass.Content, false);
            contentRt.anchorMin = Vector2.zero; contentRt.anchorMax = Vector2.one;
            contentRt.offsetMin = Vector2.zero; contentRt.offsetMax = Vector2.zero;
            var vl = content.AddComponent<VerticalLayoutGroup>();
            vl.spacing = RecapLabelRowsGap;
            vl.childAlignment = TextAnchor.UpperLeft;
            vl.childControlWidth = true;
            // Control height so the section label's and rows container's
            // LayoutElement heights are honoured. With this false the children
            // fell back to their default rects and overflowed the glass.
            vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;

            _recapSectionLabel = MakeText(contentRt, "SectionLabel", LedgeUITokens.MonoFont,
                LedgeUITokens.SectionLabelSize, LedgeUITokens.InkDim, "HOW IT TURNED");
            _recapSectionLabel.fontStyle = FontStyles.UpperCase;
            _recapSectionLabel.characterSpacing = 22f;
            _recapSectionLabel.alignment = TextAlignmentOptions.TopLeft;
            AddLayoutHeight(_recapSectionLabel.gameObject, RecapSectionLabelHeight);

            var rowsGo = new GameObject("Rows", typeof(RectTransform));
            _recapRows = (RectTransform)rowsGo.transform;
            _recapRows.SetParent(contentRt, false);
            var rowsVl = rowsGo.AddComponent<VerticalLayoutGroup>();
            rowsVl.spacing = 0f;
            rowsVl.childAlignment = TextAnchor.UpperLeft;
            rowsVl.childControlWidth = true;
            // Same reason: each row must render at its RecapRowHeight, not at
            // its default rect height.
            rowsVl.childControlHeight = true;
            rowsVl.childForceExpandWidth = true;
            rowsVl.childForceExpandHeight = false;
        }

        private void BuildRecapRow(Transform parent, RecapEntry entry, bool current)
        {
            var rowGo = new GameObject($"Row_{entry.TurnNumber}", typeof(RectTransform));
            var rowRt = (RectTransform)rowGo.transform;
            rowRt.SetParent(parent, false);
            AddLayoutHeight(rowGo, RecapRowHeight);

            var hl = rowGo.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 10f;
            hl.padding = new RectOffset(0, 0, 6, 6);
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = false;
            hl.childControlHeight = false;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = false;

            float opacity = current ? 1f : 0.62f;

            var nLabel = MakeText(rowRt, "N", LedgeUITokens.MonoFont, 10f,
                MultAlpha(LedgeUITokens.InkDim, opacity),
                entry.TurnNumber.ToString("00"));
            nLabel.characterSpacing = 5f;
            var nLe = nLabel.gameObject.AddComponent<LayoutElement>();
            nLe.minWidth = 28f; nLe.preferredWidth = 28f;

            var textLabel = MakeText(rowRt, "Text", LedgeUITokens.UIFont, 12.5f,
                MultAlpha(LedgeUITokens.InkFaint, opacity), "");
            textLabel.alignment = TextAlignmentOptions.MidlineLeft;
            // When name is empty (degraded recap from raw log lines) skip
            // the colored-name span entirely so we don't render an empty
            // wrapper + leading space.
            textLabel.text = string.IsNullOrEmpty(entry.Name)
                ? entry.Text
                : $"<color=#{ColorUtility.ToHtmlStringRGBA(MultAlpha(LedgeUITokens.Ink, opacity))}>{entry.Name}</color> {entry.Text}";
            var tLe = textLabel.gameObject.AddComponent<LayoutElement>();
            tLe.flexibleWidth = 1f;
        }

        private void BuildActionRow(Transform parent)
        {
            var rowGo = new GameObject("Actions", typeof(RectTransform));
            var rowRt = (RectTransform)rowGo.transform;
            rowRt.SetParent(parent, false);
            var hl = rowGo.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 12f;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = false;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = true;
            AddLayoutHeight(rowGo, 52f);

            _rematchBtn = LedgeButton.Build(rowRt, "Rematch", LedgeButton.Variant.Primary, LedgeButton.Size.Lg,
                () => FireAndHide(_onRematch));
            var rematchLe = _rematchBtn.gameObject.AddComponent<LayoutElement>();
            rematchLe.preferredWidth = 150f;
            rematchLe.minWidth = 120f;

            _backBtn = LedgeButton.Build(rowRt, "Back to lobby", LedgeButton.Variant.Ghost, LedgeButton.Size.Lg,
                () => FireAndHide(_onBackToLobby));
            var backLe = _backBtn.gameObject.AddComponent<LayoutElement>();
            backLe.preferredWidth = 170f;
            backLe.minWidth = 140f;

            _replayBtn = LedgeButton.Build(rowRt, "View replay", LedgeButton.Variant.Ghost, LedgeButton.Size.Lg,
                () => FireAndHide(_onViewReplay));
            var replayLe = _replayBtn.gameObject.AddComponent<LayoutElement>();
            replayLe.preferredWidth = 150f;
            replayLe.minWidth = 120f;
            // Hidden until Show() with an onViewReplay supplied.
            _replayBtn.gameObject.SetActive(false);
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

        private static Color MultAlpha(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

        // Horizontal gradient scrim. Kit specifies three stops:
        //   0-30%   solid rgba(10,13,27,0.96)
        //   30-60%  fades 0.96 → 0.40 alpha
        //   60-80%  fades 0.40 → 0.00 alpha
        //   80-100% transparent
        // Lifted into a 256×1 sprite so it stretches cleanly to canvas width.
        private static Sprite GetGradientScrimSprite()
        {
            if (_gradientScrimSprite != null) return _gradientScrimSprite;
            const int W = 256;
            var tex = new Texture2D(W, 1, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color32[W];
            // Canvas (10,13,27) — the kit scrim colour.
            const byte R = 10, G = 13, B = 27;
            for (int x = 0; x < W; x++)
            {
                float t = x / (float)(W - 1); // 0..1 left to right
                float alpha;
                if (t < 0.30f) alpha = 0.96f;
                else if (t < 0.60f) alpha = Mathf.Lerp(0.96f, 0.40f, (t - 0.30f) / 0.30f);
                else if (t < 0.80f) alpha = Mathf.Lerp(0.40f, 0.00f, (t - 0.60f) / 0.20f);
                else alpha = 0f;
                px[x] = new Color32(R, G, B, (byte)(alpha * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            _gradientScrimSprite = Sprite.Create(tex, new Rect(0, 0, W, 1), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            _gradientScrimSprite.hideFlags = HideFlags.HideAndDontSave;
            return _gradientScrimSprite;
        }
    }
}
