using Newtonsoft.Json;
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

            var settings = new JsonSerializerSettings
            {
                ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
                {
                    NamingStrategy = new Newtonsoft.Json.Serialization.CamelCaseNamingStrategy()
                },
                Formatting = Formatting.None
            };
            var json = JsonConvert.SerializeObject(specState, settings);

            var deserialized = JsonConvert.DeserializeObject<SpecGameState>(json, new JsonSerializerSettings
            {
                ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
                {
                    NamingStrategy = new Newtonsoft.Json.Serialization.CamelCaseNamingStrategy()
                },
                MissingMemberHandling = MissingMemberHandling.Ignore
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
