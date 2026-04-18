using Magi.LedgeBoardGame.Models.Spec;

namespace Magi.LedgeBoardGame.Models.Network
{
    /// Submit-only surface used when GameController runs in
    /// NetworkMode.Network (M6c3b-3). Unlike ILedgeShadowSessionSink — which
    /// is called AFTER a successful local mutation and carries the local
    /// post-apply state for PredictedStateHash — this interface is the
    /// authority flip: the controller does not mutate _gameState, so there
    /// is no post-apply state to hash. The driver submits with
    /// PredictedStateHash = 0 and the resulting echo routes through
    /// OnServerAdvance; ApplyServerState then becomes the state source.
    ///
    /// Kept distinct from ILedgeShadowSessionSink on purpose. Overloading the
    /// shadow contract with "null state means don't predict" turns its
    /// "called after the local mutation already succeeded" clause into a lie
    /// at exactly the moment authority moves to the server. The two
    /// interfaces share a driver implementation but have different call
    /// invariants, so they get different methods.
    ///
    /// All implementations must be robust to being invoked before the
    /// backing session is ready; early calls should no-op rather than throw,
    /// mirroring ILedgeShadowSessionSink.
    /// Methods return true if the submission was enqueued for delivery,
    /// false if it was dropped (sink not ready, bad seat, etc.). Callers
    /// must only increment their in-flight lock counter on true — a
    /// false-returning submission produces no echo and would freeze any
    /// counter-gated UI.
    public interface ILedgeSessionSubmitter
    {
        bool SubmitPlace(int seatIndex, SpaceId target, Tone tone);
        bool SubmitMove(int seatIndex, SpaceId from, SpaceId to, Tone tone);
        bool SubmitEndTurn(int seatIndex);

        /// Cross-commit rollback. Unlike local undo (pre-submit buffer), a
        /// takeback addresses already-echoed actions and requires server
        /// authority to rewind — the server decides policy (auto-grant same
        /// turn, route for consent, reject). Granted requests arrive as
        /// OnServerTakeback broadcasts (carrying the post-rewind state);
        /// denied/pending outcomes arrive as OnServerTakebackReply.
        bool SubmitTakeback(int seatIndex, int stepsRequested, string reason);
    }
}
