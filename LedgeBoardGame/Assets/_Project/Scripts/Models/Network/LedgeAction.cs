namespace Magi.LedgeBoardGame.Models.Network
{
    /// Wire-level committed action. Tagged-union rather than a polymorphic class
    /// hierarchy so the type stays plain for System.Text.Json and does not
    /// require attribute-based discriminator wiring — keeping this file free of
    /// STJ attributes means the Unity gameplay asmdef does not need a
    /// System.Text.Json reference just to compile it. The handful of unused
    /// fields per action type (e.g. From* on a PlaceToken) cost a few bytes on
    /// the wire and nothing in code clarity once callers route through the
    /// factory statics below.
    ///
    /// Three committed actions map to the three GameRules/GameState mutations
    /// the controller performs today:
    ///   PlaceToken — mid-turn; gates on current-phase + per-turn placement budget.
    ///   MoveToken  — mid-turn; gates on current-phase + per-turn movement budget.
    ///   EndTurn    — turn-ending; triggers state-based effects + next-player advance.
    ///
    /// UI-only intents (hover, select, drag-preview, undo-stack navigation) are
    /// deliberately NOT on the wire. Only commits that would mutate GameState
    /// under local play are shipped through the server.
    public sealed class LedgeAction
    {
        public LedgeActionKind Kind { get; set; }

        // PlaceToken + MoveToken fill these; EndTurn leaves them at defaults.
        public SpaceId From { get; set; }
        public SpaceId To { get; set; }
        public Tone Tone { get; set; }

        public LedgeAction()
        {
        }

        public static LedgeAction PlaceToken(SpaceId target, Tone tone) => new LedgeAction
        {
            Kind = LedgeActionKind.PlaceToken,
            To = target,
            Tone = tone,
        };

        public static LedgeAction MoveToken(SpaceId from, SpaceId to, Tone tone) => new LedgeAction
        {
            Kind = LedgeActionKind.MoveToken,
            From = from,
            To = to,
            Tone = tone,
        };

        public static LedgeAction EndTurn() => new LedgeAction
        {
            Kind = LedgeActionKind.EndTurn,
        };
    }

    public enum LedgeActionKind
    {
        PlaceToken,
        MoveToken,
        EndTurn,
    }
}
