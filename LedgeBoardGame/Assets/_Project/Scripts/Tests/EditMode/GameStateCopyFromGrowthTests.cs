using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Magi.LedgeBoardGame.Models;

namespace Magi.LedgeBoardGame.Tests.EditMode
{
    /// Covers GameState.CopyFrom's grow-to-match behavior used by the JIP
    /// (join-in-progress) path: a client that configured a placeholder seat
    /// count receives an authoritative SpecGameState with a larger roster,
    /// and CopyFrom must append the missing Players/Boards so rotation,
    /// win-checks, and board iteration see the full set. Never shrinks —
    /// the server pre-populates MaxSeats at session open and only flips
    /// IsConnected flags thereafter.
    [TestFixture]
    public class GameStateCopyFromGrowthTests
    {
        [Test]
        public void CopyFrom_LargerRoster_AppendsMissingPlayersAndBoards()
        {
            var small = new GameState(Player.BuildDefaultRoster(2, initiallyConnected: true), null);
            var large = new GameState(Player.BuildDefaultRoster(6, initiallyConnected: false), null);

            small.CopyFrom(large);

            Assert.AreEqual(6, small.Players.Count, "Players should grow to match source");
            Assert.AreEqual(6, small.Boards.Count, "Boards should grow to match source");
            for (int i = 0; i < 6; i++)
            {
                Assert.AreEqual(i + 1, small.Players[i].Id, $"Player[{i}] Id");
                Assert.AreEqual(i, small.Players[i].BoardId, $"Player[{i}] BoardId");
                Assert.IsFalse(small.Players[i].IsConnected, $"Player[{i}] IsConnected mirrors source");
                Assert.AreEqual(i, small.Boards[i].BoardId, $"Board[{i}] BoardId");
                Assert.AreEqual(i + 1, small.Boards[i].PlayerId, $"Board[{i}] PlayerId");
            }
        }

        [Test]
        public void CopyFrom_LargerRoster_RebuildsCrossBoardLedgeEdgesForNewBoards()
        {
            var small = new GameState(Player.BuildDefaultRoster(2), null);
            var large = new GameState(Player.BuildDefaultRoster(6), null);

            small.CopyFrom(large);

            // Cross-board edges are derived from every Board. After growth the
            // edge map must reference the new boards, otherwise cross-ledge
            // traversal from any new board would silently return no targets.
            foreach (var kvp in large.CrossBoardLedgeEdges)
            {
                Assert.That(small.CrossBoardLedgeEdges.ContainsKey(kvp.Key), $"color {kvp.Key} missing");
                Assert.AreEqual(kvp.Value.Count, small.CrossBoardLedgeEdges[kvp.Key].Count,
                    $"edge count mismatch for color {kvp.Key}");
            }
            // Sanity: at least one edge references board id 5 (post-growth).
            bool anyReferencesGrownBoard = small.CrossBoardLedgeEdges.Values
                .SelectMany(list => list)
                .Any(e => e.From.BoardId == 5 || e.To.BoardId == 5);
            Assert.IsTrue(anyReferencesGrownBoard, "Expected cross-board edges to reference grown boards");
        }

        [Test]
        public void CopyFrom_SmallerRoster_DoesNotShrink()
        {
            var large = new GameState(Player.BuildDefaultRoster(4), null);
            var small = new GameState(Player.BuildDefaultRoster(2), null);

            large.CopyFrom(small);

            // Grow-only policy: server never shrinks rosters within a session,
            // so we don't bother implementing shrink. If the policy ever
            // changes this assertion documents the current contract.
            Assert.AreEqual(4, large.Players.Count);
            Assert.AreEqual(4, large.Boards.Count);
        }

        [Test]
        public void CopyFrom_MirrorsIsConnectedFlagsOnExistingSeats()
        {
            var source = new GameState(Player.BuildDefaultRoster(4, initiallyConnected: false), null);
            var target = new GameState(Player.BuildDefaultRoster(4, initiallyConnected: false), null);
            source.Players[0].IsConnected = true;
            source.Players[2].IsConnected = true;

            target.CopyFrom(source);

            Assert.IsTrue(target.Players[0].IsConnected);
            Assert.IsFalse(target.Players[1].IsConnected);
            Assert.IsTrue(target.Players[2].IsConnected);
            Assert.IsFalse(target.Players[3].IsConnected);
        }

        [Test]
        public void CopyFrom_Roundtrip_ViaFromSpecState_GrowsRosterIdentically()
        {
            // Simulate the ApplyServerState path: FromSpecState inflates the
            // authoritative snapshot, then CopyFrom pushes it into the live
            // _gameState. The client started with 2 seats; server announces 6.
            var client = new GameState(Player.BuildDefaultRoster(2, initiallyConnected: true), null);
            var serverGame = new GameState(Player.BuildDefaultRoster(6, initiallyConnected: false), null);
            serverGame.Players[0].IsConnected = true;

            var spec = serverGame.ToSpecState();
            var inflated = GameState.FromSpecState(spec);
            client.CopyFrom(inflated);

            Assert.AreEqual(6, client.Players.Count);
            Assert.AreEqual(6, client.Boards.Count);
            Assert.IsTrue(client.Players[0].IsConnected);
            for (int i = 1; i < 6; i++)
                Assert.IsFalse(client.Players[i].IsConnected, $"Player[{i}] should be disconnected");
        }
    }
}
