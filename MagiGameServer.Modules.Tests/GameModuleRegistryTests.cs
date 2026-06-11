using System;
using System.Collections.Generic;
using MagiGameServer.Contracts.Core;
using MagiGameServer.Contracts.Rules;
using NUnit.Framework;

namespace MagiGameServer.Modules.Tests
{
    [TestFixture]
    public class GameModuleRegistryTests
    {
        private sealed class FakeState { }
        private sealed class FakeAction { }

        private sealed class FakeRules : RulesAdapterBase<FakeState, FakeAction>
        {
            public override ApplyOutcome Apply(FakeState state, FakeAction action, out FakeState newState)
            { newState = state; return ApplyOutcome.Applied; }
            public override long GetStateHash(FakeState state) => 0;
            public override FakeState ProjectStateFor(FakeState state, SeatId seat) => state;
            public override FakeState SnapshotState(FakeState state) => state;
        }

        private sealed class FakeModule : IGameModule
        {
            public FakeModule(string id, Type actionType = null, Type stateType = null)
            {
                GameId = id;
                ActionType = actionType ?? typeof(FakeAction);
                StateType = stateType ?? typeof(FakeState);
            }
            public string GameId { get; }
            public string DisplayName => GameId + " (fake)";
            public int MinSeats => 1;
            public int MaxSeats => 2;
            public IRulesAdapter Rules { get; } = new FakeRules();
            public Type ActionType { get; }
            public Type StateType { get; }
            public object CreateInitialState(GameConfig config) => new FakeState();
            public object SetSeatPresence(object state, SeatId seat, bool isConnected) => state;
        }

        [Test]
        public void Register_ThenGet_RoundTrips()
        {
            var reg = new GameModuleRegistry();
            var module = new FakeModule("fake-a");
            reg.Register(module);

            Assert.That(reg.Get("fake-a"), Is.SameAs(module));
            Assert.That(reg.Count, Is.EqualTo(1));
        }

        [Test]
        public void TryGet_MissingId_ReturnsFalse()
        {
            var reg = new GameModuleRegistry();
            Assert.That(reg.TryGet("missing", out var m), Is.False);
            Assert.That(m, Is.Null);
        }

        [Test]
        public void Get_MissingId_ThrowsWithRegisteredList()
        {
            var reg = new GameModuleRegistry();
            reg.Register(new FakeModule("fake-a"));
            reg.Register(new FakeModule("fake-b"));

            var ex = Assert.Throws<KeyNotFoundException>(() => reg.Get("missing"));
            Assert.That(ex.Message, Does.Contain("fake-a"));
            Assert.That(ex.Message, Does.Contain("fake-b"));
        }

        [Test]
        public void Register_DuplicateGameId_Throws()
        {
            // Silent replace would mask startup wiring bugs — e.g. two
            // modules each claiming the same gameId — that only surface
            // when a client gets the wrong game shape. Fail loud.
            var reg = new GameModuleRegistry();
            reg.Register(new FakeModule("fake-a"));
            Assert.That(() => reg.Register(new FakeModule("fake-a")), Throws.InvalidOperationException);
        }

        [Test]
        public void Register_Null_Throws()
        {
            var reg = new GameModuleRegistry();
            Assert.That(() => reg.Register(null), Throws.ArgumentNullException);
        }

        [Test]
        public void Register_NullActionType_Throws()
        {
            // The codec fails fast on null ActionType because it can't
            // build ActionEnvelope<T> at the wire boundary without a
            // concrete T. Catching it at Register rather than first-use
            // means startup crashes loudly instead of the first client
            // request crashing mysteriously.
            var reg = new GameModuleRegistry();
            Assert.That(() => reg.Register(new NullActionTypeModule()), Throws.ArgumentException);
        }

        [Test]
        public void Register_NullStateType_Throws()
        {
            var reg = new GameModuleRegistry();
            Assert.That(() => reg.Register(new NullStateTypeModule()), Throws.ArgumentException);
        }

        [Test]
        public void Register_EmptyGameId_Throws()
        {
            var reg = new GameModuleRegistry();
            Assert.That(() => reg.Register(new FakeModule("")), Throws.ArgumentException);
        }

        [Test]
        public void RegisteredGameIds_ReturnsSnapshot()
        {
            var reg = new GameModuleRegistry();
            reg.Register(new FakeModule("a"));
            reg.Register(new FakeModule("b"));

            var ids = reg.RegisteredGameIds();
            Assert.That(ids, Is.EquivalentTo(new[] { "a", "b" }));
        }

        private sealed class NullActionTypeModule : IGameModule
        {
            public string GameId => "null-action";
            public string DisplayName => "null-action";
            public int MinSeats => 1;
            public int MaxSeats => 1;
            public IRulesAdapter Rules { get; } = new FakeRules();
            public Type ActionType => null;
            public Type StateType => typeof(FakeState);
            public object CreateInitialState(GameConfig config) => new FakeState();
            public object SetSeatPresence(object state, SeatId seat, bool isConnected) => state;
        }

        private sealed class NullStateTypeModule : IGameModule
        {
            public string GameId => "null-state";
            public string DisplayName => "null-state";
            public int MinSeats => 1;
            public int MaxSeats => 1;
            public IRulesAdapter Rules { get; } = new FakeRules();
            public Type ActionType => typeof(FakeAction);
            public Type StateType => null;
            public object CreateInitialState(GameConfig config) => new FakeState();
            public object SetSeatPresence(object state, SeatId seat, bool isConnected) => state;
        }
    }
}
