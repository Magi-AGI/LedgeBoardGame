using System;
using System.Collections.Generic;
using System.Linq;
using Magi.LedgeBoardGame.Builder;

namespace Magi.LedgeBoardGame.Models.Spec
{
    public static class LedgeSpecValidator
    {
        public static void Validate(LedgeGameSpec spec)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            ValidateBoardConfig(spec);
            ValidateColors(spec);
            ValidatePhasesAndMoves(spec);
        }

        private static void ValidateBoardConfig(LedgeGameSpec spec)
        {
            var boardConfig = spec.Config.Board;
            if (boardConfig.Spaces != 49)
            {
                throw new InvalidOperationException($"Spec board spaces should be 49 but was {boardConfig.Spaces}.");
            }

            var builder = BoardGraphBuilder.CreateHexagonalBoard();
            var board = builder.BuildBoard(0, 0);

            if (board.SpaceMetadata.Count != 49)
            {
                throw new InvalidOperationException($"BoardGraphBuilder produced {board.SpaceMetadata.Count} spaces instead of 49.");
            }

            var specIds = new HashSet<int>();
            foreach (var ring in boardConfig.Rings)
            {
                foreach (var id in ring.SpaceIds)
                {
                    specIds.Add(id);
                    if (!board.SpaceMetadata.ContainsKey(id))
                    {
                        throw new InvalidOperationException($"Spec references space {id} in ring '{ring.Name}' that does not exist in BoardGraphBuilder.");
                    }

                    var meta = board.SpaceMetadata[id];
                    var expectedRingIndex = ring.Name switch
                    {
                        "center" => 0,
                        "inner" => 1,
                        "innerMiddle" => 2,
                        "outerMiddle" => 3,
                        "outer" => 4,
                        _ => throw new InvalidOperationException($"Unexpected ring name '{ring.Name}' in spec.")
                    };

                    if (meta.RingIndex != expectedRingIndex)
                    {
                        throw new InvalidOperationException(
                            $"Space {id} in ring '{ring.Name}' has ringIndex {meta.RingIndex} but expected {expectedRingIndex}.");
                    }
                }
            }

            var expectedIds = new HashSet<int>(Enumerable.Range(0, 49));
            if (!expectedIds.SetEquals(specIds))
            {
                throw new InvalidOperationException("Spec rings do not collectively cover exactly spaces 0-48.");
            }
        }

        private static void ValidateColors(LedgeGameSpec spec)
        {
            var colors = spec.Config.Board.LedgeColors;
            if (colors == null || colors.Count != LedgeConfigConstants.LedgeColors.Count)
            {
                throw new InvalidOperationException("Spec ledgeColors length does not match code constants.");
            }

            if (!colors.SequenceEqual(LedgeConfigConstants.LedgeColors))
            {
                throw new InvalidOperationException("Spec ledgeColors sequence does not match code constants.");
            }
        }

        private static void ValidatePhasesAndMoves(LedgeGameSpec spec)
        {
            var phaseNames = spec.Phases.Select(p => p.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            var expectedPhases = new[] { "placement", "movement" };
            if (!phaseNames.SequenceEqual(expectedPhases.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Spec phases must be exactly 'placement' and 'movement'.");
            }

            var moveNames = spec.Moves.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            var expectedMoves = new[] { "placeToken", "moveToken", "endTurn" };
            if (!moveNames.SequenceEqual(expectedMoves.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Spec moves must be exactly placeToken, moveToken, and endTurn.");
            }
        }
    }
}

