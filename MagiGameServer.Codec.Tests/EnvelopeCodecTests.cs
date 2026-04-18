using System.Text.Json;
using MagiGameServer.Contracts.Core;
using MagiGameServer.Contracts.Protocol;
using NUnit.Framework;

namespace MagiGameServer.Codec.Tests
{
    // Round-trip every envelope + every strong-typed identifier through
    // EnvelopeCodec. Serialization bugs in a wire protocol are the kind
    // that silently corrupt a live session — a missing field on one side,
    // a miscased property on the other, an enum dropped as an int instead
    // of a name — so every envelope shape gets a dedicated test that
    // pins both the wire representation and the round-trip fidelity.

    [TestFixture]
    public class EnvelopeCodecTests
    {
        private sealed class DemoState
        {
            public int Value { get; init; }
            public string Label { get; init; }
        }

        private sealed class DemoAction
        {
            public int Delta { get; init; }
        }

        // -------------------------------------------------------------
        // Strong-typed identifier shapes on the wire
        // -------------------------------------------------------------

        [Test]
        public void SessionId_SerializesAsBareString()
        {
            var json = EnvelopeCodec.SerializeToString(new SessionId("match-42"));
            Assert.That(json, Is.EqualTo("\"match-42\""),
                "strong-typed IDs unwrap to their scalar on the wire — no { \"value\": ... } wrapping");

            var round = EnvelopeCodec.Deserialize<SessionId>(json);
            Assert.That(round, Is.EqualTo(new SessionId("match-42")));
        }

        [Test]
        public void SeatId_SerializesAsBareInt()
        {
            Assert.That(EnvelopeCodec.SerializeToString(new SeatId(3)), Is.EqualTo("3"));
            Assert.That(EnvelopeCodec.Deserialize<SeatId>("3"), Is.EqualTo(new SeatId(3)));
        }

        [Test]
        public void ClientSeq_SerializesAsBareLong()
        {
            Assert.That(EnvelopeCodec.SerializeToString(new ClientSeq(12345678901234L)),
                Is.EqualTo("12345678901234"));
            Assert.That(EnvelopeCodec.Deserialize<ClientSeq>("12345678901234"),
                Is.EqualTo(new ClientSeq(12345678901234L)));
        }

        [Test]
        public void ServerSeq_SerializesAsBareLong()
        {
            Assert.That(EnvelopeCodec.SerializeToString(new ServerSeq(7)), Is.EqualTo("7"));
            Assert.That(EnvelopeCodec.Deserialize<ServerSeq>("7"), Is.EqualTo(new ServerSeq(7)));
        }

        [Test]
        public void ApplyOutcome_SerializesAsCamelCaseString()
        {
            // Enums-as-ints are unreadable on the wire and fragile if the
            // enum is reordered. Pin the string form here so a future enum
            // reshuffle that accidentally bumps Applied from 0 to 1
            // doesn't silently flip every old log line's meaning.
            Assert.That(EnvelopeCodec.SerializeToString(ApplyOutcome.Applied), Is.EqualTo("\"applied\""));
            Assert.That(EnvelopeCodec.SerializeToString(ApplyOutcome.Rejected), Is.EqualTo("\"rejected\""));
            Assert.That(EnvelopeCodec.SerializeToString(ApplyOutcome.Desynced), Is.EqualTo("\"desynced\""));
            Assert.That(EnvelopeCodec.Deserialize<ApplyOutcome>("\"applied\""), Is.EqualTo(ApplyOutcome.Applied));
        }

        [Test]
        public void TakebackOutcome_SerializesAsCamelCaseString()
        {
            Assert.That(EnvelopeCodec.SerializeToString(TakebackOutcome.Granted), Is.EqualTo("\"granted\""));
            Assert.That(EnvelopeCodec.SerializeToString(TakebackOutcome.Denied), Is.EqualTo("\"denied\""));
            Assert.That(EnvelopeCodec.SerializeToString(TakebackOutcome.PendingConsent),
                Is.EqualTo("\"pendingConsent\""));
            Assert.That(EnvelopeCodec.Deserialize<TakebackOutcome>("\"granted\""),
                Is.EqualTo(TakebackOutcome.Granted));
        }

        [Test]
        public void ApplyOutcome_IntegerForm_RejectedOnDeserialize()
        {
            // allowIntegerValues:false on the converter — if someone
            // serializes an outcome as an integer (old code, wrong codec),
            // decoding should fail loudly instead of silently mapping to
            // whatever enum value happens to live at that index.
            Assert.That(() => EnvelopeCodec.Deserialize<ApplyOutcome>("0"),
                Throws.InstanceOf<JsonException>());
        }

        // -------------------------------------------------------------
        // Envelope round-trips
        // -------------------------------------------------------------

