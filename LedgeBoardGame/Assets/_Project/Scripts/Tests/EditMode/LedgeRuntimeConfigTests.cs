using System.IO;
using NUnit.Framework;
using Magi.LedgeBoardGame.Models.Spec;
using UnityEngine;

namespace Magi.LedgeBoardGame.Tests.EditMode
{
    [TestFixture]
    public class LedgeRuntimeConfigTests
    {
        [Test]
        public void RuntimeConfig_FromSpec_UsesExpectedTurnLimits()
        {
            var specPath = Path.Combine(Application.dataPath, "_Project", "Specs", "ledge", "ledge-game.v1.json");
            Assert.IsTrue(File.Exists(specPath), $"Expected spec file at {specPath}");

            var json = File.ReadAllText(specPath);
            var spec = LedgeGameSpecLoader.LoadFromJson(json);
            Assert.IsNotNull(spec);

            var config = LedgeRuntimeConfig.FromSpec(spec);

            Assert.AreEqual(2, config.MinPlayers);
            Assert.AreEqual(8, config.MaxPlayers);

            Assert.AreEqual(2, config.PlacementMinMoves);
            Assert.AreEqual(2, config.PlacementMaxMoves);
            Assert.AreEqual(0, config.MovementMinMoves);
            Assert.AreEqual(999, config.MovementMaxMoves);
        }
    }
}

