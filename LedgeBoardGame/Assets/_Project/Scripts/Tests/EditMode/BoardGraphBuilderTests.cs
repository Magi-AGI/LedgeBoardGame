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
                .Where(kvp => kvp.Value.Type == SpaceType.Ledge)
                .Select(kvp => kvp.Key)
                .ToList();

            Assert.AreEqual(12, outerIds.Count, "Outer ring should contain 12 ledge spaces.");

            foreach (var id in outerIds)
            {
                Assert.IsTrue(distances.ContainsKey(id), $"No path found from center to space {id}.");
                Assert.AreEqual(4, distances[id], $"Expected distance 4 from center to space {id}.");
            }
        }

        [Test]
        public void LedgeSpaces_AreUniqueAndMatchColorList()
        {
            var builder = BoardGraphBuilder.CreateHexagonalBoard();
            var board = builder.BuildBoard(0, 0);

            var ledgeIds = board.SpaceMetadata
                .Where(kvp => kvp.Value.Type == SpaceType.Ledge)
                .Select(kvp => kvp.Key)
                .OrderBy(id => id)
                .ToList();

            CollectionAssert.AreEqual(Enumerable.Range(37, 12).ToList(), ledgeIds, "Expected exactly 12 ledge space IDs 37-48.");

            var colors = new HashSet<string>();
            foreach (var id in ledgeIds)
            {
                var meta = board.SpaceMetadata[id];
                Assert.IsFalse(string.IsNullOrEmpty(meta.ColorLabel), $"Ledge {id} missing color label.");
                colors.Add(meta.ColorLabel);
            }

            CollectionAssert.AreEquivalent(LedgeConfigConstants.LedgeColors, colors, "Ledge color labels should cover all configured colors.");

            // Verify adjacency retained for ledges that overrode Ring3 IDs.
            foreach (var id in ledgeIds)
            {
                Assert.IsTrue(board.Adjacency.ContainsKey(id), $"Ledge {id} should have adjacency entries.");
            }
        }

        [Test]
        public void Ring3Spaces_ConnectToTwoRing2Neighbors()
        {
            var builder = BoardGraphBuilder.CreateHexagonalBoard();
            var board = builder.BuildBoard(0, 0);

            for (int id = 25; id <= 42; id++)
            {
                Assert.IsTrue(board.SpaceMetadata.ContainsKey(id), $"Missing ring3 space {id}");
                var neighbors = board.GetAdjacentSpaces(id);
                var ring2Neighbors = neighbors.Where(n => n >= 13 && n <= 24).ToList();

                Assert.AreEqual(2, ring2Neighbors.Count, $"Ring3 space {id} should connect to exactly two Ring2 spaces.");
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
