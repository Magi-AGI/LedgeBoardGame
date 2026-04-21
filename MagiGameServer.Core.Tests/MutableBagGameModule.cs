using System;
using System.Collections.Generic;
using System.Linq;
using MagiGameServer.Contracts.Core;
using MagiGameServer.Contracts.Rules;

namespace MagiGameServer.Core.Tests
{
    // Stress-test adapter for takeback snapshot correctness. Unlike
    // CounterState, MutableBagState is mutated in place — Apply does
    // `state.Items.Add(...)` and returns the same reference. Without proper
    // snapshotting in core, every log entry would alias the live state and
    // rewind would become a no-op. If SnapshotState is a passthrough here,
    // the takeback tests below should fail — which is the whole point.

    public sealed class MutableBagState
    {
        public List<int> Items { get; } = new List<int>();

        public MutableBagState Clone()
        {
            var copy = new MutableBagState();
            copy.Items.AddRange(Items);
            return copy;
        }
    }

    public sealed class MutableBagAction
    {
        public int Item { get; init; }
    }

    public sealed class MutableBagRules : RulesAdapterBase<MutableBagState, MutableBagAction>
    {
        public override ApplyOutcome Apply(MutableBagState state, MutableBagAction action, out MutableBagState newState)
        {
            // Deliberately in-place mutation — this is the shape Codex flagged
            // and the shape LedgeBoardGame's existing adapter is closest to.
            state.Items.Add(action.Item);
            newState = state;
            return ApplyOutcome.Applied;
        }

        public override long GetStateHash(MutableBagState state)
        {
            unchecked
            {
                long h = 17;
                foreach (var v in state.Items) h = h * 31 + v;
                return h;
            }
        }

        public override MutableBagState ProjectStateFor(MutableBagState state, SeatId seat) => state;

        // Mutable-state adapter MUST deep-clone. This is the whole contract.
        public override MutableBagState SnapshotState(MutableBagState state) => state.Clone();
    }

    public sealed class MutableBagGameModule : IGameModule
    {
        public string GameId => "mutable-bag";
        public string DisplayName => "Mutable Bag (test)";
        public int MinSeats => 1;
        public int MaxSeats => 2;
        public IRulesAdapter Rules { get; } = new MutableBagRules();
        public Type ActionType => typeof(MutableBagAction);
        public Type StateType => typeof(MutableBagState);

        public object CreateInitialState(GameConfig config) => new MutableBagState();

        public object SetSeatPresence(object state, SeatId seat, bool isConnected) => state;

        public static GameConfig DefaultConfig(int seatCount = 1)
            => new GameConfig
            {
                Seed = 0,
                SeatCount = seatCount,
                Options = new Dictionary<string, string>(),
            };
    }
}
