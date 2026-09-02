using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using Magi.LedgeBoardGame.Board;
using Magi.LedgeBoardGame.UI;

namespace Magi.LedgeBoardGame.Tests.EditMode
{
    /// CP074: the top-right "BOARD VIEW" caps caption must render at a readable
    /// physical size in portrait, not just in the landscape reference frame.
    ///
    /// The kit's caption recipe (SectionLabelSize 9.5, uppercase, tracked-out,
    /// InkDim) is authored in canvas reference units against 1920x1080. The
    /// CanvasScaler then scales the canvas by screen size, so those units are
    /// not a physical size: at 720x1280 the factor is ~0.67 and the caption
    /// renders at ~6.3px, whose capitals stand ~4.3px tall with sub-half-pixel
    /// stems. Design read that as broken glyphs across CP071-CP073.
    ///
    /// These tests assert the contract in the units Design actually judges —
    /// physical pixels and cap height — against the pure resolver the runtime
    /// layout calls, and against the font asset the game really resolves
    /// (MonoFont falls back to LiberationSans SDF; no JetBrainsMono asset ships).
    [TestFixture]
    public class BoardViewHudSectionLabelContractTests
    {
        // Main.unity's canvas: ScaleWithScreenSize, MatchWidthOrHeight, 0.5.
        private const float ReferenceWidth = 1920f;
        private const float ReferenceHeight = 1080f;
        private const float Match = 0.5f;

        // The portrait frame Design has been capturing.
        private const float PortraitScreenWidth = 720f;
        private const float PortraitScreenHeight = 1280f;

        private const float Authored = LedgeUITokens.SectionLabelSize;
        private const float Tolerance = 0.01f;

        // ── The accepted landscape frame is untouched ────────────────────

        [Test]
        public void LandscapeReferenceFrame_KeepsTheAuthoredCaptionSize()
        {
            float factor = CanvasScaleFactor(ReferenceWidth, ReferenceHeight);

            Assert.That(factor, Is.EqualTo(1f).Within(Tolerance), "Premise: the reference frame scales 1:1.");
            Assert.That(BoardViewHud.ResolveSectionLabelSize(factor), Is.EqualTo(Authored).Within(Tolerance),
                "CP074 must not touch the frame Design already accepted.");
        }

        /// A 4K window scales the canvas up, which makes the authored size
        /// physically larger, not smaller. Growing it further would be a
        /// redesign of the caption, not a readability floor.
        [Test]
        public void HighDpiFrame_NeverShrinksBelowTheAuthoredSize()
        {
            Assert.That(BoardViewHud.ResolveSectionLabelSize(2f), Is.EqualTo(Authored).Within(Tolerance));
            Assert.That(BoardViewHud.ResolveSectionLabelSize(1.5f), Is.EqualTo(Authored).Within(Tolerance));
        }

        // ── Portrait ─────────────────────────────────────────────────────

        [Test]
        public void PortraitPhone_RendersTheCaptionAtTheAuthoredPhysicalSize()
        {
            float factor = PortraitScaleFactor;
            float resolved = BoardViewHud.ResolveSectionLabelSize(factor);

            Assert.That(resolved, Is.GreaterThan(Authored),
                "Portrait has to grow the caption in canvas units to hold its physical size.");
            Assert.That(resolved * factor, Is.EqualTo(Authored).Within(Tolerance),
                "On screen the caption must measure what it measures in landscape.");
        }

        /// The readability contract in the terms Design judges: how many physical
        /// pixels tall the capitals actually stand.
        [Test]
        public void PortraitPhone_CapHeightMatchesTheLandscapeBaseline()
        {
            float landscape = CapHeightPx(Authored, 1f);
            float portrait = CapHeightPx(BoardViewHud.ResolveSectionLabelSize(PortraitScaleFactor), PortraitScaleFactor);

            Assert.That(portrait, Is.GreaterThanOrEqualTo(landscape - Tolerance),
                $"Portrait caps stand {portrait:0.00}px against the accepted landscape {landscape:0.00}px.");
        }

        /// Guards the premise. If this ever fails, portrait no longer shrinks the
        /// caption and the rest of this fixture is defending a solved problem.
        [Test]
        public void OriginalFixedSize_WouldHaveRenderedFarBelowTheLandscapeBaseline()
        {
            float landscape = CapHeightPx(Authored, 1f);
            float before = CapHeightPx(Authored, PortraitScaleFactor);

            Assert.That(before, Is.LessThan(landscape - 1f),
                "The CP074 defect: a fixed unit size loses more than a pixel of cap height in portrait.");
        }

        // ── Fit budget ───────────────────────────────────────────────────

