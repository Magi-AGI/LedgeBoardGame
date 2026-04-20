using System;
using System.Collections.Generic;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Models.Network;
using Magi.LedgeBoardGame.Models.Spec;
using MagiGameServer.Contracts.Rules;

namespace Magi.LedgeBoardGame.ServerModule
{
    /// IGameModule entry point for LedgeBoardGame. Registered once at server
    /// startup under GameId "ledge-board". Session creation routes here to
    /// build the initial SpecGameState; per-seat action dispatch routes
    /// through the shared LedgeRulesAdapter (singleton, thread-safe, no
    /// per-session state).
    ///
    /// Seat counts 2–4 mirror the domain: LedgeBoardGame is designed for
    /// 2-4 players sharing a central board. Single-player is not a valid
    /// mode for this game. A UI that wants to let one human play against
    /// themselves for testing should open a session with SeatCount=2 and
    /// attach both seats from the same client — that's a session-shape
    /// concern, not a module-shape concern.
    ///
    /// Source lives under LedgeBoardGame/Assets so Unity's compiler picks
    /// it up through the Magi.LedgeBoardGame.Net asmdef; the pure-.NET
    /// LedgeBoardGame.ServerModule csproj compiles the same .cs files
    /// through its own path (EnableDefaultCompileItems=false + explicit
    /// Compile Include) so the server host and the Unity in-process driver
    /// share one source of truth. Same pattern LedgeBoardGame.Core uses.
    public sealed class LedgeGameModule : IGameModule
    {
        // Convention: pass the verbatim ledge spec JSON under this key in
        // GameConfig.Options to bind server adjudication to the same spec
        // the client loads locally. Omitting the key is legal and produces
        // an initial state with Config=null, which matches the legacy
        // no-spec local path (GameInitializer with no spec assigned).
        public const string SpecJsonOptionKey = "ledgeSpecJson";

        public LedgeGameModule()
        {
            // Both the server host (Program.CreateApp → registerModules →
            // new LedgeGameModule()) and the Unity client (single-seat
            // LedgeBoardSessionDriver constructs one for MinSeats/MaxSeats
            // probing) hit this ctor before any wire traffic, which is the
            // window the codec accepts converter registrations in.
            LedgeCodecInit.EnsureRegistered();
        }

        public string GameId => "ledge-board";
        public string DisplayName => "Ledge Board Game";
        public int MinSeats => 2;
        public int MaxSeats => 8;
        public IRulesAdapter Rules { get; } = new LedgeRulesAdapter();
        public Type ActionType => typeof(LedgeAction);
        public Type StateType => typeof(SpecGameState);

        public object CreateInitialState(GameConfig config)
        {
            int seatCount = config?.SeatCount > 0 ? config.SeatCount : MinSeats;
            if (seatCount < MinSeats || seatCount > MaxSeats)
                throw new ArgumentException($"LedgeBoardGame requires {MinSeats}-{MaxSeats} seats (got {seatCount}).");

            LedgeRuntimeConfig runtimeConfig = TryLoadRuntimeConfig(config);

            // Build players via GameState's constructor, which also builds the
            // canonical boards + cross-board ledge edges. ToSpecState then
            // projects down to the wire shape the server will persist and
            // echo. Seed is accepted here but unused at start-of-session;
            // LedgeBoardGame's initial state is fully deterministic from the
            // seat count. Future variants (scenario seeds, starting piece
            // placement) would consume Seed at this point.
            var players = Player.BuildDefaultRoster(seatCount);
            var gs = new GameState(players, runtimeConfig);
            var state = gs.ToSpecState();
            // Persist the runtime config on the wire so every RulesExecutor.TryApply
            // downstream rebuilds GameRules from the same spec, not its defaults.
            state.Config = runtimeConfig?.ToSpec();
            return state;
        }

        private static LedgeRuntimeConfig TryLoadRuntimeConfig(GameConfig config)
        {
            if (config?.Options == null) return null;
            if (!config.Options.TryGetValue(SpecJsonOptionKey, out var json)) return null;
            if (string.IsNullOrWhiteSpace(json)) return null;

            LedgeGameSpec spec;
            try
            {
                spec = LedgeGameSpecLoader.LoadFromJson(json);
            }
            catch (Exception ex)
            {
                // Newtonsoft throws JsonReaderException / JsonSerializationException
                // on malformed payloads. Surface as ArgumentException so the caller
                // sees a single expected failure type regardless of serializer choice.
                throw new ArgumentException(
                    $"LedgeBoardGame: '{SpecJsonOptionKey}' did not parse as a valid ledge spec.", ex);
            }
            if (spec == null)
                throw new ArgumentException(
                    $"LedgeBoardGame: '{SpecJsonOptionKey}' did not parse as a valid ledge spec.");

            LedgeSpecValidator.Validate(spec);
            return LedgeRuntimeConfig.FromSpec(spec);
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
