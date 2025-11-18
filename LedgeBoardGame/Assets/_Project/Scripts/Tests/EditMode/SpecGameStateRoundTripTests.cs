using System.Text.Json;
using NUnit.Framework;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Models.Spec;

namespace Magi.LedgeBoardGame.Tests.EditMode
{
    [TestFixture]
    public class SpecGameStateRoundTripTests
    {
        [Test]
        public void GameState_RoundTripsThroughSpecGameStateJson()
        {
            var players = new System.Collections.Generic.List<Player>
            {
                new Player(1, "Player1", 0),
                new Player(2, "Player2", 1)
            };

            var original = new GameState(players, null);

            var specState = original.ToSpecState();

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
            var json = JsonSerializer.Serialize(specState, options);

            var deserialized = JsonSerializer.Deserialize<SpecGameState>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var restored = GameState.FromSpecState(deserialized);

            Assert.AreEqual(original.Players.Count, restored.Players.Count);
            Assert.AreEqual(original.Boards.Count, restored.Boards.Count);
            Assert.AreEqual(original.CurrentPlayerId, restored.CurrentPlayerId);
            Assert.AreEqual(original.CurrentPhase, restored.CurrentPhase);
            Assert.AreEqual(original.TurnNumber, restored.TurnNumber);
        }
    }
}
