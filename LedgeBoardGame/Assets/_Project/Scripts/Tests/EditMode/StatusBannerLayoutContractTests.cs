using NUnit.Framework;
using UnityEngine;
using Magi.LedgeBoardGame.Board;
using Magi.LedgeBoardGame.UI;

namespace Magi.LedgeBoardGame.Tests.EditMode
{
    /// CP073: the transient top-center toast must not sit on top of the
    /// persistent top-corner chrome.
    ///
    /// CP072's portrait captures caught the defect. The toast's y offset was a
    /// constant -PanelEdgeInset, which is clear of both corners at the 1920-unit
    /// reference width but not on a phone: a 720x1280 screen scales (reference
    /// 1920x1080, match 0.5) to roughly 1080 canvas units, where the centered
    /// 560-wide toast spans 260..820 and lands on both the 360-wide You panel
    /// (28..388) and the 280-wide Board View HUD (772..1052) at the same top
    /// inset. Design read it as the toast's translucent grey box sitting over the
    /// "Place Light" headline and the TURN 1 eyebrow.
    ///
    /// These are geometry tests against StatusBanner.ResolveToastAnchoredPosition
    /// — the same pure function the runtime layout pass calls with measured rects
    /// — rather than a built canvas hierarchy. A ScreenSpaceOverlay Canvas forces
    /// its own RectTransform to the current game-view size, so a scene-based test
    /// could not pin a 1080-unit-wide canvas deterministically. Panel footprints
    /// come from the production constants, so resizing a panel retunes these
    /// expectations instead of silently invalidating them.
    [TestFixture]
    public class StatusBannerLayoutContractTests
    {
        private const float Inset = LedgeUITokens.PanelEdgeInset;
        private const float Gap = LedgeUITokens.PanelGap;
        private const float Tolerance = 0.01f;

        // Canvas widths in reference units, not screen pixels — see the class
        // doc for the 720x1280 -> ~1080 derivation.
        private const float PortraitCanvasWidth = 1080f;
        private const float LandscapeCanvasWidth = 1920f;

        // Width at which the centered toast's left edge just clears the You
        // panel's right edge by a PanelGap. Derived, not measured: this is the
        // breakpoint the production code computes, so nothing here hard-codes a
        // resolution.
        private static float YouPanelClearanceWidth =>
            2f * (Inset + LedgeYouPanel.PanelWidth + Gap + StatusBanner.ToastWidth * 0.5f);

        private static float ViewHudClearanceWidth =>
            2f * (Inset + BoardViewHud.HudWidth + Gap + StatusBanner.ToastWidth * 0.5f);

        // ── The defect, locked in ────────────────────────────────────────

        /// Guards the premise. If a future layout change makes the old fixed
        /// top-center inset legal in portrait, the rest of this fixture is
        /// asserting a constraint that no longer exists and should be revisited.
        [Test]
        public void Portrait_OriginalFixedTopInset_WouldCoverTheYouPanel()
        {
            var oldPlacement = new Vector2(0f, -Inset);

            Assert.That(Toast(PortraitCanvasWidth, oldPlacement).Overlaps(YouPanel()), Is.True,
                "The CP073 defect: at portrait width the fixed top-center toast overlaps " +
                "the You panel headline.");
            Assert.That(Toast(PortraitCanvasWidth, oldPlacement).Overlaps(ViewHud(PortraitCanvasWidth)), Is.True,
                "It overlaps the top-right Board View HUD in the same frame.");
        }

        // ── Narrow / portrait ────────────────────────────────────────────

