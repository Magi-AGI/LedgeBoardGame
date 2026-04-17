using System.Linq;
using MagiGameServer.Contracts.Core;
using MagiGameServer.Contracts.Protocol;
using NUnit.Framework;

namespace MagiGameServer.Core.Tests
{
    [TestFixture]
    public class TakebackTests
    {
        private Session NewSessionWithHistory(params int[] deltas)
        {
            var module = new CounterGameModule();
            var session = new Session(
                new SessionId("s"),
                module,
                new CounterState { Value = 0 },
                seatCount: 2);

            for (int i = 0; i < deltas.Length; i++)
            {
                session.Apply(new ActionEnvelope<object>
                {
                    Session = new SessionId("s"),
                    Seat = new SeatId(0),
                    Seq = new ClientSeq(i + 1),
                    Action = new CounterAction { Delta = deltas[i] },
                    PredictedStateHash = 0,
                });
            }
            return session;
        }

        private TakebackRequest Request(int steps, int seat = 0, long seq = 100)
            => new TakebackRequest
            {
                Session = new SessionId("s"),
                RequestingSeat = new SeatId(seat),
                SeqAtRequestTime = new ClientSeq(seq),
                StepsRequested = steps,
                Reason = "test",
            };

        [Test]
        public void Takeback_EmptyLog_Denied()
        {
            var session = NewSessionWithHistory();

            var result = session.Takeback(Request(1));

            Assert.That(result.Response.Outcome, Is.EqualTo(TakebackOutcome.Denied));
            Assert.That(result.Response.StepsGranted, Is.EqualTo(0));
            Assert.That(result.Echoes, Is.Empty);
        }

        [Test]
        public void Takeback_ZeroSteps_Denied()
        {
            var session = NewSessionWithHistory(1, 2, 3);

            var result = session.Takeback(Request(0));

            Assert.That(result.Response.Outcome, Is.EqualTo(TakebackOutcome.Denied));
            Assert.That(session.CurrentRevision.Value, Is.EqualTo(3));
        }

        [Test]
        public void Takeback_SingleStep_RewindsOneActionAndBroadcasts()
        {
            var session = NewSessionWithHistory(5, 10);

            var result = session.Takeback(Request(1));

            Assert.That(result.Response.Outcome, Is.EqualTo(TakebackOutcome.Granted));
            Assert.That(result.Response.StepsGranted, Is.EqualTo(1));
            Assert.That(result.Response.RevisionAfter.Value, Is.EqualTo(1));
            Assert.That(session.CurrentRevision.Value, Is.EqualTo(1));

            Assert.That(result.Echoes, Has.Count.EqualTo(2));
            foreach (var echo in result.Echoes)
            {
                Assert.That(((CounterState)echo.State).Value, Is.EqualTo(5),
                    "post-rewind echoes must carry the pre-rewind-action state");
                Assert.That(echo.Revision.Value, Is.EqualTo(1));
            }
        }

        [Test]
        public void Takeback_AllSteps_RestoresInitialState()
        {
            var session = NewSessionWithHistory(1, 2, 3);

            var result = session.Takeback(Request(3));

            Assert.That(result.Response.Outcome, Is.EqualTo(TakebackOutcome.Granted));
            Assert.That(result.Response.StepsGranted, Is.EqualTo(3));
            Assert.That(result.Response.RevisionAfter.Value, Is.EqualTo(0));
            Assert.That(session.CurrentRevision.Value, Is.EqualTo(0));
            Assert.That(((CounterState)result.Echoes.First().State).Value, Is.EqualTo(0));
        }

        [Test]
        public void Takeback_MoreStepsThanAvailable_ClampsToHistoryDepth()
        {
            var session = NewSessionWithHistory(4, 5);

            var result = session.Takeback(Request(99));

            Assert.That(result.Response.Outcome, Is.EqualTo(TakebackOutcome.Granted));
            Assert.That(result.Response.StepsGranted, Is.EqualTo(2));
            Assert.That(session.CurrentRevision.Value, Is.EqualTo(0));
        }

        [Test]
        public void Takeback_ThenReapply_AdvancesFromRewoundRevision()
        {
            var session = NewSessionWithHistory(10, 20);
            session.Takeback(Request(1));

            var result = session.Apply(new ActionEnvelope<object>
            {
                Session = new SessionId("s"),
                Seat = new SeatId(0),
                Seq = new ClientSeq(50),
                Action = new CounterAction { Delta = 7 },
                PredictedStateHash = 0,
            });

            Assert.That(result.Outcome, Is.EqualTo(ApplyOutcome.Applied));
            Assert.That(result.Revision.Value, Is.EqualTo(2),
                "re-apply after rewind advances from the rewound revision, not the original tip");
            Assert.That(((CounterState)result.Echoes.First().State).Value, Is.EqualTo(17));
        }
    }
}
