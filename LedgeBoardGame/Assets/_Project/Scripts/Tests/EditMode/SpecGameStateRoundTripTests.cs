using System.Collections.Generic;
using Newtonsoft.Json;
using NUnit.Framework;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Models.Spec;

namespace Magi.LedgeBoardGame.Tests.EditMode
{
    [TestFixture]
    public class SpecGameStateRoundTripTests
    {
        private static JsonSerializerSettings BuildSettings() => new JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
            {
                NamingStrategy = new Newtonsoft.Json.Serialization.CamelCaseNamingStrategy()
            },
            MissingMemberHandling = MissingMemberHandling.Ignore,
            Formatting = Formatting.None
        };

        private static GameState RoundTrip(GameState source)
        {
            var spec = source.ToSpecState();
            var settings = BuildSettings();
            var json = JsonConvert.SerializeObject(spec, settings);
            var rebuilt = JsonConvert.DeserializeObject<SpecGameState>(json, settings);
            return GameState.FromSpecState(rebuilt);
        }

        [Test]
        public void GameState_RoundTripsThroughSpecGameStateJson()
        {
            var players = new List<Player>
            {
                new Player(1, "Player1", 0),
                new Player(2, "Player2", 1)
            };

            var original = new GameState(players, null);
            var restored = RoundTrip(original);

            Assert.AreEqual(original.Players.Count, restored.Players.Count);
            Assert.AreEqual(original.Boards.Count, restored.Boards.Count);
            Assert.AreEqual(original.CurrentPlayerId, restored.CurrentPlayerId);
            Assert.AreEqual(original.CurrentPhase, restored.CurrentPhase);
            Assert.AreEqual(original.TurnNumber, restored.TurnNumber);
        }

        // Mid-turn state must roundtrip without losing the per-turn action log —
        // the server-authoritative reconcile path depends on restoring a partially
        // advanced turn (already placed one tone, made some moves) with the same
        // remaining budget. Dropping these lists silently would let a reconciled
        // state re-spend placements that were already made.
        [Test]
        public void GameState_MidTurn_PreservesCurrentTurnMovesAndPlacements()
        {
            var players = new List<Player>
            {
                new Player(1, "Player1", 0),
                new Player(2, "Player2", 1)
            };

            var original = new GameState(players, null)
            {
                CurrentPhase = GamePhase.Movement,
                TurnNumber = 4,
                CurrentPlayerId = 2
            };

            original.CurrentTurnPlacements.Add(
                new PlacementMove(new SpaceId(1, 7), Tone.Light, 1) { Result = MoveResult.Stack });
            original.CurrentTurnPlacements.Add(
                new PlacementMove(new SpaceId(1, 9), Tone.Dark, 1) { Result = MoveResult.Clear });

            original.CurrentTurnMoves.Add(
                new Move(new SpaceId(1, 7), new SpaceId(1, 8), Tone.Light, 2) { Result = MoveResult.Stack });
            original.CurrentTurnMoves.Add(
                new Move(new SpaceId(1, 8), new SpaceId(1, 10), Tone.Light, 1) { Result = MoveResult.Lock });

            var restored = RoundTrip(original);

            Assert.AreEqual(original.CurrentPhase, restored.CurrentPhase);
            Assert.AreEqual(original.TurnNumber, restored.TurnNumber);
            Assert.AreEqual(original.CurrentPlayerId, restored.CurrentPlayerId);

            Assert.AreEqual(2, restored.CurrentTurnPlacements.Count,
                "Both placements should survive the roundtrip.");
            Assert.IsTrue(restored.HasPlacedLight,
                "Light placement must be visible to the per-turn budget check after rebuild.");
            Assert.IsTrue(restored.HasPlacedDark,
                "Dark placement must be visible to the per-turn budget check after rebuild.");

            Assert.AreEqual(2, restored.CurrentTurnMoves.Count,
                "Both moves should survive the roundtrip.");

            // Verify field-level fidelity on one representative entry per list so
            // the codec is pinned on every property (SpaceId, Tone, Count, Result).
            var place0 = restored.CurrentTurnPlacements[0];
            Assert.AreEqual(new SpaceId(1, 7), place0.Target);
            Assert.AreEqual(Tone.Light, place0.Tone);
            Assert.AreEqual(1, place0.Count);
            Assert.AreEqual(MoveResult.Stack, place0.Result);

            var move0 = restored.CurrentTurnMoves[0];
            Assert.AreEqual(new SpaceId(1, 7), move0.From);
            Assert.AreEqual(new SpaceId(1, 8), move0.To);
            Assert.AreEqual(Tone.Light, move0.Tone);
            Assert.AreEqual(2, move0.Count);
            Assert.AreEqual(MoveResult.Stack, move0.Result);
        }

        // JIP/LIP: IsConnected must survive the wire so a snapshot delivered
        // to a reconnecting client carries the full seat-occupancy picture.
        // Turn-rotation (P2) consults this flag, so a dropped presence bit
        // would silently re-include an absent seat in the rotation.
        [Test]
        public void GameState_RoundTrips_IsConnectedPerPlayer()
        {
            var players = new List<Player>
            {
                new Player(1, "Player1", 0) { IsConnected = true },
                new Player(2, "Player2", 1) { IsConnected = false },
                new Player(3, "Player3", 2) { IsConnected = true },
                new Player(4, "Player4", 3) { IsConnected = false }
            };

            var original = new GameState(players, null);
            var restored = RoundTrip(original);

            Assert.AreEqual(4, restored.Players.Count);
            Assert.IsTrue(restored.Players[0].IsConnected, "seat 0 present");
            Assert.IsFalse(restored.Players[1].IsConnected, "seat 1 absent");
            Assert.IsTrue(restored.Players[2].IsConnected, "seat 2 present");
            Assert.IsFalse(restored.Players[3].IsConnected, "seat 3 absent");
        }

        // BuildDefaultRoster's initiallyConnected argument is how client and
        // server agree on the network-mode initial state (all seats absent).
        // A drift here lands in the hash on the very first shadow compare,
        // so pin both the true and false paths explicitly.
        [Test]
        public void BuildDefaultRoster_InitiallyConnectedFlag_HonoredOnEverySeat()
        {
            var local = Player.BuildDefaultRoster(4);
            foreach (var p in local) Assert.IsTrue(p.IsConnected, "Local default roster is all-present");

            var network = Player.BuildDefaultRoster(4, initiallyConnected: false);
            foreach (var p in network) Assert.IsFalse(p.IsConnected, "Network initial roster is all-absent");
        }
    }
}
