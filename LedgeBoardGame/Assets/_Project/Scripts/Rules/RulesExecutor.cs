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
            var rules = new GameRules();

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
            return move != null;
        }

        private static bool ApplyEndTurn(GameState gs)
        {
            // EndTurn is unconditional in GameState today — no explicit guard
            // against calling it in the wrong phase. M6 mirrors current local
            // behaviour. A future rules pass could add a phase check here;
            // for now the client gates the "End Turn" button on phase, so
            // malformed EndTurn actions are a test/adversarial concern only.
            if (gs.GameOver) return false;
            gs.EndTurn();
            return true;
        }
    }
}
