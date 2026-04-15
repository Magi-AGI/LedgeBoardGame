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
            var specPath = Path.Combine(Application.dataPath, "_Project", "Specs", "ledge", "ledge-game.v1.json");
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

                    // Ring3 vertex spaces (IDs 37-42) appear in both the "outerMiddle" and "outer" rings of
                    // the spec but carry ringIndex 3 in code. Tolerate that overlap for the "outer" ring.
                    var ringIndexMatches = meta.RingIndex == expectedRingIndex ||
                                           (!string.IsNullOrEmpty(meta.ColorLabel) && ring.Name == "outer" && meta.RingIndex == 3);

                    if (!ringIndexMatches)
                    {
                        Assert.AreEqual(expectedRingIndex, meta.RingIndex, $"Space {id} ringIndex mismatch for ring '{ring.Name}'.");
                    }
                }
            }
        }

        [Test]
        public void BoardLayoutConfig_Ring3Spaces_HaveExpectedRing2Connections()
        {
            var layout = ScriptableObject.CreateInstance<BoardLayoutConfig>();
            layout.GenerateDefaultLayout();
            var adjacency = layout.GetAdjacencyMap();

            // Ring3-off spaces (25-36) flank the ring3-vertex; they connect to two Ring2 neighbours.
            // Ring3-vertex spaces (37-42) sit on the vertex axis and connect to exactly one Ring2 neighbour.
            foreach (var space in layout.Spaces)
            {
                if (space.type != SpaceType.Ring3) continue;

                Assert.IsTrue(adjacency.ContainsKey(space.spaceId), $"Adjacency missing for ring3 space {space.spaceId}");
                var ring2Neighbors = adjacency[space.spaceId].FindAll(n => n >= 13 && n <= 24);

                if (string.IsNullOrEmpty(space.colorLabel))
                {
                    Assert.AreEqual(2, ring2Neighbors.Count, $"Ring3-off space {space.spaceId} should connect to two Ring2 neighbors.");
                }
                else
                {
                    Assert.AreEqual(1, ring2Neighbors.Count, $"Ring3-vertex space {space.spaceId} should connect to exactly one Ring2 neighbor.");
                }
            }
        }
    }
}
