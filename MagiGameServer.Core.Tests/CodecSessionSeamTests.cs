using MagiGameServer.Codec;
using MagiGameServer.Contracts.Core;
using MagiGameServer.Contracts.Protocol;
using NUnit.Framework;

namespace MagiGameServer.Core.Tests
{
    // End-to-end seam proof: JSON wire bytes → EnvelopeCodec.DeserializeActionEnvelope
    // → Session.Apply. This is the exact path the ASP.NET host will take in M4b.
    // Pins the shape Codex flagged: the codec's runtime-typed output must be
    // assignable to ActionEnvelope<object> without casts or reflection glue.
    [TestFixture]
    public class CodecSessionSeamTests
    {
        private static Session NewSession()
        {
            var module = new CounterGameModule();
            var state = new CounterState { Value = 0 };
            return new Session(new SessionId("s"), module, state, seatCount: 2);
        }

        [Test]
        public void Codec_To_SessionApply_AdvancesState()
        {
            var session = NewSession();
            var module = new CounterGameModule();

            // Typed envelope on the "client" side.
            var outgoing = new ActionEnvelope<CounterAction>
            {
                Session = new SessionId("s"),
                Seat = new SeatId(0),
                Seq = new ClientSeq(1),
                Action = new CounterAction { Delta = 7 },
                PredictedStateHash = 0,
            };

            string json = EnvelopeCodec.SerializeToString(outgoing);

            // Server side: only module.ActionType is known, not the concrete type.
            ActionEnvelope<object> incoming = EnvelopeCodec.DeserializeActionEnvelope(json, module.ActionType);

            var result = session.Apply(incoming);

            Assert.That(result.Outcome, Is.EqualTo(ApplyOutcome.Applied));
            Assert.That(result.Revision.Value, Is.EqualTo(1));
            Assert.That(((CounterState)result.Echoes[0].State).Value, Is.EqualTo(7));
        }

        [Test]
        public void Codec_To_SessionApply_PreservesPredictedHash()
        {
            var session = NewSession();
            var module = new CounterGameModule();

            // A Delta of 3 from value 0 makes CounterState.Value=3 and
            // CounterRules.GetStateHash returns Value — so predicting 3 matches
            // and predicting 99 desyncs. Both survive the JSON round-trip.
            var outgoing = new ActionEnvelope<CounterAction>
            {
                Session = new SessionId("s"),
                Seat = new SeatId(0),
                Seq = new ClientSeq(1),
                Action = new CounterAction { Delta = 3 },
                PredictedStateHash = 99,
            };

            string json = EnvelopeCodec.SerializeToString(outgoing);
            ActionEnvelope<object> incoming = EnvelopeCodec.DeserializeActionEnvelope(json, module.ActionType);

            Assert.That(incoming.PredictedStateHash, Is.EqualTo(99));
            var result = session.Apply(incoming);
            Assert.That(result.Outcome, Is.EqualTo(ApplyOutcome.Desynced),
                "Predicted 99 must not match server-computed hash 3 after JSON round-trip");
        }

        [Test]
        public void Codec_To_SessionApply_RejectsInvalidAction()
        {
            var session = NewSession();
            var module = new CounterGameModule();

            var outgoing = new ActionEnvelope<CounterAction>
            {
                Session = new SessionId("s"),
                Seat = new SeatId(0),
                Seq = new ClientSeq(1),
                Action = new CounterAction { Delta = -5 },
                PredictedStateHash = 0,
            };

            string json = EnvelopeCodec.SerializeToString(outgoing);
            ActionEnvelope<object> incoming = EnvelopeCodec.DeserializeActionEnvelope(json, module.ActionType);

            var result = session.Apply(incoming);

            Assert.That(result.Outcome, Is.EqualTo(ApplyOutcome.Rejected));
            Assert.That(session.CurrentRevision.Value, Is.EqualTo(0));
        }
    }
}
