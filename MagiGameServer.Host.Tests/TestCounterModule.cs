using System;
using System.Collections.Generic;
using MagiGameServer.Contracts.Core;
using MagiGameServer.Contracts.Rules;

namespace MagiGameServer.Host.Tests
{
    // Minimal perfect-info module for host-level tests. Duplicated (not
    // referenced) from Core.Tests to keep Host.Tests independent of
    // another test assembly — referencing a second test project would
    // drag its NUnit registrations into this runner and double-execute
    // its tests.

    public sealed class CounterState
    {
        public int Value { get; init; }
        public CounterState With(int value) => new() { Value = value };
    }

    public sealed class CounterAction
    {
        public int Delta { get; init; }
    }

    public sealed class CounterRules : RulesAdapterBase<CounterState, CounterAction>
    {
        public override ApplyOutcome Apply(CounterState state, CounterAction action, out CounterState newState)
        {
            if (action.Delta <= 0)
            {
                newState = state;
                return ApplyOutcome.Rejected;
            }
            newState = state.With(state.Value + action.Delta);
            return ApplyOutcome.Applied;
        }

        public override long GetStateHash(CounterState state) => state.Value;
        public override CounterState ProjectStateFor(CounterState state, SeatId seat) => state;
        public override CounterState SnapshotState(CounterState state) => state;
    }

    public sealed class TestCounterModule : IGameModule
    {
        public string GameId => "counter";
        public string DisplayName => "Counter (host test)";
        public int MinSeats => 1;
        public int MaxSeats => 4;
        public IRulesAdapter Rules { get; } = new CounterRules();
        public Type ActionType => typeof(CounterAction);
        public Type StateType => typeof(CounterState);

        public object CreateInitialState(GameConfig config)
        {
            int start = 0;
            if (config?.Options != null && config.Options.TryGetValue("start", out var s) && int.TryParse(s, out var parsed))
            {
                start = parsed;
            }
            return new CounterState { Value = start };
        }
    }
}
