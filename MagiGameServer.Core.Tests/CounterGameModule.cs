using System.Collections.Generic;
using MagiGameServer.Contracts.Core;
using MagiGameServer.Contracts.Rules;

namespace MagiGameServer.Core.Tests
{
    // Minimal game module for core-layer tests. Perfect-info, deterministic,
    // no game-specific dependencies. State is a single counter; actions are
    // integer deltas; rules reject non-positive deltas so we can exercise
    // both Applied and Rejected paths.

    public sealed class CounterState
    {
        public int Value { get; init; }

        public CounterState With(int value) => new CounterState { Value = value };
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
    }

    public sealed class CounterGameModule : IGameModule
    {
        public string GameId => "counter";
        public string DisplayName => "Counter (test)";
        public int MinSeats => 1;
        public int MaxSeats => 4;
        public IRulesAdapter Rules { get; } = new CounterRules();

        public object CreateInitialState(GameConfig config)
        {
            int start = 0;
            if (config?.Options != null && config.Options.TryGetValue("start", out var s) && int.TryParse(s, out var parsed))
            {
                start = parsed;
            }
            return new CounterState { Value = start };
        }

        public static GameConfig DefaultConfig(int seatCount = 2, IReadOnlyDictionary<string, string> options = null)
            => new GameConfig
            {
                Seed = 0,
                SeatCount = seatCount,
                Options = options ?? new Dictionary<string, string>(),
            };
    }
}
