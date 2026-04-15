using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Models.Spec;
using Magi.LedgeBoardGame.Rules;
using Magi.LedgeBoardGame.Board;

namespace Magi.LedgeBoardGame
{
    public class GameController : MonoBehaviour
    {
        [SerializeField] private BoardPresenter boardPresenterPrefab;
        [SerializeField] private TextAsset ledgeSpecJson;
        [SerializeField] private Button endTurnButton;
        [SerializeField] private Button undoButton;
        [SerializeField] private GameHud gameHud;
        [SerializeField] private Board.MultiBoardLayout multiBoardLayout;
        [SerializeField] private Board.PlacementGhost placementGhost;
        [SerializeField] private Board.InHandGhost inHandGhost;
        [SerializeField] private Tone defaultMovementTone = Tone.Light;

        private GameState _gameState;
        private GameRules _rules;
        private readonly Dictionary<int, BoardPresenter> _boardPresenters = new Dictionary<int, BoardPresenter>();
        private SpaceId? _selectedSpace;
        private Tone _selectedTone = Tone.Light;
        private int _pickedUpLight;
        private int _pickedUpDark;
        private readonly Stack<GameState> _undoStack = new Stack<GameState>();
        private bool _moveInProgress;
        private const float MoveTweenDuration = 0.28f;

        private void Start()
        {
            var players = new List<Player>
            {
                new Player(1, "Player1", 0),
                new Player(2, "Player2", 1)
            };

            LedgeRuntimeConfig runtimeConfig = null;
            var useSpec = false;
            if (ledgeSpecJson != null && !string.IsNullOrEmpty(ledgeSpecJson.text))
            {
                var spec = LedgeGameSpecLoader.LoadFromJson(ledgeSpecJson.text);
                if (spec != null)
                {
                    // Validate that the loaded spec matches our code assumptions.
                    LedgeSpecValidator.Validate(spec);
                    runtimeConfig = LedgeRuntimeConfig.FromSpec(spec);
                    useSpec = true;
                }
                else if (Application.isEditor)
                {
                    UnityEngine.Debug.LogWarning("GameController: Failed to parse ledge spec JSON. Falling back to defaults.");
                }
            }
            else if (Application.isEditor)
            {
                UnityEngine.Debug.LogWarning("GameController: No ledgeSpecJson assigned. Falling back to defaults.");
            }

            _gameState = new GameState(players, runtimeConfig);
            _rules = new GameRules(useSpec ? runtimeConfig : null);

            if (multiBoardLayout == null)
            {
                multiBoardLayout = GetComponent<Board.MultiBoardLayout>();
                if (multiBoardLayout == null)
                {
                    multiBoardLayout = gameObject.AddComponent<Board.MultiBoardLayout>();
                }
            }

            CreateBoardPresenters();

            EnsureInHandGhost();

            SpaceClickedEvent.Register(OnSpaceClicked);

            if (endTurnButton != null)
            {
                endTurnButton.onClick.AddListener(OnEndTurnClicked);
            }

            if (undoButton != null)
            {
                undoButton.onClick.AddListener(OnUndoClicked);
            }

            UpdateStatusUI();
            RefreshUndoButton();
        }

        private void OnDestroy()
        {
            SpaceClickedEvent.Unregister(OnSpaceClicked);
        }

        private void CreateBoardPresenters()
        {
            var presenterParent = multiBoardLayout != null ? multiBoardLayout.transform : transform;

            foreach (var board in _gameState.Boards)
            {
                BoardPresenter presenterInstance;

                if (boardPresenterPrefab != null)
                {
                    var go = Instantiate(boardPresenterPrefab.gameObject, presenterParent);
                    go.name = $"Board_{board.BoardId}_Presenter";
                    presenterInstance = go.GetComponent<BoardPresenter>();
                }
                else
                {
                    var go = new GameObject($"Board_{board.BoardId}_Presenter");
                    go.transform.SetParent(presenterParent, false);
                    presenterInstance = go.AddComponent<BoardPresenter>();
                }

                presenterInstance.Initialize(board);
                _boardPresenters[board.BoardId] = presenterInstance;
            }

            RefreshBoards();

            // Update multi-board layout positions if present
            if (multiBoardLayout != null)
            {
                multiBoardLayout.Refresh();
            }

            gameHud?.UpdateHud(_gameState);
        }

