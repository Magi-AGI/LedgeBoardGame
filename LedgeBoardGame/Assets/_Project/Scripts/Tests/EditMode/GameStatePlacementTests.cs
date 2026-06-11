using System.Collections.Generic;
using NUnit.Framework;
using Magi.LedgeBoardGame.Models;

namespace Magi.LedgeBoardGame.Tests.EditMode
{
    [TestFixture]
    public class GameStatePlacementTests
    {
        [Test]
        public void PlacementComplete_DerivesFromCurrentTurnPlacements()
        {
            var players = new List<Player>
            {
                new Player(1, "Player1", 0),
                new Player(2, "Player2", 1)
            };

            var gameState = new GameState(players, null);

            Assert.IsFalse(gameState.IsPlacementComplete());

            gameState.CurrentTurnPlacements.Add(new PlacementMove(new SpaceId(0, 1), Tone.Light));
            Assert.IsFalse(gameState.IsPlacementComplete(), "Only Light placed, should not be complete.");

            gameState.CurrentTurnPlacements.Add(new PlacementMove(new SpaceId(0, 2), Tone.Dark));
            Assert.IsTrue(gameState.IsPlacementComplete(), "Light + Dark placed, placement should be complete.");
        }
    }
}
