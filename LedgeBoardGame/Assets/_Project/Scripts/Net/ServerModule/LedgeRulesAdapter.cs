using System.IO;
using System.Security.Cryptography;
using System.Text;
using Magi.LedgeBoardGame.Models.Network;
using Magi.LedgeBoardGame.Models.Spec;
using Magi.LedgeBoardGame.Rules;
using MagiGameServer.Contracts.Core;
using MagiGameServer.Contracts.Rules;
using Newtonsoft.Json;

namespace Magi.LedgeBoardGame.ServerModule
{
    /// Rules adapter for LedgeBoardGame. Bridges the per-game mutating
    /// GameRules surface (lives in LedgeBoardGame.Core) into the pure
    /// accept-state / return-state contract MagiGameServer needs.
    ///
    /// TState is SpecGameState — the wire-serializable projection that
    /// round-trips cleanly through JSON and carries the mid-turn action log
    /// the server needs for takeback rewinds. Not GameState, because GameState
    /// embeds CrossBoardLedgeEdges that are deterministically derivable from
    /// Players/Boards and would bloat the wire without adding server-side
    /// value. RulesExecutor reconstitutes the full GameState inside Apply,
    /// runs the mutation, and projects back.
    ///
    /// TAction is LedgeAction — the tagged-union wire action type shared with
    /// the Unity client. Committed actions only (Place/Move/EndTurn). No UI
    /// intents, no hover/select state.
    ///
    /// Source lives under LedgeBoardGame/Assets so both the Unity in-process
    /// driver (Magi.LedgeBoardGame.Net asmdef) and the pure-.NET server host
    /// (LedgeBoardGame.ServerModule csproj) compile from the same .cs file.
    public sealed class LedgeRulesAdapter : RulesAdapterBase<SpecGameState, LedgeAction>
    {
        // Newtonsoft-based hash because the local path (Unity) already
        // depends on Newtonsoft for spec load/save and so the hash computed
        // here is automatically consistent with whatever a Unity-side
        // predict-state-hash path produces if it chooses to hash the same
        // SpecGameState. Determinism mandate: no wall-clock, no unseeded
        // PRNG, no hash-order-iteration — Newtonsoft with default settings
        // serializes property-declaration order for classes, which is
        // stable across runs.
        private static readonly JsonSerializerSettings HashSerializerSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Include,
            DefaultValueHandling = DefaultValueHandling.Include,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        };

        public override ApplyOutcome Apply(SpecGameState state, LedgeAction action, out SpecGameState newState)
        {
            if (!RulesExecutor.TryApply(state, action, out var applied))
            {
                newState = state;
                return ApplyOutcome.Rejected;
            }

            newState = applied;
            return ApplyOutcome.Applied;
        }

        public override long GetStateHash(SpecGameState state)
        {
            if (state == null) return 0L;

            // SHA-256 over the canonical JSON form, truncated to long. Much
            // stronger than GetHashCode and, more importantly, stable across
            // process restarts — GetHashCode on strings is randomized per-
            // process in .NET Core+ and would break the server/client hash
            // comparison the moment either side recycled.
            var json = JsonConvert.SerializeObject(state, HashSerializerSettings);
            var bytes = Encoding.UTF8.GetBytes(json);
            using var sha = SHA256.Create();
            var digest = sha.ComputeHash(bytes);
            // Take the first 8 bytes as a big-endian long. Sign doesn't matter
            // for equality comparison; using unchecked reinterpretation.
            long hash = 0;
            for (int i = 0; i < 8; i++)
            {
                hash = (hash << 8) | digest[i];
            }
            return hash;
        }

        /// LedgeBoardGame is a perfect-information game: every seat sees the
        /// same canonical state. ProjectStateFor is therefore the identity
        /// function. The method exists on the interface precisely because
        /// sibling games (LedgeTCG hides opponent hands, LedgeRPG hides
        /// unexplored tiles) will return a per-seat projection here; for
        /// LedgeBoardGame this is the first live proof that the projection
        /// hook runs end-to-end on a public-state game, not the hidden-
        /// information proof itself.
        public override SpecGameState ProjectStateFor(SpecGameState state, SeatId seat) => state;

        /// RulesExecutor already returns a fresh SpecGameState per Apply
        /// (FromSpecState clones boards/players, ToSpecState clones boards
        /// again on the way out), so the adapter's Apply never mutates its
        /// input. Snapshot can therefore be an identity passthrough — the
        /// server's takeback log holds references that can safely outlive
        /// subsequent Apply calls. If any future mutation path skips the
        /// FromSpecState/ToSpecState round-trip, this method must switch to
        /// a deep clone (FromSpecState(state).ToSpecState() is the obvious
        /// recipe) or takeback rewinds will silently "restore" live state.
        public override SpecGameState SnapshotState(SpecGameState state) => state;
    }
}