        private void OnSpaceClicked(SpaceView view)
        {
            if (view == null || _gameState == null)
                return;

            // Gate clicks during a move-tween so the player can't queue a second move
            // before the current chip has landed.
            if (_moveInProgress)
                return;

            var boardId = FindBoardIdForView(view);
            if (boardId == null)
                return;

            var spaceId = new SpaceId(boardId.Value, view.SpaceId);

            if (_gameState.CurrentPhase == GamePhase.Placement)
            {
                HandlePlacementClick(spaceId);
            }
            else if (_gameState.CurrentPhase == GamePhase.Movement)
            {
                HandleMovementClick(spaceId);
            }
        }

        private int? FindBoardIdForView(SpaceView view)
        {
            foreach (var kvp in _boardPresenters)
            {
                if (kvp.Value.SpaceViews.TryGetValue(view.SpaceId, out var candidate) && candidate == view)
                {
                    return kvp.Key;
                }
            }

            return null;
        }

        private SpaceView FindSpaceView(SpaceId id)
        {
            if (_boardPresenters.TryGetValue(id.BoardId, out var presenter) &&
                presenter.SpaceViews.TryGetValue(id.Id, out var view))
            {
                return view;
            }
            return null;
        }

        private Transform ResolveOverlayParent(SpaceView fallbackView)
        {
            // Prefer the Canvas root so the overlay can cross boards on ledge hops without
            // having to parent-hop mid-tween.
            var canvas = fallbackView != null ? fallbackView.GetComponentInParent<Canvas>() : null;
            if (canvas == null) canvas = GetComponentInParent<Canvas>();
            if (canvas != null) return canvas.transform;
            // Last resort: own transform. Positioning still works in world space.
            return transform;
        }

        private void OnMoveTweenComplete()
        {
            _moveInProgress = false;
            // Destination was held at its pre-move state during the tween so the chips
            // read as arriving — now that they've landed, catch every board up to state.
            RefreshBoards();
            if (_gameState == null || _gameState.GameOver)
            {
                RefreshUndoButton();
                return;
            }
            HighlightMovablePieces();
            RefreshUndoButton();
        }

        private void HandlePlacementClick(SpaceId target)
        {
            var currentPlayer = _gameState.GetCurrentPlayer();
            if (currentPlayer == null)
                return;

            // Place first Light, then Dark
            Tone toneToPlace;
            if (!_gameState.CurrentTurnPlacements.Exists(p => p.Tone == Tone.Light))
            {
                toneToPlace = Tone.Light;
            }
            else if (!_gameState.CurrentTurnPlacements.Exists(p => p.Tone == Tone.Dark))
            {
                toneToPlace = Tone.Dark;
            }
            else
            {
                return;
            }

            if (_rules.CanPlaceToken(_gameState, target, toneToPlace))
            {
                PushUndoSnapshot();
                var move = _rules.PlaceToken(_gameState, target, toneToPlace);
                if (move != null)
                {
                    RefreshBoards();
                    UpdateStatusUI();
                    if (_gameState.CurrentPhase == GamePhase.Placement)
                    {
                        HighlightPlacementTargets();
                    }
                    else
                    {
                        ClearHighlights();
                        HighlightMovablePieces();
                    }
                    RefreshUndoButton();
                }
                else
                {
                    // Placement actually failed — drop the speculative snapshot.
                    _undoStack.Pop();
                    RefreshUndoButton();
                }
            }
        }

        private void HandleMovementClick(SpaceId clicked)
        {
            var currentPlayer = _gameState.GetCurrentPlayer();
            if (currentPlayer == null)
                return;

            if (_selectedSpace == null)
            {
                SelectMovementSource(clicked, currentPlayer.Id);
            }
            else
            {
                var from = _selectedSpace.Value;
                var stack = _gameState.GetBoard(from.BoardId)?.GetStack(from.Id);
                if (stack == null)
                {
                    ClearMovementSelection();
                    HighlightMovablePieces();
                    return;
                }

                var targets = GetStackValidTargets(from, stack);
                if (targets.Contains(clicked))
                {
                    ExecuteStackMove(from, clicked);
                }
                else if (clicked.Equals(from))
                {
                    // Tapping the same source deselects without further side effects.
                    ClearMovementSelection();
                    HighlightMovablePieces();
                }
                else
                {
                    // Re-target: treat the new click as a fresh source selection.
                    ClearMovementSelection();
                    HighlightMovablePieces();
                    HandleMovementClick(clicked);
                }
            }
        }

