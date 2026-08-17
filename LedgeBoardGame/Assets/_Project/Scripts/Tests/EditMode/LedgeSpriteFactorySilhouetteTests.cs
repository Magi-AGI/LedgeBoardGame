using NUnit.Framework;
using UnityEngine;
using Magi.LedgeBoardGame.Board;

namespace Magi.LedgeBoardGame.Tests.EditMode
{
    /// Guards the CP065 neutral-silhouette contract that the visitor overlay depends on.
    ///
    /// The visitor rim/wave/keyline tint a silhouette to one uniform seat accent. That only
    /// produces a uniform hue if the mask is genuinely untinted (a UI.Image tint multiplies),
    /// and it only stays aligned to the tile face if the mask's coverage matches the fill it
    /// shadows texel for texel. Both properties are invisible at the call site — nothing about
    /// `img.sprite = LedgeSpriteFactory.HexSilhouette` reveals a wrong sprite — so they are
    /// pinned here.
    ///
    /// The sharpest guard is HexSilhouetteIsSolid_NotTheHexFrameGlowBloom: HexFrameGlow sits
    /// right next to HexSilhouette in the factory, and its bridge/wall siblings really ARE
    /// silhouettes, so it is the natural wrong pick. Its alpha peaks at the hex boundary and
    /// is zero at the center, which is exactly what that test rejects.
    [TestFixture]
    public class LedgeSpriteFactorySilhouetteTests
    {
        private const int ExpectedSize = 128;

        // Sprites come from a process-wide cache, so silhouette and fill are literally the same
        // build path with the same float ordering and should agree exactly. A tolerance of one
        // byte absorbs any incidental rounding difference without weakening the test: a bloom or
        // a mismatched shape differs by hundreds of texels at full scale, not by ±1.
        private const int AlphaTolerance = 1;

        private static Color32[] Texels(Sprite sprite)
        {
            Assert.IsNotNull(sprite, "Sprite was null.");
            Assert.IsNotNull(sprite.texture, $"{sprite.name} had no texture.");
            Assert.AreEqual(ExpectedSize, sprite.texture.width, $"{sprite.name} width.");
            Assert.AreEqual(ExpectedSize, sprite.texture.height, $"{sprite.name} height.");
            return sprite.texture.GetPixels32();
        }

        private static Color32 CenterTexel(Sprite sprite)
        {
            var px = Texels(sprite);
            const int mid = ExpectedSize / 2;
            return px[mid * ExpectedSize + mid];
        }

        private static void AssertUntintedWhite(Sprite sprite)
        {
            var px = Texels(sprite);
            for (int i = 0; i < px.Length; i++)
            {
                // Alpha carries the shape (and varies with antialiasing); RGB must be pure white
                // everywhere it is visible, or a tint would multiply against baked pigment.
                if (px[i].a == 0) continue;
                if (px[i].r != 255 || px[i].g != 255 || px[i].b != 255)
                {
                    Assert.Fail(
                        $"{sprite.name} texel {i % ExpectedSize},{i / ExpectedSize} is tinted " +
                        $"(rgba {px[i].r},{px[i].g},{px[i].b},{px[i].a}); silhouettes must be pure white.");
                }
            }
        }

        private static void AssertCoverageMatches(Sprite silhouette, Sprite fill)
        {
            var s = Texels(silhouette);
            var f = Texels(fill);
            Assert.AreEqual(f.Length, s.Length, "Texel counts differ.");

            int mismatches = 0;
            int firstMismatch = -1;
            for (int i = 0; i < s.Length; i++)
            {
                if (Mathf.Abs(s[i].a - f[i].a) <= AlphaTolerance) continue;
                mismatches++;
                if (firstMismatch < 0) firstMismatch = i;
            }

            if (mismatches > 0)
            {
                Assert.Fail(
                    $"{silhouette.name} coverage differs from {fill.name} at {mismatches} texel(s); " +
                    $"first at {firstMismatch % ExpectedSize},{firstMismatch / ExpectedSize} " +
                    $"(silhouette a={s[firstMismatch].a}, fill a={f[firstMismatch].a}). " +
                    "The visitor overlay would no longer match the tile silhouette.");
            }
        }

