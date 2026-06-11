using System;
using System.Collections.Generic;
using System.Linq;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Models.Network;
using Magi.LedgeBoardGame.Models.Spec;
using MagiGameServer.Contracts.Core;
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
    public sealed class LedgeGameModule : IGameModule, IActionSubmissionValidator
    {
        // Convention: pass the verbatim ledge spec JSON under this key in
        // GameConfig.Options to bind server adjudication to the same spec
        // the client loads locally. Omitting the key is legal and produces
        // an initial state with Config=null, which matches the legacy
        // no-spec local path (GameInitializer with no spec assigned).
        public const string SpecJsonOptionKey = "ledgeSpecJson";

        // Pacing accelerator (U12): when set to N>0, every player's board
        // starts the session with N singleton Light + N singleton Dark
        // tokens pre-seeded onto random non-center spaces. Rules and turn
        // order are unchanged — players just begin from a partially-filled
        // position so demos reach the strategic mid-game faster.
        // Precedence: GameConfig.Options[SeedPlacementsPerToneKey] > env
        // var LEDGE_SEED_PLACEMENTS > 0 (no seeding). Ignored / clamped if
        // the requested count exceeds the non-center space capacity.
        public const string SeedPlacementsPerToneKey = "seedPlacementsPerTone";
        private const string SeedPlacementsEnvVar = "LEDGE_SEED_PLACEMENTS";

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
            // Server is always network mode: seats begin unclaimed. The first
            // WebSocket attach on a seat flips IsConnected true via an echoed
            // state mutation (wired in P5). Must match the client's Network-
            // mode initial-state construction exactly or the initial-state
            // hash diverges on the first shadow compare.
            var players = Player.BuildDefaultRoster(seatCount, initiallyConnected: false);
            var gs = new GameState(players, runtimeConfig);

            int seedPerTone = ResolveSeedPlacementsPerTone(config);
            if (seedPerTone > 0)
            {
                SeedInitialPlacements(gs, seedPerTone, config?.Seed ?? 0);
            }

            var state = gs.ToSpecState();
            // Persist the runtime config on the wire so every RulesExecutor.TryApply
            // downstream rebuilds GameRules from the same spec, not its defaults.
            state.Config = runtimeConfig?.ToSpec();
            return state;
        }

        private static int ResolveSeedPlacementsPerTone(GameConfig config)
        {
            if (config?.Options != null
                && config.Options.TryGetValue(SeedPlacementsPerToneKey, out var raw)
                && int.TryParse(raw, out var v) && v > 0)
            {
                return v;
            }
            var env = Environment.GetEnvironmentVariable(SeedPlacementsEnvVar);
            if (!string.IsNullOrWhiteSpace(env)
                && int.TryParse(env, out var envV) && envV > 0)
            {
                return envV;
            }
            return 0;
        }

        /// Places `perTone` singleton Light and `perTone` singleton Dark tokens
        /// on each player's board before play begins. Target spaces are drawn
        /// from the non-center pool (center is reserved — a Dark on your own
        /// center would trip SBE on turn 1). RNG is seeded deterministically
        /// from (config.Seed ^ boardId) so replays / reopens with the same
        /// seed reproduce the same opening board. Clamped silently if the
        /// requested count can't be satisfied by non-center space capacity.
        private static void SeedInitialPlacements(GameState gs, int perTone, long seed)
        {
            foreach (var player in gs.Players)
            {
                var board = gs.GetBoardForPlayer(player.Id);
                if (board == null) continue;

                // Build pool of non-center spaces — center is the default
                // Light-locked stack and Dark here would instant-eliminate.
                var pool = new List<int>();
                foreach (var key in board.Spaces.Keys)
                {
                    if (!board.IsCenterSpace(key)) pool.Add(key);
                }
                // Deterministic per-board RNG: mix seed with boardId so
                // players don't all get the same layout. The xor-with-hash
                // pattern keeps same-seed/same-boardId reproducible across
                // host restarts.
                var rng = new Random(unchecked((int)(seed ^ (board.BoardId * 397L))));
                // Fisher-Yates shuffle
                for (int i = pool.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (pool[i], pool[j]) = (pool[j], pool[i]);
                }

                int wanted = perTone * 2;
                int available = Math.Min(wanted, pool.Count);

                for (int i = 0; i < available; i++)
                {
                    // First `perTone` slots seed Light, next `perTone` slots
                    // seed Dark. Singleton stacks so each seed is independently
                    // movable and non-locked.
                    var tone = i < perTone ? Tone.Light : Tone.Dark;
                    var lightCount = tone == Tone.Light ? 1 : 0;
                    var darkCount = tone == Tone.Dark ? 1 : 0;
                    board.SetStack(pool[i], new TokenStack(lightCount, darkCount, tone));
                }
            }
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

        public object SetSeatPresence(object state, SeatId seat, bool isConnected)
        {
            if (!(state is SpecGameState spec) || spec.Players == null) return state;

            // Seat/Player mapping: BuildDefaultRoster assigns player.Id =
            // seatIndex + 1 (1-based) with BoardId = seatIndex (0-based).
            // The 1-based Id is the wire format — SpecPlayer.Id is a string
            // so matches go through int.Parse.
            int seatIndex = seat.Value;
            int playerId = seatIndex + 1;
            var specPlayer = spec.Players.FirstOrDefault(p =>
                int.TryParse(p.Id, out var pid) && pid == playerId);
            if (specPlayer == null) return state;
            if (specPlayer.IsConnected == isConnected) return state;

            // Deep-clone via the GameState round-trip (FromSpecState clones
            // boards + players, ToSpecState re-projects) so the Session's
            // takeback log holds a state reference that can't be aliased
            // by a later presence flip. Matches RulesExecutor.TryApply's
            // own Snapshot-safety pattern.
            var gs = GameState.FromSpecState(spec);
            var player = gs?.Players?.FirstOrDefault(p => p.Id == playerId);
            if (player == null) return state;
            player.IsConnected = isConnected;

            var next = gs.ToSpecState();
            // Config lives on SpecGameState but not on GameState — preserve
            // it across the round-trip so downstream RulesExecutor.TryApply
            // still sees the same runtime config the session opened with.
            next.Config = spec.Config;
            return next;
        }

        /// Pre-Apply check: SetDisplayName must target the submitting seat's
        /// own player slot — a client can only rename itself, not other
        /// seats. Without this guard an adversarial client could clobber
        /// another player's display name every turn. Place/Move/EndTurn
        /// carry no PlayerId and are already constrained by the
        /// CurrentTurnSeat gate inside RulesExecutor, so they don't need a
        /// validator pass here.
        ///
        /// Reason codes are snake_case per IActionSubmissionValidator's
        /// contract so clients can branch on them without parsing
        /// free-form messages.
        public string ValidateSubmission(SeatId submittingSeat, object action)
        {
            if (!(action is LedgeAction la)) return null;
            if (la.Kind != LedgeActionKind.SetDisplayName) return null;
            // BuildDefaultRoster seeds playerId = seatIndex + 1. Same
            // mapping SetSeatPresence uses above — keep the two paths in
            // lockstep so a future roster change updates both or neither.
            int expectedPlayerId = submittingSeat.Value + 1;
            if (la.PlayerId != expectedPlayerId)
            {
                return "display_name_not_owned";
            }
            return null;
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