        [Test]
        public void ActionEnvelope_Typed_RoundTrips()
        {
            var env = new ActionEnvelope<DemoAction>
            {
                Session = new SessionId("s"),
                Seat = new SeatId(1),
                Seq = new ClientSeq(42),
                Action = new DemoAction { Delta = 7 },
                PredictedStateHash = 0x1234_5678_9ABCDEF0L,
            };

            var json = EnvelopeCodec.SerializeToString(env);
            Assert.That(json, Does.Contain("\"session\":\"s\""));
            Assert.That(json, Does.Contain("\"seat\":1"));
            Assert.That(json, Does.Contain("\"seq\":42"));
            Assert.That(json, Does.Contain("\"predictedStateHash\":1311768467463790320"));

            var round = EnvelopeCodec.Deserialize<ActionEnvelope<DemoAction>>(json);
            Assert.That(round.Session, Is.EqualTo(env.Session));
            Assert.That(round.Seat, Is.EqualTo(env.Seat));
            Assert.That(round.Seq, Is.EqualTo(env.Seq));
            Assert.That(round.Action.Delta, Is.EqualTo(7));
            Assert.That(round.PredictedStateHash, Is.EqualTo(env.PredictedStateHash));
        }

        [Test]
        public void StateEcho_Typed_RoundTrips()
        {
            var echo = new StateEcho<DemoState>
            {
                Session = new SessionId("s"),
                ForSeat = new SeatId(0),
                SubmittingSeat = new SeatId(1),
                AckedSeq = new ClientSeq(3),
                Revision = new ServerSeq(10),
                State = new DemoState { Value = 99, Label = "live" },
                StateHash = 0xDEADBEEFL,
                Outcome = ApplyOutcome.Applied,
            };

            var json = EnvelopeCodec.SerializeToString(echo);
            Assert.That(json, Does.Contain("\"outcome\":\"applied\""));

            var round = EnvelopeCodec.Deserialize<StateEcho<DemoState>>(json);
            Assert.That(round.Session, Is.EqualTo(echo.Session));
            Assert.That(round.ForSeat, Is.EqualTo(echo.ForSeat));
            Assert.That(round.SubmittingSeat, Is.EqualTo(echo.SubmittingSeat));
            Assert.That(round.AckedSeq, Is.EqualTo(echo.AckedSeq));
            Assert.That(round.Revision, Is.EqualTo(echo.Revision));
            Assert.That(round.State.Value, Is.EqualTo(99));
            Assert.That(round.State.Label, Is.EqualTo("live"));
            Assert.That(round.StateHash, Is.EqualTo(echo.StateHash));
            Assert.That(round.Outcome, Is.EqualTo(ApplyOutcome.Applied));
        }

        [Test]
        public void JoinSnapshot_Typed_RoundTrips()
        {
            var snap = new JoinSnapshot<DemoState>
            {
                Session = new SessionId("s"),
                ForSeat = new SeatId(0),
                Revision = new ServerSeq(5),
                State = new DemoState { Value = 1, Label = "seed" },
                StateHash = 42,
            };

            var round = EnvelopeCodec.Deserialize<JoinSnapshot<DemoState>>(
                EnvelopeCodec.SerializeToString(snap));
            Assert.That(round.Session, Is.EqualTo(snap.Session));
            Assert.That(round.ForSeat, Is.EqualTo(snap.ForSeat));
            Assert.That(round.Revision, Is.EqualTo(snap.Revision));
            Assert.That(round.State.Value, Is.EqualTo(1));
            Assert.That(round.State.Label, Is.EqualTo("seed"));
            Assert.That(round.StateHash, Is.EqualTo(42));
        }

        [Test]
        public void TakebackRequest_RoundTrips()
        {
            var req = new TakebackRequest
            {
                Session = new SessionId("s"),
                RequestingSeat = new SeatId(1),
                SeqAtRequestTime = new ClientSeq(9),
                StepsRequested = 2,
                Reason = "misclick",
            };

            var round = EnvelopeCodec.Deserialize<TakebackRequest>(EnvelopeCodec.SerializeToString(req));
            Assert.That(round.Session, Is.EqualTo(req.Session));
            Assert.That(round.RequestingSeat, Is.EqualTo(req.RequestingSeat));
            Assert.That(round.SeqAtRequestTime, Is.EqualTo(req.SeqAtRequestTime));
            Assert.That(round.StepsRequested, Is.EqualTo(2));
            Assert.That(round.Reason, Is.EqualTo("misclick"));
        }

        [Test]
        public void TakebackResponse_RoundTrips()
        {
            var resp = new TakebackResponse
            {
                Session = new SessionId("s"),
                RequestingSeat = new SeatId(0),
                AckedRequestSeq = new ClientSeq(11),
                Outcome = TakebackOutcome.Denied,
                StepsGranted = 0,
                RevisionAfter = new ServerSeq(5),
                Message = "policy: opponent must consent",
            };

            var json = EnvelopeCodec.SerializeToString(resp);
            Assert.That(json, Does.Contain("\"outcome\":\"denied\""));

            var round = EnvelopeCodec.Deserialize<TakebackResponse>(json);
            Assert.That(round.Outcome, Is.EqualTo(TakebackOutcome.Denied));
            Assert.That(round.Message, Is.EqualTo(resp.Message));
            Assert.That(round.RevisionAfter, Is.EqualTo(resp.RevisionAfter));
        }

