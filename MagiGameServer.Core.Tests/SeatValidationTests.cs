using MagiGameServer.Contracts.Core;
using MagiGameServer.Contracts.Protocol;
using NUnit.Framework;

namespace MagiGameServer.Core.Tests
{
    [TestFixture]
    public class SeatValidationTests
    {
        private Session NewSession(int seatCount)
        {
            var module = new CounterGameModule();
            return new Session(new SessionId("s"), module, new CounterState { Value = 0 }, seatCount);
        }

        [Test]
        public void Apply_SeatBelowZero_Throws()
        {
            var session = NewSession(seatCount: 2);
            var envelope = new ActionEnvelope<object>
            {
                Session = new SessionId("s"),
                Seat = new SeatId(-1),
                Seq = new ClientSeq(1),
                Action = new CounterAction { Delta = 1 },
                PredictedStateHash = 0,
            };

            Assert.That(() => session.Apply(envelope), Throws.ArgumentException);
        }

        [Test]
        public void Apply_SeatAtOrAboveSeatCount_Throws()
        {
            var session = NewSession(seatCount: 2);
            var envelope = new ActionEnvelope<object>
            {
                Session = new SessionId("s"),
                Seat = new SeatId(2),
                Seq = new ClientSeq(1),
                Action = new CounterAction { Delta = 1 },
                PredictedStateHash = 0,
            };

            Assert.That(() => session.Apply(envelope), Throws.ArgumentException);
        }

        [Test]
        public void Takeback_RequestingSeatOutOfRange_Throws()
        {
            var session = NewSession(seatCount: 2);

            var request = new TakebackRequest
            {
                Session = new SessionId("s"),
                RequestingSeat = new SeatId(5),
                SeqAtRequestTime = new ClientSeq(1),
                StepsRequested = 1,
                Reason = "test",
            };

            Assert.That(() => session.Takeback(request), Throws.ArgumentException);
        }
    }
}
