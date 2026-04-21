using System;
using System.Linq;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Models.Network;
using Magi.LedgeBoardGame.Models.Spec;

namespace Magi.LedgeBoardGame.Rules
{
    /// Pure accept-state / return-state bridge over the existing mutating
    /// GameRules surface. GameRules and GameState.EndTurn mutate the passed
    /// instance in place — fine for the local Unity path, but IRulesAdapter.Apply
    /// needs a function that leaves the input untouched and returns a fresh
    /// state value. RulesExecutor is the single place that closes that gap.
    ///
    /// Strategy: FromSpecState rebuilds a fresh GameState (cloning boards /
    /// players / turn logs), the in-place GameRules / EndTurn mutates that
    /// clone, and ToSpecState projects back to the wire shape. The caller's
    /// SpecGameState reference is never touched. That means every Apply pays
    /// a full state-shape round-trip; that is the price of reusing the
    /// mutating local rules unchanged, and it is the right price for M6 —
    /// cost is linear in board size, board size is tiny, and we keep a single
    /// source of truth for rule behaviour.
    ///
    /// Outcome return mirrors RulesAdapter semantics: true when the action
    /// produced a legal state transition, false when the committed action
    /// was rejected (e.g. placing a Light after already placing a Light this
    /// turn). Rejected actions return the input SpecGameState unchanged so
    /// callers can hash identity and the dispatcher's rejection path fires.
    public static class RulesExecutor
    {
        public static bool TryApply(SpecGameState state, LedgeAction action, out SpecGameState newState)
        {
            if (state == null || action == null)
            {
                newState = state;
                return false;
            }

            var gs = GameState.FromSpecState(state);
            // Honour the runtime config carried on the wire. Server mode loads it
            // from the spec JSON in GameConfig.Options; local mode today leaves
            // Config null, which keeps GameRules on its built-in defaults (same as
            // the legacy `new GameRules()` call). Threading config here is what
            // guarantees server adjudication can't drift from the local spec-driven
            // rules (placement/movement max-moves in particular).
            var runtimeConfig = LedgeRuntimeConfig.FromSpec(state.Config);
            var rules = new GameRules(runtimeConfig);

            bool applied;
            switch (action.Kind)
            {
                case LedgeActionKind.PlaceToken:
                    applied = ApplyPlace(rules, gs, action);
                    break;
                case LedgeActionKind.MoveToken:
                    applied = ApplyMove(rules, gs, action);
                    break;
                case LedgeActionKind.EndTurn:
                    applied = ApplyEndTurn(gs);
                    break;
                case LedgeActionKind.SetDisplayName:
                    applied = ApplySetDisplayName(gs, action);
                    break;
                default:
                    applied = false;
                    break;
            }

            if (!applied)
            {
                newState = state;
                return false;
            }

            newState = gs.ToSpecState();
            // Config is not stored on GameState, so ToSpecState can't re-emit it.
            // Reattach here so every echo downstream still carries the authoritative
            // config — new seats joining mid-session see the same rules the session
            // was created with.
            newState.Config = state.Config;
            return true;
        }

        private static bool ApplyPlace(GameRules rules, GameState gs, LedgeAction action)
        {
            // GameRules.PlaceToken guards internally via CanPlaceToken, returning
            // null on rejection. That's the authoritative signal — don't re-check.
            var placement = rules.PlaceToken(gs, action.To, action.Tone);
            return placement != null;
        }

        private static bool ApplyMove(GameRules rules, GameState gs, LedgeAction action)
        {
            var move = rules.MoveToken(gs, action.From, action.To, action.Tone);
            if (move == null) return false;
            // Mid-turn eliminations: a cross-board move can drop a second Dark
            // counter onto a victim's center, dark-locking it. The local Unity
            // path runs this pass from OnMoveTweenComplete; the server-auth path
            // has to do it here so the echo carries the IsEliminated flip and
            // any resulting last-player-standing win. ApplyOverflowCap is NOT
            // run here — the 3-stack trim is an end-of-turn rule only.
            gs.ApplyStateBasedEffects();
            return true;
        }

        private static bool ApplySetDisplayName(GameState gs, LedgeAction action)
        {
            // Phase- and turn-independent: a player may rename themselves at
            // any point. Empty / whitespace names are rejected so the roster
            // can't be clobbered back to blanks; length cap (32) keeps a
            // malformed client from ballooning the echoed state. Both
            // constraints are cheap and echo identically through the hash,
            // so client and server agree on what "too long" looks like.
            if (action.PlayerId <= 0) return false;
            var name = action.DisplayName;
            if (string.IsNullOrWhiteSpace(name)) return false;
            name = name.Trim();
            if (name.Length > 32) name = name.Substring(0, 32);
            var target = gs.Players.FirstOrDefault(p => p.Id == action.PlayerId);
            if (target == null) return false;
            // Name comparison is ordinal: if the incoming trimmed name
            // matches, nothing moved — echo as rejected so no pointless
            // state advance fires.
            if (string.Equals(target.Name, name, StringComparison.Ordinal)) return false;
            target.Name = name;
            return true;
        }

        private static bool ApplyEndTurn(GameState gs)
        {
            if (gs.GameOver) return false;
            // Mirror the local client's EndTurn gate (GameController.OnEndTurnClicked):
            // during Placement, both tones must be placed before the turn can end.
            // Server-auth needs this check in the rules path itself — without it, an
            // adversarial client could bypass the placement budget by skipping the
            // button and sending a raw EndTurn action.
            if (gs.CurrentPhase == GamePhase.Placement && !gs.IsPlacementComplete())
                return false;
            gs.EndTurn();
            return true;
        }
    }
}
