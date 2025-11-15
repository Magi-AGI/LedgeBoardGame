using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Magi.LedgeBoardGame.Builder;
using Magi.LedgeBoardGame.Models;

namespace Magi.LedgeBoardGame.Tests.EditMode
{
    [TestFixture]
    public class BoardGraphBuilderTests
    {
        [Test]
        public void CreateHexagonalBoard_ProducesExpectedSpaceCountAndIds()
        {
            var builder = BoardGraphBuilder.CreateHexagonalBoard();
            var board = builder.BuildBoard(0, 0);

            Assert.AreEqual(49, board.SpaceMetadata.Count, "Expected 49 distinct spaces in metadata.");

            var ids = board.SpaceMetadata.Keys.OrderBy(id => id).ToList();
            CollectionAssert.AreEqual(Enumerable.Range(0, 49).ToList(), ids, "Space IDs should be contiguous from 0 to 48.");
        }

        [Test]
        public void InnerRing_HasSixBridgesConnectedToCenter_AndStopsNotConnected()
        {
            var builder = BoardGraphBuilder.CreateHexagonalBoard();
            var board = builder.BuildBoard(0, 0);

            var bridgeIds = board.SpaceMetadata
                .Where(kvp => kvp.Value.Type == SpaceType.InnerBridge)
                .Select(kvp => kvp.Key)
                .ToList();

            var stopIds = board.SpaceMetadata
                .Where(kvp => kvp.Value.Type == SpaceType.InnerStop)
                .Select(kvp => kvp.Key)
                .ToList();

            Assert.AreEqual(6, bridgeIds.Count, "There should be six inner bridge spaces.");
            Assert.AreEqual(6, stopIds.Count, "There should be six inner stop spaces.");

            foreach (var id in bridgeIds)
            {
                Assert.Contains(0, board.GetAdjacentSpaces(id), "Bridge spaces should be adjacent to the center.");
            }

            foreach (var id in stopIds)
            {
                CollectionAssert.DoesNotContain(board.GetAdjacentSpaces(id), 0, "Stop spaces should not be adjacent to the center.");
            }
        }

        [Test]
        public void OuterRingSpaces_AreFourStepsFromCenter()
        {
            var builder = BoardGraphBuilder.CreateHexagonalBoard();
            var board = builder.BuildBoard(0, 0);

            var distances = ComputeDistancesFromCenter(board);

            var outerIds = board.SpaceMetadata
                .Where(kvp => kvp.Value.Type == SpaceType.Ledge || kvp.Value.Type == SpaceType.OuterAdded)
                .Select(kvp => kvp.Key)
                .ToList();

            Assert.AreEqual(18, outerIds.Count, "Outer ring should contain 18 spaces (12 ledges + 6 outer).");

            foreach (var id in outerIds)
            {
                Assert.IsTrue(distances.ContainsKey(id), $"No path found from center to space {id}.");
                Assert.AreEqual(4, distances[id], $"Expected distance 4 from center to space {id}.");
            }
        }

        private static Dictionary<int, int> ComputeDistancesFromCenter(BoardState board)
        {
            var distances = new Dictionary<int, int>();
            var queue = new Queue<int>();

            const int centerId = 0;
            distances[centerId] = 0;
            queue.Enqueue(centerId);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var currentDistance = distances[current];

                foreach (var neighbor in board.GetAdjacentSpaces(current))
                {
                    if (!distances.ContainsKey(neighbor))
                    {
                        distances[neighbor] = currentDistance + 1;
                        queue.Enqueue(neighbor);
                    }
                }
            }

            return distances;
        }
    }
}
