using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Models.Network;
using Magi.LedgeBoardGame.ServerModule;
using MagiGameServer.Codec;
using NUnit.Framework;

namespace Magi.LedgeBoardGame.ServerModule.Tests
{
    /// Regression coverage for the SpaceId round-trip bug: STJ 8 ignores the
    /// parameterised ctor on a readonly-prop struct because the implicit
    /// parameterless struct ctor is also present, so deserializing
    /// {"boardId":1,"id":5} yields SpaceId(0,0). LedgeCodecInit plugs a
    /// converter into EnvelopeCodec at module ctor time to fix this; these
    /// tests assert the converter is live and the action envelope round-trips
    /// through the same options the WebSocket path uses.
    [TestFixture]
    public class LedgeActionCodecRoundTripTests
    {
        [SetUp]
        public void Setup()
        {
            // Instantiating the module runs LedgeCodecInit.EnsureRegistered()
            // which adds SpaceIdJsonConverter to EnvelopeCodec.Options.
            _ = new LedgeGameModule();
        }

        [Test]
        public void SpaceId_RoundTripsThroughEnvelopeCodec()
        {
            var original = new SpaceId(3, 17);
            string json = EnvelopeCodec.SerializeToString(original);
            var decoded = EnvelopeCodec.Deserialize<SpaceId>(json);
            Assert.That(decoded.BoardId, Is.EqualTo(original.BoardId));
            Assert.That(decoded.Id, Is.EqualTo(original.Id));
        }

        [Test]
        public void LedgeAction_Place_RoundTripsTarget()
        {
            var action = LedgeAction.PlaceToken(new SpaceId(2, 9), Tone.Dark);
            string json = EnvelopeCodec.SerializeToString(action);
            var decoded = EnvelopeCodec.Deserialize<LedgeAction>(json);
            Assert.That(decoded.Kind, Is.EqualTo(LedgeActionKind.PlaceToken));
            Assert.That(decoded.To.BoardId, Is.EqualTo(2));
            Assert.That(decoded.To.Id, Is.EqualTo(9));
            Assert.That(decoded.Tone, Is.EqualTo(Tone.Dark));
        }

        [Test]
        public void LedgeAction_Move_RoundTripsBothSpaces()
        {
            var action = LedgeAction.MoveToken(new SpaceId(1, 4), new SpaceId(2, 11), Tone.Light);
            string json = EnvelopeCodec.SerializeToString(action);
            var decoded = EnvelopeCodec.Deserialize<LedgeAction>(json);
            Assert.That(decoded.Kind, Is.EqualTo(LedgeActionKind.MoveToken));
            Assert.That(decoded.From.BoardId, Is.EqualTo(1));
            Assert.That(decoded.From.Id, Is.EqualTo(4));
            Assert.That(decoded.To.BoardId, Is.EqualTo(2));
            Assert.That(decoded.To.Id, Is.EqualTo(11));
        }

        [Test]
        public void LedgeAction_SetDisplayName_RoundTripsPayload()
        {
            var action = LedgeAction.SetDisplayName(4, "Anna");
            string json = EnvelopeCodec.SerializeToString(action);
            var decoded = EnvelopeCodec.Deserialize<LedgeAction>(json);
            Assert.That(decoded.Kind, Is.EqualTo(LedgeActionKind.SetDisplayName));
            Assert.That(decoded.PlayerId, Is.EqualTo(4));
            Assert.That(decoded.DisplayName, Is.EqualTo("Anna"));
        }
    }
}
