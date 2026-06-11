using System.IO;
using System.Linq;
using NUnit.Framework;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Models.Spec;
using Magi.LedgeBoardGame.Rules;
using UnityEngine;

namespace Magi.LedgeBoardGame.Tests.EditMode
{
    [TestFixture]
    public class GameRulesSpecIntegrationTests
    {
        private static LedgeGameSpec LoadSpec()
        {
            var specPath = Path.Combine(Application.dataPath, "_Project", "Specs", "ledge", "ledge-game.v1.json");
            Assert.IsTrue(File.Exists(specPath), $"Expected spec file at {specPath}");

            var json = File.ReadAllText(specPath);
            var spec = LedgeGameSpecLoader.LoadFromJson(json);
            Assert.IsNotNull(spec);
            return spec;
        }

        [Test]
        public void PlacementPhase_BehaviorMatchesSpecTurnLimits()
        {
            var spec = LoadSpec();
            var config = LedgeRuntimeConfig.FromSpec(spec);

            Assert.AreEqual(2, config.PlacementMinMoves);
            Assert.AreEqual(2, config.PlacementMaxMoves);

            var players = new System.Collections.Generic.List<Player>
            {
                new Player(1, "Player1", 0),
                new Player(2, "Player2", 1)
            };

            var gameState = new GameState(players, config);
            var rules = new GameRules(config);

            Assert.AreEqual(GamePhase.Placement, gameState.CurrentPhase);

            var target1 = new SpaceId(0, 1);
            var target2 = new SpaceId(0, 2);

            // First placement (Light)
            Assert.IsTrue(rules.CanPlaceToken(gameState, target1, Tone.Light));
            var move1 = rules.PlaceToken(gameState, target1, Tone.Light);
            Assert.IsNotNull(move1);
            Assert.AreEqual(GamePhase.Placement, gameState.CurrentPhase, "After first placement, phase should still be Placement.");

            // Second placement (Dark)
            Assert.IsTrue(rules.CanPlaceToken(gameState, target2, Tone.Dark));
            var move2 = rules.PlaceToken(gameState, target2, Tone.Dark);
            Assert.IsNotNull(move2);

            // After two placements, spec and implementation both say we enter Movement.
            Assert.AreEqual(GamePhase.Movement, gameState.CurrentPhase, "After second placement, phase should switch to Movement.");

            // A third placement should not be allowed this turn.
            Assert.IsFalse(rules.CanPlaceToken(gameState, target2, Tone.Light));
        }

        [Test]
        public void MoveAndPlaceToken_RespectSpecAllowedPhases()
        {
            var spec = LoadSpec();

            var placeSpec = spec.Moves["placeToken"];
            var moveSpec = spec.Moves["moveToken"];

            CollectionAssert.Contains(placeSpec.AllowedPhases, "placement");
            CollectionAssert.DoesNotContain(placeSpec.AllowedPhases, "movement");

            CollectionAssert.Contains(moveSpec.AllowedPhases, "movement");
            CollectionAssert.DoesNotContain(moveSpec.AllowedPhases, "placement");

            var players = new System.Collections.Generic.List<Player>
            {
                new Player(1, "Player1", 0),
                new Player(2, "Player2", 1)
            };

            var gameState = new GameState(players, null);
            var rules = new GameRules(null);

            var placeTarget = new SpaceId(0, 1);
            var moveFrom = new SpaceId(0, 1);
            var moveTo = new SpaceId(0, 0);

            // In Placement phase, placeToken allowed, moveToken not allowed.
            Assert.AreEqual(GamePhase.Placement, gameState.CurrentPhase);
            Assert.IsTrue(rules.CanPlaceToken(gameState, placeTarget, Tone.Light));
            Assert.IsFalse(rules.CanMoveToken(gameState, moveFrom, moveTo, Tone.Light));

            // Switch to Movement phase manually for this test.
            gameState.CurrentPhase = GamePhase.Movement;

            // In Movement phase, moveToken may be allowed (ignoring adjacency/setup), placeToken not allowed.
            Assert.IsFalse(rules.CanPlaceToken(gameState, placeTarget, Tone.Light));
        }
    }
}
