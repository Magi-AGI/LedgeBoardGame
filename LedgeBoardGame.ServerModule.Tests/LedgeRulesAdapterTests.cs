using System.Linq;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Models.Network;
using Magi.LedgeBoardGame.Models.Spec;
using Magi.LedgeBoardGame.Rules;
using Magi.LedgeBoardGame.ServerModule;
using MagiGameServer.Contracts.Core;
using MagiGameServer.Contracts.Rules;
using NUnit.Framework;

namespace Magi.LedgeBoardGame.ServerModule.Tests
{
    /// Unit-level coverage for the adapter. Verifies Apply semantics
    /// (Applied / Rejected), hash stability, and identity ProjectStateFor.
    /// Integration with the server's Session host lives in the separate
    /// LedgeGameModuleHostIntegrationTests file.
    [TestFixture]
    public class LedgeRulesAdapterTests
    {
        private static SpecGameState NewInitialState(int seatCount = 2)
        {
            var module = new LedgeGameModule();
            return (SpecGameState)module.CreateInitialState(LedgeGameModule.DefaultConfig(seatCount));
        }

        private static SpaceId FirstValidPlacementTarget(SpecGameState state)
        {
            var gs = GameState.FromSpecState(state);
            var rules = new GameRules();
            var player = gs.GetCurrentPlayer();
            var targets = rules.GetValidPlacementTargets(gs, player.Id);
            Assert.That(targets, Is.Not.Empty, "initial state should have valid placement targets");
            return targets.First();
        }

        [Test]
        public void Apply_LegalPlaceToken_ReturnsAppliedAndFreshState()
        {
            var adapter = new LedgeRulesAdapter();
            var state = NewInitialState();
            var target = FirstValidPlacementTarget(state);
            var action = LedgeAction.PlaceToken(target, Tone.Light);

            var outcome = adapter.Apply(state, action, out var newState);

            Assert.That(outcome, Is.EqualTo(ApplyOutcome.Applied));
            Assert.That(newState, Is.Not.SameAs(state), "Apply must not mutate input");
            Assert.That(newState.CurrentTurnPlacements, Has.Count.EqualTo(1));
            Assert.That(newState.CurrentTurnPlacements[0].Tone, Is.EqualTo(Tone.Light));
            Assert.That(state.CurrentTurnPlacements, Has.Count.EqualTo(0),
                "input state's turn log must remain untouched");
        }

        [Test]
        public void Apply_PlaceTokenOnOpponentBoard_IsRejected()
        {
            var adapter = new LedgeRulesAdapter();
            var state = NewInitialState(seatCount: 2);
            // Seat 0 (player 1) is the current player; targeting player 2's
            // board violates CanPlaceToken's boardId guard.
            var foreignBoardTarget = new SpaceId(boardId: 2, id: 0);
            var action = LedgeAction.PlaceToken(foreignBoardTarget, Tone.Light);

            var outcome = adapter.Apply(state, action, out var newState);

            Assert.That(outcome, Is.EqualTo(ApplyOutcome.Rejected));
            Assert.That(newState, Is.SameAs(state),
                "rejected applies must return the input reference so hashes compare identical");
        }

        [Test]
        public void Apply_DoublePlaceSameTone_SecondIsRejected()
        {
            var adapter = new LedgeRulesAdapter();
            var state = NewInitialState();
            var firstTarget = FirstValidPlacementTarget(state);

            adapter.Apply(state, LedgeAction.PlaceToken(firstTarget, Tone.Light), out var afterFirst);
            // Pick any other valid target; placing Light twice in the same
            // turn is forbidden regardless of where.
            var secondTarget = FirstValidPlacementTarget(afterFirst);
            var outcome = adapter.Apply(afterFirst, LedgeAction.PlaceToken(secondTarget, Tone.Light), out var afterSecond);

            Assert.That(outcome, Is.EqualTo(ApplyOutcome.Rejected));
            Assert.That(afterSecond, Is.SameAs(afterFirst));
        }

        [Test]
        public void Apply_EndTurn_AdvancesToNextPlayerAndClearsTurnLog()
        {
            var adapter = new LedgeRulesAdapter();
            var state = NewInitialState(seatCount: 2);
            var startingPlayer = int.Parse(state.Ctx.CurrentPlayer);

            var outcome = adapter.Apply(state, LedgeAction.EndTurn(), out var newState);

            Assert.That(outcome, Is.EqualTo(ApplyOutcome.Applied));
            Assert.That(int.Parse(newState.Ctx.CurrentPlayer), Is.Not.EqualTo(startingPlayer));
            Assert.That(newState.CurrentTurnPlacements, Is.Empty);
            Assert.That(newState.CurrentTurnMoves, Is.Empty);
        }

        [Test]
        public void GetStateHash_IsStableForSameState()
        {
            var adapter = new LedgeRulesAdapter();
            var state1 = NewInitialState();
            var state2 = NewInitialState();

            Assert.That(adapter.GetStateHash(state1), Is.EqualTo(adapter.GetStateHash(state2)),
                "hash must be stable for structurally identical states — not dependent on object identity");
        }

        [Test]
        public void GetStateHash_ChangesAfterApply()
        {
            var adapter = new LedgeRulesAdapter();
            var state = NewInitialState();
            long before = adapter.GetStateHash(state);

            var target = FirstValidPlacementTarget(state);
            adapter.Apply(state, LedgeAction.PlaceToken(target, Tone.Light), out var newState);
            long after = adapter.GetStateHash(newState);

            Assert.That(after, Is.Not.EqualTo(before));
        }

        [Test]
        public void ProjectStateFor_IsIdentityForEverySeat()
        {
            var adapter = new LedgeRulesAdapter();
            var state = NewInitialState(seatCount: 4);

            for (int seat = 0; seat < 4; seat++)
            {
                var projected = adapter.ProjectStateFor(state, new SeatId(seat));
                Assert.That(projected, Is.SameAs(state),
                    $"seat {seat} must see canonical state unchanged — BoardGame is perfect-info");
                Assert.That(adapter.GetStateHashForSeat(state, new SeatId(seat)),
                    Is.EqualTo(adapter.GetStateHash(state)),
                    "seat hash must equal canonical hash under identity projection");
            }
        }

        [Test]
        public void SnapshotState_AllowsInputReferenceToSurviveSubsequentApply()
        {
            var adapter = new LedgeRulesAdapter();
            var state = NewInitialState();
            var snapshot = (SpecGameState)((IRulesAdapter)adapter).SnapshotState(state);
            var snapshotHashBefore = adapter.GetStateHash(snapshot);

            // Run an Apply; adapter must return a fresh state so the snapshot
            // reference stays pointing at the pre-Apply value. If Apply ever
            // aliases into a shared mutable buffer, the snapshot hash would
            // silently drift to match the post-Apply hash and takeback would
            // rewind to "now" instead of "then."
            var target = FirstValidPlacementTarget(state);
            adapter.Apply(state, LedgeAction.PlaceToken(target, Tone.Light), out _);

            Assert.That(adapter.GetStateHash(snapshot), Is.EqualTo(snapshotHashBefore));
        }
    }
}
