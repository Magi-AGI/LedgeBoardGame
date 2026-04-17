using System.Collections.Generic;
using NUnit.Framework;
using Magi.LedgeBoardGame.Builder;
using Magi.LedgeBoardGame.Models;

namespace Magi.LedgeBoardGame.Tests.EditMode
{
    /// Exhaustively verifies every one of the 49 space ids on a freshly built board
    /// resolves to the expected wheel-color name. The expected-name table is the
    /// single source of truth — if these break, the convention itself changed.
    [TestFixture]
    public class SpaceNamerTests
    {
        private static readonly Dictionary<int, string> ExpectedNames = new Dictionary<int, string>
        {
            // Center
            { 0, "Core" },

            // InnerBridge (1-6) — even wedges 0,2,4,6,8,10 → Ela, Yun, Glei, Rha, Wim, Quae
            { 1, "Ela Bridge" },
            { 2, "Yun Bridge" },
            { 3, "Glei Bridge" },
            { 4, "Rha Bridge" },
            { 5, "Wim Bridge" },
            { 6, "Quae Bridge" },

            // InnerWall (7-12) — odd wedges 1,3,5,7,9,11 → Biz, Jutu, Sace, Dau, Pfi, Vei
            { 7, "Biz Wall" },
            { 8, "Jutu Wall" },
            { 9, "Sace Wall" },
            { 10, "Dau Wall" },
            { 11, "Pfi Wall" },
            { 12, "Vei Wall" },

            // Ring2 (13-24) — one per wedge in order
            { 13, "Inner Ela" },
            { 14, "Inner Biz" },
            { 15, "Inner Yun" },
            { 16, "Inner Jutu" },
            { 17, "Inner Glei" },
            { 18, "Inner Sace" },
            { 19, "Inner Rha" },
            { 20, "Inner Dau" },
            { 21, "Inner Wim" },
            { 22, "Inner Pfi" },
            { 23, "Inner Quae" },
            { 24, "Inner Vei" },

            // Ring3-off (25-36) — pairs flank odd-wedge vertex; CCW slot stores prev outer wedge.
            // Vertex k=0 (Biz, wedge 1): 25 (Ela side) → "Ela Biz"; 26 (Yun side) → "Biz Yun"
            { 25, "Ela Biz" },
            { 26, "Biz Yun" },
            // Vertex k=1 (Jutu, wedge 3): 27 (Yun side) → "Yun Jutu"; 28 (Glei side) → "Jutu Glei"
            { 27, "Yun Jutu" },
            { 28, "Jutu Glei" },
            // Vertex k=2 (Sace, wedge 5): 29 → "Glei Sace"; 30 → "Sace Rha"
            { 29, "Glei Sace" },
            { 30, "Sace Rha" },
            // Vertex k=3 (Dau, wedge 7): 31 → "Rha Dau"; 32 → "Dau Wim"
            { 31, "Rha Dau" },
            { 32, "Dau Wim" },
            // Vertex k=4 (Pfi, wedge 9): 33 → "Wim Pfi"; 34 → "Pfi Quae"
            { 33, "Wim Pfi" },
            { 34, "Pfi Quae" },
            // Vertex k=5 (Vei, wedge 11): 35 → "Quae Vei"; 36 → "Vei Ela" (wraparound)
            { 35, "Quae Vei" },
            { 36, "Vei Ela" },

            // Ring3-vertex ledges (37-42) — odd-wedge colors
            { 37, "Biz Ledge" },
            { 38, "Jutu Ledge" },
            { 39, "Sace Ledge" },
            { 40, "Dau Ledge" },
            { 41, "Pfi Ledge" },
            { 42, "Vei Ledge" },

            // OuterAdded ledges (43-48) — even-wedge colors
            { 43, "Ela Ledge" },
            { 44, "Yun Ledge" },
            { 45, "Glei Ledge" },
            { 46, "Rha Ledge" },
            { 47, "Wim Ledge" },
            { 48, "Quae Ledge" },
        };

        [Test]
        public void Name_AllFortyNineSpaces_MatchExpectedNames()
        {
            var board = BoardGraphBuilder.CreateHexagonalBoard().BuildBoard(0, 0);

            for (int id = 0; id < 49; id++)
            {
                var meta = board.SpaceMetadata[id];
                var actual = SpaceNamer.Name(id, meta);
                Assert.AreEqual(ExpectedNames[id], actual, $"Space {id} name mismatch.");
            }
        }

        [Test]
        public void Name_Center_WithPlayerName_PrependsPlayer()
        {
            var meta = new SpaceMeta(SpaceType.Center, 0, 0);
            Assert.AreEqual("Alice Core", SpaceNamer.Name(0, meta, "Alice"));
        }

        [Test]
        public void Name_Center_NullOrEmptyPlayer_FallsBackToBareCore()
        {
            var meta = new SpaceMeta(SpaceType.Center, 0, 0);
            Assert.AreEqual("Core", SpaceNamer.Name(0, meta, null));
            Assert.AreEqual("Core", SpaceNamer.Name(0, meta, string.Empty));
        }

        [Test]
        public void Name_NonCenter_IgnoresPlayerName()
        {
            var board = BoardGraphBuilder.CreateHexagonalBoard().BuildBoard(0, 0);
            // A bridge name shouldn't change just because a caller passes a player name.
            Assert.AreEqual("Rha Bridge", SpaceNamer.Name(4, board.SpaceMetadata[4], "Alice"));
            Assert.AreEqual("Dau Ledge", SpaceNamer.Name(40, board.SpaceMetadata[40], "Alice"));
        }

        [Test]
        public void LabelOf_HandlesWraparound()
        {
            // Wrap-safety so callers don't have to mod ahead of time.
            Assert.AreEqual("Ela", SpaceNamer.LabelOf(12));
            Assert.AreEqual("Vei", SpaceNamer.LabelOf(-1));
            Assert.AreEqual("Ela", SpaceNamer.LabelOf(0));
        }
    }
}
