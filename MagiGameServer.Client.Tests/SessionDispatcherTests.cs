using System.Collections.Generic;
using MagiGameServer.Contracts.Core;
using MagiGameServer.Contracts.Protocol;
using NUnit.Framework;

namespace MagiGameServer.Client.Tests
{
    // The dispatcher is the single place where inbound envelope routing is
    // decided. Every test here pins down one row of the routing table
    // documented on ISessionObserver so future changes to the dispatcher
    // can't silently re-route events. Envelopes are crafted by hand — no
    // MagiGameServer.Core dependency, no in-process Session round-trip.

    [TestFixture]
    public class SessionDispatcherTests
    {
        private const string SessionName = "s";
        private static readonly SessionId Session = new SessionId(SessionName);
        private static readonly SeatId Own = new SeatId(0);
        private static readonly SeatId Remote = new SeatId(1);

        private sealed class TestState
        {
            public int Value { get; init; }
        }

        private sealed class TestAction
        {
            public int Delta { get; init; }
        }

        private sealed class Capture
        {
            public List<JoinSnapshot<TestState>> Joined { get; } = new();
            public List<StateEcho<TestState>> Advanced { get; } = new();
            public List<StateEcho<TestState>> Matched { get; } = new();
            public List<StateEcho<TestState>> Diverged { get; } = new();
            public List<TakebackBroadcast<TestState>> Broadcasts { get; } = new();
            public List<TakebackResponse> Replies { get; } = new();
            public List<ErrorEnvelope> Errors { get; } = new();
            public List<ActionEnvelope<TestAction>> OutgoingActions { get; } = new();
            public List<TakebackRequest> OutgoingTakebacks { get; } = new();
        }

        private static SessionDispatcher<TestState, TestAction> NewDispatcher(Capture cap = null)
        {
            var d = new SessionDispatcher<TestState, TestAction>(Session, Own);
            if (cap != null)
            {
                d.OnSessionJoined += e => cap.Joined.Add(e);
                d.OnStateAdvanced += e => cap.Advanced.Add(e);
                d.OnPredictionMatched += e => cap.Matched.Add(e);
                d.OnPredictionDiverged += e => cap.Diverged.Add(e);
                d.OnTakebackBroadcast += e => cap.Broadcasts.Add(e);
                d.OnTakebackReply += e => cap.Replies.Add(e);
                d.OnError += e => cap.Errors.Add(e);
                d.OutgoingAction += e => cap.OutgoingActions.Add(e);
                d.OutgoingTakeback += e => cap.OutgoingTakebacks.Add(e);
            }
            return d;
        }

        private static StateEcho<TestState> Echo(
            SeatId submittingSeat,
            long ackedSeq,
            long revision,
            int stateValue,
            long stateHash,
            ApplyOutcome outcome = ApplyOutcome.Applied)
            => new StateEcho<TestState>
            {
                Session = Session,
                ForSeat = Own,
                SubmittingSeat = submittingSeat,
                AckedSeq = new ClientSeq(ackedSeq),
                Revision = new ServerSeq(revision),
                State = new TestState { Value = stateValue },
                StateHash = stateHash,
                Outcome = outcome,
            };

        // --------------------------------------------------------------
        // Outbound side: Submit stamps + emits
        // --------------------------------------------------------------

        [Test]
        public void Submit_StampsClientSeq_AndEmitsOutgoingAction()
        {
            var cap = new Capture();
            var d = NewDispatcher(cap);

            d.Submit(new TestAction { Delta = 1 }, predictedStateHash: 42);
            d.Submit(new TestAction { Delta = 2 }, predictedStateHash: 43);

            Assert.That(cap.OutgoingActions.Count, Is.EqualTo(2));
            Assert.That(cap.OutgoingActions[0].Seq.Value, Is.EqualTo(1));
            Assert.That(cap.OutgoingActions[1].Seq.Value, Is.EqualTo(2));
            Assert.That(cap.OutgoingActions[0].Seat, Is.EqualTo(Own));
            Assert.That(cap.OutgoingActions[0].PredictedStateHash, Is.EqualTo(42));
            Assert.That(d.PendingCount, Is.EqualTo(2));
        }

