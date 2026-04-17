using System.Linq;
using MagiGameServer.Contracts.Core;
using MagiGameServer.Contracts.Protocol;
using NUnit.Framework;

namespace MagiGameServer.Core.Tests
{
    [TestFixture]
    public class SessionTests
    {
        private Session NewSession(int seatCount = 2, int initialValue = 0)
        {
            var module = new CounterGameModule();
            var state = new CounterState { Value = initialValue };
            return new Session(new SessionId("s"), module, state, seatCount);
        }

        private ActionEnvelope<object> Envelope(int delta, long predictedHash = 0, int seat = 0, long seq = 1)
            => new ActionEnvelope<object>
            {
                Session = new SessionId("s"),
                Seat = new SeatId(seat),
                Seq = new ClientSeq(seq),
                Action = new CounterAction { Delta = delta },
                PredictedStateHash = predictedHash,
            };

        [Test]
        public void Apply_ValidAction_AdvancesStateAndRevision()
        {
            var session = NewSession();

            var result = session.Apply(Envelope(delta: 5));

            Assert.That(result.Outcome, Is.EqualTo(ApplyOutcome.Applied));
            Assert.That(result.Revision.Value, Is.EqualTo(1));
            Assert.That(session.CurrentRevision.Value, Is.EqualTo(1));
            Assert.That(result.Echoes, Has.Count.EqualTo(2));
            foreach (var echo in result.Echoes)
            {
                Assert.That(echo.Outcome, Is.EqualTo(ApplyOutcome.Applied));
                Assert.That(echo.Revision.Value, Is.EqualTo(1));
                Assert.That(((CounterState)echo.State).Value, Is.EqualTo(5));
            }
        }

        [Test]
        public void Apply_RejectedAction_LeavesStateAndRevisionAtPrior()
        {
            var session = NewSession();
            session.Apply(Envelope(delta: 3, seq: 1));

            var result = session.Apply(Envelope(delta: -1, seq: 2));

            Assert.That(result.Outcome, Is.EqualTo(ApplyOutcome.Rejected));
            Assert.That(result.Revision.Value, Is.EqualTo(1), "revision must not advance on reject");
            Assert.That(session.CurrentRevision.Value, Is.EqualTo(1));
            foreach (var echo in result.Echoes)
            {
                Assert.That(echo.Outcome, Is.EqualTo(ApplyOutcome.Rejected));
                Assert.That(((CounterState)echo.State).Value, Is.EqualTo(3),
                    "rejected echoes must carry the pre-action (still-canonical) state");
            }
        }

        [Test]
        public void Apply_DesyncDetected_DowngradesToDesyncedButStateStillAdvances()
        {
            var session = NewSession();

            // Client predicts the wrong hash (should be 5 after delta=5 from 0).
            var result = session.Apply(Envelope(delta: 5, predictedHash: 999_999));

            Assert.That(result.Outcome, Is.EqualTo(ApplyOutcome.Desynced));
            Assert.That(result.Revision.Value, Is.EqualTo(1),
                "Desynced is a canonical advance with a mismatch flag, not a reject");
            Assert.That(((CounterState)result.Echoes.First().State).Value, Is.EqualTo(5));
        }

        [Test]
        public void Apply_PredictedHashZero_SkipsDesyncCheck()
        {
            var session = NewSession();

            var result = session.Apply(Envelope(delta: 7, predictedHash: 0));

            Assert.That(result.Outcome, Is.EqualTo(ApplyOutcome.Applied));
        }

        [Test]
        public void Apply_CorrectPredictedHash_NoDowngrade()
        {
            var session = NewSession();

            // CounterRules.GetStateHash returns state.Value; projection is identity.
            var result = session.Apply(Envelope(delta: 4, predictedHash: 4));

            Assert.That(result.Outcome, Is.EqualTo(ApplyOutcome.Applied));
        }

        [Test]
        public void Apply_EchoForSubmittingSeat_CarriesAckedSeqAndSubmittingSeat()
        {
            var session = NewSession();

            var result = session.Apply(Envelope(delta: 1, seat: 1, seq: 42));

            var submitterEcho = result.Echoes.Single(e => e.ForSeat.Value == 1);
            Assert.That(submitterEcho.SubmittingSeat.Value, Is.EqualTo(1));
            Assert.That(submitterEcho.AckedSeq.Value, Is.EqualTo(42));
        }

        [Test]
        public void Apply_EchoForOtherSeats_StillCarriesOriginatingSubmittingSeat()
        {
            var session = NewSession(seatCount: 3);

            var result = session.Apply(Envelope(delta: 1, seat: 2, seq: 99));

            foreach (var echo in result.Echoes)
            {
                Assert.That(echo.SubmittingSeat.Value, Is.EqualTo(2),
                    "every recipient must see the same SubmittingSeat so broadcasts resolve identity");
            }
        }

        [Test]
        public void Apply_EnvelopeForDifferentSession_Throws()
        {
            var session = NewSession();

            var badEnvelope = new ActionEnvelope<object>
            {
                Session = new SessionId("wrong"),
                Seat = new SeatId(0),
                Seq = new ClientSeq(1),
                Action = new CounterAction { Delta = 1 },
                PredictedStateHash = 0,
            };

            Assert.That(() => session.Apply(badEnvelope), Throws.ArgumentException);
        }

        [Test]
        public void Apply_MultipleActions_RevisionIsMonotonic()
        {
            var session = NewSession();

            session.Apply(Envelope(delta: 1, seq: 1));
            session.Apply(Envelope(delta: 2, seq: 2));
            var last = session.Apply(Envelope(delta: 3, seq: 3));

            Assert.That(last.Revision.Value, Is.EqualTo(3));
            Assert.That(((CounterState)last.Echoes.First().State).Value, Is.EqualTo(6));
        }
    }
}
