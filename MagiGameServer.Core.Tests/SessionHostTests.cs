using System.Collections.Generic;
using MagiGameServer.Contracts.Core;
using MagiGameServer.Contracts.Rules;
using NUnit.Framework;

namespace MagiGameServer.Core.Tests
{
    [TestFixture]
    public class SessionHostTests
    {
        [Test]
        public void RegisterModule_ThenTryGet_ReturnsTheModule()
        {
            var host = new SessionHost();
            var module = new CounterGameModule();

            host.RegisterModule(module);

            Assert.That(host.TryGetModule("counter", out var resolved), Is.True);
            Assert.That(resolved, Is.SameAs(module));
        }

        [Test]
        public void RegisterModule_DuplicateGameId_Throws()
        {
            var host = new SessionHost();
            host.RegisterModule(new CounterGameModule());

            Assert.That(() => host.RegisterModule(new CounterGameModule()),
                Throws.InvalidOperationException);
        }

        [Test]
        public void CreateSession_UnknownGameId_Throws()
        {
            var host = new SessionHost();

            Assert.That(
                () => host.CreateSession("no-such", CounterGameModule.DefaultConfig()),
                Throws.InstanceOf<KeyNotFoundException>());
        }

        [Test]
        public void CreateSession_SeatCountBelowMin_Throws()
        {
            var host = new SessionHost();
            host.RegisterModule(new CounterGameModule());

            Assert.That(
                () => host.CreateSession("counter", CounterGameModule.DefaultConfig(seatCount: 0)),
                Throws.InstanceOf<System.ArgumentOutOfRangeException>());
        }

        [Test]
        public void CreateSession_SeatCountAboveMax_Throws()
        {
            var host = new SessionHost();
            host.RegisterModule(new CounterGameModule());

            Assert.That(
                () => host.CreateSession("counter", CounterGameModule.DefaultConfig(seatCount: 99)),
                Throws.InstanceOf<System.ArgumentOutOfRangeException>());
        }

        [Test]
        public void CreateSession_ReturnsFreshSessionWithDeterministicIdWhenFactoryInjected()
        {
            int counter = 0;
            var host = new SessionHost(() => new SessionId($"sess-{counter++}"));
            host.RegisterModule(new CounterGameModule());

            var s1 = host.CreateSession("counter", CounterGameModule.DefaultConfig());
            var s2 = host.CreateSession("counter", CounterGameModule.DefaultConfig());

            Assert.That(s1.Id.Value, Is.EqualTo("sess-0"));
            Assert.That(s2.Id.Value, Is.EqualTo("sess-1"));
            Assert.That(s1.CurrentRevision.Value, Is.EqualTo(0));
            Assert.That(s1.GameId, Is.EqualTo("counter"));
            Assert.That(s1.SeatCount, Is.EqualTo(2));
        }

        [Test]
        public void GetSession_AfterCreate_ReturnsSameInstance()
        {
            var host = new SessionHost();
            host.RegisterModule(new CounterGameModule());
            var created = host.CreateSession("counter", CounterGameModule.DefaultConfig());

            var resolved = host.GetSession(created.Id);

            Assert.That(resolved, Is.SameAs(created));
        }

        [Test]
        public void GetSession_UnknownId_ReturnsNull()
        {
            var host = new SessionHost();

            Assert.That(host.GetSession(new SessionId("nope")), Is.Null);
        }

        [Test]
        public void CloseSession_RemovesFromHost()
        {
            var host = new SessionHost();
            host.RegisterModule(new CounterGameModule());
            var session = host.CreateSession("counter", CounterGameModule.DefaultConfig());

            bool removed = host.CloseSession(session.Id);

            Assert.That(removed, Is.True);
            Assert.That(host.GetSession(session.Id), Is.Null);
        }

        [Test]
        public void CreateSession_WithOptions_PassesThroughToModule()
        {
            var host = new SessionHost();
            host.RegisterModule(new CounterGameModule());

            var options = new Dictionary<string, string> { { "start", "100" } };
            var session = host.CreateSession("counter", CounterGameModule.DefaultConfig(options: options));

            // Sanity: an Apply on the new session should advance from 100, not 0.
            var result = session.Apply(new MagiGameServer.Contracts.Protocol.ActionEnvelope<object>
            {
                Session = session.Id,
                Seat = new SeatId(0),
                Seq = new ClientSeq(1),
                Action = new CounterAction { Delta = 1 },
                PredictedStateHash = 0,
            });

            Assert.That(((CounterState)result.Echoes[0].State).Value, Is.EqualTo(101));
        }
    }
}