        /// A window narrow enough would ask for a caption taller than its row.
        /// It degrades to the largest size that fits rather than clipping.
        [Test]
        public void ExtremelyNarrowCanvas_ClampsToTheCaptionRowBudget()
        {
            float max = PrivateConst("SectionLabelMaxSize");

            Assert.That(BoardViewHud.ResolveSectionLabelSize(0.4f), Is.EqualTo(max).Within(Tolerance));
            Assert.That(BoardViewHud.ResolveSectionLabelSize(0.1f), Is.EqualTo(max).Within(Tolerance));
            Assert.That(max, Is.GreaterThan(Authored), "The ceiling has to leave room above the authored size.");
        }

        /// TMP's TopLeft alignment pins the ascender line to the rect top, so the
        /// row holds rowHeight / ascentRatio units of type. Asserted against the
        /// live font asset rather than a copied metric, so swapping the caption
        /// face retunes the budget instead of silently clipping it.
        [Test]
        public void CaptionRow_CanHoldTheLargestResolvedSize()
        {
            var font = LedgeUITokens.MonoFont;
            Assert.That(font, Is.Not.Null, "No caption font resolved — not even the TMP fallback.");

            float rowHeight = PrivateConst("SectionLabelRowHeight");
            float max = PrivateConst("SectionLabelMaxSize");
            float ascentRatio = font.faceInfo.ascentLine / font.faceInfo.pointSize;

            Assert.That(max * ascentRatio, Is.LessThanOrEqualTo(rowHeight),
                $"A {max} unit caption ascends {max * ascentRatio:0.00} units into a {rowHeight} unit row.");
        }

        [Test]
        public void PortraitPhone_ResolvedSizeStillFitsTheCaptionRow()
        {
            var font = LedgeUITokens.MonoFont;
            float rowHeight = PrivateConst("SectionLabelRowHeight");
            float ascentRatio = font.faceInfo.ascentLine / font.faceInfo.pointSize;
            float resolved = BoardViewHud.ResolveSectionLabelSize(PortraitScaleFactor);

            Assert.That(resolved * ascentRatio, Is.LessThanOrEqualTo(rowHeight),
                "The portrait caption must fit its row without the ceiling having to catch it.");
        }

        // ── Tracking ─────────────────────────────────────────────────────

        /// The second half of the defect: 0.22em of tracking scatters 4px
        /// capitals into unrelated marks. Tightened to the value the You panel's
        /// turn-meta caption already uses, so this stays inside the family.
        [Test]
        public void Tracking_IsTightenedButStillReadsAsACaption()
        {
            float tracking = PrivateConst("SectionLabelTracking");

            Assert.That(tracking, Is.LessThan(22f), "CP074 tightens the caption tracking.");
            Assert.That(tracking, Is.GreaterThan(0f), "Caps captions in this kit are tracked out, not set solid.");
        }

        // ── The glyphs were never missing ────────────────────────────────

        /// Design described "broken glyphs", which invites a missing-glyph fix
        /// (importing a font, changing the string). This pins the real cause: the
        /// resolved face carries every character in the caption, so the defect is
        /// rasterisation at size, and CP074 is right to treat it as such.
        [Test]
        public void ResolvedCaptionFont_CarriesEveryCharacterInTheCaption()
        {
            var font = LedgeUITokens.MonoFont;
            Assert.That(font, Is.Not.Null);

            const string caption = "BOARD VIEW";
            foreach (char c in caption)
            {
                if (char.IsWhiteSpace(c)) continue;
                Assert.That(font.HasCharacter(c), Is.True,
                    $"'{c}' is missing from {font.name} — this would be a genuine missing-glyph defect.");
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static float PortraitScaleFactor =>
            CanvasScaleFactor(PortraitScreenWidth, PortraitScreenHeight);

        /// CanvasScaler.ScaleWithScreenSize / MatchWidthOrHeight, reproduced so
        /// the tests read in screen resolutions rather than a magic factor.
        private static float CanvasScaleFactor(float screenWidth, float screenHeight)
        {
            float logWidth = Mathf.Log(screenWidth / ReferenceWidth, 2f);
            float logHeight = Mathf.Log(screenHeight / ReferenceHeight, 2f);
            return Mathf.Pow(2f, Mathf.Lerp(logWidth, logHeight, Match));
        }

        /// Physical pixels of capital height for a caption of <paramref name="sizeInCanvasUnits"/>.
        private static float CapHeightPx(float sizeInCanvasUnits, float canvasScaleFactor)
        {
            var font = LedgeUITokens.MonoFont;
            Assert.That(font, Is.Not.Null);
            float capRatio = font.faceInfo.capLine / font.faceInfo.pointSize;
            return sizeInCanvasUnits * canvasScaleFactor * capRatio;
        }

        private static float PrivateConst(string name)
        {
            var field = typeof(BoardViewHud).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null, $"BoardViewHud.{name} not found — was it renamed?");
            return (float)field.GetValue(null);
        }
    }
}
