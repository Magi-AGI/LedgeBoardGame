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
        public void InnerRing_HasSixBridgesConnectedToCenter_AndWallsNotConnected()
        {
            var builder = BoardGraphBuilder.CreateHexagonalBoard();
            var board = builder.BuildBoard(0, 0);

            var bridgeIds = board.SpaceMetadata
                .Where(kvp => kvp.Value.Type == SpaceType.InnerBridge)
                .Select(kvp => kvp.Key)
                .ToList();

            var wallIds = board.SpaceMetadata
                .Where(kvp => kvp.Value.Type == SpaceType.InnerWall)
                .Select(kvp => kvp.Key)
                .ToList();

            Assert.AreEqual(6, bridgeIds.Count, "There should be six inner bridge spaces.");
            Assert.AreEqual(6, wallIds.Count, "There should be six inner wall spaces.");

            foreach (var id in bridgeIds)
            {
                Assert.Contains(0, board.GetAdjacentSpaces(id), "Bridge spaces should be adjacent to the center.");
            }

            foreach (var id in wallIds)
            {
                CollectionAssert.DoesNotContain(board.GetAdjacentSpaces(id), 0, "Wall spaces should not be adjacent to the center.");
            }
        }

        [Test]
        public void InnerRing_FormsAlternatingBridgeWallTwelveCycle()
        {
            var builder = BoardGraphBuilder.CreateHexagonalBoard();
            var board = builder.BuildBoard(0, 0);

            // Every wall must connect to exactly two bridges (no center, no ring2-outer).
            for (int wallId = 7; wallId <= 12; wallId++)
            {
                var bridges = board.GetAdjacentSpaces(wallId)
                    .Where(n => board.SpaceMetadata[n].Type == SpaceType.InnerBridge)
                    .ToList();
                Assert.AreEqual(2, bridges.Count, $"Wall {wallId} should be flanked by exactly two bridges.");
            }

            // Every bridge must connect to exactly two walls on the inner ring (the 12-cycle neighbours).
            for (int bridgeId = 1; bridgeId <= 6; bridgeId++)
            {
                var walls = board.GetAdjacentSpaces(bridgeId)
                    .Where(n => board.SpaceMetadata[n].Type == SpaceType.InnerWall)
                    .ToList();
                Assert.AreEqual(2, walls.Count, $"Bridge {bridgeId} should be flanked by exactly two walls.");
            }
        }

        [Test]
        public void LedgeSpaces_AreExactlyFourStepsFromCenter()
        {
            var builder = BoardGraphBuilder.CreateHexagonalBoard();
            var board = builder.BuildBoard(0, 0);

            var distances = ComputeDistancesFromCenter(board);

            var ledgeIds = board.SpaceMetadata
                .Where(kvp => !string.IsNullOrEmpty(kvp.Value.ColorLabel))
                .Select(kvp => kvp.Key)
                .OrderBy(id => id)
                .ToList();

            CollectionAssert.AreEqual(Enumerable.Range(37, 12).ToList(), ledgeIds, "Color-labeled ledge spaces should be IDs 37-48.");

            foreach (var id in ledgeIds)
            {
                Assert.IsTrue(distances.ContainsKey(id), $"No path found from center to space {id}.");
                Assert.AreEqual(4, distances[id], $"Expected distance 4 from center to space {id}.");
            }
        }

        [Test]
        public void LedgeSpaces_CoverAllTwelveColors()
        {
            var builder = BoardGraphBuilder.CreateHexagonalBoard();
            var board = builder.BuildBoard(0, 0);

            var colorByType = new Dictionary<SpaceType, List<string>>
            {
                { SpaceType.Ring3, new List<string>() },
                { SpaceType.OuterAdded, new List<string>() }
            };

            foreach (var kvp in board.SpaceMetadata)
            {
                if (string.IsNullOrEmpty(kvp.Value.ColorLabel)) continue;
                colorByType[kvp.Value.Type].Add(kvp.Value.ColorLabel);
            }

            Assert.AreEqual(6, colorByType[SpaceType.Ring3].Count, "Six ring3-vertex spaces should carry color labels.");
            Assert.AreEqual(6, colorByType[SpaceType.OuterAdded].Count, "All six outer-added spaces should carry color labels.");

            var allColors = colorByType[SpaceType.Ring3].Concat(colorByType[SpaceType.OuterAdded]).ToList();
            CollectionAssert.AreEquivalent(LedgeConfigConstants.LedgeColors, allColors, "Ledge color labels should cover all configured colors exactly once.");
        }

        [Test]
        public void Ring3OffSpaces_ConnectToTwoRing2Neighbors_Ring3VerticesConnectToOne()
        {
            var builder = BoardGraphBuilder.CreateHexagonalBoard();
            var board = builder.BuildBoard(0, 0);

            for (int id = 25; id <= 36; id++)
            {
                Assert.IsTrue(board.SpaceMetadata.ContainsKey(id), $"Missing ring3-off space {id}");
                Assert.IsTrue(string.IsNullOrEmpty(board.SpaceMetadata[id].ColorLabel), $"Ring3-off {id} should not carry a ledge color.");
                var ring2Neighbors = board.GetAdjacentSpaces(id).Where(n => n >= 13 && n <= 24).ToList();
                Assert.AreEqual(2, ring2Neighbors.Count, $"Ring3-off {id} should connect to exactly two Ring2 spaces.");
            }

            for (int id = 37; id <= 42; id++)
            {
                Assert.IsTrue(board.SpaceMetadata.ContainsKey(id), $"Missing ring3-vertex space {id}");
                Assert.IsFalse(string.IsNullOrEmpty(board.SpaceMetadata[id].ColorLabel), $"Ring3-vertex {id} should carry a ledge color.");
                var ring2Neighbors = board.GetAdjacentSpaces(id).Where(n => n >= 13 && n <= 24).ToList();
                Assert.AreEqual(1, ring2Neighbors.Count, $"Ring3-vertex {id} should connect to exactly one Ring2 space.");
            }
        }

        [Test]
        public void OuterAddedSpaces_ConnectToTwoRing3OffNeighborsOnly()
        {
            var builder = BoardGraphBuilder.CreateHexagonalBoard();
            var board = builder.BuildBoard(0, 0);

            for (int id = 43; id <= 48; id++)
            {
                var neighbors = board.GetAdjacentSpaces(id);
                Assert.AreEqual(2, neighbors.Count, $"OuterAdded {id} should have exactly two neighbours.");
                foreach (var n in neighbors)
                {
                    Assert.IsTrue(n >= 25 && n <= 36, $"OuterAdded {id} should only connect to ring3-off spaces (got {n}).");
                }
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
