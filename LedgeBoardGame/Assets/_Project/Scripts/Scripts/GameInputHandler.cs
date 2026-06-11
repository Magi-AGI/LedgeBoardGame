using UnityEngine;
using Magi.LedgeBoardGame.Board;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Rules;

namespace Magi.LedgeBoardGame
{
    /// <summary>
    /// Separates click handling from GameController. Requires explicit wiring from GameController.
    /// </summary>
    public class GameInputHandler : MonoBehaviour
    {
        public GameState State { get; set; }
        public GameRules Rules { get; set; }

        public System.Action RefreshBoards { get; set; }
        public System.Action ClearHighlights { get; set; }
        public System.Action HighlightPlacementTargets { get; set; }
        public System.Action HighlightMovablePieces { get; set; }
        public System.Action UpdateHud { get; set; }

        public Tone SelectedTone { get; set; } = Tone.Light;
        public SpaceId? SelectedSpace { get; private set; }

        public void OnSpaceClicked(SpaceView view)
        {
            if (view == null || State == null || Rules == null)
                return;

            var board = FindBoardIdForView(view);
            if (board == null)
                return;

            var spaceId = new SpaceId(board.Value, view.SpaceId);
            if (State.CurrentPhase == GamePhase.Placement)
            {
                HandlePlacement(spaceId);
            }
            else if (State.CurrentPhase == GamePhase.Movement)
            {
                HandleMovement(spaceId);
            }
        }

        private void HandlePlacement(SpaceId target)
        {
            var currentPlayer = State.GetCurrentPlayer();
            if (currentPlayer == null)
                return;

            Tone toneToPlace;
            if (!State.HasPlacedLight)
            {
                toneToPlace = Tone.Light;
            }
            else if (!State.HasPlacedDark)
            {
                toneToPlace = Tone.Dark;
            }
            else
            {
                return;
            }

            if (Rules.CanPlaceToken(State, target, toneToPlace))
            {
                var move = Rules.PlaceToken(State, target, toneToPlace);
                if (move != null)
                {
                    RefreshBoards?.Invoke();
                    UpdateHud?.Invoke();
                    if (State.CurrentPhase == GamePhase.Placement)
                    {
                        HighlightPlacementTargets?.Invoke();
                    }
                    else
                    {
                        ClearHighlights?.Invoke();
                        HighlightMovablePieces?.Invoke();
                    }
                }
            }
        }

        private void HandleMovement(SpaceId clicked)
        {
            var currentPlayer = State.GetCurrentPlayer();
            if (currentPlayer == null)
                return;

            if (SelectedSpace == null)
            {
                var movablePieces = Rules.GetMovablePieces(State, currentPlayer.Id);
                if (!movablePieces.Contains(clicked))
                    return;

                var stack = State.GetBoard(clicked.BoardId)?.GetStack(clicked.Id);
                if (stack != null && !stack.CanMove(SelectedTone))
                {
                    SelectedTone = stack.CanMove(Tone.Dark) ? Tone.Dark : Tone.Light;
                }

                SelectedSpace = clicked;
                var targets = Rules.GetValidMoveTargets(State, clicked, SelectedTone);
                HighlightTargets(targets);
            }
            else
            {
                var from = SelectedSpace.Value;
                var targets = Rules.GetValidMoveTargets(State, from, SelectedTone);
                if (targets.Contains(clicked))
                {
                    var move = Rules.MoveToken(State, from, clicked, SelectedTone);
                    if (move != null)
                    {
                        SelectedSpace = null;
                        ClearHighlights?.Invoke();
                        RefreshBoards?.Invoke();
                        HighlightMovablePieces?.Invoke();
                        UpdateHud?.Invoke();
                    }
                }
                else
                {
                    SelectedSpace = null;
                    ClearHighlights?.Invoke();
                }
            }
        }

        private void HighlightTargets(System.Collections.Generic.List<SpaceId> spaces)
        {
            ClearHighlights?.Invoke();
            // No direct access to presenters here; expect caller to hook this delegate to BoardPresenter highlighting if desired.
        }

        private int? FindBoardIdForView(SpaceView view)
        {
            var presenter = view.GetComponentInParent<BoardPresenter>();
            if (presenter != null && presenter.BoardState != null)
            {
                return presenter.BoardState.BoardId;
            }
            return null;
        }
    }
}