        [Test]
        public void Submit_WithZeroPredictedHash_DoesNotTrackOptimisticEntry()
        {
            var cap = new Capture();
            var d = NewDispatcher(cap);

            d.Submit(new TestAction { Delta = 1 }, predictedStateHash: 0);

            Assert.That(cap.OutgoingActions.Count, Is.EqualTo(1),
                "envelope should still go out — transport needs to deliver it");
            Assert.That(d.PendingCount, Is.EqualTo(0),
                "non-predicting submit must not populate the optimistic stack, else the " +
                "first echo would fire OnPredictionMatched/Diverged instead of OnStateAdvanced");
        }

        [Test]
        public void SubmitTakeback_EmitsRequest()
        {
            var cap = new Capture();
            var d = NewDispatcher(cap);

            d.SubmitTakeback(stepsRequested: 2, reason: "misclick");

            Assert.That(cap.OutgoingTakebacks.Count, Is.EqualTo(1));
            Assert.That(cap.OutgoingTakebacks[0].RequestingSeat, Is.EqualTo(Own));
            Assert.That(cap.OutgoingTakebacks[0].StepsRequested, Is.EqualTo(2));
            Assert.That(cap.OutgoingTakebacks[0].Reason, Is.EqualTo("misclick"));
        }

        // --------------------------------------------------------------
        // Inbound routing: the seven paths ISessionObserver documents
        // --------------------------------------------------------------

        [Test]
        public void Ingest_RemoteAction_RoutesToOnStateAdvanced()
        {
            var cap = new Capture();
            var d = NewDispatcher(cap);

            d.Ingest(Echo(submittingSeat: Remote, ackedSeq: 7, revision: 1, stateValue: 10, stateHash: 99));

            Assert.That(cap.Advanced.Count, Is.EqualTo(1));
            Assert.That(cap.Matched, Is.Empty);
            Assert.That(cap.Diverged, Is.Empty);
            Assert.That(cap.Advanced[0].SubmittingSeat, Is.EqualTo(Remote));
        }

        [Test]
        public void Ingest_OwnEcho_WithMatchingHash_RoutesToOnPredictionMatched()
        {
            var cap = new Capture();
            var d = NewDispatcher(cap);

            d.Submit(new TestAction { Delta = 1 }, predictedStateHash: 42);
            d.Ingest(Echo(submittingSeat: Own, ackedSeq: 1, revision: 1, stateValue: 1, stateHash: 42));

            Assert.That(cap.Matched.Count, Is.EqualTo(1));
            Assert.That(cap.Advanced, Is.Empty);
            Assert.That(cap.Diverged, Is.Empty);
            Assert.That(d.PendingCount, Is.EqualTo(0),
                "matched echo must retire its optimistic stack entry");
        }