        [Test]
        public void Portrait_ToastDropsBelowTheTopChrome()
        {
            var pos = Resolve(PortraitCanvasWidth, LedgeYouPanel.FullHeight);

            Assert.That(pos.y, Is.LessThan(-Inset),
                "Portrait must not leave the toast at the shared top inset.");
            // Both corners are in the way at this width, so the toast clears the
            // deeper of the two (the 144-tall Board View HUD).
            float deepest = Mathf.Max(LedgeYouPanel.FullHeight, BoardViewHud.HudHeight);
            Assert.That(pos.y, Is.EqualTo(-(Inset + deepest + Gap)).Within(Tolerance));
            Assert.That(pos.y, Is.LessThanOrEqualTo(-(Inset + LedgeYouPanel.FullHeight + Gap) + Tolerance),
                "At minimum the toast must clear the full-height You panel by a PanelGap.");
        }

        [Test]
        public void Portrait_ResolvedToast_ClearsTheYouPanelByAPanelGap()
        {
            var toast = Toast(PortraitCanvasWidth, Resolve(PortraitCanvasWidth, LedgeYouPanel.FullHeight));
            var you = YouPanel();

            Assert.That(toast.Overlaps(you), Is.False, "Toast still overlaps the You panel.");
            Assert.That(toast.yMin - you.yMax, Is.GreaterThanOrEqualTo(Gap - Tolerance),
                "Non-overlap is not enough — the toast needs a PanelGap of air below the panel.");
        }

        [Test]
        public void Portrait_ResolvedToast_ClearsTheBoardViewHudByAPanelGap()
        {
            var toast = Toast(PortraitCanvasWidth, Resolve(PortraitCanvasWidth, LedgeYouPanel.FullHeight));
            var hud = ViewHud(PortraitCanvasWidth);

            Assert.That(toast.Overlaps(hud), Is.False, "Toast still overlaps the Board View HUD.");
            Assert.That(toast.yMin - hud.yMax, Is.GreaterThanOrEqualTo(Gap - Tolerance));
        }

        /// Comparison view at 3+ seats slims the You panel to CompactHeight. The
        /// toast is placed off the measured rect, so it rides up with it — but
        /// only as far as the (unchanged) Board View HUD allows.
        [Test]
        public void Portrait_CompactYouPanel_StillClearsBothCorners()
        {
            var pos = Resolve(PortraitCanvasWidth, LedgeYouPanel.CompactHeight);
            var toast = Toast(PortraitCanvasWidth, pos);

            Assert.That(toast.Overlaps(YouPanel(LedgeYouPanel.CompactHeight)), Is.False);
            Assert.That(toast.Overlaps(ViewHud(PortraitCanvasWidth)), Is.False);
            Assert.That(pos.y, Is.EqualTo(-(Inset + BoardViewHud.HudHeight + Gap)).Within(Tolerance),
                "With the You panel slimmed, the Board View HUD is the binding obstacle.");
        }

        // ── Wide / landscape: the accepted placement is preserved ────────

        [Test]
        public void Landscape_KeepsTheOriginalTopCenterInset()
        {
            var pos = Resolve(LandscapeCanvasWidth, LedgeYouPanel.FullHeight);

            Assert.That(pos.x, Is.EqualTo(0f).Within(Tolerance), "The toast stays centered.");
            Assert.That(pos.y, Is.EqualTo(-Inset).Within(Tolerance),
                "CP073 must not move the toast on layouts where it never collided.");
        }

        [Test]
        public void Landscape_ToastAndCornerChrome_DoNotOverlap()
        {
            var toast = Toast(LandscapeCanvasWidth, Resolve(LandscapeCanvasWidth, LedgeYouPanel.FullHeight));

            Assert.That(toast.Overlaps(YouPanel()), Is.False);
            Assert.That(toast.Overlaps(ViewHud(LandscapeCanvasWidth)), Is.False);
        }

        // ── Breakpoint is derived from tokens, not a resolution ─────────

