using System;
using Magi.LedgeBoardGame.Models.Spec;

namespace Magi.LedgeBoardGame.Models.Network
{
    /// M6c3b-1 plumbing. Domain-side projection of the server-side echo stream
    /// so GameController can react to authoritative frames without pulling
    /// MagiGameServer.Contracts (StateEcho, ErrorEnvelope, ApplyOutcome, etc.)
    /// into the domain asmdef. LedgeBoardSessionDriver translates the DLL
    /// types into these fields before raising the events.
    ///
    /// Outcome mirror of MagiGameServer.Contracts.Core.ApplyOutcome:
    ///   * Applied  — server rules accepted the action; State is post-apply.
    ///   * Rejected — server rules refused; State is the last accepted state.
    ///   * Desynced — server rules accepted, but predicted hash disagreed;
    ///                State is the canonical server post-apply state.
    public enum LedgeSessionOutcome
    {
        Applied = 0,
        Rejected = 1,
        Desynced = 2,
    }

    /// One-shot JoinSnapshot payload — the first authoritative state each
    /// seat receives after its transport attaches. Drives initial state
    /// hydration when NetworkMode flips from Local to Network (M6c3b-3).
    public readonly struct LedgeSessionJoinInfo
    {
        public int ForSeatIndex { get; }
        public long Revision { get; }
        public long ServerHash { get; }
        public SpecGameState State { get; }

        public LedgeSessionJoinInfo(int forSeatIndex, long revision, long serverHash, SpecGameState state)
        {
            ForSeatIndex = forSeatIndex;
            Revision = revision;
            ServerHash = serverHash;
            State = state;
        }
    }

    /// Every StateEcho — Applied/Rejected/Desynced — flattened into a
    /// domain-safe struct. SubmittingSeatIndex is -1 if the echo was
    /// delivered before the dispatcher could attribute it, which shouldn't
    /// happen in M6c3b-1 but is reserved here to avoid an exception path
    /// that the driver would otherwise have to choose between.
    public readonly struct LedgeSessionEchoInfo
    {
        public int SubmittingSeatIndex { get; }
        public int ForSeatIndex { get; }
        public long AckedSeq { get; }
        public long Revision { get; }
        public long ServerHash { get; }
        public LedgeSessionOutcome Outcome { get; }
        public SpecGameState State { get; }

        public LedgeSessionEchoInfo(
            int submittingSeatIndex,
            int forSeatIndex,
            long ackedSeq,
            long revision,
            long serverHash,
            LedgeSessionOutcome outcome,
            SpecGameState state)
        {
            SubmittingSeatIndex = submittingSeatIndex;
            ForSeatIndex = forSeatIndex;
            AckedSeq = ackedSeq;
            Revision = revision;
            ServerHash = serverHash;
            Outcome = outcome;
            State = state;
        }
    }

    /// Carries an ErrorEnvelope in domain-safe form. SubscribingSeatIndex
    /// is the seat whose MagiSession raised the error, not necessarily the
    /// seat the server referenced — the server emits per-seat error frames
    /// and the dispatcher routes them to the owning dispatcher.
    public readonly struct LedgeSessionErrorInfo
    {
        public int SubscribingSeatIndex { get; }
        public long AckedSeq { get; }
        public string Code { get; }
        public string Message { get; }

        public LedgeSessionErrorInfo(int subscribingSeatIndex, long ackedSeq, string code, string message)
        {
            SubscribingSeatIndex = subscribingSeatIndex;
            AckedSeq = ackedSeq;
            Code = code;
            Message = message;
        }
    }

    /// Observer surface fanned out from the driver's per-seat MagiSession
    /// events. In M6c3b-1 GameController subscribes for diagnostics only;
    /// M6c3b-2 starts feeding authoritative State into ApplyServerState;
    /// M6c3b-3 makes it the primary state source.
    ///
    /// Events fire on the Unity main thread — the driver subscribes inside
    /// its own Tick() via SessionDispatcher dispatch, and LedgeShadowBootstrap
    /// pumps Tick() from MonoBehaviour.Update.
    public interface ILedgeSessionObserver
    {
        event Action<LedgeSessionJoinInfo> OnServerJoin;
        event Action<LedgeSessionEchoInfo> OnServerAdvance;
        event Action<LedgeSessionEchoInfo> OnServerMatched;
        event Action<LedgeSessionEchoInfo> OnServerDiverged;
        event Action<LedgeSessionErrorInfo> OnServerError;
    }

    /// Combined sink + observer surface so GameController receives a single
    /// object that covers both directions of the session (submit + listen).
    /// LedgeBoardSessionDriver is the one implementation.
    public interface ILedgeSessionBinding : ILedgeShadowSessionSink, ILedgeSessionObserver
    {
    }
}