        [Test]
        public void TakebackBroadcast_Typed_RoundTrips()
        {
            var bc = new TakebackBroadcast<DemoState>
            {
                Session = new SessionId("s"),
                ForSeat = new SeatId(1),
                RequestingSeat = new SeatId(0),
                AckedRequestSeq = new ClientSeq(11),
                RevisionAfter = new ServerSeq(4),
                StepsRewound = 2,
                State = new DemoState { Value = 0, Label = "pre-take" },
                StateHash = 77,
            };

            var round = EnvelopeCodec.Deserialize<TakebackBroadcast<DemoState>>(
                EnvelopeCodec.SerializeToString(bc));
            Assert.That(round.ForSeat, Is.EqualTo(bc.ForSeat));
            Assert.That(round.RequestingSeat, Is.EqualTo(bc.RequestingSeat));
            Assert.That(round.AckedRequestSeq, Is.EqualTo(bc.AckedRequestSeq));
            Assert.That(round.RevisionAfter, Is.EqualTo(bc.RevisionAfter));
            Assert.That(round.StepsRewound, Is.EqualTo(2));
            Assert.That(round.State.Label, Is.EqualTo("pre-take"));
        }

        [Test]
        public void ErrorEnvelope_RoundTrips()
        {
            var err = new ErrorEnvelope
            {
                Session = new SessionId("s"),
                AckedSeq = new ClientSeq(4),
                Code = "malformed",
                Message = "payload failed validation",
            };

            var round = EnvelopeCodec.Deserialize<ErrorEnvelope>(EnvelopeCodec.SerializeToString(err));
            Assert.That(round.Session, Is.EqualTo(err.Session));
            Assert.That(round.AckedSeq, Is.EqualTo(err.AckedSeq));
            Assert.That(round.Code, Is.EqualTo("malformed"));
            Assert.That(round.Message, Is.EqualTo("payload failed validation"));
        }

        // -------------------------------------------------------------
        // Runtime-typed action envelope path (server-side entry point)
        // -------------------------------------------------------------

        [Test]
        public void DeserializeActionEnvelope_ByType_ReturnsActionEnvelopeOfObject()
        {
            // This is the exact path the server host uses: it receives a
            // JSON blob with an unknown-at-compile-time TAction, resolves
            // the session's module to a Type, and hands that Type to the
            // codec. The return type MUST be ActionEnvelope<object> —
            // the shape Session.Apply consumes — because C# generic
            // types are invariant: ActionEnvelope<DemoAction> would not
            // be assignment-compatible, forcing the host into reflection
            // glue. Action is boxed as its concrete runtime type so the
            // non-generic IRulesAdapter can cast it back downstream.
            var env = new ActionEnvelope<DemoAction>
            {
                Session = new SessionId("s"),
                Seat = new SeatId(0),
                Seq = new ClientSeq(1),
                Action = new DemoAction { Delta = 5 },
                PredictedStateHash = 42,
            };
            var json = EnvelopeCodec.SerializeToString(env);

            // The very fact that this compiles is part of the test —
            // declaring the receiver as ActionEnvelope<object> proves
            // the seam is assignment-compatible with Session.Apply.
            ActionEnvelope<object> forCore = EnvelopeCodec.DeserializeActionEnvelope(json, typeof(DemoAction));

            Assert.That(forCore.Session, Is.EqualTo(env.Session));
            Assert.That(forCore.Seat, Is.EqualTo(env.Seat));
            Assert.That(forCore.Seq, Is.EqualTo(env.Seq));
            Assert.That(forCore.PredictedStateHash, Is.EqualTo(42));
            Assert.That(forCore.Action, Is.InstanceOf<DemoAction>(),
                "Action must be the concrete runtime type so the non-generic " +
                "IRulesAdapter can cast it downstream");
            Assert.That(((DemoAction)forCore.Action).Delta, Is.EqualTo(5));
        }

        [Test]
        public void DeserializeActionEnvelope_NullActionJson_ReturnsNullAction()
        {
            // Edge case: a malformed client might send "action":null. The
            // server-side codec shouldn't crash — it should pass null
            // through so Session.Apply can reject it via its own
            // null-action path.
            var json = "{\"session\":\"s\",\"seat\":0,\"seq\":1,\"action\":null,\"predictedStateHash\":0}";
            var forCore = EnvelopeCodec.DeserializeActionEnvelope(json, typeof(DemoAction));
            Assert.That(forCore.Action, Is.Null);
        }

        [Test]
        public void DeserializeActionEnvelope_NullType_Throws()
        {
            Assert.That(() => EnvelopeCodec.DeserializeActionEnvelope("{}", null),
                Throws.ArgumentNullException);
        }

        [Test]
        public void Options_Singleton_ReturnsSameInstance()
        {
            // Shared, mutated-nowhere options is a correctness guarantee,
            // not a perf optimisation: if a caller could mutate a local
            // copy, server and client could diverge on casing and the
            // wire would silently miscompile. Assert the static never
            // returns a fresh instance.
            Assert.That(EnvelopeCodec.Options, Is.SameAs(EnvelopeCodec.Options));
        }
    }
}
