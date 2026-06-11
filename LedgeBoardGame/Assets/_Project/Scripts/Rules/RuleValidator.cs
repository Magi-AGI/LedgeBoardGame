using System.Collections.Generic;
using System.Linq;
using Magi.LedgeBoardGame.Models;

namespace Magi.LedgeBoardGame.Rules
{
    public static class RuleValidator
    {
        public static bool ValidateTokenStackInvariant(TokenStack stack)
        {
            if (stack.IsEmpty)
            {
                return !stack.BottomTone.HasValue;
            }

            if (stack.TotalCount == 1)
            {
                return stack.BottomTone.HasValue;
            }

            if (stack.LightCount > 0 && stack.DarkCount > 0)
            {
                return false;
            }

            if (stack.TotalCount > 1)
            {
                return stack.BottomTone.HasValue;
            }

            return true;
        }

        public static bool ValidateBoardState(BoardState board)
        {
            foreach (var stack in board.Spaces.Values)
            {
                if (!ValidateTokenStackInvariant(stack))
                    return false;
            }

            var centerStack = board.GetStack(0);
            if (centerStack.IsEmpty && board.BoardId >= 0)
                return false;

            return true;
        }

        public static bool ValidateGameState(GameState gameState)
        {
            foreach (var board in gameState.Boards)
            {
                if (!ValidateBoardState(board))
                    return false;
            }

            var activePlayers = gameState.Players.Count(p => !p.IsEliminated);
            if (gameState.GameOver && activePlayers > 1)
                return false;

            if (activePlayers == 1 && !gameState.GameOver)
                return false;

            return true;
        }

        public static bool ValidateMoveSequence(List<Move> moves, GameState initialState)
        {
            var state = initialState.Clone();

            foreach (var move in moves)
            {
                var fromBoard = state.GetBoard(move.From.BoardId);
                var fromStack = fromBoard?.GetStack(move.From.Id);

                if (fromStack == null || !fromStack.CanMove(move.Tone))
                    return false;

                fromStack.RemoveOne(move.Tone);

                var toBoard = state.GetBoard(move.To.BoardId);
                var toStack = toBoard?.GetStack(move.To.Id);

                if (toStack == null)
                    return false;

                toStack.ResolveEntry(move.Tone, 1);
            }

            return ValidateGameState(state);
        }

        public static string GetInvariantViolations(GameState gameState)
        {
            var violations = new List<string>();

            foreach (var board in gameState.Boards)
            {
                foreach (var kvp in board.Spaces)
                {
                    var stack = kvp.Value;
                    if (!ValidateTokenStackInvariant(stack))
                    {
                        violations.Add($"Board {board.BoardId}, Space {kvp.Key}: Invalid stack state - {stack}");
                    }

                    if (stack.IsEmpty && stack.BottomTone.HasValue)
                    {
                        violations.Add($"Board {board.BoardId}, Space {kvp.Key}: Empty stack has bottom tone");
                    }

                    if (stack.TotalCount == 1 && !stack.BottomTone.HasValue)
                    {
                        violations.Add($"Board {board.BoardId}, Space {kvp.Key}: Single token without bottom tone");
                    }
                }
            }

            return violations.Count > 0 ? string.Join("\n", violations) : null;
        }
    }
}