using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Magi.LedgeBoardGame.Board;

namespace Magi.LedgeBoardGame.Tests.EditMode
{
    /// CP072: the visitor rim and the movable-source rim must breathe over the
    /// same alpha envelope.
    ///
    /// CP071's portrait captures caught the regression these tests lock down.
    /// The visitor rim's floor of 0.55 was tuned at CP054, when tiles rendered
    /// ~120px wide in landscape and the primitive was still a diffuse glow that
    /// needed a deep trough to feel calm. At 720x1280 the same tile is ~49px, so
    /// the visible band is roughly a third of the pixels at the same alpha, and
    /// Claude Design could not locate the rim at all at 0.550 — the visitor pill
    /// was carrying the entire visitor signal on a phone. The source rim, floored
    /// at 0.85 by CP067, stayed findable in the same frames at the same tile size.
    ///
    /// Asserted against the source rim's constants rather than hard-coded numbers
    /// so the two families cannot silently drift apart again: retuning the source
    /// rim alone breaks this test rather than quietly reintroducing the gap.
    [TestFixture]
    public class SpaceViewRimAlphaContractTests
    {
        // Design's stated requirement from the CP071 verdict ("raise the visitor
        // rim alpha floor to ~0.80-0.85 in line with the source rim"). Kept as an
        // absolute lower bound alongside the parity checks so the contract still
        // fails if someone lowers BOTH families together.
        private const float DesignMinimumRimFloor = 0.80f;
        private const float Tolerance = 0.0001f;

        private GameObject _go;
        private SpaceView _view;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("SpaceViewUnderTest", typeof(RectTransform));
            _view = _go.AddComponent<SpaceView>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void VisitorRimFloor_MatchesSourceRimFloor()
        {
            float visitorMin = GetFloatField("visitorRimAlphaMin");
            float sourceMin = GetFloatField("sourceRimAlphaMin");

            Assert.That(visitorMin, Is.EqualTo(sourceMin).Within(Tolerance),
                "Visitor and source rims must share an alpha floor so neither family " +
                "becomes unfindable at portrait tile sizes (CP071/CP072).");
        }

        [Test]
        public void VisitorRimPeak_MatchesSourceRimPeak()
        {
            float visitorMax = GetFloatField("visitorRimAlphaMax");
            float sourceMax = GetFloatField("sourceRimAlphaMax");

            Assert.That(visitorMax, Is.EqualTo(sourceMax).Within(Tolerance),
                "Visitor and source rims must share an alpha peak (CP072: breathe 0.85 -> 1.0).");
        }

        [Test]
        public void VisitorRimFloor_MeetsDesignMinimum()
        {
            float visitorMin = GetFloatField("visitorRimAlphaMin");

            Assert.That(visitorMin, Is.GreaterThanOrEqualTo(DesignMinimumRimFloor),
                "CP071 Design verdict: the visitor rim is not findable in portrait below ~0.80.");
        }

        [Test]
        public void VisitorRimEnvelope_IsAscendingAndWithinUnitRange()
        {
            float visitorMin = GetFloatField("visitorRimAlphaMin");
            float visitorMax = GetFloatField("visitorRimAlphaMax");

            Assert.That(visitorMin, Is.LessThanOrEqualTo(visitorMax), "Floor must not exceed peak.");
            Assert.That(visitorMax, Is.LessThanOrEqualTo(1f), "Alpha cannot exceed 1.");
        }

        /// Exercises the real breathe rather than only the serialized constants.
        /// ComputeSteadyRimAlpha samples Time.unscaledTime, so the phase is
        /// arbitrary in EditMode and the exact value is not assertable — but the
        /// result must land inside the envelope whatever the phase, which is the
        /// property that actually reaches the screen.
        [Test]
        public void ComputeSteadyRimAlpha_StaysWithinTheAssertedEnvelope()
        {
            float visitorMin = GetFloatField("visitorRimAlphaMin");
            float visitorMax = GetFloatField("visitorRimAlphaMax");

            var method = typeof(SpaceView).GetMethod("ComputeSteadyRimAlpha",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "ComputeSteadyRimAlpha not found — did the breathe move?");

            float alpha = (float)method.Invoke(_view, null);

            Assert.That(alpha, Is.GreaterThanOrEqualTo(visitorMin - Tolerance));
            Assert.That(alpha, Is.LessThanOrEqualTo(visitorMax + Tolerance));
            Assert.That(alpha, Is.GreaterThanOrEqualTo(DesignMinimumRimFloor - Tolerance),
                "Every phase of the visitor breathe must stay above the portrait findability floor.");
        }

        /// The tests above construct a SpaceView directly, which reads the C# field
        /// initializers. Runtime does not: BoardPresenter.CreateSpaceView instantiates
        /// spaceViewPrefab when it is assigned, and it is — Main.unity wires
        /// GameController.boardPresenterPrefab to BoardPresenter.prefab, whose
        /// spaceViewPrefab points at SpaceView.prefab. Serialized values on that asset
        /// therefore win over the initializers for any field the asset carries, which is
        /// how pulseFrequencyHz currently ships at 1.4 while the source default says 0.9.
        /// CP072's runtime capture measured the visitor rim at 0.612 and 0.792 — both
        /// inside the old [0.55, 0.85] envelope — so this asserts the envelope on the
        /// asset the game actually instantiates, not just on a fresh component.
        [Test]
        public void PrefabSerializedVisitorRim_MatchesSourceRimEnvelope()
        {
            const string prefabPath = "Assets/_Project/Prefabs/SpaceView.prefab";
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"{prefabPath} not found — did the prefab move?");

            var prefabView = prefab.GetComponent<SpaceView>();
            Assert.That(prefabView, Is.Not.Null, $"{prefabPath} has no SpaceView component.");

            float visitorMin = GetFloatFieldOn(prefabView, "visitorRimAlphaMin");
            float visitorMax = GetFloatFieldOn(prefabView, "visitorRimAlphaMax");
            float sourceMin = GetFloatFieldOn(prefabView, "sourceRimAlphaMin");
            float sourceMax = GetFloatFieldOn(prefabView, "sourceRimAlphaMax");

            Assert.That(visitorMin, Is.EqualTo(sourceMin).Within(Tolerance),
                "Prefab visitor rim floor must match the source rim floor — this is the " +
                "instance Play Mode actually spawns.");
            Assert.That(visitorMax, Is.EqualTo(sourceMax).Within(Tolerance),
                "Prefab visitor rim peak must match the source rim peak.");
            Assert.That(visitorMin, Is.GreaterThanOrEqualTo(DesignMinimumRimFloor),
                "CP071 Design verdict: below ~0.80 the visitor rim is unfindable in portrait.");
        }

        private float GetFloatField(string name)
        {
            return GetFloatFieldOn(_view, name);
        }

        private static float GetFloatFieldOn(SpaceView target, string name)
        {
            var field = typeof(SpaceView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"SpaceView.{name} not found — was the field renamed?");
            return (float)field.GetValue(target);
        }
    }
}
