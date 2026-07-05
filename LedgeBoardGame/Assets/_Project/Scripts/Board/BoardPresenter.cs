using UnityEngine;
using System.Collections.Generic;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Config;
using Magi.LedgeBoardGame.UI;
using TMPro;
using UnityEngine.UI;

namespace Magi.LedgeBoardGame.Board
{
    public class BoardPresenter : MonoBehaviour
    {
        [SerializeField] private BoardLayoutConfig layoutConfig;
        [SerializeField] private SpaceView spaceViewPrefab;
        [Header("Layout")]
        [SerializeField] private float innerRingRadius = 100f;
        [SerializeField] private float ring2Radius = 200f;
        [SerializeField] private float ring3Radius = 300f;
        [SerializeField] private float outerRadius = 400f;
        [SerializeField] private float ledgeRadius = 450f;

        [Header("Optional decoration (null by default)")]
        [SerializeField] private Sprite backgroundSprite;
        [SerializeField] private Color backgroundColor = Color.white;
        [SerializeField] private Vector3 backgroundScale = new Vector3(0.53f, 0.53f, 0.53f);

        private BoardState _boardState;
        private string _ownerName;
        private Image _backgroundImage;
        private Image _eliminatedOverlay;
        // Bottom-center kit "nameplate" — replaces the legacy top-of-board
        // white title text. Glass pill with placeholder skin chip + owner
        // name + optional ACTIVE caption when this is the current-turn
        // board. Matches kit/ledge-board-game/project/ui/frame-hud-np.jsx
        // → NamedBoard.
        private GameObject _nameplate;
        private TMP_Text _nameplateName;
        private TMP_Text _nameplateActiveCaption;

        // Control-handoff visitor chrome (CONTROL-HANDOFF-SPEC.md 2026-06-15):
        // small pill above the board reading "<Name> is acting here" + a
        // cool dot in the visitor's skin accent. Visible only while a
        // visiting move is in flight on this board.
        private GameObject _visitorPill;
        private Image _visitorPillDot;
        private TMP_Text _visitorPillName;
        private int _activeVisitorEntryTileId = -1;

        private bool _isEliminated;
        private readonly Dictionary<int, SpaceView> _spaceViews = new Dictionary<int, SpaceView>();

        public BoardState BoardState => _boardState;
        public IReadOnlyDictionary<int, SpaceView> SpaceViews => _spaceViews;
        public string OwnerName => _ownerName;

        public void Initialize(BoardState state, string ownerName = null)
        {
            _boardState = state;
            _ownerName = ownerName;
            EnsureBackground();
            BuildSpaceViews();
            PositionSpaceViews();
            UpdateView();
            EnsureNameplate();
            RefreshNameplate();
        }

        /// Propagates a renamed player (see LedgeAction.SetDisplayName / U14)
        /// down to the board title banner and the owner's Center space label.
        /// No-op if the name is unchanged — keeps the scene echo-driven refresh
        /// from thrashing the text component every frame.
        public void SetOwnerName(string ownerName)
        {
            if (string.Equals(_ownerName, ownerName, System.StringComparison.Ordinal)) return;
            _ownerName = ownerName;
            RefreshNameplate();
            RefreshCenterSpaceLabel();
        }

        /// Toggle the kit nameplate's ACTIVE caption + accent halo. Called
        /// from GameController whenever the current-turn board changes (see
        /// UpdateDreamCanvasActiveBoard). Idempotent — safe to call every
        /// turn even when state hasn't changed.
        public void SetActiveDisplay(bool active)
        {
            if (_nameplateActiveCaption != null) _nameplateActiveCaption.gameObject.SetActive(active);
            if (_nameplate == null) return;
            var outline = _nameplate.GetComponent<Outline>();
            if (outline == null) return;
            outline.effectColor = active
                ? new Color(LedgeUITokens.Accent.r, LedgeUITokens.Accent.g, LedgeUITokens.Accent.b, 0.55f)
                : LedgeUITokens.PanelEdge2;
        }

