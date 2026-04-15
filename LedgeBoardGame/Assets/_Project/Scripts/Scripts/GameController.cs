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
        [SerializeField] private Tone defaultMovementTone = Tone.Light;

        private GameState _gameState;
        private GameRules _rules;
        private readonly Dictionary<int, BoardPresenter> _boardPresenters = new Dictionary<int, BoardPresenter>();
        private SpaceId? _selectedSpace;
        private Tone _selectedTone = Tone.Light;
        private readonly Stack<GameState> _undoStack = new Stack<GameState>();

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
                // Selecting a source
                var movablePieces = _rules.GetMovablePieces(_gameState, currentPlayer.Id);
                if (!movablePieces.Contains(clicked))
                    return;

                var stack = _gameState.GetBoard(clicked.BoardId)?.GetStack(clicked.Id);
                if (stack != null)
                {
                    // Re-pick tone each selection: prefer the default if movable, otherwise the other tone.
                    // Without this, a prior fallback to Dark would stick and later Light-only stacks would appear stuck.
                    if (stack.CanMove(defaultMovementTone))
                    {
                        _selectedTone = defaultMovementTone;
                    }
                    else
                    {
                        var other = defaultMovementTone == Tone.Light ? Tone.Dark : Tone.Light;
                        _selectedTone = stack.CanMove(other) ? other : defaultMovementTone;
                    }
                }

                _selectedSpace = clicked;
                var targets = _rules.GetValidMoveTargets(_gameState, clicked, _selectedTone);
                HighlightSpaces(targets);
                HighlightSelectedSource();
            }
            else
            {
                var from = _selectedSpace.Value;
                var targets = _rules.GetValidMoveTargets(_gameState, from, _selectedTone);
                if (targets.Contains(clicked))
                {
                    PushUndoSnapshot();
                    var move = _rules.MoveToken(_gameState, from, clicked, _selectedTone);
                    if (move != null)
                    {
                        _selectedSpace = null;
                        ClearHighlights();
                        RefreshBoards();
                        HighlightMovablePieces();
                        UpdateStatusUI();
                        RefreshUndoButton();
                    }
                    else
                    {
                        _undoStack.Pop();
                        RefreshUndoButton();
                    }
                }
                else if (clicked.Equals(from))
                {
                    // Tapping the same source deselects without further side effects.
                    _selectedSpace = null;
                    ClearHighlights();
                    HighlightMovablePieces();
                }
                else
                {
                    // Re-target: try treating the new click as a fresh source selection.
                    _selectedSpace = null;
                    ClearHighlights();
                    HighlightMovablePieces();
                    HandleMovementClick(clicked);
                }
            }
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
                kvp.Value.HighlightValidMoves(null);
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
            HighlightSpaces(movable);
        }

        private void OnEndTurnClicked()
        {
            if (_gameState == null || _gameState.GameOver)
                return;

            if (_gameState.CurrentPhase == GamePhase.Placement && !_gameState.IsPlacementComplete())
            {
                // Must place both tones before ending the turn.
                return;
            }

            _selectedSpace = null;
            ClearHighlights();

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

            var snapshot = _undoStack.Pop();
            _gameState.CopyFrom(snapshot);

            _selectedSpace = null;
            ClearHighlights();
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
                undoButton.interactable = _undoStack.Count > 0;
            }
        }

        private void UpdateStatusUI()
        {
            gameHud?.UpdateHud(_gameState);
        }
    }
}
