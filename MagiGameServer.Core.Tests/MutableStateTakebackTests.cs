using System.Linq;
using MagiGameServer.Contracts.Core;
using MagiGameServer.Contracts.Protocol;
using NUnit.Framework;

namespace MagiGameServer.Core.Tests
{
    // These tests exist specifically to catch the snapshot-aliasing bug
    // Codex flagged on the M3 first draft. With a naive implementation
    // (raw reference stored in LogEntry.PostState), every test below
    // would fail because every log entry would point to the live _state
    // and would record the later-mutated List<int>.

    [TestFixture]
    public class MutableStateTakebackTests
    {
        private Session NewBagSession()
        {
            var module = new MutableBagGameModule();
            var state = new MutableBagState();
            return new Session(new SessionId("s"), module, state, seatCount: 1);
        }

        private ActionEnvelope<object> BagEnvelope(int item, long seq)
            => new ActionEnvelope<object>
            {
                Session = new SessionId("s"),
                Seat = new SeatId(0),
                Seq = new ClientSeq(seq),
                Action = new MutableBagAction { Item = item },
                PredictedStateHash = 0,
            };

        [Test]
        public void Takeback_MutableState_RewindsToExactPriorItems()
        {
            var session = NewBagSession();
            session.Apply(BagEnvelope(item: 10, seq: 1));
            session.Apply(BagEnvelope(item: 20, seq: 2));
            session.Apply(BagEnvelope(item: 30, seq: 3));

            var result = session.Takeback(new TakebackRequest
            {
                Session = new SessionId("s"),
                RequestingSeat = new SeatId(0),
                SeqAtRequestTime = new ClientSeq(100),
                StepsRequested = 2,
                Reason = "test",
            });

            Assert.That(result.Response.Outcome, Is.EqualTo(TakebackOutcome.Granted));
            Assert.That(result.Response.StepsGranted, Is.EqualTo(2));
            var restoredState = (MutableBagState)result.Echoes[0].State;
            Assert.That(restoredState.Items, Is.EqualTo(new[] { 10 }),
                "after rewinding two of three in-place mutations, only the first item must remain");
        }

        [Test]
        public void Takeback_MutableState_InitialStateIsRestoredCleanWhenAllRewound()
        {
            var session = NewBagSession();
            session.Apply(BagEnvelope(item: 1, seq: 1));
            session.Apply(BagEnvelope(item: 2, seq: 2));

            var result = session.Takeback(new TakebackRequest
            {
                Session = new SessionId("s"),
                RequestingSeat = new SeatId(0),
                SeqAtRequestTime = new ClientSeq(100),
                StepsRequested = 99,
                Reason = "test",
            });

            Assert.That(result.Response.StepsGranted, Is.EqualTo(2));
            var restoredState = (MutableBagState)result.Echoes[0].State;
            Assert.That(restoredState.Items, Is.Empty,
                "rewind past the first action must land on a clean initial state, not a mutated _initialState");
        }

        [Test]
        public void Takeback_MutableState_PostRewindApplyDoesNotCorruptHistoricalSnapshots()
        {
            var session = NewBagSession();
            session.Apply(BagEnvelope(item: 100, seq: 1));
            session.Apply(BagEnvelope(item: 200, seq: 2));

            // Rewind to just after the first action, then apply something new.
            session.Takeback(new TakebackRequest
            {
                Session = new SessionId("s"),
                RequestingSeat = new SeatId(0),
                SeqAtRequestTime = new ClientSeq(50),
                StepsRequested = 1,
                Reason = "test",
            });
            session.Apply(BagEnvelope(item: 300, seq: 3));

            // Now rewind again — we should land on [100], not [100, 300] or [].
            var result = session.Takeback(new TakebackRequest
            {
                Session = new SessionId("s"),
                RequestingSeat = new SeatId(0),
                SeqAtRequestTime = new ClientSeq(51),
                StepsRequested = 1,
                Reason = "test",
            });

            var restoredState = (MutableBagState)result.Echoes[0].State;
            Assert.That(restoredState.Items, Is.EqualTo(new[] { 100 }),
                "the post-first-action snapshot must have survived a subsequent in-place apply");
        }

        [Test]
        public void Takeback_MutableState_ExternalMutationOfCallerInitialStateDoesNotAffectSession()
        {
            // Callers may retain a reference to the initial state they
            // handed to Session. If Session doesn't snapshot on intake,
            // caller-side mutation bleeds into our canonical state.
            var module = new MutableBagGameModule();
            var callerOwnedState = new MutableBagState();
            var session = new Session(new SessionId("s"), module, callerOwnedState, seatCount: 1);

            callerOwnedState.Items.Add(999);

            var result = session.Apply(BagEnvelope(item: 1, seq: 1));
            var live = (MutableBagState)result.Echoes[0].State;
            Assert.That(live.Items, Is.EqualTo(new[] { 1 }),
                "Session must ignore post-construction mutation of the caller-owned initial state");
        }
    }
}
