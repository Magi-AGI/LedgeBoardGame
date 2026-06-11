using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Magi.LedgeBoardGame.Models;

namespace Magi.LedgeBoardGame.Tests.EditMode
{
    [TestFixture]
    public class GameStateInitializationTests
    {
        [Test]
        public void InitializeBoards_UsesBoardGraphBuilderLayout()
        {
            var players = new List<Player>
            {
                new Player(1, "Player1", 0),
                new Player(2, "Player2", 1)
            };

            var gameState = new GameState(players, null);

            Assert.AreEqual(2, gameState.Boards.Count);

            foreach (var board in gameState.Boards)
            {
                // Verify basic layout facts from the builder
                Assert.AreEqual(49, board.SpaceMetadata.Count);
                Assert.IsTrue(board.SpaceMetadata.ContainsKey(0), "Center space should exist.");

                var innerBridges = board.SpaceMetadata
                    .Where(kvp => kvp.Value.Type == SpaceType.InnerBridge)
                    .Select(kvp => kvp.Key)
                    .ToList();

                Assert.AreEqual(6, innerBridges.Count, "There should be six inner bridge spaces.");

                foreach (var id in innerBridges)
                {
                    CollectionAssert.Contains(board.GetAdjacentSpaces(id), 0, "Inner bridge spaces should connect to center.");
                }
            }
        }

        [Test]
        public void InitializeBoards_PopulatesCrossBoardLedgeEdges()
        {
            var players = new List<Player>
            {
                new Player(1, "Player1", 0),
                new Player(2, "Player2", 1)
            };

            var gameState = new GameState(players, null);

            // For at least one ledge color, there should be cross-board edges between boards.
            Assert.IsNotEmpty(gameState.CrossBoardLedgeEdges.Keys);

            foreach (var kvp in gameState.CrossBoardLedgeEdges)
            {
                var color = kvp.Key;
                var edges = kvp.Value;

                // All edges for a given color should have that color set
                foreach (var edge in edges)
                {
                    Assert.AreEqual(color, edge.Color);
                    Assert.AreNotEqual(edge.From.BoardId, edge.To.BoardId, "Cross-board edges must connect different boards.");
                }
            }
        }

        [Test]
        public void CrossBoardEdges_ExistForEveryColorBetweenAllBoards()
        {
            var players = new List<Player>
            {
                new Player(1, "Player1", 0),
                new Player(2, "Player2", 1),
                new Player(3, "Player3", 2)
            };

            var gameState = new GameState(players, null);

            foreach (var color in LedgeConfigConstants.LedgeColors)
            {
                Assert.IsTrue(gameState.CrossBoardLedgeEdges.ContainsKey(color), $"Missing cross-board edges for color {color}.");
                var edges = gameState.CrossBoardLedgeEdges[color];

                // Each board has exactly one ledge of each color; with N boards expect N*(N-1) directed edges.
                var expectedCount = players.Count * (players.Count - 1);
                Assert.AreEqual(expectedCount, edges.Count, $"Unexpected edge count for color {color}.");

                foreach (var edge in edges)
                {
                    Assert.AreEqual(color, edge.Color);
                    Assert.AreNotEqual(edge.From.BoardId, edge.To.BoardId);
                }
            }
        }
    }
}
