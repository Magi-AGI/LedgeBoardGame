using NUnit.Framework;
using System.Collections.Generic;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Rules;

namespace Magi.LedgeBoardGame.Tests.EditMode
{
    /// Covers the Energy-Siphoning / reach-extension model: same-tone intermediates
    /// extend a stack's travel budget and bias path selection toward pickup-rich routes,
    /// and chained MoveToken calls grow the traveling stack via ResolveEntry at each
    /// same-tone waypoint.
    [TestFixture]
    public class PickupReachabilityTests
    {
        private GameRules _rules;
        private GameState _gameState;

        [SetUp]
        public void Setup()
        {
            _rules = new GameRules(null);
            var players = new List<Player>
            {
                new Player(1, "Player1", 0),
                new Player(2, "Player2", 1)
            };
            _gameState = new GameState(players, null);
            _gameState.CurrentPhase = GamePhase.Movement;

            // Minimal metadata + linear adjacency on board 0: 1 — 2 — 3 — 4 — 5.
            // Secondary branch 1 — 10 — 11 provides an equal-length alternate route
            // to (implicitly) anywhere — tests wire specific targets through whichever
            // spaces they need.
            var board = _gameState.GetBoard(0);
            for (int i = 1; i <= 12; i++)
            {
                board.SpaceMetadata[i] = new SpaceMeta(SpaceType.InnerBridge, 1, i - 1);
            }
            board.Adjacency[1] = new List<int> { 2, 10 };
            board.Adjacency[2] = new List<int> { 1, 3 };
            board.Adjacency[3] = new List<int> { 2, 4 };
            board.Adjacency[4] = new List<int> { 3, 5 };
            board.Adjacency[5] = new List<int> { 4 };
            board.Adjacency[10] = new List<int> { 1, 11 };
            board.Adjacency[11] = new List<int> { 10, 12 };
            board.Adjacency[12] = new List<int> { 11 };
        }

        [Test]
        public void FindShortestPath_PrefersSameToneRouteAtEqualDistance()
        {
            // Source has 2 Light on space 1.
            // Two equal-length (2-hop) routes to reach distance-2 destinations:
            //   (a) 1 → 2 (empty) → 3 (empty)
            //   (b) 1 → 10 (Light pickup) → 11 (empty)
            // Both are 2 hops with 0 clashes; route (b) has a pickup, so it wins
            // the tertiary tiebreak and FindShortestPath(1, ?, Light) should prefer
            // going through 10 for any destination only reachable via that branch.
            var board = _gameState.GetBoard(0);
            board.SetStack(1, new TokenStack(2, 0, Tone.Light));
            board.SetStack(10, new TokenStack(2, 0, Tone.Light));

            // Make spaces 3 and 11 both distance-2 leaves; we ask for paths from 1
            // through each branch and confirm the lex key prefers the pickup route
            // when both branches are feasible to the same terminal.
            //
            // To make branches converge on the same target, connect 3 ↔ 12 and
            // 11 ↔ 12 so space 12 is reachable at distance 3 by either route.
            board.Adjacency[3] = new List<int> { 2, 4, 12 };
            board.Adjacency[11] = new List<int> { 10, 12 };
            board.Adjacency[12] = new List<int> { 3, 11 };

            var path = _rules.FindShortestPath(_gameState, new SpaceId(0, 1), new SpaceId(0, 12), Tone.Light, 4);

            // Path should include 10 (pickup side), not 2 (empty side).
            var ids = new List<int>();
            foreach (var p in path) ids.Add(p.Id);
            CollectionAssert.Contains(ids, 10, "Expected path to route through space 10 (same-tone pickup).");
            CollectionAssert.DoesNotContain(ids, 2, "Expected path NOT to route through space 2 (empty).");
        }

        [Test]
        public void FindShortestPath_PrefersLowerClashOverMorePickups()
        {
            // Two equal-length routes: one lossy-but-rich, one clean. Safety wins.
            //   (a) 1 → 2 (2 Dark, clash) → 3 (3 Light, pickup)      : clashes=2, pickups=3
            //   (b) 1 → 10 (empty) → 11 (empty)                       : clashes=0, pickups=0
            // Lex order (hops, clashes asc, pickups desc): (b) wins because clashes
            // is the secondary key and 0 < 2 regardless of the pickup advantage.
            var board = _gameState.GetBoard(0);
            board.SetStack(1, new TokenStack(3, 0, Tone.Light));
            board.SetStack(2, new TokenStack(0, 2, Tone.Dark));
            board.SetStack(3, new TokenStack(3, 0, Tone.Light));

            board.Adjacency[3] = new List<int> { 2, 4, 12 };
            board.Adjacency[11] = new List<int> { 10, 12 };
            board.Adjacency[12] = new List<int> { 3, 11 };

            var path = _rules.FindShortestPath(_gameState, new SpaceId(0, 1), new SpaceId(0, 12), Tone.Light, 6);

            var ids = new List<int>();
            foreach (var p in path) ids.Add(p.Id);
            CollectionAssert.Contains(ids, 10, "Clean route through 10 should beat clash-heavy route through 2.");
            CollectionAssert.DoesNotContain(ids, 2, "Route through 2 would cost 2 Light to clashes; should not be chosen.");
        }

