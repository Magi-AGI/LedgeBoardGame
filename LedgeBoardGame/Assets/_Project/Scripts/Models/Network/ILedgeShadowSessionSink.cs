using Magi.LedgeBoardGame.Models.Spec;

namespace Magi.LedgeBoardGame.Models.Network
{
    /// Sink that mirrors the GameController's committed-action stream onto a
    /// parallel MagiGameServer.Core.Session so divergences between the local
    /// rules path and the server-authoritative path surface during normal
    /// play. Shadow mode only — the UI is still driven by the local
    /// _gameState; this interface is how M6c3a proves the transport +
    /// session + adapter chain produces identical hashes before M6c3b flips
    /// authority.
    ///
    /// Lives in the domain asmdef (not the Net asmdef) so GameController can
    /// hold a reference without pulling MagiGameServer.Contracts /
    /// MagiGameServer.Core into its compile chain. The Net-side implementor
    /// (LedgeBoardSessionDriver) fills in the session-plumbing side.
    ///
    /// Contract for every method below:
    ///   * Called AFTER the local GameRules mutation has already succeeded.
    ///   * `localPostApplyState` is the wire-shape projection of _gameState
    ///     at the moment of the call, with its Config field re-attached by
    ///     the caller. The implementor hashes it and sends the hash as
    ///     PredictedStateHash; a mismatch surfaces as OnPredictionDiverged
    ///     downstream.
    ///   * `seatIndex` is the 0-based session seat that submitted the action
    ///     (domain PlayerId - 1). For EndTurn this is the seat of the player
    ///     who JUST ended their turn, not the next player.
    ///   * Implementations are expected to be robust to being invoked before
    ///     the backing session is ready (e.g. during the scene's first few
    ///     frames); early calls should no-op rather than throw.
    public interface ILedgeShadowSessionSink
    {
        void ShadowPlace(int seatIndex, SpecGameState localPostApplyState, SpaceId target, Tone tone);
        void ShadowMove(int seatIndex, SpecGameState localPostApplyState, SpaceId from, SpaceId to, Tone tone);
        void ShadowEndTurn(int seatIndex, SpecGameState localPostApplyState);
    }
}