        private void SelectMovementSource(SpaceId clicked, int playerId)
        {
            var movablePieces = _rules.GetMovablePieces(_gameState, playerId);
            if (!movablePieces.Contains(clicked))
                return;

            var stack = _gameState.GetBoard(clicked.BoardId)?.GetStack(clicked.Id);
            if (stack == null)
                return;

            _pickedUpLight = stack.GetMovableCount(Tone.Light);
            _pickedUpDark = stack.GetMovableCount(Tone.Dark);
            if (_pickedUpLight + _pickedUpDark == 0)
                return;

            // _selectedTone kept for legacy call sites but not load-bearing — targets are
            // identical across movable tones since reachability is positional, not tone-bound.
            _selectedTone = _pickedUpLight > 0 ? Tone.Light : Tone.Dark;
            _selectedSpace = clicked;

            var targets = GetStackValidTargets(clicked, stack);
            HighlightSpaces(targets);
            HighlightSelectedSource();
            NotifyInHandGhost();
        }

        private void ClearMovementSelection()
        {
            _selectedSpace = null;
            _pickedUpLight = 0;
            _pickedUpDark = 0;
            ClearHighlights();
            NotifyInHandGhost();
        }

        private List<SpaceId> GetStackValidTargets(SpaceId from, TokenStack stack)
        {
            // Reachability is positional — a stack's valid targets are the union across
            // movable tones, but both tones yield the same adjacency/cross-board set
            // when they can move, so whichever is movable suffices.
            if (stack.CanMove(Tone.Light))
                return _rules.GetValidMoveTargets(_gameState, from, Tone.Light);
            if (stack.CanMove(Tone.Dark))
                return _rules.GetValidMoveTargets(_gameState, from, Tone.Dark);
            return new List<SpaceId>();
        }

        private void ExecuteStackMove(SpaceId from, SpaceId clicked)
        {
            var fromView = FindSpaceView(from);
            var toView = FindSpaceView(clicked);
            Vector3 fromPos = fromView != null ? fromView.transform.position : Vector3.zero;
            Vector3 toPos = toView != null ? toView.transform.position : Vector3.zero;

            int lightToMove = _pickedUpLight;
            int darkToMove = _pickedUpDark;

            PushUndoSnapshot();

            int lightMoved = 0;
            int darkMoved = 0;
            for (int i = 0; i < lightToMove; i++)
            {
                if (_rules.MoveToken(_gameState, from, clicked, Tone.Light) == null) break;
                lightMoved++;
            }
            for (int i = 0; i < darkToMove; i++)
            {
                if (_rules.MoveToken(_gameState, from, clicked, Tone.Dark) == null) break;
                darkMoved++;
            }

            if (lightMoved + darkMoved == 0)
            {
                // Nothing landed — drop the speculative snapshot.
                _undoStack.Pop();
                RefreshUndoButton();
                return;
            }

            ClearMovementSelection();

            // Source view drains immediately so the "in hand" chips read as lifted; the
            // destination stays at its pre-move state until the tween lands.
            if (fromView != null)
            {
                var postMoveFrom = _gameState.GetBoard(from.BoardId)?.GetStack(from.Id);
                if (postMoveFrom != null) fromView.UpdateTokenDisplay(postMoveFrom);
            }
            UpdateStatusUI();

            _moveInProgress = true;
            RefreshUndoButton();
            var overlayParent = ResolveOverlayParent(fromView ?? toView);
            MovingCounter.Play(overlayParent, fromPos, toPos, lightMoved, darkMoved, MoveTweenDuration, OnMoveTweenComplete);
        }

        private void NotifyInHandGhost()
        {
            if (inHandGhost == null) return;
            inHandGhost.SetStack(_pickedUpLight, _pickedUpDark);
        }