        [Test]
        public void GetReachableTargets_ExtendsReachThroughSameToneStacks()
        {
            // Source has 2 Light. Without pickups, reach is 2 hops.
            // Space 2 holds 2 Light: traversing it extends budget by 2 (net +1 per hop
            // since one hop is consumed). Chain: 1(b=2) → 2 (+2, b=3) → 3 (b=2) →
            // 4 (b=1) → 5 (b=0). So space 5 should be reachable at distance 4.
            var board = _gameState.GetBoard(0);
            board.SetStack(1, new TokenStack(2, 0, Tone.Light));
            board.SetStack(2, new TokenStack(2, 0, Tone.Light));

            var reach = _rules.GetReachableTargets(_gameState, new SpaceId(0, 1), Tone.Light, 2);

            Assert.IsTrue(reach.ContainsKey(new SpaceId(0, 5)),
                "Reach should extend to space 5 via pickup at space 2.");
            Assert.AreEqual(4, reach[new SpaceId(0, 5)], "Space 5 is 4 hops away along the extended path.");

            // Sanity: without the pickup, a 2-budget stack wouldn't reach space 4 either.
            board.SetStack(2, new TokenStack());
            var reachNoPickup = _rules.GetReachableTargets(_gameState, new SpaceId(0, 1), Tone.Light, 2);
            Assert.IsFalse(reachNoPickup.ContainsKey(new SpaceId(0, 4)),
                "Without pickup at 2, reach should stop at distance 2.");
        }

        [Test]
        public void ChainedMove_GrowsCarriedStackAtSameToneWaypoint()
        {
            // Simulates ExecuteStackMove's per-hop loop for a pickup chain. Bottom-tone
            // lock means each Light-bottomed stack keeps one Light pinned:
            //   - Source 1 has 3 Light bottom-Light → 2 are movable.
            //   - Space 2 has 2 Light bottom-Light → 1 is movable.
            //   - Destination 3 empty.
            // Hop 1: move 2 Light from 1 → 2. After: 1=1L (locked), 2=4L bottom-Light.
            // Hop 2: carried = full stack at 2 = 4. Only 3 can move (4th is locked).
            //        After: 2=1L (locked), 3=3L. The traveling stack has grown from the
            //        2 that left origin to the 3 that arrive — that's the +1 siphon.
            var board = _gameState.GetBoard(0);
            board.SetStack(1, new TokenStack(3, 0, Tone.Light));
            board.SetStack(2, new TokenStack(2, 0, Tone.Light));
            board.SetStack(3, new TokenStack());

            int carried = board.GetStack(1).GetMovableCount(Tone.Light);
            Assert.AreEqual(2, carried, "Source should expose 2 movable Light (3 with 1 locked).");

            int hopLight = 0;
            for (int i = 0; i < carried; i++)
            {
                var mv = _rules.MoveToken(_gameState, new SpaceId(0, 1), new SpaceId(0, 2), Tone.Light);
                if (mv == null) break;
                hopLight++;
            }
            Assert.AreEqual(2, hopLight, "Hop 1 should move all 2 movable Light.");
            Assert.AreEqual(1, board.GetStack(1).LightCount, "Source retains its bottom-locked Light.");
            Assert.AreEqual(4, board.GetStack(2).LightCount,
                "Pickup waypoint should hold 4 Light (2 original + 2 arrived).");

            // Carry the full waypoint stack forward; the per-iteration lock check
            // inside MoveToken prevents the bottom-locked counter from being taken.
            carried = board.GetStack(2).LightCount;
            hopLight = 0;
            for (int i = 0; i < carried; i++)
            {
                var mv = _rules.MoveToken(_gameState, new SpaceId(0, 2), new SpaceId(0, 3), Tone.Light);
                if (mv == null) break;
                hopLight++;
            }
            Assert.AreEqual(3, hopLight,
                "Hop 2 should move 3 Light (2 siphoned + 1 carried passthrough), leaving the bottom-lock.");
            Assert.AreEqual(3, board.GetStack(3).LightCount,
                "Destination should hold 3 Light — 2 originals + 1 net siphon after bottom-lock tax.");
            Assert.AreEqual(1, board.GetStack(2).LightCount, "Pickup waypoint retains its bottom-locked Light.");
        }
    }
}