        [Test]
        public void Silhouettes_AreUntintedWhite()
        {
            AssertUntintedWhite(LedgeSpriteFactory.HexSilhouette);
            AssertUntintedWhite(LedgeSpriteFactory.BridgeSilhouette);
            AssertUntintedWhite(LedgeSpriteFactory.WallSilhouette);
        }

        [Test]
        public void Silhouettes_AreSolidAtCenter()
        {
            Assert.AreEqual(255, CenterTexel(LedgeSpriteFactory.HexSilhouette).a, "HexSilhouette center.");
            Assert.AreEqual(255, CenterTexel(LedgeSpriteFactory.BridgeSilhouette).a, "BridgeSilhouette center.");
            Assert.AreEqual(255, CenterTexel(LedgeSpriteFactory.WallSilhouette).a, "WallSilhouette center.");
        }

        [Test]
        public void HexSilhouetteIsSolid_NotTheHexFrameGlowBloom()
        {
            // HexFrameGlow's alpha peaks at the hex boundary and falls to zero at the center.
            // If these two ever agree at the center, someone has swapped one for the other.
            byte silhouetteCenter = CenterTexel(LedgeSpriteFactory.HexSilhouette).a;
            byte bloomCenter = CenterTexel(LedgeSpriteFactory.HexFrameGlow).a;

            Assert.AreEqual(255, silhouetteCenter, "HexSilhouette must be solid at its center.");
            Assert.AreNotEqual(silhouetteCenter, bloomCenter,
                "HexFrameGlow is a boundary bloom, not a silhouette — it must not be usable as a hard mask.");
        }

        [Test]
        public void BridgeAndWallSilhouettes_AliasTheirFullShapeGlows()
        {
            // These are deliberate aliases, not second bakes — the assert keeps a future
            // "cleanup" from quietly duplicating a 128x128 texture per shape.
            Assert.AreSame(LedgeSpriteFactory.BridgeFrameGlow, LedgeSpriteFactory.BridgeSilhouette);
            Assert.AreSame(LedgeSpriteFactory.WallFrameGlow, LedgeSpriteFactory.WallSilhouette);
        }

        [Test]
        public void HexSilhouette_CoversSameAreaAsHexFill()
        {
            AssertCoverageMatches(LedgeSpriteFactory.HexSilhouette, LedgeSpriteFactory.GetHexFill(Color.red));
        }

        [Test]
        public void HexSilhouette_CoversSameAreaAsHexSplitFill()
        {
            // Ring2 and Ring3-off tiles use the split fill; one hex mask has to serve both, which
            // only holds because both bakes share the same radius/apothem constants.
            AssertCoverageMatches(
                LedgeSpriteFactory.HexSilhouette,
                LedgeSpriteFactory.GetHexSplitFill(Color.red, Color.blue, 90f));
        }

        [Test]
        public void BridgeSilhouette_CoversSameAreaAsBridgeFill()
        {
            AssertCoverageMatches(
                LedgeSpriteFactory.BridgeSilhouette,
                LedgeSpriteFactory.GetBridgeFill(Color.red, Color.blue));
        }

        [Test]
        public void WallSilhouette_CoversSameAreaAsWallFill()
        {
            AssertCoverageMatches(LedgeSpriteFactory.WallSilhouette, LedgeSpriteFactory.GetWallFill());
        }

        [Test]
        public void Silhouettes_ShareRectAndPivotWithFills()
        {
            // A differing rect/pivot/PPU would offset the overlay behind the tile face even with
            // identical coverage.
            var hexFill = LedgeSpriteFactory.GetHexFill(Color.red);
            var hexSilhouette = LedgeSpriteFactory.HexSilhouette;
            Assert.AreEqual(hexFill.rect, hexSilhouette.rect, "Hex rect.");
            Assert.AreEqual(hexFill.pivot, hexSilhouette.pivot, "Hex pivot.");
            Assert.AreEqual(hexFill.pixelsPerUnit, hexSilhouette.pixelsPerUnit, "Hex PPU.");

            var wallFill = LedgeSpriteFactory.GetWallFill();
            var wallSilhouette = LedgeSpriteFactory.WallSilhouette;
            Assert.AreEqual(wallFill.rect, wallSilhouette.rect, "Wall rect.");
            Assert.AreEqual(wallFill.pivot, wallSilhouette.pivot, "Wall pivot.");
            Assert.AreEqual(wallFill.pixelsPerUnit, wallSilhouette.pixelsPerUnit, "Wall PPU.");
        }
    }
}
