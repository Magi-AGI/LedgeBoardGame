using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using Magi.LedgeBoardGame.Board;
using Magi.LedgeBoardGame.UI;

namespace Magi.LedgeBoardGame.Tests.EditMode
{
    /// Pins the CP076 type-voice rule, in both directions:
    ///
    ///   italic  = the game NARRATING — transient, third-person event and
    ///             ceremony copy ("Player2 wins!", "Player1 eliminated.",
    ///             "… no legal moves — turn skipped"). StatusBanner.
    ///   upright = the interface INSTRUCTING — persistent, second-person
    ///             imperatives the player must act on ("Place Light",
    ///             "Select a stack", "Choose destination"). LedgeYouPanel.
    ///
    /// Both surfaces draw the same DisplayFont at the same TurnBannerSize (32f),
    /// so the ONLY thing separating them is the style bit — which makes this
    /// exactly the kind of deliberate asymmetry a later reader "fixes" by
    /// accident in the name of consistency. Hence a test on each side rather
    /// than a comment on each side.
    ///
    /// Note the asymmetry in what these two tests are FOR: the You-panel test
    /// was written red-first against the CP076 change and is the regression
    /// guard. The StatusBanner test never failed — it is a lock on the half of
    /// the ruling that deliberately did NOT change, so that flipping it becomes
    /// a conscious act with a red test attached.
    [TestFixture]
    public class LedgeTypeVoiceTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _spawned.Count; i++)
                if (_spawned[i] != null) Object.DestroyImmediate(_spawned[i]);
            _spawned.Clear();
        }

        private GameObject Spawn(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            _spawned.Add(go);
            return go;
        }

        private static TMP_Text FindLabel(GameObject root, string childName)
        {
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
                if (t.gameObject.name == childName) return t;
            return null;
        }

        /// The You panel exposes EnsureBuilt(), so it needs no special handling.
        private LedgeYouPanel BuildYouPanel()
        {
            var panel = Spawn("YouPanelUnderTest").AddComponent<LedgeYouPanel>();
            panel.EnsureBuilt();
            return panel;
        }

        /// StatusBanner builds its label in a private EnsureVisuals() called
        /// from Awake, and Unity does not run Awake for a component added by
        /// script outside Play Mode (only [ExecuteAlways] types get that). So
        /// the label genuinely does not exist unless the lifecycle is driven
        /// here — the earlier "Label not found" failures were this test's
        /// setup, not a change in the production surface.
        ///
        /// Driving Awake by reflection rather than adding a public build
        /// entrypoint: CP076's scope is one style property, and StatusBanner is
        /// explicitly comment-only in this pass. Widening its public API purely
        /// for a test would be the larger change of the two. Reflection into
        /// private members already has precedent here — see
        /// BoardViewHudSectionLabelContractTests.PrivateConst.
        private StatusBanner BuildStatusBanner()
        {
            var go = Spawn("StatusBannerUnderTest");
            // [RequireComponent] adds CanvasGroup on AddComponent; Awake reads
            // it, so add it up front rather than relying on ordering.
            go.AddComponent<CanvasGroup>();
            var banner = go.AddComponent<StatusBanner>();

            var awake = typeof(StatusBanner).GetMethod(
                "Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(awake, Is.Not.Null,
                "StatusBanner.Awake not found — its lifecycle entrypoint was renamed, and this " +
                "test can no longer drive the visual build.");
            awake.Invoke(banner, null);
            return banner;
        }

        [Test]
        public void YouPanelActionHeadline_IsUpright_NotItalic()
        {
            var panel = BuildYouPanel();

            var action = FindLabel(panel.gameObject, "ActionLabel");
            Assert.IsNotNull(action, "ActionLabel not found — the You panel row layout changed.");

            Assert.AreEqual(
                (FontStyles)0,
                action.fontStyle & FontStyles.Italic,
                "The You-panel action headline carries second-person imperatives (\"Place Light\", " +
                "\"Select a stack\"). It is interface instruction, not narration, so it must be " +
                "upright. See CP076.");
        }

        /// Compact mode (Comparison view, 3+ seats) retunes the same label's
        /// position and fontSizeMax. Upright has to survive that flip — a style
        /// re-applied on one path and not the other is how this regresses.
        [Test]
        public void YouPanelActionHeadline_StaysUpright_AcrossCompactModeFlips()
        {
            var panel = BuildYouPanel();
            var action = FindLabel(panel.gameObject, "ActionLabel");

            panel.SetCompactMode(true);
            Assert.AreEqual((FontStyles)0, action.fontStyle & FontStyles.Italic,
                "Compact mode re-applied italic to the action headline.");

            panel.SetCompactMode(false);
            Assert.AreEqual((FontStyles)0, action.fontStyle & FontStyles.Italic,
                "Returning to full mode re-applied italic to the action headline.");
        }

        [Test]
        public void StatusBannerToast_StaysItalic_NarrationVoice()
        {
            var banner = BuildStatusBanner();

            var label = FindLabel(banner.gameObject, "Label");
            Assert.IsNotNull(label, "StatusBanner Label not found — its visual build changed.");

            Assert.AreNotEqual(
                (FontStyles)0,
                label.fontStyle & FontStyles.Italic,
                "The StatusBanner toast narrates events and ceremony in the third person " +
                "(\"Player2 wins!\", \"Player1 eliminated.\"). Italic there is deliberate and is " +
                "what keeps the upright You-panel headline meaningful as a separate voice. " +
                "If this is being changed on purpose, revisit the CP076 ruling rather than " +
                "just deleting the assert.");
        }

        [Test]
        public void BothSurfaces_ShareSizeAndFont_SoStyleIsTheOnlyDistinction()
        {
            // If these ever stop matching, the CP076 rule loses its force: the two
            // voices would then differ by size or family as well, and the style bit
            // would no longer be carrying the distinction on its own.
            var panel = BuildYouPanel();
            var action = FindLabel(panel.gameObject, "ActionLabel");

            var banner = BuildStatusBanner();
            var label = FindLabel(banner.gameObject, "Label");
            Assert.IsNotNull(label, "StatusBanner Label not found — its visual build changed.");

            Assert.AreEqual(LedgeUITokens.TurnBannerSize, label.fontSize,
                "StatusBanner no longer renders at TurnBannerSize.");
            Assert.AreEqual(LedgeUITokens.TurnBannerSize, action.fontSizeMax,
                "You-panel headline no longer tops out at TurnBannerSize.");
            Assert.AreSame(action.font, label.font,
                "The two voices no longer share a font asset.");
        }
    }
}
