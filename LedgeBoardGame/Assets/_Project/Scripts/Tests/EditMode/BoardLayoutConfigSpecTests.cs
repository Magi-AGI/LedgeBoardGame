using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Magi.LedgeBoardGame.Builder;
using Magi.LedgeBoardGame.Config;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Models.Spec;
using UnityEngine;

namespace Magi.LedgeBoardGame.Tests.EditMode
{
    [TestFixture]
    public class BoardLayoutConfigSpecTests
    {
        [Test]
        public void BoardLayoutConfig_MatchesBoardGraphBuilderMetadata()
        {
            var layout = ScriptableObject.CreateInstance<BoardLayoutConfig>();
            layout.GenerateDefaultLayout();

            var layoutMeta = layout.GetAllSpaceMetadata();

            var builder = BoardGraphBuilder.CreateHexagonalBoard();
            var board = builder.BuildBoard(0, 0);

            Assert.AreEqual(49, layoutMeta.Count, "BoardLayoutConfig should define 49 spaces.");
            Assert.AreEqual(49, board.SpaceMetadata.Count, "BoardGraphBuilder should define 49 spaces.");

            var layoutIds = new HashSet<int>(layoutMeta.Keys);
            var builderIds = new HashSet<int>(board.SpaceMetadata.Keys);

            CollectionAssert.AreEquivalent(
                builderIds,
                layoutIds,
                "BoardLayoutConfig and BoardGraphBuilder should define the same space IDs.");

            foreach (var id in builderIds)
            {
                var fromBuilder = board.SpaceMetadata[id];
                var fromLayout = layoutMeta[id];

                Assert.AreEqual(fromBuilder.Type, fromLayout.Type, $"Space {id} type mismatch between builder and layout.");
                Assert.AreEqual(fromBuilder.RingIndex, fromLayout.RingIndex, $"Space {id} ringIndex mismatch between builder and layout.");
                Assert.AreEqual(fromBuilder.WedgeIndex, fromLayout.WedgeIndex, $"Space {id} wedgeIndex mismatch between builder and layout.");

                Assert.AreEqual(fromBuilder.ColorLabel, fromLayout.ColorLabel, $"Space {id} colorLabel mismatch between builder and layout.");
            }
        }

        [Test]
        public void BoardLayoutConfig_MatchesSpecRingAssignments()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var specPath = Path.Combine(projectRoot, "Specs", "ledge", "ledge-game.v1.json");
            Assert.IsTrue(File.Exists(specPath), $"Expected spec file at {specPath}");

            var json = File.ReadAllText(specPath);
            var spec = LedgeGameSpecLoader.LoadFromJson(json);
            Assert.IsNotNull(spec);

            var layout = ScriptableObject.CreateInstance<BoardLayoutConfig>();
            layout.GenerateDefaultLayout();
            var layoutMeta = layout.GetAllSpaceMetadata();

            foreach (var ring in spec.Config.Board.Rings)
            {
                foreach (var id in ring.SpaceIds)
                {
                    Assert.IsTrue(layoutMeta.ContainsKey(id), $"BoardLayoutConfig is missing space {id} from ring '{ring.Name}'.");

                    var meta = layoutMeta[id];
                    var expectedRingIndex = ring.Name switch
                    {
                        "center" => 0,
                        "inner" => 1,
                        "innerMiddle" => 2,
                        "outerMiddle" => 3,
                        "outer" => 4,
                        _ => throw new InvalidOperationException($"Unexpected ring name '{ring.Name}' in spec.")
                    };

                    if (!(meta.Type == SpaceType.Ledge && ring.Name == "outerMiddle" && meta.RingIndex == 4))
                    {
                        Assert.AreEqual(expectedRingIndex, meta.RingIndex, $"Space {id} ringIndex mismatch for ring '{ring.Name}'.");
                    }
                }
            }
        }

        [Test]
        public void BoardLayoutConfig_Ring3Spaces_HaveTwoRing2Connections()
        {
            var layout = ScriptableObject.CreateInstance<BoardLayoutConfig>();
            layout.GenerateDefaultLayout();
            var adjacency = layout.GetAdjacencyMap();
            var ring3Ids = new List<int>();
            foreach (var space in layout.Spaces)
            {
                if (space.type == SpaceType.Ring3)
                {
                    ring3Ids.Add(space.spaceId);
                }
            }

            foreach (var id in ring3Ids)
            {
                Assert.IsTrue(adjacency.ContainsKey(id), $"Adjacency missing for ring3 space {id}");
                var neighbors = adjacency[id];
                var ring2Neighbors = neighbors.FindAll(n => n >= 13 && n <= 24);
                Assert.AreEqual(2, ring2Neighbors.Count, $"Ring3 space {id} should connect to two Ring2 neighbors.");
            }
        }
    }
}