        [Test]
        public void Breakpoint_SitsExactlyWhereTheGeometrySaysItShould()
        {
            float atClearance = Resolve(YouPanelClearanceWidth, LedgeYouPanel.FullHeight).y;
            float justBelow = Resolve(YouPanelClearanceWidth - 1f, LedgeYouPanel.FullHeight).y;

            Assert.That(atClearance, Is.EqualTo(-Inset).Within(Tolerance),
                "At exactly a PanelGap of clearance the toast keeps the top inset.");
            Assert.That(justBelow, Is.EqualTo(-(Inset + LedgeYouPanel.FullHeight + Gap)).Within(Tolerance),
                "One unit narrower the You panel is in the way and the toast drops below it.");
        }

        /// The two corners have different widths, so they bind at different
        /// canvas widths. Between the thresholds only the You panel is in the way
        /// and the toast must clear only that — dropping to the deeper HUD there
        /// would waste vertical space no obstacle is using.
        [Test]
        public void BetweenThresholds_ClearsOnlyTheCornerItActuallyCollidesWith()
        {
            Assert.That(YouPanelClearanceWidth, Is.GreaterThan(ViewHudClearanceWidth),
                "Premise: the wider You panel binds first, so a band exists where only it collides.");

            float width = 0.5f * (YouPanelClearanceWidth + ViewHudClearanceWidth);
            var pos = Resolve(width, LedgeYouPanel.FullHeight);
            var toast = Toast(width, pos);

            Assert.That(pos.y, Is.EqualTo(-(Inset + LedgeYouPanel.FullHeight + Gap)).Within(Tolerance),
                "Only the You panel collides here, so its height is the clearance.");
            Assert.That(toast.Overlaps(YouPanel()), Is.False);
            Assert.That(toast.Overlaps(ViewHud(width)), Is.False);
        }

        // ── Degenerate inputs ───────────────────────────────────────────

        [Test]
        public void NoCornerChrome_KeepsTheTopCenterInset()
        {
            var pos = StatusBanner.ResolveToastAnchoredPosition(PortraitCanvasWidth, 0f, 0f, 0f, 0f);

            Assert.That(pos.y, Is.EqualTo(-Inset).Within(Tolerance),
                "With nothing in the top band the toast has no reason to move.");
        }

        /// The banner's Awake runs before the canvas has been laid out, so the
        /// first layout pass can see a zero-width canvas. It must fall back to the
        /// accepted placement rather than compute a nonsense drop.
        [Test]
        public void UnlaidOutCanvas_KeepsTheTopCenterInset()
        {
            Assert.That(Resolve(0f, LedgeYouPanel.FullHeight).y, Is.EqualTo(-Inset).Within(Tolerance));
            Assert.That(Resolve(-1f, LedgeYouPanel.FullHeight).y, Is.EqualTo(-Inset).Within(Tolerance));
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static Vector2 Resolve(float canvasWidth, float youPanelHeight)
        {
            return StatusBanner.ResolveToastAnchoredPosition(
                canvasWidth,
                LedgeYouPanel.PanelWidth, youPanelHeight,
                BoardViewHud.HudWidth, BoardViewHud.HudHeight);
        }

        // Rects are expressed in "canvas top-down" space: x from the canvas left
        // edge, y as depth below the canvas top edge. All three surfaces are
        // top-anchored, so this keeps the overlap math readable — and Rect.Overlaps
        // only needs both rects to share a convention, not a handedness.
        private static Rect Toast(float canvasWidth, Vector2 anchoredPosition)
        {
            return new Rect(
                canvasWidth * 0.5f + anchoredPosition.x - StatusBanner.ToastWidth * 0.5f,
                -anchoredPosition.y,
                StatusBanner.ToastWidth,
                StatusBanner.ToastHeight);
        }

        private static Rect YouPanel(float height = LedgeYouPanel.FullHeight)
        {
            return new Rect(Inset, Inset, LedgeYouPanel.PanelWidth, height);
        }

        private static Rect ViewHud(float canvasWidth)
        {
            return new Rect(
                canvasWidth - Inset - BoardViewHud.HudWidth, Inset,
                BoardViewHud.HudWidth, BoardViewHud.HudHeight);
        }
    }
}
