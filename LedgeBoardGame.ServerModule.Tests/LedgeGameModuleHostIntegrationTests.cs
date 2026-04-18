using System.Linq;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Models.Network;
using Magi.LedgeBoardGame.Models.Spec;
using Magi.LedgeBoardGame.Rules;
using Magi.LedgeBoardGame.ServerModule;
using MagiGameServer.Contracts.Core;
using MagiGameServer.Contracts.Protocol;
using MagiGameServer.Core;
using NUnit.Framework;

namespace Magi.LedgeBoardGame.ServerModule.Tests
{
    /// End-to-end proof that LedgeGameModule plugs into the game-agnostic
    /// Session host without any LedgeBoardGame-specific infrastructure. A
    /// seat-1 action must produce a legal echo visible to seat 2 with the
    /// same post-apply hash — that's the full "action leaves trust boundary,
    /// reconciles on the other side" loop for a public-info game. Commit 2
    /// will layer InProcessMagiTransport on top of this same Session
    /// surface so the Unity client can exercise the loop without a running
    /// HTTP host.
    [TestFixture]
    public class LedgeGameModuleHostIntegrationTests
    {
        private static (Session session, LedgeGameModule module) NewHostedSession(int seatCount = 2)
        {
            var module = new LedgeGameModule();
            var initial = module.CreateInitialState(LedgeGameModule.DefaultConfig(seatCount));
            var session = new Session(new SessionId("ledge-test"), module, initial, seatCount);
            return (session, module);
        }

        private static SpaceId FirstValidPlacementFor(Session session)
        {
            var (projected, _, _) = session.ProjectForSeat(new SeatId(0));
            var spec = (SpecGameState)projected;
            var gs = GameState.FromSpecState(spec);
            var rules = new GameRules();
            var currentPlayer = gs.GetCurrentPlayer();
            return rules.GetValidPlacementTargets(gs, currentPlayer.Id).First();
        }

        [Test]
        public void Seat0_PlaceToken_EchoesLegalStateToBothSeats()
        {
            var (session, _) = NewHostedSession(seatCount: 2);
            var target = FirstValidPlacementFor(session);
            var envelope = new ActionEnvelope<object>
            {
                Session = session.Id,
                Seat = new SeatId(0),
                Seq = new ClientSeq(1),
                Action = LedgeAction.PlaceToken(target, Tone.Light),
                PredictedStateHash = 0,
            };

            var result = session.Apply(envelope);

            Assert.That(result.Outcome, Is.EqualTo(ApplyOutcome.Applied));
            Assert.That(result.Revision.Value, Is.EqualTo(1));
            Assert.That(result.Echoes, Has.Count.EqualTo(2), "both seats receive an echo");

            // Perfect-info game: both seats get identical state hashes back,
            // so the reconcile path on seat 1 sees the same canonical state
            // seat 0 just produced — no hidden-info projection divergence.
            var hashes = result.Echoes.Select(e => e.StateHash).Distinct().ToList();
            Assert.That(hashes, Has.Count.EqualTo(1),
                "identity projection → all seats hash the same canonical state");

            foreach (var echo in result.Echoes)
            {
                Assert.That(echo.Outcome, Is.EqualTo(ApplyOutcome.Applied));
                var echoedState = (SpecGameState)echo.State;
                Assert.That(echoedState.CurrentTurnPlacements, Has.Count.EqualTo(1),
                    "echoed state must reflect the placement");
                Assert.That(echoedState.CurrentTurnPlacements[0].Tone, Is.EqualTo(Tone.Light));
            }
        }

        [Test]
        public void Seat1_PlaceTokenDuringSeat0Turn_IsRejectedWithStateAtPrior()
        {
            var (session, _) = NewHostedSession(seatCount: 2);
            var target = FirstValidPlacementFor(session);
            // Seat 1 (player 2) tries to place while it's still seat 0's
            // turn. Even if the target is syntactically valid for player 2's
            // own board, CanPlaceToken gates on the current player — so the
            // adapter rejects. The Session layer still broadcasts the
            // rejection echo to both seats so seat 1's optimistic prediction
            // can roll back.
            var seat1Board = new SpaceId(boardId: 2, id: target.Id);
            var envelope = new ActionEnvelope<object>
            {
                Session = session.Id,
                Seat = new SeatId(1),
                Seq = new ClientSeq(1),
                Action = LedgeAction.PlaceToken(seat1Board, Tone.Light),
                PredictedStateHash = 0,
            };

            var result = session.Apply(envelope);

            Assert.That(result.Outcome, Is.EqualTo(ApplyOutcome.Rejected));
            Assert.That(result.Revision.Value, Is.EqualTo(0),
                "rejected action must not advance the revision");
            Assert.That(result.Echoes, Has.Count.EqualTo(2));
            foreach (var echo in result.Echoes)
            {
                Assert.That(echo.Outcome, Is.EqualTo(ApplyOutcome.Rejected));
                var echoedState = (SpecGameState)echo.State;
                Assert.That(echoedState.CurrentTurnPlacements, Is.Empty,
                    "rejected echo carries the pre-action (still-canonical) state");
            }
        }

        [Test]
        public void TwoRoundTrip_Place_Place_EndTurn_AdvancesSeatAndClearsTurnLog()
        {
            var (session, _) = NewHostedSession(seatCount: 2);

            // Seat 0 places Light, then Dark, then ends turn.
            var lightTarget = FirstValidPlacementFor(session);
            session.Apply(new ActionEnvelope<object>
            {
                Session = session.Id,
                Seat = new SeatId(0),
                Seq = new ClientSeq(1),
                Action = LedgeAction.PlaceToken(lightTarget, Tone.Light),
                PredictedStateHash = 0,
            });

            var darkTarget = FirstValidPlacementFor(session);
            session.Apply(new ActionEnvelope<object>
            {
                Session = session.Id,
                Seat = new SeatId(0),
                Seq = new ClientSeq(2),
                Action = LedgeAction.PlaceToken(darkTarget, Tone.Dark),
                PredictedStateHash = 0,
            });

            var endTurnResult = session.Apply(new ActionEnvelope<object>
            {
                Session = session.Id,
                Seat = new SeatId(0),
                Seq = new ClientSeq(3),
                Action = LedgeAction.EndTurn(),
                PredictedStateHash = 0,
            });

            Assert.That(endTurnResult.Outcome, Is.EqualTo(ApplyOutcome.Applied));
            var finalState = (SpecGameState)endTurnResult.Echoes[0].State;
            Assert.That(finalState.CurrentTurnPlacements, Is.Empty,
                "turn log must clear after EndTurn");
            Assert.That(finalState.CurrentTurnMoves, Is.Empty);
            Assert.That(finalState.Ctx.CurrentPlayer, Is.Not.EqualTo("1"),
                "EndTurn must advance the current-player pointer");
        }
    }
}
