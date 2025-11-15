using System.Collections.Generic;
using System.Linq;
using Magi.LedgeBoardGame.Models;

namespace Magi.LedgeBoardGame.Rules
{
    public class GameRules : IGameRules
    {
        public bool CanPlaceToken(GameState gameState, SpaceId target, Tone tone)
        {
            if (gameState.GameOver) return false;
            if (gameState.CurrentPhase != GamePhase.Placement) return false;

            var currentPlayer = gameState.GetCurrentPlayer();
            if (currentPlayer == null || currentPlayer.IsEliminated) return false;

            var playerBoard = gameState.GetBoardForPlayer(currentPlayer.Id);
            if (playerBoard == null) return false;

            if (target.BoardId != playerBoard.BoardId) return false;

            if (tone == Tone.Light && gameState.CurrentTurnPlacements.Any(p => p.Tone == Tone.Light))
                return false;
            if (tone == Tone.Dark && gameState.CurrentTurnPlacements.Any(p => p.Tone == Tone.Dark))
                return false;

            return true;
        }

        public PlacementMove PlaceToken(GameState gameState, SpaceId target, Tone tone)
        {
            if (!CanPlaceToken(gameState, target, tone))
                return null;

            var board = gameState.GetBoard(target.BoardId);
            var stack = board.GetStack(target.Id);
            var result = stack.ResolveEntry(tone, 1);

            var move = new PlacementMove(target, tone, 1)
            {
                Result = result
            };

            gameState.CurrentTurnPlacements.Add(move);
            gameState.RecordPlacement(tone);

            if (gameState.IsPlacementComplete())
            {
                gameState.StartMovementPhase();
            }

            return move;
        }

        public bool CanMoveToken(GameState gameState, SpaceId from, SpaceId to, Tone tone)
        {
            if (gameState.GameOver) return false;
            if (gameState.CurrentPhase != GamePhase.Movement) return false;

            var currentPlayer = gameState.GetCurrentPlayer();
            if (currentPlayer == null || currentPlayer.IsEliminated) return false;

            var fromBoard = gameState.GetBoard(from.BoardId);
            var toBoard = gameState.GetBoard(to.BoardId);
            if (fromBoard == null || toBoard == null) return false;

            var fromStack = fromBoard.GetStack(from.Id);
            if (!fromStack.CanMove(tone)) return false;

            if (!IsValidMove(gameState, from, to))
                return false;

            bool playerControlsSource = IsSpaceControlled(gameState, from, currentPlayer.Id);
            if (!playerControlsSource) return false;

            return true;
        }

        public Move MoveToken(GameState gameState, SpaceId from, SpaceId to, Tone tone)
        {
            if (!CanMoveToken(gameState, from, to, tone))
                return null;

            var fromBoard = gameState.GetBoard(from.BoardId);
            var toBoard = gameState.GetBoard(to.BoardId);
            var fromStack = fromBoard.GetStack(from.Id);
            var toStack = toBoard.GetStack(to.Id);

            fromStack.RemoveOne(tone);

            var result = toStack.ResolveEntry(tone, 1);

            var move = new Move(from, to, tone, 1)
            {
                Result = result
            };

            gameState.CurrentTurnMoves.Add(move);

            return move;
        }

        public List<SpaceId> GetValidMoveTargets(GameState gameState, SpaceId from, Tone tone)
        {
            var targets = new List<SpaceId>();

            if (gameState.CurrentPhase != GamePhase.Movement)
                return targets;

            var fromBoard = gameState.GetBoard(from.BoardId);
            if (fromBoard == null) return targets;

            var fromStack = fromBoard.GetStack(from.Id);
            if (!fromStack.CanMove(tone)) return targets;

            var adjacentSpaces = fromBoard.GetAdjacentSpaces(from.Id);
            foreach (var spaceId in adjacentSpaces)
            {
                targets.Add(new SpaceId(from.BoardId, spaceId));
            }

            if (fromBoard.IsLedgeSpace(from.Id) && gameState.CanCrossBoard(from))
            {
                var crossBoardTargets = gameState.GetCrossBoardTargets(from);
                targets.AddRange(crossBoardTargets);
            }

            return targets;
        }

        public List<SpaceId> GetMovablePieces(GameState gameState, int playerId)
        {
            var movable = new List<SpaceId>();

            if (gameState.CurrentPhase != GamePhase.Movement)
                return movable;

            var player = gameState.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null || player.IsEliminated)
                return movable;

            var playerBoard = gameState.GetBoardForPlayer(playerId);
            if (playerBoard != null)
            {
                var lightMovable = playerBoard.GetMovableSpaces(Tone.Light);
                var darkMovable = playerBoard.GetMovableSpaces(Tone.Dark);

                foreach (var spaceId in lightMovable.Union(darkMovable))
                {
                    movable.Add(new SpaceId(playerBoard.BoardId, spaceId));
                }
            }

            foreach (var move in gameState.CurrentTurnMoves)
            {
                var board = gameState.GetBoard(move.To.BoardId);
                if (board != null && board.BoardId != playerBoard?.BoardId)
                {
                    var stack = board.GetStack(move.To.Id);
                    if (stack.CanMove(Tone.Light) || stack.CanMove(Tone.Dark))
                    {
                        movable.Add(move.To);
                    }
                }
            }

            return movable.Distinct().ToList();
        }

        public List<SpaceId> GetValidPlacementTargets(GameState gameState, int playerId)
        {
            var targets = new List<SpaceId>();

            if (gameState.CurrentPhase != GamePhase.Placement)
                return targets;

            var player = gameState.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null || player.IsEliminated)
                return targets;

            var board = gameState.GetBoardForPlayer(playerId);
            if (board == null) return targets;

            var validSpaces = board.GetValidPlacementTargets();
            foreach (var spaceId in validSpaces)
            {
                targets.Add(new SpaceId(board.BoardId, spaceId));
            }

            return targets;
        }

        public bool IsGameOver(GameState gameState)
        {
            return gameState.GameOver;
        }

        public int? GetWinner(GameState gameState)
        {
            return gameState.WinnerId;
        }

        private bool IsValidMove(GameState gameState, SpaceId from, SpaceId to)
        {
            if (from.BoardId == to.BoardId)
            {
                var board = gameState.GetBoard(from.BoardId);
                var adjacentSpaces = board.GetAdjacentSpaces(from.Id);
                return adjacentSpaces.Contains(to.Id);
            }
            else
            {
                if (!gameState.CanCrossBoard(from))
                    return false;

                var validTargets = gameState.GetCrossBoardTargets(from);
                return validTargets.Contains(to);
            }
        }

        private bool IsSpaceControlled(GameState gameState, SpaceId space, int playerId)
        {
            var board = gameState.GetBoard(space.BoardId);
            if (board == null) return false;

            if (board.PlayerId == playerId)
                return true;

            foreach (var move in gameState.CurrentTurnMoves)
            {
                if (move.To.Equals(space))
                    return true;
            }

            return false;
        }
    }
}