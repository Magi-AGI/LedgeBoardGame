using System.Collections.Generic;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Magi.LedgeBoardGame.Board
{
    /// Bottom-center thumbnail strip for 6-8 seat sessions. Each `SeatThumb`
    /// shows the seat's skin chip + display name; clicking a thumb pins
    /// that seat into Comparison's right slot without cycling through the
    /// others. Mirrors `kit/ledge-board-game/project/ui/frame-hud-np.jsx` →
    /// thumbnail strip block.
    ///
    /// Visibility rules:
    ///   - hidden when seat count < 6 (the cycler alone is sufficient)
    ///   - hidden when not in Comparison mode (no "right slot" to drive)
    ///   - hidden when no game state attached yet (boot)
    ///
    /// Procedurally constructed; no prefab dependency.
    public class BoardThumbStrip : MonoBehaviour
    {
        [Tooltip("Minimum seat count at which the strip appears. The cycler in BoardViewHud already covers ≤5 seats, so the strip exists to short-circuit a long cycle.")]
        [SerializeField] private int minSeatCount = 6;

        private MultiBoardLayout _layout;
        private GameState _state;

        private RectTransform _rootRect;
        private RectTransform _canvasRect;
        private RectTransform _row;
        private TMP_Text _seatsLabel;

        // Deterministic layout metrics. The strip does NOT rely on a
        // ContentSizeFitter (in this procedural canvas the fitter collapsed
        // the root to 0×0); instead the width is computed from seat count
        // and clamped to the canvas, and the row's HorizontalLayoutGroup
        // controls child widths from these values.
        private const float StripHeight = 64f;
        private const float RowSpacing = 8f;
        private const float LabelWidth = 56f;
        private const float ThumbPreferredWidth = 132f;
        private const float ThumbMinWidth = 96f;
        // Absolute floor used only when the canvas is too narrow to fit even
        // ThumbMinWidth thumbs (e.g. 8 seats on a 720px portrait canvas). At
        // this point names ellipsize; the strip still never exceeds the canvas.
        private const float CompactThumbMinWidth = 60f;
        private const float GlassPadX = 12f;    // matches LedgeGlassPanel padding.x below
        private const float SafeEdgeInset = 24f; // keep clear of canvas edges

        // boardId → thumb hooks so Refresh can re-tint without rebuilding.
        private struct Thumb
        {
            public GameObject Root;
            public Image Background;
            public Outline Outline;
            public TMP_Text Name;
            public TMP_Text Caption;
        }
        private readonly Dictionary<int, Thumb> _thumbs = new Dictionary<int, Thumb>();

        public void Initialize(MultiBoardLayout layout, GameState state)
        {
            _layout = layout;
            _state = state;
            BuildUi();
            if (_layout != null) _layout.LayoutChanged += Refresh;
            Refresh();
        }

        public void UpdateGameState(GameState state)
        {
            _state = state;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_layout != null) _layout.LayoutChanged -= Refresh;
        }

        public void Refresh()
        {
            if (_rootRect == null) return;

            bool show = ShouldShow();
            _rootRect.gameObject.SetActive(show);
            if (!show) return;

            // Cheap (≤8 seats) — rebuild on every refresh to absorb roster
            // mutations (rename, JIP, late-join) without tracking deltas.
            RebuildThumbs();
        }

        private bool ShouldShow()
        {
            if (_layout == null || _state == null) return false;
            if (_layout.Mode != MultiBoardLayout.ViewMode.Comparison) return false;
            if (_state.Boards == null || _state.Boards.Count < minSeatCount) return false;
            return true;
        }

        // ── UI construction ────────────────────────────────────────────────

        private void BuildUi()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;
            _canvasRect = canvas.transform as RectTransform;

            var root = new GameObject("BoardThumbStrip", typeof(RectTransform));
            _rootRect = (RectTransform)root.transform;
            _rootRect.SetParent(canvas.transform, false);
            _rootRect.anchorMin = new Vector2(0.5f, 0f);
            _rootRect.anchorMax = new Vector2(0.5f, 0f);
            _rootRect.pivot = new Vector2(0.5f, 0f);
            // Kit specifies bottom-center at 26px above the canvas edge, but
            // Unity's BL StatusLog (420×180) and BR ActionBar (360×64) both
            // anchor to the bottom; a center-anchored strip at the same Y
            // would collide. Lift it above the taller chrome (StatusLog) so
            // the kit's bottom-center read is preserved without overlap.
            // 28 (inset) + 180 (StatusLog) + 14 (panel gap) = 222.
            _rootRect.anchoredPosition = new Vector2(0f, 222f);
            // Explicit initial size; RebuildThumbs() recomputes width from the
            // live seat count and clamps it to the canvas each Refresh.
            _rootRect.sizeDelta = new Vector2(600f, StripHeight);
            _rootRect.SetAsLastSibling();

            var glass = LedgeGlassPanel.Build(_rootRect, "Glass", padding: new Vector2(GlassPadX, 10f));
            var gRt = glass.GetComponent<RectTransform>();
            gRt.anchorMin = Vector2.zero;
            gRt.anchorMax = Vector2.one;
            gRt.offsetMin = Vector2.zero;
            gRt.offsetMax = Vector2.zero;

            // Horizontal row: "SEATS N" label + N seat thumbs.
            var rowGo = new GameObject("Row", typeof(RectTransform));
            _row = (RectTransform)rowGo.transform;
            _row.SetParent(glass.Content, false);
            _row.anchorMin = Vector2.zero;
            _row.anchorMax = Vector2.one;
            _row.offsetMin = Vector2.zero;
            _row.offsetMax = Vector2.zero;
            var hl = rowGo.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = RowSpacing;
            hl.childAlignment = TextAnchor.MiddleLeft;
            // Control child widths so each thumb honours its LayoutElement
            // (preferred/min) and shrinks gracefully when the clamped strip
            // is narrower than the sum of preferred widths. Relying on the
            // children's own rects (childControlWidth=false) + a
            // ContentSizeFitter collapsed the root to 0×0 in this canvas.
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = false;

            _seatsLabel = MakeText(_row, "SeatsLabel", LedgeUITokens.MonoFont,
                LedgeUITokens.SectionLabelSize, LedgeUITokens.Ink, "SEATS 0");
            _seatsLabel.fontStyle = FontStyles.UpperCase;
            // Lighter tracking + full-strength ink so the strip's own title
            // stays legible at 8 seats (Claude Design fold-in). Heavy 22f
            // tracking read as noise at the compact label width.
            _seatsLabel.characterSpacing = 12f;
            var labelLe = _seatsLabel.gameObject.AddComponent<LayoutElement>();
            labelLe.minWidth = LabelWidth;
            labelLe.preferredWidth = LabelWidth;
            labelLe.flexibleWidth = 0f;
        }

        private float RowChrome(int seatCount)
            => GlassPadX * 2f + LabelWidth + seatCount * RowSpacing; // label + N gaps

        /// Strip width = chrome + N preferred thumbs, but NEVER wider than the
        /// canvas minus safe insets. Staying inside the canvas takes priority
        /// over preserving full thumb width — at 8 seats on a 720px portrait
        /// canvas this returns the (narrower) canvas width, and the thumbs
        /// compact via ComputeThumbWidth. Absolute lower bound guards only the
        /// pathological near-zero-canvas case and is itself capped to maxWidth.
        private float ComputeStripWidth(int seatCount)
        {
            float preferred = RowChrome(seatCount) + seatCount * ThumbPreferredWidth;
            if (_canvasRect == null) return preferred;
            float maxWidth = _canvasRect.rect.width - SafeEdgeInset * 2f;
            float floor = Mathf.Min(maxWidth, RowChrome(seatCount) + seatCount * CompactThumbMinWidth);
            return Mathf.Clamp(preferred, floor, maxWidth);
        }

        /// Per-thumb width derived from the already-clamped strip width so the
        /// row (childControlWidth=true) lays thumbs out without pushing the root
        /// past the canvas. Clamped to [CompactThumbMinWidth, ThumbPreferredWidth].
        private float ComputeThumbWidth(float stripWidth, int seatCount)
        {
            if (seatCount <= 0) return ThumbPreferredWidth;
            float available = stripWidth - RowChrome(seatCount);
            float per = available / seatCount;
            return Mathf.Clamp(per, CompactThumbMinWidth, ThumbPreferredWidth);
        }

        private void RebuildThumbs()
        {
            if (_row == null || _state == null || _layout == null) return;

            int seatCount = _state.Boards?.Count ?? 0;
            if (_seatsLabel != null) _seatsLabel.text = $"SEATS {seatCount}";

            // Drive the strip width explicitly (no ContentSizeFitter) so the
            // root has real dimensions; the row HLG then lays out the thumbs.
            float stripWidth = ComputeStripWidth(seatCount);
            if (_rootRect != null)
                _rootRect.sizeDelta = new Vector2(stripWidth, StripHeight);
            float thumbWidth = ComputeThumbWidth(stripWidth, seatCount);

            // Clear stale thumbs. The label stays — it's the first child.
            // Walk in reverse so child re-indexing doesn't skip entries.
            for (int i = _row.childCount - 1; i >= 1; i--)
            {
                var child = _row.GetChild(i);
                if (child != null) DestroyImmediate(child.gameObject);
            }
            _thumbs.Clear();

            int currentOpponent = _layout.CurrentOpponentBoardId;
            int leftId = _layout.ComparisonLeftBoardId;
            int localId = _layout.LocalBoardId;

            foreach (var b in _state.Boards)
            {
                if (b == null) continue;
                int boardId = b.BoardId;

                bool isActive = boardId == currentOpponent;
                bool isLeft = boardId == leftId;
                bool isLocal = boardId == localId;

                string name = ResolveOwnerName(b.PlayerId);

                var thumb = BuildSeatThumb(_row, boardId, name, isLocal, isActive, isLeft, thumbWidth);
                _thumbs[boardId] = thumb;
            }
        }

        private string ResolveOwnerName(int playerId)
        {
            if (_state?.Players == null) return $"Player {playerId}";
            for (int i = 0; i < _state.Players.Count; i++)
            {
                var p = _state.Players[i];
                if (p != null && p.Id == playerId) return p.Name;
            }
            return $"Player {playerId}";
        }

        private Thumb BuildSeatThumb(Transform parent, int boardId, string name,
                                     bool isLocal, bool isActive, bool isLeft, float thumbWidth)
        {
            var go = new GameObject($"Thumb_{boardId}",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                typeof(Outline), typeof(Button), typeof(HorizontalLayoutGroup),
                typeof(LayoutElement));
            go.transform.SetParent(parent, false);

            var bg = go.GetComponent<Image>();
            var outline = go.GetComponent<Outline>();
            outline.effectDistance = new Vector2(LedgeUITokens.HairlineWidth, -LedgeUITokens.HairlineWidth);
            TintSeatThumb(bg, outline, isActive);

            var le = go.GetComponent<LayoutElement>();
            le.minWidth = thumbWidth;
            le.preferredWidth = thumbWidth;
            le.flexibleWidth = 0f;
            le.minHeight = 38f;
            le.preferredHeight = 38f;

            var hl = go.GetComponent<HorizontalLayoutGroup>();
            // Symmetric horizontal padding so the tray reads intentional at
            // both 6 and 8 seats (was 8/12 asymmetric — the extra right pad
            // looked reactive against the compact 8-seat thumb width).
            hl.padding = new RectOffset(10, 10, 7, 7);
            hl.spacing = 8f;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = false;
            hl.childControlHeight = false;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = false;

            // Skin chip placeholder — same flat-dark fill as YouPanel's
            // chip and the board nameplate's. Will be replaced by the
            // player's chosen skin once the skin catalog lands.
            var chipGo = new GameObject("SkinChip",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                typeof(Outline), typeof(LayoutElement));
            chipGo.transform.SetParent(go.transform, false);
            var chipRt = (RectTransform)chipGo.transform;
            chipRt.sizeDelta = new Vector2(22f, 22f);
            var chipImg = chipGo.GetComponent<Image>();
            // Placeholder swatch lifted well above the tray/thumb fill
            // (LedgeUITokens.Panel is ~0.08 luma) so the unselected chip
            // reads as a distinct element until the skin catalog supplies
            // the player's real colour. Paired with a brighter edge below.
            chipImg.color = new Color(0.22f, 0.26f, 0.42f, 1f);
            chipImg.raycastTarget = false;
            var chipOutline = chipGo.GetComponent<Outline>();
            chipOutline.effectColor = LedgeUITokens.PanelEdge2;
            chipOutline.effectDistance = new Vector2(LedgeUITokens.HairlineWidth, -LedgeUITokens.HairlineWidth);
            var chipLe = chipGo.GetComponent<LayoutElement>();
            chipLe.minWidth = 22f;
            chipLe.preferredWidth = 22f;
            chipLe.minHeight = 22f;
            chipLe.preferredHeight = 22f;

            // Name + optional "YOU" caption stacked vertically.
            var textCol = new GameObject("TextCol", typeof(RectTransform), typeof(VerticalLayoutGroup));
            textCol.transform.SetParent(go.transform, false);
            var col = textCol.GetComponent<VerticalLayoutGroup>();
            col.spacing = 2f;
            col.childAlignment = TextAnchor.MiddleLeft;
            col.childControlWidth = true;
            col.childControlHeight = true;
            col.childForceExpandWidth = false;
            col.childForceExpandHeight = false;

            var nameText = MakeText(textCol.transform, "Name", LedgeUITokens.UIFont, 12f,
                isActive ? LedgeUITokens.Accent : LedgeUITokens.Ink, name);
            nameText.fontStyle = FontStyles.Bold;
            nameText.alignment = TextAlignmentOptions.MidlineLeft;

            TMP_Text caption = null;
            if (isLocal)
            {
                // Quiet but legible at compact thumb widths: a touch larger,
                // lighter tracking, and InkFaint (0.62) instead of InkDim
                // (0.40) so "YOU" doesn't read as clipped/washed out.
                caption = MakeText(textCol.transform, "Caption", LedgeUITokens.MonoFont,
                    9f, LedgeUITokens.InkFaint, "YOU");
                caption.fontStyle = FontStyles.UpperCase;
                caption.characterSpacing = 8f;
                caption.alignment = TextAlignmentOptions.MidlineLeft;
            }

            // Click → pin this board into the right slot. The local seat
            // and the current left slot are no-ops (the layout itself
            // ignores collisions), so we wire the button uniformly and
            // let SetOpponentBoardId gate.
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = bg;
            int capturedId = boardId;
            btn.onClick.AddListener(() =>
            {
                if (_layout == null) return;
                _layout.SetOpponentBoardId(capturedId);
            });

            return new Thumb
            {
                Root = go,
                Background = bg,
                Outline = outline,
                Name = nameText,
                Caption = caption,
            };
        }

        private static void TintSeatThumb(Image bg, Outline outline, bool active)
        {
            if (active)
            {
                bg.color = new Color(LedgeUITokens.Accent.r, LedgeUITokens.Accent.g, LedgeUITokens.Accent.b, 0.10f);
                outline.effectColor = new Color(LedgeUITokens.Accent.r, LedgeUITokens.Accent.g, LedgeUITokens.Accent.b, 0.45f);
            }
            else
            {
                bg.color = LedgeUITokens.Panel;
                outline.effectColor = LedgeUITokens.PanelEdge;
            }
        }

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
    }
}