        /// Apply the control-handoff visitor visualization (path glow on
        /// the entry tile + pill tag near the board). Per
        /// CONTROL-HANDOFF-SPEC.md the visitor's COOL skin accent is the
        /// identity — never the warm turn accent. Pass entryTileId < 0 to
        /// show the pill only (no tile glow).
        public void SetVisitor(string visitorName, Color visitorAccent, int entryTileId)
        {
            ClearVisitorOverlayInternal();
            EnsureVisitorPill();
            if (_visitorPillName != null) _visitorPillName.text =
                string.IsNullOrEmpty(visitorName) ? "Visitor is acting here" : $"{visitorName} is acting here";
            if (_visitorPillDot != null) _visitorPillDot.color = visitorAccent;
            if (_visitorPill != null) _visitorPill.SetActive(true);

            if (entryTileId >= 0 && _spaceViews.TryGetValue(entryTileId, out var entryView) && entryView != null)
            {
                entryView.SetVisitorOverlay(visitorAccent);
            }
            _activeVisitorEntryTileId = entryTileId;
        }

        public void ClearVisitor()
        {
            ClearVisitorOverlayInternal();
            if (_visitorPill != null) _visitorPill.SetActive(false);
        }

        private void ClearVisitorOverlayInternal()
        {
            if (_activeVisitorEntryTileId >= 0
                && _spaceViews.TryGetValue(_activeVisitorEntryTileId, out var view)
                && view != null)
            {
                view.ClearVisitorOverlay();
            }
            _activeVisitorEntryTileId = -1;
        }

