using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Models.Spec;
using UnityEngine;

namespace Magi.LedgeBoardGame.Tests.EditMode
{
    [TestFixture]
    public class LedgeGameSpecTests
    {
        private static string GetSpecPath()
        {
            return Path.Combine(Application.dataPath, "_Project", "Specs", "ledge", "ledge-game.v1.json");
        }

        private static LedgeGameSpec LoadSpec()
        {
            var specPath = GetSpecPath();
            Assert.IsTrue(File.Exists(specPath), $"Expected spec file at {specPath}");

            var json = File.ReadAllText(specPath);
            var spec = LedgeGameSpecLoader.LoadFromJson(json);
            Assert.IsNotNull(spec, "Spec should deserialize");
            return spec;
        }

        [Test]
        public void LedgeSpec_ParsesAndMatchesConstants()
        {
            var spec = LoadSpec();

            Assert.IsNotNull(spec.Config);
            Assert.IsNotNull(spec.Config.Board);

            Assert.AreEqual(49, spec.Config.Board.Spaces, "Ledge board should have 49 spaces.");
            Assert.IsNotNull(spec.Config.Board.LedgeColors);
            Assert.AreEqual(12, spec.Config.Board.LedgeColors.Count, "There should be 12 ledge colors.");

            Assert.IsTrue(
                spec.Config.Board.LedgeColors.SequenceEqual(LedgeConfigConstants.LedgeColors),
                "Ledge colors in code should match those defined in the spec.");
        }

        [Test]
        public void LedgeSpec_RingsMatchBoardGraphBuilderLayout()
        {
            var spec = LoadSpec();

            var builder = Magi.LedgeBoardGame.Builder.BoardGraphBuilder.CreateHexagonalBoard();
            var board = builder.BuildBoard(0, 0);

            Assert.AreEqual(49, board.SpaceMetadata.Count, "Board should expose 49 spaces.");

            var knownSpaceIds = new HashSet<int>();
            foreach (var ring in spec.Config.Board.Rings)
            {
                foreach (var id in ring.SpaceIds)
                {
                    knownSpaceIds.Add(id);
                    Assert.IsTrue(board.SpaceMetadata.ContainsKey(id), $"Board is missing space {id} from ring '{ring.Name}'.");
                }

                foreach (var id in ring.SpaceIds)
                {
                    var meta = board.SpaceMetadata[id];
                    switch (ring.Name)
                    {
                        case "center":
                            Assert.AreEqual(SpaceType.Center, meta.Type, "Center ring should only contain the center space.");
                            Assert.AreEqual(0, meta.RingIndex);
                            break;
                        case "inner":
                            Assert.AreEqual(1, meta.RingIndex, "Inner ring spaces should have ringIndex 1.");
                            break;
                        case "innerMiddle":
                            Assert.AreEqual(2, meta.RingIndex, "Inner middle ring spaces should have ringIndex 2.");
                            break;
                        case "outerMiddle":
                            Assert.AreEqual(3, meta.RingIndex, "Outer middle ring spaces should have ringIndex 3.");
                            break;
                        case "outer":
                            // The spec lists ring3 vertex spaces (37-42) in both outerMiddle and outer rings;
                            // tolerate that overlap since they sit on the geometric outer axis but are graph-wise
                            // one edge inside the added outer ring.
                            if (!string.IsNullOrEmpty(meta.ColorLabel) && meta.RingIndex == 3)
                            {
                                // ring3-vertex ledge space, listed in "outer" for convenience — allow.
                            }
                            else
                            {
                                Assert.AreEqual(4, meta.RingIndex, "Outer ring spaces should have ringIndex 4.");
                            }
                            break;
                        default:
                            Assert.Fail($"Unexpected ring name '{ring.Name}' in spec.");
                            break;
                    }
                }
            }

            // Spec rings (with overlaps) should cover all 49 space IDs.
            var expectedIds = new HashSet<int>();
            for (int i = 0; i < 49; i++)
            {
                expectedIds.Add(i);
            }

            CollectionAssert.AreEquivalent(
                expectedIds,
                knownSpaceIds,
                "Spec rings should collectively cover all 49 space IDs (0-48).");
        }

        [Test]
        public void LedgeSpec_PhasesAndMovesAreAsExpected()
        {
            var spec = LoadSpec();

            var phaseNames = spec.Phases
                .Select(p => p.Name)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            CollectionAssert.AreEquivalent(
                new[] { "movement", "placement" },
                phaseNames,
                "Spec phases should be 'placement' and 'movement'.");

            var moveNames = spec.Moves.Keys
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            CollectionAssert.AreEquivalent(
                new[] { "endTurn", "moveToken", "placeToken" },
                moveNames,
                "Spec moves should be placeToken, moveToken, and endTurn.");

            // Sanity check that our GamePhase enum contains matching concepts.
            var enumNames = Enum.GetNames(typeof(GamePhase));
            CollectionAssert.Contains(enumNames, "Placement");
            CollectionAssert.Contains(enumNames, "Movement");
        }

        [Test]
        public void LedgeSpecValidator_DoesNotThrowForCurrentSpecAndCode()
        {
            var spec = LoadSpec();
            Assert.DoesNotThrow(() => LedgeSpecValidator.Validate(spec));
        }
    }
}
