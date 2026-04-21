using UnityEngine;
using System.Collections.Generic;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Config;
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
        private Text _titleLabel;
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
            EnsureTitleLabel();
            RefreshTitleLabel();
        }

        /// Propagates a renamed player (see LedgeAction.SetDisplayName / U14)
        /// down to the board title banner and the owner's Center space label.
        /// No-op if the name is unchanged — keeps the scene echo-driven refresh
        /// from thrashing the text component every frame.
        public void SetOwnerName(string ownerName)
        {
            if (string.Equals(_ownerName, ownerName, System.StringComparison.Ordinal)) return;
            _ownerName = ownerName;
            RefreshTitleLabel();
            RefreshCenterSpaceLabel();
        }

        /// U-126 birds-eye board title banner. Spawned above the board at a
        /// distance proportional to the outer radius so it scales sensibly
        /// with the presenter's configured size. Text is plain UnityEngine.UI.Text
        /// to stay inside the existing Canvas without a TMP dependency on
        /// this prefab. Null-safe — if no Canvas ancestor is found the label
        /// quietly stays unspawned (board still renders fine).
        private void EnsureTitleLabel()
        {
            if (_titleLabel != null) return;
            var go = new GameObject("BoardTitleLabel");
            go.transform.SetParent(transform, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, outerRadius + 60f);
            rect.sizeDelta = new Vector2(outerRadius * 2.2f, 60f);
            _titleLabel = go.AddComponent<Text>();
            _titleLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _titleLabel.alignment = TextAnchor.MiddleCenter;
            _titleLabel.fontSize = 28;
            _titleLabel.color = Color.white;
            _titleLabel.raycastTarget = false;
            _titleLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            _titleLabel.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private void RefreshTitleLabel()
        {
            if (_titleLabel == null) return;
            _titleLabel.text = string.IsNullOrWhiteSpace(_ownerName)
                ? (_boardState != null ? $"Board {_boardState.BoardId}" : "Board")
                : _ownerName;
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
