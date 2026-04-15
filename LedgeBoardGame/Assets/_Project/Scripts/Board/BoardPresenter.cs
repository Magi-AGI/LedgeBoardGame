// 9/29/2025 AI-Tag
// This was created with the help of Assistant, a Unity Artificial Intelligence product.

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
        [Header("Highlighting")]
        [SerializeField] private Color highlightColor = new Color(0.4f, 0.9f, 0.4f, 0.35f);
        [SerializeField] private Color selectionColor = new Color(1f, 0.85f, 0.2f, 0.6f);

        [Header("Background")]
        [SerializeField] private Sprite backgroundSprite;
        [SerializeField] private Color backgroundColor = Color.white;
        [SerializeField] private Vector3 backgroundScale = new Vector3(0.53f, 0.53f, 0.53f);

        private BoardState _boardState;
        private Image _backgroundImage;
        private readonly Dictionary<int, SpaceView> _spaceViews = new Dictionary<int, SpaceView>();

        public BoardState BoardState => _boardState;
        public IReadOnlyDictionary<int, SpaceView> SpaceViews => _spaceViews;

        public void Initialize(BoardState state)
        {
            _boardState = state;
            EnsureBackground();
            BuildSpaceViews();
            PositionSpaceViews();
            UpdateView();
        }

        private void EnsureBackground()
        {
            if (backgroundSprite == null)
                return;

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
                // Sit behind any existing space views.
                go.transform.SetSiblingIndex(0);
            }

            _backgroundImage.sprite = backgroundSprite;
            _backgroundImage.color = backgroundColor;
            _backgroundImage.preserveAspect = true;

            // Use the sprite's native pixel size for sizeDelta and apply per-axis scale
            // so artists can tune fit without the size drifting as radii change.
            var bgRect = _backgroundImage.rectTransform;
            bgRect.sizeDelta = new Vector2(backgroundSprite.rect.width, backgroundSprite.rect.height);
            bgRect.localScale = backgroundScale;
        }

        private void BuildSpaceViews()
        {
            foreach (Transform child in transform)
            {
                // Preserve the background if we already spawned it.
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
                view.SetHighlightColor(highlightColor);
                _spaceViews[spaceId] = view;
            }
        }

        private SpaceView CreateSpaceView(int spaceId, SpaceMeta meta)
        {
            SpaceView viewInstance;

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
                rect.sizeDelta = new Vector2(60f, 60f);

                go.AddComponent<Image>();

                viewInstance = go.AddComponent<SpaceView>();
            }

            var stack = _boardState.GetStack(spaceId);
            viewInstance.SetData(spaceId, meta, stack);

            return viewInstance;
        }

        private void PositionSpaceViews()
        {
            if (_boardState == null)
                return;

            var radii = new BoardLayoutHelper.Radii
            {
                Center = 0f,
                Inner = innerRingRadius,
                Ring2 = ring2Radius,
                Ring3 = ring3Radius,
                Outer = outerRadius,
                Ledge = ledgeRadius
            };

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
                view.SetHighlight(false);
            }
        }

        public void HighlightValidMoves(List<SpaceId> spaces)
        {
            // Clear all highlights first
            foreach (var view in _spaceViews.Values)
            {
                view.SetHighlightColor(highlightColor);
                view.SetHighlight(false);
            }

            if (spaces == null)
                return;

            foreach (var space in spaces)
            {
                if (space.BoardId != _boardState.BoardId)
                    continue;

                if (_spaceViews.TryGetValue(space.Id, out var view))
                {
                    view.SetHighlightColor(highlightColor);
                    view.SetHighlight(true);
                }
            }
        }

        public void HighlightSelection(SpaceId? selected)
        {
            if (!selected.HasValue) return;
            if (selected.Value.BoardId != _boardState.BoardId) return;

            if (_spaceViews.TryGetValue(selected.Value.Id, out var view))
            {
                view.SetHighlightColor(selectionColor);
                view.SetHighlight(true);
            }
        }
    }
}
