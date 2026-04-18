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

    /// Domain mirror of MagiGameServer.Contracts.Protocol.TakebackOutcome.
    public enum LedgeTakebackOutcome
    {
        Granted = 0,
        PendingConsent = 1,
        Denied = 2,
    }

    /// Granted takeback broadcast fanned per-seat. State is the post-rewind
    /// state projected for ForSeatIndex — receivers apply it directly; no
    /// separate StateEcho accompanies a granted takeback. Revision moves
    /// backward compared to the previous echo, which is the one server-
    /// authored event where the timeline regresses.
    public readonly struct LedgeSessionTakebackInfo
    {
        public int RequestingSeatIndex { get; }
        public int ForSeatIndex { get; }
        public long AckedRequestSeq { get; }
        public long RevisionAfter { get; }
        public long ServerHash { get; }
        public int StepsRewound { get; }
        public SpecGameState State { get; }

        public LedgeSessionTakebackInfo(
            int requestingSeatIndex,
            int forSeatIndex,
            long ackedRequestSeq,
            long revisionAfter,
            long serverHash,
            int stepsRewound,
            SpecGameState state)
        {
            RequestingSeatIndex = requestingSeatIndex;
            ForSeatIndex = forSeatIndex;
            AckedRequestSeq = ackedRequestSeq;
            RevisionAfter = revisionAfter;
            ServerHash = serverHash;
            StepsRewound = stepsRewound;
            State = state;
        }
    }

    /// Denied / PendingConsent takeback reply. Granted outcomes do NOT route
    /// here — they arrive as LedgeSessionTakebackInfo broadcasts instead.
    /// SubscribingSeatIndex is the seat whose MagiSession raised the reply,
    /// which is the requester (replies fan only to the requester).
    public readonly struct LedgeSessionTakebackReplyInfo
    {
        public int SubscribingSeatIndex { get; }
        public int RequestingSeatIndex { get; }
        public long AckedRequestSeq { get; }
        public LedgeTakebackOutcome Outcome { get; }
        public int StepsGranted { get; }
        public string Message { get; }

        public LedgeSessionTakebackReplyInfo(
            int subscribingSeatIndex,
            int requestingSeatIndex,
            long ackedRequestSeq,
            LedgeTakebackOutcome outcome,
            int stepsGranted,
            string message)
        {
            SubscribingSeatIndex = subscribingSeatIndex;
            RequestingSeatIndex = requestingSeatIndex;
            AckedRequestSeq = ackedRequestSeq;
            Outcome = outcome;
            StepsGranted = stepsGranted;
            Message = message;
        }
    }

    /// Observer surface fanned out from the driver's per-seat MagiSession
    /// events. In M6c3b-1 GameController subscribes for diagnostics only;
    /// M6c3b-2 starts feeding authoritative State into ApplyServerState;
    /// M6c3b-3 makes it the primary state source; M6c3b-4 adds the takeback
    /// broadcast/reply stream.
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
        event Action<LedgeSessionTakebackInfo> OnServerTakeback;
        event Action<LedgeSessionTakebackReplyInfo> OnServerTakebackReply;
    }

    /// Combined sink + submitter + observer surface so GameController
    /// receives a single object that covers both directions of the session.
    /// Shadow path (Local mode) uses ILedgeShadowSessionSink; authoritative
    /// submit path (Network mode, M6c3b-3) uses ILedgeSessionSubmitter; both
    /// raise the same ILedgeSessionObserver events. LedgeBoardSessionDriver
    /// is the one implementation.
    public interface ILedgeSessionBinding
        : ILedgeShadowSessionSink, ILedgeSessionSubmitter, ILedgeSessionObserver
    {
    }
}
