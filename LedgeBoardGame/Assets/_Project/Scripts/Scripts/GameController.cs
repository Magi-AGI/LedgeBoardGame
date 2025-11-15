using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Rules;
using Magi.LedgeBoardGame.Board;

namespace Magi.LedgeBoardGame
{
    public class GameController : MonoBehaviour
    {
        [SerializeField] private BoardPresenter boardPresenterPrefab;
        [SerializeField] private Button endTurnButton;
        [SerializeField] private Text statusText;
        [SerializeField] private Text phaseText;
        [SerializeField] private Text currentPlayerText;

        private GameState _gameState;
        private GameRules _rules;
        private readonly Dictionary<int, BoardPresenter> _boardPresenters = new Dictionary<int, BoardPresenter>();
        private SpaceId? _selectedSpace;
        private Tone _selectedTone = Tone.Light;

        private void Start()
        {
            _rules = new GameRules();

            var players = new List<Player>
            {
                new Player(1, "Player1", 0),
                new Player(2, "Player2", 1)
            };

            _gameState = new GameState(players);

            CreateBoardPresenters();

            SpaceClickedEvent.Register(OnSpaceClicked);

            if (endTurnButton != null)
            {
                endTurnButton.onClick.AddListener(OnEndTurnClicked);
            }

            UpdateStatusUI();
        }

        private void OnDestroy()
        {
            SpaceClickedEvent.Unregister(OnSpaceClicked);
        }

        private void CreateBoardPresenters()
        {
            foreach (var board in _gameState.Boards)
            {
                BoardPresenter presenterInstance;

                if (boardPresenterPrefab != null)
                {
                    var go = Instantiate(boardPresenterPrefab.gameObject, transform);
                    go.name = $"Board_{board.BoardId}_Presenter";
                    presenterInstance = go.GetComponent<BoardPresenter>();
                }
                else
                {
                    var go = new GameObject($"Board_{board.BoardId}_Presenter");
                    go.transform.SetParent(transform, false);
                    presenterInstance = go.AddComponent<BoardPresenter>();
                }

                presenterInstance.Initialize(board);
                _boardPresenters[board.BoardId] = presenterInstance;
            }

            RefreshBoards();
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

                _selectedSpace = clicked;
                var targets = _rules.GetValidMoveTargets(_gameState, clicked, _selectedTone);
                HighlightSpaces(targets);
            }
            else
            {
                var from = _selectedSpace.Value;
                var targets = _rules.GetValidMoveTargets(_gameState, from, _selectedTone);
                if (targets.Contains(clicked))
                {
                    var move = _rules.MoveToken(_gameState, from, clicked, _selectedTone);
                    if (move != null)
                    {
                        _selectedSpace = null;
                        ClearHighlights();
                        RefreshBoards();
                        HighlightMovablePieces();
                        UpdateStatusUI();
                    }
                }
                else
                {
                    // Deselect if clicking elsewhere
                    _selectedSpace = null;
                    ClearHighlights();
                }
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

            _selectedSpace = null;
            ClearHighlights();

            _gameState.EndTurn();

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

        private void UpdateStatusUI()
        {
            if (phaseText != null)
            {
                phaseText.text = $"Phase: {_gameState.CurrentPhase}";
            }

            if (currentPlayerText != null)
            {
                var player = _gameState.GetCurrentPlayer();
                currentPlayerText.text = player != null
                    ? $"Player: {player.Name}"
                    : "Player: -";
            }

            if (statusText != null)
            {
                if (_gameState.GameOver)
                {
                    statusText.text = _gameState.WinnerId.HasValue
                        ? $"Game Over - Winner: Player {_gameState.WinnerId.Value}"
                        : "Game Over - No Winner";
                }
                else if (_gameState.CurrentPhase == GamePhase.Placement)
                {
                    statusText.text = "Place one Light and one Dark token.";
                }
                else
                {
                    statusText.text = "Select a movable stack, then a valid destination.";
                }
            }

            if (endTurnButton != null)
            {
                endTurnButton.interactable = !_gameState.GameOver;
            }
        }
    }
}
