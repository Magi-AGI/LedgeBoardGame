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
    /// Four committed actions map to GameRules/GameState mutations:
    ///   PlaceToken      — mid-turn; gates on current-phase + per-turn placement budget.
    ///   MoveToken       — mid-turn; gates on current-phase + per-turn movement budget.
    ///   EndTurn         — turn-ending; triggers state-based effects + next-player advance.
    ///   SetDisplayName  — identity metadata; renames the carrying PlayerId's Player.Name
    ///                     at any time, independent of turn/phase. Client submits once per
    ///                     session post-claim so "Anna" shows up on every opponent's HUD
    ///                     instead of the default "Player4".
    ///
    /// UI-only intents (hover, select, drag-preview, undo-stack navigation) are
    /// deliberately NOT on the wire. Only commits that would mutate GameState
    /// under local play are shipped through the server.
    public sealed class LedgeAction
    {
        public LedgeActionKind Kind { get; set; }

        // PlaceToken + MoveToken fill these; EndTurn + SetDisplayName leave them at defaults.
        public SpaceId From { get; set; }
        public SpaceId To { get; set; }
        public Tone Tone { get; set; }

        // SetDisplayName payload. PlayerId identifies which roster entry's
        // Name to rewrite; today this is trusted (the client fills its own
        // claimed seat's PlayerId). Server-side validation that PlayerId
        // maps to the submitting envelope's Seat is a hardening follow-up
        // — pre-playtest scope treats name spoofing as out-of-scope for a
        // 6-player friendly session.
        public int PlayerId { get; set; }
        public string DisplayName { get; set; }

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

        public static LedgeAction SetDisplayName(int playerId, string displayName) => new LedgeAction
        {
            Kind = LedgeActionKind.SetDisplayName,
            PlayerId = playerId,
            DisplayName = displayName,
        };
    }

    public enum LedgeActionKind
    {
        PlaceToken,
        MoveToken,
        EndTurn,
        SetDisplayName,
    }
}