        private void EnsureInHandGhost()
        {
            if (inHandGhost != null) return;
            // Auto-spawn under the Canvas so existing scenes work without a setup patch.
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                foreach (var presenter in _boardPresenters.Values)
                {
                    canvas = presenter.GetComponentInParent<Canvas>();
                    if (canvas != null) break;
                }
            }
            if (canvas == null) return;

            var go = new GameObject("InHandGhost", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(canvas.transform, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(48f, 48f);
            inHandGhost = go.AddComponent<InHandGhost>();
        }

        private void HighlightSelectedSource()
        {
            if (!_selectedSpace.HasValue)
                return;

            foreach (var presenter in _boardPresenters.Values)
            {
                presenter.HighlightSelection(_selectedSpace);
            }
        }

        private void HighlightSpaces(List<SpaceId> spaces)
        {
            ClearHighlights();
            foreach (var kvp in _boardPresenters)
            {
                kvp.Value.HighlightValidMoves(spaces);
            }
        }

        private void ClearHighlights()
        {
            foreach (var kvp in _boardPresenters)
            {
                kvp.Value.ClearAllStates();
            }
        }

        private void RefreshBoards()
        {
            foreach (var presenter in _boardPresenters.Values)
            {
                presenter.UpdateView();
            }
        }

        private void HighlightPlacementTargets()
        {
            var currentPlayer = _gameState.GetCurrentPlayer();
            if (currentPlayer == null)
                return;

            var targets = _rules.GetValidPlacementTargets(_gameState, currentPlayer.Id);
            HighlightSpaces(targets);
        }

        private void HighlightMovablePieces()
        {
            if (_gameState.CurrentPhase != GamePhase.Movement)
                return;

            var currentPlayer = _gameState.GetCurrentPlayer();
            if (currentPlayer == null)
                return;

            var movable = _rules.GetMovablePieces(_gameState, currentPlayer.Id);
            // Source breathe, not destination pulse — readers can tell at a glance which
            // stacks they can pick up vs. where a selected stack can go.
            ClearHighlights();
            foreach (var presenter in _boardPresenters.Values)
            {
                presenter.HighlightMovableSources(movable);
            }
        }

        private void OnEndTurnClicked()
        {
            if (_gameState == null || _gameState.GameOver)
                return;

            if (_moveInProgress)
                return;

            if (_gameState.CurrentPhase == GamePhase.Placement && !_gameState.IsPlacementComplete())
            {
                // Must place both tones before ending the turn.
                return;
            }

            ClearMovementSelection();

            _gameState.EndTurn();

            // Turn boundaries invalidate undo history — the prior player cannot rewind
            // into the next player's turn.
            _undoStack.Clear();
            RefreshUndoButton();

            RefreshBoards();
            UpdateStatusUI();

            if (!_gameState.GameOver)
            {
                if (_gameState.CurrentPhase == GamePhase.Placement)
                {
                    HighlightPlacementTargets();
                }
                else
                {
                    HighlightMovablePieces();
                }
            }
        }

        private void PushUndoSnapshot()
        {
            if (_gameState == null)
                return;
            _undoStack.Push(_gameState.Clone());
        }

        private void OnUndoClicked()
        {
            if (_undoStack.Count == 0 || _gameState == null)
                return;

            if (_moveInProgress)
                return;

            var snapshot = _undoStack.Pop();
            _gameState.CopyFrom(snapshot);

            ClearMovementSelection();
            RefreshBoards();
            UpdateStatusUI();

            if (!_gameState.GameOver)
            {
                if (_gameState.CurrentPhase == GamePhase.Placement)
                {
                    HighlightPlacementTargets();
                }
                else
                {
                    HighlightMovablePieces();
                }
            }

            RefreshUndoButton();
        }

        private void RefreshUndoButton()
        {
            if (undoButton != null)
            {
                undoButton.interactable = _undoStack.Count > 0 && !_moveInProgress;
            }
        }

        private void UpdateStatusUI()
        {
            gameHud?.UpdateHud(_gameState);
            placementGhost?.Refresh(_gameState);
        }
    }
}