        /// Glass pill anchored above the top of the board, sized to fit
        /// "Name is acting here" at UI 12pt. Built once, reused by SetVisitor.
        private void EnsureVisitorPill()
        {
            if (_visitorPill != null) return;

            var go = new GameObject("VisitorPill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            // Above the board — clear of the outermost ledge tiles (which sit
            // near ledgeRadius) so the pill never occludes the entry tile it
            // annotates. Sits higher than the nameplate's mirror distance below.
            rt.anchoredPosition = new Vector2(0f, ledgeRadius + 90f);

            var bg = go.GetComponent<Image>();
            bg.color = LedgeUITokens.Panel;
            bg.raycastTarget = false;
            var outline = go.GetComponent<Outline>();
            outline.effectColor = LedgeUITokens.PanelEdge2;
            outline.effectDistance = new Vector2(LedgeUITokens.HairlineWidth, -LedgeUITokens.HairlineWidth);

            var hl = go.GetComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(20, 24, 12, 12);
            hl.spacing = 12f;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = false;
            hl.childControlHeight = false;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = false;

            var fitter = go.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Cool dot — visitor's skin accent.
            var dotGo = new GameObject("Dot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
            dotGo.transform.SetParent(go.transform, false);
            var dotRt = (RectTransform)dotGo.transform;
            dotRt.sizeDelta = new Vector2(20f, 20f);
            _visitorPillDot = dotGo.GetComponent<Image>();
            _visitorPillDot.color = LedgeUITokens.AccentCool;
            _visitorPillDot.raycastTarget = false;
            var dotLe = dotGo.GetComponent<LayoutElement>();
            dotLe.preferredWidth = 20f; dotLe.minWidth = 20f;
            dotLe.preferredHeight = 20f; dotLe.minHeight = 20f;

            // "<Name> is acting here" UI bold 18pt.
            var nameGo = new GameObject("Label", typeof(RectTransform));
            nameGo.transform.SetParent(go.transform, false);
            _visitorPillName = nameGo.AddComponent<TextMeshProUGUI>();
            _visitorPillName.font = LedgeUITokens.UIFont;
            _visitorPillName.fontSize = 18f;
            _visitorPillName.fontStyle = FontStyles.Bold;
            _visitorPillName.color = LedgeUITokens.Ink;
            _visitorPillName.alignment = TextAlignmentOptions.MidlineLeft;
            _visitorPillName.text = "Visitor";
            _visitorPillName.raycastTarget = false;

            _visitorPill = go;
            _visitorPill.SetActive(false);
        }

        /// Kit "nameplate" — glass pill at bottom-center of the board carrying
        /// skin-chip + owner name. Replaces the legacy top-of-board white
        /// Text banner. Mirrors frame-hud-np.jsx → NamedBoard. The skin chip
        /// is a flat dark placeholder until the skin catalog lands. ACTIVE
        /// caption (mono accent, hidden by default) toggles via
        /// <see cref="SetActiveDisplay"/>.
        private void EnsureNameplate()
        {
            if (_nameplate != null) return;

            var go = new GameObject("BoardNameplate", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
            go.transform.SetParent(transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            // Below the board, mirroring where the legacy title sat above it.
            rect.anchoredPosition = new Vector2(0f, -(outerRadius + 60f));
            rect.sizeDelta = new Vector2(outerRadius * 1.4f, 56f);

            var bg = go.GetComponent<Image>();
            bg.color = LedgeUITokens.Panel;
            bg.raycastTarget = false;
            var outline = go.GetComponent<Outline>();
            outline.effectColor = LedgeUITokens.PanelEdge2;
            outline.effectDistance = new Vector2(LedgeUITokens.HairlineWidth, -LedgeUITokens.HairlineWidth);

            _nameplate = go;

            // Skin chip — placeholder flat-dark square. Sized to read at
            // multi-board distance (boards live at outerRadius ~400-500px).
            var chipGo = new GameObject("SkinChip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
            chipGo.transform.SetParent(rect, false);
            var chipRt = (RectTransform)chipGo.transform;
            chipRt.anchorMin = new Vector2(0f, 0.5f);
            chipRt.anchorMax = new Vector2(0f, 0.5f);
            chipRt.pivot = new Vector2(0f, 0.5f);
            chipRt.anchoredPosition = new Vector2(16f, 0f);
            chipRt.sizeDelta = new Vector2(36f, 36f);
            var chipImg = chipGo.GetComponent<Image>();
            chipImg.color = new Color(0.10f, 0.12f, 0.22f, 1f); // ~#1A1F38 — kit fallback
            chipImg.raycastTarget = false;
            var chipOutline = chipGo.GetComponent<Outline>();
            chipOutline.effectColor = LedgeUITokens.PanelEdge2;
            chipOutline.effectDistance = new Vector2(LedgeUITokens.HairlineWidth, -LedgeUITokens.HairlineWidth);

            // Name + ACTIVE caption stacked vertically to the right of chip.
            _nameplateName = MakeNameplateText(rect, "Name", LedgeUITokens.UIFont, 24f, LedgeUITokens.Ink);
            _nameplateName.fontStyle = FontStyles.Bold;
            _nameplateName.alignment = TextAlignmentOptions.MidlineLeft;
            var nameRt = _nameplateName.rectTransform;
            nameRt.anchorMin = new Vector2(0f, 0f);
            nameRt.anchorMax = new Vector2(1f, 1f);
            nameRt.pivot = new Vector2(0f, 0.5f);
            nameRt.offsetMin = new Vector2(60f, 0f);
            nameRt.offsetMax = new Vector2(-16f, 0f);

            _nameplateActiveCaption = MakeNameplateText(rect, "ActiveCaption", LedgeUITokens.MonoFont, 14f, LedgeUITokens.Accent);
            _nameplateActiveCaption.text = "ACTIVE";
            _nameplateActiveCaption.fontStyle = FontStyles.UpperCase;
            _nameplateActiveCaption.characterSpacing = 18f;
            _nameplateActiveCaption.alignment = TextAlignmentOptions.MidlineRight;
            var activeRt = _nameplateActiveCaption.rectTransform;
            activeRt.anchorMin = new Vector2(1f, 0.5f);
            activeRt.anchorMax = new Vector2(1f, 0.5f);
            activeRt.pivot = new Vector2(1f, 0.5f);
            activeRt.anchoredPosition = new Vector2(-16f, 0f);
            activeRt.sizeDelta = new Vector2(80f, 16f);
            _nameplateActiveCaption.gameObject.SetActive(false);
        }

        private void RefreshNameplate()
        {
            if (_nameplateName == null) return;
            _nameplateName.text = string.IsNullOrWhiteSpace(_ownerName)
                ? (_boardState != null ? $"Board {_boardState.BoardId}" : "Board")
                : _ownerName;
        }

        private static TMP_Text MakeNameplateText(Transform parent, string name, TMP_FontAsset font, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSize = size;
            t.color = color;
            t.raycastTarget = false;
            return t;
        }

        /// Only the Center space's label depends on ownerName
        /// (SpaceNamer.Name formats it as "[Owner] Core"); other rings are
        /// owner-agnostic. Scan the metadata map once, update whichever space
        /// is the Center, and leave the rest untouched.
        private void RefreshCenterSpaceLabel()
        {
            if (_boardState == null) return;
            foreach (var kvp in _boardState.SpaceMetadata)
            {
                if (kvp.Value.Type != SpaceType.Center) continue;
                if (_spaceViews.TryGetValue(kvp.Key, out var view) && view != null)
                    view.SetSpaceLabel(SpaceNamer.Name(kvp.Key, kvp.Value, _ownerName));
                break;
            }
        }

        private void EnsureBackground()
        {
            if (backgroundSprite == null)
            {
                if (_backgroundImage != null)
                {
                    Destroy(_backgroundImage.gameObject);
                    _backgroundImage = null;
                }
                return;
            }

            if (_backgroundImage == null)
            {
                var go = new GameObject("LedgeWheelBackground");
                go.transform.SetParent(transform, false);
                var rect = go.AddComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                _backgroundImage = go.AddComponent<Image>();
                _backgroundImage.raycastTarget = false;
                go.transform.SetSiblingIndex(0);
            }

            _backgroundImage.sprite = backgroundSprite;
            _backgroundImage.color = backgroundColor;
            _backgroundImage.preserveAspect = true;

            var bgRect = _backgroundImage.rectTransform;
            bgRect.sizeDelta = new Vector2(backgroundSprite.rect.width, backgroundSprite.rect.height);
            bgRect.localScale = backgroundScale;
        }

        private void BuildSpaceViews()
        {
            foreach (Transform child in transform)
            {
                if (_backgroundImage != null && child == _backgroundImage.transform)
                    continue;
                Destroy(child.gameObject);
            }

            _spaceViews.Clear();

            if (_boardState == null)
                return;

            foreach (var kvp in _boardState.SpaceMetadata)
            {
                var spaceId = kvp.Key;
                var meta = kvp.Value;

                var view = CreateSpaceView(spaceId, meta);
                _spaceViews[spaceId] = view;
            }
        }

        private SpaceView CreateSpaceView(int spaceId, SpaceMeta meta)
        {
            SpaceView viewInstance;
            float hexSize = LedgeSpriteFactory.RectSizeForCircumradius(
                BoardLayoutHelper.ComputeHexVisualRadius(BuildRadii()));

            if (spaceViewPrefab != null)
            {
                var go = Instantiate(spaceViewPrefab.gameObject, transform);
                go.name = $"Space_{spaceId:00}_{meta.Type}";
                viewInstance = go.GetComponent<SpaceView>();
            }
            else
            {
                var go = new GameObject($"Space_{spaceId:00}_{meta.Type}");
                go.transform.SetParent(transform, false);

                var rect = go.AddComponent<RectTransform>();
                rect.sizeDelta = new Vector2(hexSize, hexSize);

                go.AddComponent<Image>();

                viewInstance = go.AddComponent<SpaceView>();
            }

            var ownRect = viewInstance.GetComponent<RectTransform>();
            if (ownRect != null)
            {
                ownRect.sizeDelta = new Vector2(hexSize, hexSize);
            }

            var stack = _boardState.GetStack(spaceId);
            viewInstance.SetData(spaceId, meta, stack);
            viewInstance.SetSpaceLabel(SpaceNamer.Name(spaceId, meta, _ownerName));

            return viewInstance;
        }

        private BoardLayoutHelper.Radii BuildRadii() => new BoardLayoutHelper.Radii
        {
            Center = 0f,
            Inner = innerRingRadius,
            Ring2 = ring2Radius,
            Ring3 = ring3Radius,
            Outer = outerRadius,
            Ledge = ledgeRadius
        };

        private void PositionSpaceViews()
        {
            if (_boardState == null)
                return;

            var radii = BuildRadii();

            foreach (var kvp in _spaceViews)
            {
                var spaceId = kvp.Key;
                var view = kvp.Value;

                if (!_boardState.SpaceMetadata.TryGetValue(spaceId, out var meta))
                    continue;

                var pos = BoardLayoutHelper.ComputePosition(spaceId, meta, radii);
                var rect = view.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.anchoredPosition = pos;
                }
                else
                {
                    view.transform.localPosition = pos;
                }
            }
        }

        public void UpdateView()
        {
            if (_boardState == null)
                return;

            foreach (var kvp in _spaceViews)
            {
                var spaceId = kvp.Key;
                var view = kvp.Value;

                var stack = _boardState.GetStack(spaceId);
                view.UpdateTokenDisplay(stack);
            }
        }

        /// Mark this board as owned by an eliminated player. The board stays
        /// fully interactive — remaining counters can still be captured and
        /// spaces still register for ledge-hop pathing — but a semi-transparent
        /// grey overlay signals "this player is out". Called from the state-diff
        /// hook, not an animation callback, so Network mode fires too.
        public void SetEliminated(bool eliminated)
        {
            if (_isEliminated == eliminated && _eliminatedOverlay != null == eliminated) return;
            _isEliminated = eliminated;
            if (eliminated)
            {
                EnsureEliminatedOverlay();
                if (_eliminatedOverlay != null) _eliminatedOverlay.enabled = true;
            }
            else if (_eliminatedOverlay != null)
            {
                _eliminatedOverlay.enabled = false;
            }
        }

        private void EnsureEliminatedOverlay()
        {
            if (_eliminatedOverlay != null) return;
            var go = new GameObject("EliminatedOverlay");
            go.transform.SetParent(transform, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(outerRadius * 2.2f, outerRadius * 2.2f);
            _eliminatedOverlay = go.AddComponent<Image>();
            _eliminatedOverlay.color = new Color(0.15f, 0.15f, 0.18f, 0.45f);
            _eliminatedOverlay.raycastTarget = false;
            go.transform.SetAsLastSibling();
        }

        public void HighlightValidMoves(List<SpaceId> spaces)
        {
            foreach (var v in _spaceViews.Values)
            {
                v.SetValidTarget(false);
            }

            if (spaces == null)
                return;

            foreach (var space in spaces)
            {
                if (space.BoardId != _boardState.BoardId)
                    continue;

                if (_spaceViews.TryGetValue(space.Id, out var view))
                {
                    view.SetValidTarget(true);
                }
            }
        }

        /// Multi-hop reach highlight: each space pulses at an intensity tied to its
        /// distance from the selected source. Step-1 neighbors get the full pulse and
        /// farther hops fade toward `minIntensity`, so the player's eye reads the
        /// brightest rings as the easiest/shortest moves and the dimmest as the
        /// edge-of-range reach. `tone` paints the pulse white or near-black to match
        /// the energy being moved/placed; the hop distance is also passed through so
        /// SpaceView can phase-shift the pulse into an outward-traveling ripple.
        /// Distance 0 (source itself) is not highlighted here — HighlightSelection
        /// carries the source frame.
        /// When `uniformIntensity` is true, every target pulses at full brightness
        /// regardless of hop distance — use this for placement, where all open
        /// spaces are equally valid and only the ripple's travel should signal
        /// distance from the origin.
        public void HighlightValidMovesWithDistance(Dictionary<SpaceId, int> distances, int maxDistance, Tone tone, float minIntensity = 0.35f, bool uniformIntensity = false)
        {
            foreach (var v in _spaceViews.Values)
            {
                v.SetValidTargetIntensity(0f);
            }

            if (distances == null || maxDistance <= 0) return;

            foreach (var kvp in distances)
            {
                if (kvp.Key.BoardId != _boardState.BoardId) continue;
                if (kvp.Value <= 0) continue;

                if (_spaceViews.TryGetValue(kvp.Key.Id, out var view))
                {
                    float intensity;
                    if (uniformIntensity || maxDistance == 1)
                        intensity = 1f;
                    else
                        intensity = Mathf.Lerp(minIntensity, 1f, (maxDistance - kvp.Value) / (float)(maxDistance - 1));
                    view.SetValidTargetIntensity(intensity, tone, kvp.Value);
                }
            }
        }

        public void HighlightSelection(SpaceId? selected)
        {
            foreach (var v in _spaceViews.Values)
            {
                v.SetSelected(false);
            }

            if (!selected.HasValue) return;
            if (selected.Value.BoardId != _boardState.BoardId) return;

            if (_spaceViews.TryGetValue(selected.Value.Id, out var view))
            {
                view.SetSelected(true);
            }
        }

        public void HighlightMovableSources(List<SpaceId> sources)
        {
            foreach (var v in _spaceViews.Values)
            {
                v.SetMovableSource(false);
            }

            if (sources == null) return;

            foreach (var space in sources)
            {
                if (space.BoardId != _boardState.BoardId)
                    continue;

                if (_spaceViews.TryGetValue(space.Id, out var view))
                {
                    view.SetMovableSource(true);
                }
            }
        }

        public void ClearAllStates()
        {
            foreach (var view in _spaceViews.Values)
            {
                view.SetSelected(false);
                view.SetValidTarget(false);
                view.SetMovableSource(false);
            }
        }
    }
}
