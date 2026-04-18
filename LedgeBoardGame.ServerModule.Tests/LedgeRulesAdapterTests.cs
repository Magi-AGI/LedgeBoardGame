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

            // Complete the placement budget (one Light + one Dark) before EndTurn —
            // the server-auth EndTurn gate rejects EndTurn during Placement phase
            // until IsPlacementComplete() is true, matching the local client.
            var lightTarget = FirstValidPlacementTarget(state);
            adapter.Apply(state, LedgeAction.PlaceToken(lightTarget, Tone.Light), out var afterLight);
            var darkTarget = FirstValidPlacementTarget(afterLight);
            adapter.Apply(afterLight, LedgeAction.PlaceToken(darkTarget, Tone.Dark), out var afterDark);

            var outcome = adapter.Apply(afterDark, LedgeAction.EndTurn(), out var newState);

            Assert.That(outcome, Is.EqualTo(ApplyOutcome.Applied));
            Assert.That(int.Parse(newState.Ctx.CurrentPlayer), Is.Not.EqualTo(startingPlayer));
            Assert.That(newState.CurrentTurnPlacements, Is.Empty);
            Assert.That(newState.CurrentTurnMoves, Is.Empty);
        }

        [Test]
        public void Apply_EndTurnDuringPlacement_WithoutBothTones_IsRejected()
        {
            // Codex reviewer blocker: the local path at GameController.OnEndTurnClicked
            // refuses EndTurn while CurrentPhase==Placement && !IsPlacementComplete().
            // Server adjudication must enforce the same gate, otherwise an adversarial
            // client can skip the per-turn placement budget by emitting EndTurn
            // directly. This covers the no-placements-yet case and the one-tone-only
            // case — both must reject.
            var adapter = new LedgeRulesAdapter();

            var fresh = NewInitialState(seatCount: 2);
            var freshOutcome = adapter.Apply(fresh, LedgeAction.EndTurn(), out var afterFresh);
            Assert.That(freshOutcome, Is.EqualTo(ApplyOutcome.Rejected),
                "EndTurn with zero placements must be rejected");
            Assert.That(afterFresh, Is.SameAs(fresh),
                "rejected EndTurn must return the input reference unchanged");

            var oneTone = NewInitialState(seatCount: 2);
            var target = FirstValidPlacementTarget(oneTone);
            adapter.Apply(oneTone, LedgeAction.PlaceToken(target, Tone.Light), out var afterLight);
            var partialOutcome = adapter.Apply(afterLight, LedgeAction.EndTurn(), out var afterPartial);
            Assert.That(partialOutcome, Is.EqualTo(ApplyOutcome.Rejected),
                "EndTurn with only Light placed must be rejected");
            Assert.That(afterPartial, Is.SameAs(afterLight));
        }

        [Test]
        public void CreateInitialState_WithSpecJsonOption_ThreadsConfigOntoState()
        {
            // Codex reviewer blocker: GameConfig.Options must shape server adjudication
            // or a spec-driven client and a default-ruled server will silently diverge.
            // Convention under test: the ledge spec JSON rides on Options under the
            // SpecJsonOptionKey; CreateInitialState parses it, validates, and pins
            // the runtime config onto SpecGameState so every subsequent Apply reuses it.
            var module = new LedgeGameModule();
            var options = new System.Collections.Generic.Dictionary<string, string>
            {
                [LedgeGameModule.SpecJsonOptionKey] = LoadCanonicalSpecJson(),
            };
            var state = (SpecGameState)module.CreateInitialState(
                LedgeGameModule.DefaultConfig(2, options));

            Assert.That(state.Config, Is.Not.Null, "spec-driven config must attach to initial state");
            Assert.That(state.Config.MinPlayers, Is.EqualTo(2));
            Assert.That(state.Config.MaxPlayers, Is.EqualTo(4));
            Assert.That(state.Config.PlacementMaxMoves, Is.GreaterThan(0),
                "spec-driven placement budget must survive CreateInitialState");

            // And survives an Apply — RulesExecutor re-emits Config so every echo
            // reaches downstream seats with the authoritative rules bound to the state.
            var adapter = new LedgeRulesAdapter();
            var target = FirstValidPlacementTarget(state);
            adapter.Apply(state, LedgeAction.PlaceToken(target, Tone.Light), out var afterPlace);
            Assert.That(afterPlace.Config, Is.Not.Null, "Apply must preserve Config on round-trip");
            Assert.That(afterPlace.Config.PlacementMaxMoves, Is.EqualTo(state.Config.PlacementMaxMoves));
            Assert.That(afterPlace.Config.MovementMaxMoves, Is.EqualTo(state.Config.MovementMaxMoves));
        }

        [Test]
        public void CreateInitialState_WithInvalidSpecJsonOption_Throws()
        {
            var module = new LedgeGameModule();
            var options = new System.Collections.Generic.Dictionary<string, string>
            {
                [LedgeGameModule.SpecJsonOptionKey] = "not-json",
            };
            Assert.Throws<System.ArgumentException>(
                () => module.CreateInitialState(LedgeGameModule.DefaultConfig(2, options)),
                "garbage in the spec option must fail fast rather than silently fall back to defaults");
        }

        internal static string LoadCanonicalSpecJson()
        {
            // The csproj copies ledge-game.v1.json to <TestOutput>/Specs/ — loading
            // the canonical file (rather than hand-authored minimal JSON) keeps the
            // test honest against LedgeSpecValidator, which enforces full board
            // structure, not just phase config.
            var baseDir = System.AppContext.BaseDirectory;
            var path = System.IO.Path.Combine(baseDir, "Specs", "ledge-game.v1.json");
            if (!System.IO.File.Exists(path))
            {
                Assert.Fail($"Spec fixture not copied to test output: {path}");
            }
            return System.IO.File.ReadAllText(path);
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