        [Test]
        public void Ingest_OwnEcho_HashMismatch_RoutesToOnPredictionDiverged()
        {
            var cap = new Capture();
            var d = NewDispatcher(cap);

            d.Submit(new TestAction { Delta = 1 }, predictedStateHash: 42);
            // Server applied but projected hash differs from what client predicted.
            d.Ingest(Echo(submittingSeat: Own, ackedSeq: 1, revision: 1, stateValue: 1, stateHash: 99));

            Assert.That(cap.Diverged.Count, Is.EqualTo(1));
            Assert.That(cap.Matched, Is.Empty);
            Assert.That(cap.Advanced, Is.Empty);
            Assert.That(d.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void Ingest_OwnEcho_Rejected_RoutesToOnPredictionDiverged()
        {
            var cap = new Capture();
            var d = NewDispatcher(cap);

            d.Submit(new TestAction { Delta = 1 }, predictedStateHash: 42);
            d.Ingest(Echo(submittingSeat: Own, ackedSeq: 1, revision: 0, stateValue: 0, stateHash: 42,
                outcome: ApplyOutcome.Rejected));

            Assert.That(cap.Diverged.Count, Is.EqualTo(1),
                "Rejected own-ack is a divergence regardless of hash — the client optimistically " +
                "applied something the server refused");
            Assert.That(cap.Matched, Is.Empty);
        }

        [Test]
        public void Ingest_OwnEcho_Desynced_RoutesToOnPredictionDiverged()
        {
            var cap = new Capture();
            var d = NewDispatcher(cap);

            d.Submit(new TestAction { Delta = 1 }, predictedStateHash: 42);
            d.Ingest(Echo(submittingSeat: Own, ackedSeq: 1, revision: 1, stateValue: 1, stateHash: 42,
                outcome: ApplyOutcome.Desynced));

            Assert.That(cap.Diverged.Count, Is.EqualTo(1));
            Assert.That(cap.Matched, Is.Empty);
        }

        [Test]
        public void Ingest_OwnEcho_NoMatchingPrediction_RoutesToOnStateAdvanced()
        {
            // Non-predicting path / reconnect catch-up: own submitting seat,
            // but no live optimistic entry — e.g. a bot that submitted with
            // PredictedStateHash=0, or a client catching up after a socket
            // reset dropped its optimistic stack.
            var cap = new Capture();
            var d = NewDispatcher(cap);

            d.Submit(new TestAction { Delta = 1 }, predictedStateHash: 0); // no stack entry
            d.Ingest(Echo(submittingSeat: Own, ackedSeq: 1, revision: 1, stateValue: 1, stateHash: 77));

            Assert.That(cap.Advanced.Count, Is.EqualTo(1));
            Assert.That(cap.Matched, Is.Empty);
            Assert.That(cap.Diverged, Is.Empty);
        }

        [Test]
        public void Ingest_JoinSnapshot_FiresOnSessionJoined()
        {
            var cap = new Capture();
            var d = NewDispatcher(cap);

            var snap = new JoinSnapshot<TestState>
            {
                Session = Session,
                ForSeat = Own,
                Revision = new ServerSeq(5),
                State = new TestState { Value = 99 },
                StateHash = 12345,
            };
            d.Ingest(snap);

            Assert.That(cap.Joined.Count, Is.EqualTo(1));
            Assert.That(cap.Joined[0].State.Value, Is.EqualTo(99));
            Assert.That(cap.Advanced, Is.Empty,
                "join snapshot is its own event — must not fall into the StateEcho routing path");
        }

        [Test]
        public void Ingest_TakebackBroadcast_Requester_BranchCutsPendingAfterRequest()
        {
            // Requester submitted seqs 1, 2, then SubmitTakeback (seq 3),
            // then speculatively submitted seq 4 before the broadcast
            // arrived. The broadcast's AckedRequestSeq=3 should drop seq 4
            // (post-request) but leave earlier entries — they were already
            // in the canonical timeline that got rewound.
            var cap = new Capture();
            var d = NewDispatcher(cap);

            d.Submit(new TestAction { Delta = 1 }, predictedStateHash: 11); // seq 1
            d.Submit(new TestAction { Delta = 2 }, predictedStateHash: 22); // seq 2
            d.SubmitTakeback(stepsRequested: 1, reason: "test");            // seq 3
            d.Submit(new TestAction { Delta = 4 }, predictedStateHash: 44); // seq 4

            Assume.That(d.PendingCount, Is.EqualTo(3));

            var broadcast = new TakebackBroadcast<TestState>
            {
                Session = Session,
                ForSeat = Own,
                RequestingSeat = Own,
                AckedRequestSeq = new ClientSeq(3),
                RevisionAfter = new ServerSeq(0),
                StepsRewound = 1,
                State = new TestState { Value = 0 },
                StateHash = 0,
            };
            d.Ingest(broadcast);

            Assert.That(cap.Broadcasts.Count, Is.EqualTo(1));
            Assert.That(d.PendingCount, Is.EqualTo(2),
                "post-request prediction (seq 4) must be dropped; pre-request (1,2) kept");
        }

        [Test]
        public void Ingest_TakebackBroadcast_NonRequester_ClearsAllPending()
        {
            // A remote player had a rewind granted. Our optimistic
            // predictions were built on top of a timeline that just moved
            // backward; all of them are stale.
            var cap = new Capture();
            var d = NewDispatcher(cap);

            d.Submit(new TestAction { Delta = 1 }, predictedStateHash: 11);
            d.Submit(new TestAction { Delta = 2 }, predictedStateHash: 22);
            Assume.That(d.PendingCount, Is.EqualTo(2));

            var broadcast = new TakebackBroadcast<TestState>
            {
                Session = Session,
                ForSeat = Own,
                RequestingSeat = Remote,
                AckedRequestSeq = new ClientSeq(999),
                RevisionAfter = new ServerSeq(0),
                StepsRewound = 2,
                State = new TestState { Value = 0 },
                StateHash = 0,
            };
            d.Ingest(broadcast);

            Assert.That(cap.Broadcasts.Count, Is.EqualTo(1));
            Assert.That(d.PendingCount, Is.EqualTo(0),
                "remote-requested takeback invalidates every local prediction");
        }

        [Test]
        public void Ingest_TakebackResponse_FiresOnReply_NotBroadcast()
        {
            var cap = new Capture();
            var d = NewDispatcher(cap);

            var denied = new TakebackResponse
            {
                Session = Session,
                RequestingSeat = Own,
                AckedRequestSeq = new ClientSeq(1),
                Outcome = TakebackOutcome.Denied,
                StepsGranted = 0,
                RevisionAfter = new ServerSeq(5),
                Message = "policy",
            };
            d.Ingest(denied);

            Assert.That(cap.Replies.Count, Is.EqualTo(1));
            Assert.That(cap.Broadcasts, Is.Empty,
                "TakebackResponse carries Denied/PendingConsent only — Granted arrives as TakebackBroadcast");
        }

        [Test]
        public void Ingest_Error_ClearsMatchingPendingEntry_AndFiresOnError()
        {
            var cap = new Capture();
            var d = NewDispatcher(cap);

            d.Submit(new TestAction { Delta = 1 }, predictedStateHash: 42); // seq 1
            d.Submit(new TestAction { Delta = 2 }, predictedStateHash: 43); // seq 2
            Assume.That(d.PendingCount, Is.EqualTo(2));

            var err = new ErrorEnvelope
            {
                Session = Session,
                AckedSeq = new ClientSeq(1),
                Code = "malformed",
                Message = "bad envelope",
            };
            d.Ingest(err);

            Assert.That(cap.Errors.Count, Is.EqualTo(1));
            Assert.That(d.PendingCount, Is.EqualTo(1),
                "the optimistic entry for the failed seq must be removed so the client doesn't " +
                "wait forever for an echo that will never come");
        }

        [Test]
        public void RoutingEvents_AreMutuallyExclusive_PerEcho()
        {
            // Walk every inbound echo shape and assert that exactly one of
            // OnStateAdvanced / OnPredictionMatched / OnPredictionDiverged
            // fires per echo. This is the invariant M5a transport relies on.
            var cap = new Capture();
            var d = NewDispatcher(cap);

            d.Submit(new TestAction { Delta = 1 }, predictedStateHash: 42); // own predicted, seq 1
            d.Ingest(Echo(Own, ackedSeq: 1, revision: 1, stateValue: 1, stateHash: 42)); // match

            d.Submit(new TestAction { Delta = 1 }, predictedStateHash: 50); // own predicted, seq 2
            d.Ingest(Echo(Own, ackedSeq: 2, revision: 2, stateValue: 2, stateHash: 99)); // diverge (hash)

            d.Ingest(Echo(Remote, ackedSeq: 7, revision: 3, stateValue: 3, stateHash: 10)); // remote advance

            d.Submit(new TestAction { Delta = 1 }, predictedStateHash: 0); // own non-predicted, seq 3
            d.Ingest(Echo(Own, ackedSeq: 3, revision: 4, stateValue: 4, stateHash: 20)); // advance

            Assert.That(cap.Matched.Count + cap.Diverged.Count + cap.Advanced.Count, Is.EqualTo(4),
                "each of the four echoes fired exactly one routing event");
            Assert.That(cap.Matched.Count, Is.EqualTo(1));
            Assert.That(cap.Diverged.Count, Is.EqualTo(1));
            Assert.That(cap.Advanced.Count, Is.EqualTo(2));
        }
    }
}
