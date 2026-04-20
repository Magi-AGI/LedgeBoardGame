using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Models.Network;
using Magi.LedgeBoardGame.Models.Spec;
using Magi.LedgeBoardGame.Rules;
using Magi.LedgeBoardGame.Board;

namespace Magi.LedgeBoardGame
{
    public class GameController : MonoBehaviour
    {
        /// Authority mode. Local (default) = local GameRules is the source of
        /// truth; the shadow driver runs in parallel but its echoes are only
        /// diagnostic. Network = entry points submit-only via the session
        /// sink, the scene renders entirely from server echoes
        /// (ApplyServerState + RefreshBoards + UpdateStatusUI), and the UI
        /// locks on _pendingSubmissions between submit and echo. The undo
        /// button is repurposed as the takeback trigger (RequestTakeback);
        /// pre-submit undo has no analogue server-side. Multi-hop movement
        /// is supported via a speculative-clone resolve that computes
        /// per-hop carried counts without mutating _gameState.
        public enum NetworkMode
        {
            Local = 0,
            Network = 1,
        }

        [SerializeField] private BoardPresenter boardPresenterPrefab;
        [SerializeField] private TextAsset ledgeSpecJson;
        [SerializeField] private Button endTurnButton;
        [SerializeField] private Button undoButton;
        [SerializeField] private GameHud gameHud;
        [SerializeField] private Board.MultiBoardLayout multiBoardLayout;
        [SerializeField] private Board.PlacementGhost placementGhost;
        [SerializeField] private Board.InHandGhost inHandGhost;
        [SerializeField] private Board.StatusBanner statusBanner;
        [SerializeField] private Board.StatusLog statusLog;
        [SerializeField] private Board.IdentityBadge identityBadge;
        [Tooltip("When on, records placements/moves/turn-ends/undos to the on-screen log panel. Leave on for playtest/video; turn off to hide the panel during normal play.")]
        [SerializeField] private bool showEventLog = true;
        [SerializeField] private Tone defaultMovementTone = Tone.Light;
        [Tooltip("Local = local rules authoritative (current production). Network = accept authoritative state from server echoes. Leave Local until M6c3b-3.")]
        [SerializeField] private NetworkMode networkMode = NetworkMode.Local;
        [Tooltip("Network mode only: seat index (0-based) this client controls. Actions submit for this seat and input is gated on IsLocalSeatsTurn. Ignored in Local/hot-seat.")]
        [SerializeField] private int networkLocalSeatIndex = 0;
        [Tooltip("Total seats in the match. Drives BuildDefaultRoster and must match the server-side SeatCount for Network mode. The lobby overrides this via ConfigureNetwork before Start runs.")]
        [SerializeField, Range(2, 8)] private int seatCount = 2;

        private GameState _gameState;
        private GameRules _rules;
        private LedgeRuntimeConfig _runtimeConfig;

        // Shadow-mode binding, attached at scene start by LedgeShadowBootstrap.
        // When null (production builds with shadow disabled, or before the
        // driver has finished ConnectAsync) every submit hook no-ops and no
        // observer events fire. The combined ILedgeSessionBinding carries the
        // outgoing sink (ShadowPlace/Move/EndTurn) AND the incoming observer
        // (OnServerJoin/Advance/Matched/Diverged/Error). The interface lives
        // in Magi.LedgeBoardGame.Models.Network so this controller can hold
        // the reference without pulling the Net asmdef into its compile graph.
        private ILedgeSessionBinding _shadowSink;

        // The player ID this client controls. Distinct from GameState.CurrentPlayerId —
        // which is "whose turn is it" (authoritative, server-set in the online model).
        // In hot-seat every turn transition moves _localSeatId with the current player;
        // in the online model it's pinned at session join and only the current player
        // changes. UI that reads "can *I* act right now" must gate on IsLocalSeatsTurn(),
        // not on CurrentPlayerId directly.
        private int _localSeatId;

        private readonly Dictionary<int, BoardPresenter> _boardPresenters = new Dictionary<int, BoardPresenter>();
        private SpaceId? _selectedSpace;
        private Tone _selectedTone = Tone.Light;
        private int _pickedUpLight;
        private int _pickedUpDark;
        private readonly Stack<UndoFrame> _undoStack = new Stack<UndoFrame>();
        private bool _moveInProgress;
        // Count of in-flight submit-only actions whose server echo hasn't
        // returned yet. Network mode uses this to lock the UI — a new click
        // while any submission is pending would queue a second action against
        // stale local state. Incremented in Submit*; decremented in the
        // OnServer* handlers for the local seat. Unused in Local mode.
        private int _pendingSubmissions;
        // Narration line to emit once the authoritative echo for this batch of
        // network submissions lands. Local mode logs "placed/moved" at rule-
        // apply time; Network mode deferred that to here so the log matches the
        // moment the state actually changes client-side. Null when no local
        // submission is in flight. Cleared on emit, on takeback, and on error.
        private string _pendingEchoNarration;
        private SpaceId? _pendingRetarget;
        private SpaceView _sourcePhantomView;
        // Reach map for the currently selected stack: key = space, value = hop distance
        // from source. Populated by SelectMovementSource and consumed by HandleMovementClick
        // to distinguish single-hop from chained multi-hop destinations.
        private Dictionary<SpaceId, int> _selectedReach;
        private int _selectedReachMax;
        private const float MoveTweenDuration = 0.28f;
        // Alpha applied to the source SpaceView while its counters are "in hand," so the
        // origin reads as a faded placeholder while the opaque flying/in-hand stack
        // becomes the player's visual anchor.
        private const float SourcePhantomAlpha = 0.35f;

        private struct UndoFrame
        {
            public GameState State;
            public bool HasAnimation;
            public SpaceId From;
            public SpaceId To;
            public int Light;
            public int Dark;
        }

        public int LocalSeatId => _localSeatId;

        public bool IsLocalSeatsTurn() =>
            _gameState != null && _gameState.CurrentPlayerId == _localSeatId;

        /// Seat count the shadow driver should spin up. Matches the roster
        /// built during Start; hard-coded to 2 until the scene exposes a
        /// per-match player count slider. Kept as a public getter so the
        /// bootstrap doesn't hard-code the number in two places.
        public int PlayerCount => _gameState?.Players?.Count ?? 2;

        /// Hands the ledge spec JSON text (if any) to LedgeShadowBootstrap so
        /// the server-side CreateInitialState loads from the same spec file
        /// the client does. A null return is legal — LedgeGameModule tolerates
        /// a missing option key and produces the default initial state.
        public string GetLedgeSpecJson() => ledgeSpecJson != null ? ledgeSpecJson.text : null;

        /// Authority mode selected in the inspector. Load-bearing since
        /// M6c3b-3: gates whether entry points mutate locally or submit via
        /// the session sink.
        public NetworkMode CurrentNetworkMode => networkMode;

        /// Called once by LedgeShadowBootstrap after the driver has finished
        /// ConnectAsync for every seat. Idempotent and null-tolerant — passing
        /// null detaches shadow mode (useful if the bootstrap is torn down
        /// during play for any reason). The controller never calls through
        /// the sink before AttachShadowSink completes, so the ordering here
        /// (construct driver → await ConnectAsync → attach) is exactly what
        /// keeps the first submission's hash check valid.
        ///
        /// Re-attachment paths: calling with a non-null sink while one is
        /// already attached unsubscribes from the previous sink's events
        /// before swapping, so a bootstrap that rebuilds its driver (e.g.
        /// across a scene reload where the bootstrap survives and the
        /// GameController doesn't — not a path today, but cheap to be safe)
        /// never double-fires.
        public void AttachShadowSink(ILedgeSessionBinding sink)
        {
            if (ReferenceEquals(_shadowSink, sink)) return;
            if (_shadowSink != null) UnsubscribeObserver(_shadowSink);
            _shadowSink = sink;
            if (_shadowSink != null) SubscribeObserver(_shadowSink);
        }

        private void SubscribeObserver(ILedgeSessionObserver observer)
        {
            observer.OnServerJoin += HandleServerJoin;
            observer.OnServerAdvance += HandleServerAdvance;
            observer.OnServerMatched += HandleServerMatched;
            observer.OnServerDiverged += HandleServerDiverged;
            observer.OnServerError += HandleServerError;
            observer.OnServerTakeback += HandleServerTakeback;
            observer.OnServerTakebackReply += HandleServerTakebackReply;
        }

        private void UnsubscribeObserver(ILedgeSessionObserver observer)
        {
            observer.OnServerJoin -= HandleServerJoin;
            observer.OnServerAdvance -= HandleServerAdvance;
            observer.OnServerMatched -= HandleServerMatched;
            observer.OnServerDiverged -= HandleServerDiverged;
            observer.OnServerError -= HandleServerError;
            observer.OnServerTakeback -= HandleServerTakeback;
            observer.OnServerTakebackReply -= HandleServerTakebackReply;
        }

        private void HandleServerJoin(LedgeSessionJoinInfo info)
        {
            if (Application.isEditor)
                UnityEngine.Debug.Log(
                    $"[net] join seat={info.ForSeatIndex} revision={info.Revision} " +
                    $"hash=0x{info.ServerHash:X16} mode={networkMode}");

            // _localSeatId is set at Start from networkLocalSeatIndex and is
            // NOT re-pinned here — the in-process test harness creates one
            // MagiSession per seat and fans every seat's join into this one
            // controller, so pinning from the join would clobber on whichever
            // seat happened to join last. Real single-client networking will
            // only see its own seat's join, and the Start-time pin remains
            // consistent with that anyway.
            if (networkMode == NetworkMode.Network
                && info.ForSeatIndex == _localSeatId - 1)
            {
                ApplyServerStateAndRefresh(info.State);
            }
        }

        private void HandleServerAdvance(LedgeSessionEchoInfo info)
        {
            if (Application.isEditor)
                UnityEngine.Debug.Log(
                    $"[net] advance submittingSeat={info.SubmittingSeatIndex} " +
                    $"seq={info.AckedSeq} revision={info.Revision} " +
                    $"hash=0x{info.ServerHash:X16} outcome={info.Outcome} mode={networkMode}");

            // Rejected echoes carry the last accepted state for snap-back.
            // Network mode also arrives here for its own (non-predicting)
            // submissions — shadow submissions still route through
            // OnServerMatched/Diverged when predicting.
            if (networkMode == NetworkMode.Network)
            {
                ApplyServerStateAndRefresh(info.State);
                ReleaseSubmissionIfLocal(info.SubmittingSeatIndex);
            }
        }

        private void HandleServerMatched(LedgeSessionEchoInfo info)
        {
            if (Application.isEditor)
                UnityEngine.Debug.Log(
                    $"[net] matched submittingSeat={info.SubmittingSeatIndex} " +
                    $"seq={info.AckedSeq} revision={info.Revision} mode={networkMode}");

            // In Network mode Submit* uses predictedHash=0, so Matched
            // shouldn't fire for local-seat network submissions. It may still
            // arrive for shadow-mode submissions in mixed-mode testing, or
            // for frames other clients predicted — we still apply + unlock
            // defensively so a stray attribution can't wedge the UI.
            if (networkMode == NetworkMode.Network)
            {
                ApplyServerStateAndRefresh(info.State);
                ReleaseSubmissionIfLocal(info.SubmittingSeatIndex);
            }
        }

        private void HandleServerDiverged(LedgeSessionEchoInfo info)
        {
            // The driver already LogError's this. Controller-side, we still
            // apply the canonical state so the scene tracks the server
            // (hard-snap); M6c3b-3 intentionally has no optimistic prediction
            // to roll back, so this is the same code path as Matched.
            if (Application.isEditor)
                UnityEngine.Debug.LogWarning(
                    $"[net] diverged submittingSeat={info.SubmittingSeatIndex} " +
                    $"seq={info.AckedSeq} revision={info.Revision} " +
                    $"outcome={info.Outcome} mode={networkMode}");

            if (networkMode == NetworkMode.Network)
            {
                ApplyServerStateAndRefresh(info.State);
                ReleaseSubmissionIfLocal(info.SubmittingSeatIndex);
            }
        }

        private void HandleServerError(LedgeSessionErrorInfo info)
        {
            if (Application.isEditor)
                UnityEngine.Debug.LogWarning(
                    $"[net] server-error seat={info.SubscribingSeatIndex} " +
                    $"seq={info.AckedSeq} code={info.Code} message={info.Message} mode={networkMode}");

            // Protocol-layer errors don't carry state, but the submission
            // did land (and get refused) — release the lock so the user can
            // try again. Without this the UI would stay frozen on a failed
            // submit in Network mode. ErrorInfo's SubscribingSeatIndex is
            // the seat whose session raised the error, which is always the
            // local seat here (observer events are per-seat-scoped).
            if (networkMode == NetworkMode.Network)
            {
                // Drop any cached landing narration — the action was refused,
                // so logging "X placed/moved ..." on the pending→0 transition
                // would misrepresent the outcome.
                _pendingEchoNarration = null;
                ReleaseSubmissionIfLocal(info.SubscribingSeatIndex);
                RefreshBoards();
                UpdateStatusUI();
            }
        }

        private void HandleServerTakeback(LedgeSessionTakebackInfo info)
        {
            if (Application.isEditor)
                UnityEngine.Debug.Log(
                    $"[net] takeback requestingSeat={info.RequestingSeatIndex} " +
                    $"forSeat={info.ForSeatIndex} stepsRewound={info.StepsRewound} " +
                    $"revisionAfter={info.RevisionAfter} hash=0x{info.ServerHash:X16} mode={networkMode}");

            if (networkMode != NetworkMode.Network) return;

            // The driver fans every seat's broadcast through this one
            // controller (in-process test harness). Apply state only on the
            // broadcast addressed to the local seat; otherwise ApplyServerState
            // would overwrite _gameState N times.
            if (info.ForSeatIndex == _localSeatId - 1)
            {
                // Takeback clears any pre-submit local selection — the rewind
                // may have unwound the very action that selection was built
                // against. Clearing mirrors the undo-path behavior from
                // OnUndoClicked without a reverse tween (no animation; hard
                // snap to post-rewind state).
                ClearMovementSelection();
                ClearHighlights();
                // Drop any pending placed/moved narration — the action it
                // described just got rewound, so logging it on the pending→0
                // transition below would be false attribution.
                _pendingEchoNarration = null;
                ApplyServerStateAndRefresh(info.State);
                LogEvent($"↶ takeback granted ({info.StepsRewound} step{(info.StepsRewound == 1 ? "" : "s")})");
            }

            // Only the requester's in-flight lock was this takeback; others
            // never incremented theirs for it. Release it so the requester's
            // UI unlocks. Done outside the ForSeatIndex==local branch because
            // in the in-process harness the RequestingSeat broadcast arrives
            // on every seat; we gate on RequestingSeatIndex instead.
            if (info.RequestingSeatIndex == _localSeatId - 1
                && info.ForSeatIndex == _localSeatId - 1)
            {
                if (_pendingSubmissions > 0) _pendingSubmissions--;
                if (_pendingSubmissions == 0) RefreshUndoButton();
            }
        }

        private void HandleServerTakebackReply(LedgeSessionTakebackReplyInfo info)
        {
            if (Application.isEditor)
                UnityEngine.Debug.Log(
                    $"[net] takeback reply seat={info.SubscribingSeatIndex} " +
                    $"requestingSeat={info.RequestingSeatIndex} outcome={info.Outcome} " +
                    $"stepsGranted={info.StepsGranted} seq={info.AckedRequestSeq} " +
                    $"message={info.Message} mode={networkMode}");

            if (networkMode != NetworkMode.Network) return;

            // Granted outcomes never route here — they arrive as the
            // LedgeSessionTakebackInfo broadcast. So a reply means Denied or
            // PendingConsent, both of which leave authoritative state
            // untouched; just surface the result and release the lock.
            switch (info.Outcome)
            {
                case LedgeTakebackOutcome.Denied:
                    LogEvent($"↶ takeback denied{(string.IsNullOrEmpty(info.Message) ? "" : $": {info.Message}")}");
                    break;
                case LedgeTakebackOutcome.PendingConsent:
                    LogEvent("↶ takeback awaiting consent");
                    break;
                case LedgeTakebackOutcome.Granted:
                    // Unexpected — the broadcast path should handle this.
                    // Log and fall through to release the lock defensively.
                    if (Application.isEditor)
                        UnityEngine.Debug.LogWarning(
                            "[net] takeback Granted outcome arrived via reply — expected broadcast");
                    break;
            }

            // PendingConsent is the one outcome where the takeback isn't
            // resolved yet — a follow-up Granted broadcast or Denied reply
            // will arrive later. Releasing the lock now would let the player
            // fire another action mid-consent; hold the lock and let the
            // follow-up clear it.
            if (info.Outcome == LedgeTakebackOutcome.PendingConsent) return;

            if (info.RequestingSeatIndex == _localSeatId - 1)
            {
                if (_pendingSubmissions > 0) _pendingSubmissions--;
                if (_pendingSubmissions == 0) RefreshUndoButton();
            }
        }

        private void ReleaseSubmissionIfLocal(int submittingSeatIndex)
        {
            if (submittingSeatIndex != _localSeatId - 1) return;
            if (_pendingSubmissions > 0) _pendingSubmissions--;
            if (_pendingSubmissions == 0)
            {
                // Post-echo landing narration. Submit-time already logged a
                // "→ submit ..." line; this second log matches Local mode's
                // "placed/moved" wording and confirms the action landed
                // authoritatively. Multi-counter moves batch to a single log.
                if (!string.IsNullOrEmpty(_pendingEchoNarration))
                {
                    LogEvent(_pendingEchoNarration);
                    _pendingEchoNarration = null;
                }

                // UI was frozen while waiting for the authoritative echo.
                // Re-enable interactive buttons now that the round-trip is
                // done. Status/highlight refresh happens in
                // ApplyServerStateAndRefresh above, not here, so this stays
                // purely a lock-release.
                RefreshUndoButton();
            }
        }

        /// M6c3b-2 glue between the observer handlers and the state
        /// application path. Applies the authoritative snapshot, then refreshes
        /// boards + status UI so the scene mirrors the server truth. The
        /// refresh is suppressed while a local tween owns the visual state —
        /// RefreshBoards snaps SpaceView transforms directly and would fight
        /// an in-flight placement/move/return/undo animation. The tween
        /// completion callback already calls RefreshBoards + UpdateStatusUI,
        /// so the just-applied state lands naturally when the tween ends.
        private void ApplyServerStateAndRefresh(SpecGameState specState)
        {
            if (specState == null) return;
            // Pre-apply snapshot for Network-mode state-diff narration. Local mode
            // narrates from OnMoveTweenComplete / OnEndTurnClicked; Network mode
            // only sees echoes here, so eliminations, game-over, and end-of-turn
            // overflow trims would otherwise fire silently. Capture is cheap and
            // only consumed when networkMode == Network, so gate is applied later.
            var preEliminated = new HashSet<int>();
            int? preCurrentPlayerId = null;
            bool preGameOver = false;
            Dictionary<int, (int light, int dark)> preEndingBoardStacks = null;
            if (_gameState != null)
            {
                preCurrentPlayerId = _gameState.CurrentPlayerId;
                preGameOver = _gameState.GameOver;
                if (_gameState.Players != null)
                {
                    foreach (var p in _gameState.Players)
                        if (p.IsEliminated) preEliminated.Add(p.Id);
                }
                var endingBoard = _gameState.Boards?.FirstOrDefault(b => b.PlayerId == _gameState.CurrentPlayerId);
                if (endingBoard?.Spaces != null)
                {
                    preEndingBoardStacks = new Dictionary<int, (int, int)>();
                    foreach (var kv in endingBoard.Spaces)
                        preEndingBoardStacks[kv.Key] = (kv.Value.LightCount, kv.Value.DarkCount);
                }
            }
            ApplyServerState(specState);
            if (networkMode == NetworkMode.Network)
                NarrateServerStateDiff(preEliminated, preCurrentPlayerId, preGameOver, preEndingBoardStacks);
            if (Application.isEditor)
            {
                var occ = new System.Text.StringBuilder();
                if (_gameState?.Boards != null)
                {
                    foreach (var b in _gameState.Boards)
                    {
                        if (b?.Spaces == null) continue;
                        foreach (var kv in b.Spaces)
                        {
                            if (kv.Value.LightCount == 0 && kv.Value.DarkCount == 0) continue;
                            occ.Append($" {FormatSpace(new SpaceId(b.BoardId, kv.Key))}=L{kv.Value.LightCount}/D{kv.Value.DarkCount}");
                        }
                    }
                }
                int presenters = _boardPresenters?.Count ?? 0;
                int tp = _gameState?.CurrentTurnPlacements?.Count ?? 0;
                UnityEngine.Debug.Log($"[net] post-apply presenters={presenters} turnPlacements={tp} mip={_moveInProgress} occ={{{occ}}}");
                if (_gameState?.CurrentTurnPlacements != null)
                {
                    foreach (var p in _gameState.CurrentTurnPlacements)
                        UnityEngine.Debug.Log($"[net] placement in state: {p.Tone} at {FormatSpace(p.Target)}");
                }
            }
            if (_moveInProgress) return;
            RefreshBoards();
            UpdateStatusUI();

            // Network mode: this echo is the only signal a given player gets
            // that the board state changed, so re-apply highlights the same
            // way OnPlacementTweenComplete does after a local tween in Local
            // mode. Gate on IsLocalSeatsTurn so echoes from a remote player's
            // action don't paint their targets on this client's board.
            if (networkMode != NetworkMode.Network) return;
            if (_gameState == null || _gameState.GameOver)
            {
                ClearHighlights();
                return;
            }
            if (!IsLocalSeatsTurn())
            {
                ClearHighlights();
                return;
            }
            if (_gameState.CurrentPhase == GamePhase.Placement)
            {
                HighlightPlacementTargets();
            }
            else
            {
                ClearHighlights();
                // Network-mode auto-skip: Local mode fires this from
                // OnMoveTweenComplete / the placement→movement hook, but
                // Network-mode moves land here via the server echo instead,
                // so the check has to run on every echo the local seat
                // receives. OnEndTurnClicked's _pendingSubmissions gate keeps
                // repeated echoes from double-submitting before the EndTurn
                // echo rotates CurrentPlayerId off this seat.
                if (!MaybeAutoSkipTurn())
                    HighlightMovablePieces();
            }
        }

        /// Replaces _gameState's mutable fields from an authoritative server
        /// snapshot without triggering tweens, ghosts, or audio. By design
        /// this does NOT:
        ///   * play movement/placement animations (scene sync is idempotent)
        ///   * raise undo frames (authoritative state isn't something the
        ///     local player can undo)
        ///   * reset selection / picked-up counts (M6c3b-3 will clear those
        ///     around the call when its own action was the one echoed)
        ///
        /// The helper inflates via GameState.FromSpecState (which rebuilds
        /// cross-board ledge edges) and copies into _gameState via CopyFrom
        /// so downstream references — BoardPresenters keyed off _gameState.
        /// Boards[i] in particular — stay valid. When the snapshot carries a
        /// Config it also replaces _runtimeConfig + _rules so server-mode
        /// rule evaluation uses the same phase bounds the server used. Fails
        /// silently on null or when _gameState hasn't finished Start yet;
        /// callers should gate on CurrentNetworkMode == Network before
        /// calling.
        public void ApplyServerState(SpecGameState specState)
        {
            if (specState == null || _gameState == null) return;
            var inflated = GameState.FromSpecState(specState);
            if (inflated == null) return;
            int priorBoardCount = _gameState.Boards.Count;
            _gameState.CopyFrom(inflated);
            // JIP: CopyFrom grew Boards to match the server's roster. Spawn
            // presenters for the newly appended boards — CreateBoardPresenters
            // is idempotent and will skip ones we already instantiated.
            if (_gameState.Boards.Count > priorBoardCount)
            {
                CreateBoardPresenters();
            }

            // The snapshot's Config is authoritative, including when it is
            // null — a remote seat whose server runs without a spec must end
            // up with _rules = new GameRules(null), not whatever startup
            // config the local bootstrap happened to load.
            var runtimeConfig = specState.Config != null
                ? LedgeRuntimeConfig.FromSpec(specState.Config)
                : null;
            _runtimeConfig = runtimeConfig;
            _rules = new GameRules(runtimeConfig);
        }

        /// Produces a fresh SpecGameState snapshot of the current _gameState
        /// with its Config re-attached, matching what
        /// LedgeGameModule.CreateInitialState does on the server side. This
        /// is the state the shadow sink hashes for PredictedStateHash, so its
        /// shape MUST match the server's ProjectStateFor output byte-for-byte
        /// or the hashes will never agree. ToSpecState already clones boards,
        /// so the returned object is safe to hand to a background thread if
        /// the sink ever chooses to offload hashing.
        private SpecGameState BuildCurrentSpecState()
        {
            if (_gameState == null) return null;
            var state = _gameState.ToSpecState();
            state.Config = _runtimeConfig?.ToSpec();
            return state;
        }

        /// Lobby hook. Called before Start runs (LedgeLobbyBootstrap gates
        /// GameController.enabled while the selector is up) to override the
        /// inspector defaults with what the user picked. Throws if the
        /// controller has already started — once _gameState exists the
        /// roster is baked and switching seat counts mid-game is not a
        /// supported path.
        public void ConfigureNetwork(NetworkMode mode, int ownedSeatIndex, int totalSeats)
        {
            if (_gameState != null)
                throw new System.InvalidOperationException(
                    "GameController.ConfigureNetwork must run before Start — _gameState is already initialised.");
            networkMode = mode;
            networkLocalSeatIndex = ownedSeatIndex;
            seatCount = totalSeats;
        }

        private void Start()
        {
            // Network mode must match the server's LedgeGameModule.CreateInitialState
            // exactly (IsConnected=false for every seat) — the initial state hash
            // is compared shadow-style on the first submit, and any mismatch fires
            // a divergence log from the very first action. Local/hot-seat keeps
            // the historical "everyone present" default.
            var players = Player.BuildDefaultRoster(
                seatCount,
                initiallyConnected: networkMode != NetworkMode.Network);

            LedgeRuntimeConfig runtimeConfig = null;
            var useSpec = false;
            if (ledgeSpecJson != null && !string.IsNullOrEmpty(ledgeSpecJson.text))
            {
                var spec = LedgeGameSpecLoader.LoadFromJson(ledgeSpecJson.text);
                if (spec != null)
                {
                    // Validate that the loaded spec matches our code assumptions.
                    LedgeSpecValidator.Validate(spec);
                    runtimeConfig = LedgeRuntimeConfig.FromSpec(spec);
                    useSpec = true;
                }
                else if (Application.isEditor)
                {
                    UnityEngine.Debug.LogWarning("GameController: Failed to parse ledge spec JSON. Falling back to defaults.");
                }
            }
            else if (Application.isEditor)
            {
                UnityEngine.Debug.LogWarning("GameController: No ledgeSpecJson assigned. Falling back to defaults.");
            }

            _gameState = new GameState(players, runtimeConfig);
            _rules = new GameRules(useSpec ? runtimeConfig : null);
            _runtimeConfig = useSpec ? runtimeConfig : null;
            // Hot-seat / Local mode: _localSeatId tracks whoever's turn it is
            // and rotates in OnEndTurnClicked. Network mode pins to the seat
            // this client owns, configured via networkLocalSeatIndex, and
            // never rotates — IsLocalSeatsTurn() becomes the input gate.
            _localSeatId = networkMode == NetworkMode.Network
                ? networkLocalSeatIndex + 1
                : _gameState.CurrentPlayerId;

            if (multiBoardLayout == null)
            {
                multiBoardLayout = GetComponent<Board.MultiBoardLayout>();
                if (multiBoardLayout == null)
                {
                    multiBoardLayout = gameObject.AddComponent<Board.MultiBoardLayout>();
                }
            }

            // Pan/zoom lives on the layout's own GameObject so it shares the
            // RectTransform that holds every board. Auto-attach at runtime so
            // existing scenes pick it up without a re-save.
            if (multiBoardLayout.GetComponent<Board.MultiBoardPanZoom>() == null)
            {
                multiBoardLayout.gameObject.AddComponent<Board.MultiBoardPanZoom>();
            }

            CreateBoardPresenters();

            EnsureInHandGhost();
            EnsurePlacementGhost();
            EnsureStatusBanner();
            EnsureStatusLog();
            EnsureIdentityBadge();

            SpaceClickedEvent.Register(OnSpaceClicked);

            if (endTurnButton != null)
            {
                endTurnButton.onClick.AddListener(OnEndTurnClicked);
            }

            if (undoButton != null)
            {
                undoButton.onClick.AddListener(OnUndoClicked);
            }

            UpdateStatusUI();
            RefreshUndoButton();

            // Game begins in Placement. Without this, P1's very first Light placement
            // has no ripple — highlights only started triggering after the first tween
            // completed. Kick off the initial ripple so the player sees valid targets
            // immediately on game start.
            if (_gameState.CurrentPhase == GamePhase.Placement)
            {
                HighlightPlacementTargets();
            }
            else
            {
                HighlightMovablePieces();
            }
        }

        private void OnDestroy()
        {
            SpaceClickedEvent.Unregister(OnSpaceClicked);
            if (_shadowSink != null)
            {
                UnsubscribeObserver(_shadowSink);
                _shadowSink = null;
            }
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.spaceKey.wasPressedThisFrame)
            {
                OnEndTurnClicked();
            }
#endif
        }

        private void CreateBoardPresenters()
        {
            var presenterParent = multiBoardLayout != null ? multiBoardLayout.transform : transform;

            // Idempotent: skip boards that already have a presenter so a JIP
            // growth path (ApplyServerState detects Boards.Count grew, calls
            // back in) only instantiates the new boards.
            foreach (var board in _gameState.Boards)
            {
                if (_boardPresenters.ContainsKey(board.BoardId)) continue;

                BoardPresenter presenterInstance;

                if (boardPresenterPrefab != null)
                {
                    var go = Instantiate(boardPresenterPrefab.gameObject, presenterParent);
                    go.name = $"Board_{board.BoardId}_Presenter";
                    presenterInstance = go.GetComponent<BoardPresenter>();
                }
                else
                {
                    var go = new GameObject($"Board_{board.BoardId}_Presenter");
                    go.transform.SetParent(presenterParent, false);
                    presenterInstance = go.AddComponent<BoardPresenter>();
                }

                var owner = _gameState.Players?.FirstOrDefault(p => p.Id == board.PlayerId);
                presenterInstance.Initialize(board, owner?.Name);
                _boardPresenters[board.BoardId] = presenterInstance;
            }

            RefreshBoards();

            // Update multi-board layout positions if present. Local board is the
            // one owned by this client's seat — comparison mode uses it as the
            // fixed-left slot.
            if (multiBoardLayout != null)
            {
                int localBoardId = ResolveLocalBoardId();
                multiBoardLayout.SetLocalBoardId(localBoardId);
                multiBoardLayout.Refresh();
            }

            EnsureBoardViewHud();

            gameHud?.UpdateHud(_gameState);
        }

        private int ResolveLocalBoardId()
        {
            if (_gameState == null) return -1;
            foreach (var board in _gameState.Boards)
            {
                if (board.PlayerId == _localSeatId) return board.BoardId;
            }
            return _gameState.Boards.Count > 0 ? _gameState.Boards[0].BoardId : -1;
        }

        private Board.BoardViewHud _boardViewHud;
        private void EnsureBoardViewHud()
        {
            if (_boardViewHud != null) { _boardViewHud.Refresh(); return; }
            if (multiBoardLayout == null) return;
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                foreach (var presenter in _boardPresenters.Values)
                {
                    canvas = presenter.GetComponentInParent<Canvas>();
                    if (canvas != null) break;
                }
            }
            if (canvas == null) return;
            var go = new GameObject("BoardViewHudHost");
            go.transform.SetParent(canvas.transform, false);
            _boardViewHud = go.AddComponent<Board.BoardViewHud>();
            _boardViewHud.Initialize(multiBoardLayout);
        }

        private void OnSpaceClicked(SpaceView view)
        {
            if (view == null || _gameState == null)
                return;

            // Gate clicks during a move-tween so the player can't queue a second move
            // before the current counter has landed.
            if (_moveInProgress)
                return;

            if (networkMode == NetworkMode.Network)
            {
                // Drop input while a previously submitted action is still
                // awaiting its echo. Without this the user could submit a
                // second action against the pre-echo state.
                if (_pendingSubmissions > 0)
                    return;
                // Only act on our own turn in Network mode. Without this
                // gate the client could submit actions for the remote
                // player just by clicking during their turn.
                if (!IsLocalSeatsTurn())
                    return;
            }

            var boardId = FindBoardIdForView(view);
            if (boardId == null)
                return;

            var spaceId = new SpaceId(boardId.Value, view.SpaceId);

            if (_gameState.CurrentPhase == GamePhase.Placement)
            {
                HandlePlacementClick(spaceId);
            }
            else if (_gameState.CurrentPhase == GamePhase.Movement)
            {
                HandleMovementClick(spaceId);
            }
        }

        private int? FindBoardIdForView(SpaceView view)
        {
            foreach (var kvp in _boardPresenters)
            {
                if (kvp.Value.SpaceViews.TryGetValue(view.SpaceId, out var candidate) && candidate == view)
                {
                    return kvp.Key;
                }
            }

            return null;
        }

        private SpaceView FindSpaceView(SpaceId id)
        {
            if (_boardPresenters.TryGetValue(id.BoardId, out var presenter) &&
                presenter.SpaceViews.TryGetValue(id.Id, out var view))
            {
                return view;
            }
            return null;
        }

        private Transform ResolveOverlayParent(SpaceView fallbackView)
        {
            // Prefer the Canvas root so the overlay can cross boards on ledge hops without
            // having to parent-hop mid-tween.
            var canvas = fallbackView != null ? fallbackView.GetComponentInParent<Canvas>() : null;
            if (canvas == null) canvas = GetComponentInParent<Canvas>();
            if (canvas != null) return canvas.transform;
            // Last resort: own transform. Positioning still works in world space.
            return transform;
        }

        private void OnMoveTweenComplete()
        {
            _moveInProgress = false;
            ClearSourcePhantom();
            // Destination was held at its pre-move state during the tween so the counters
            // read as arriving — now that they've landed, catch every board up to state.
            RefreshBoards();
            // A move can lock an enemy center (elimination) or leave the active player
            // with no legal responses. Run SBE before auto-skip so the narration reads
            // elimination → game-over → skip in the order those effects actually occur.
            RunStateBasedEffects();
            UpdateStatusUI();
            if (_gameState == null || _gameState.GameOver)
            {
                RefreshUndoButton();
                return;
            }
            if (!MaybeAutoSkipTurn())
            {
                HighlightMovablePieces();
            }
            RefreshUndoButton();
        }

        private void HandlePlacementClick(SpaceId target)
        {
            var currentPlayer = _gameState.GetCurrentPlayer();
            if (currentPlayer == null)
                return;

            // Place first Light, then Dark
            Tone toneToPlace;
            if (!_gameState.CurrentTurnPlacements.Exists(p => p.Tone == Tone.Light))
            {
                toneToPlace = Tone.Light;
            }
            else if (!_gameState.CurrentTurnPlacements.Exists(p => p.Tone == Tone.Dark))
            {
                toneToPlace = Tone.Dark;
            }
            else
            {
                return;
            }

            // CanPlaceToken is a pure read on _gameState; safe to use as a
            // UI gate even in Network mode (the server re-validates).
            if (!_rules.CanPlaceToken(_gameState, target, toneToPlace))
                return;

            int seatIndex = currentPlayer.Id - 1;

            if (networkMode == NetworkMode.Network)
            {
                // Authority flip: submit-only. No local mutation, no tween,
                // no undo frame — the echo drives the visual change via
                // ApplyServerStateAndRefresh. UI stays locked on
                // _pendingSubmissions until the echo returns. Submit from
                // _localSeatId (not currentPlayer.Id) so we never send an
                // action for another seat; input is already gated on
                // IsLocalSeatsTurn so the two are equal here, but deriving
                // from _localSeatId keeps the invariant local to the call.
                if (_shadowSink != null
                    && _shadowSink.SubmitPlace(_localSeatId - 1, target, toneToPlace))
                {
                    _pendingSubmissions++;
                    LogEvent($"{currentPlayer.Name} → submit place {toneToPlace} at {FormatSpace(target)}");
                    // Cache the landed-narration now — by echo time the turn
                    // may have rotated, so _gameState.GetCurrentPlayer() is
                    // not a reliable attribution source. Emitted once the
                    // last echo for this batch lands (see ReleaseSubmissionIfLocal).
                    _pendingEchoNarration = $"{currentPlayer.Name} placed {toneToPlace} at {FormatSpace(target)}";
                    ClearHighlights();
                    RefreshUndoButton();
                }
                else if (Application.isEditor)
                {
                    UnityEngine.Debug.LogWarning("[net] place submission dropped (sink null or not ready)");
                }
                return;
            }

            PushPlacementUndo();
            var move = _rules.PlaceToken(_gameState, target, toneToPlace);
            if (move != null)
            {
                LogEvent($"{currentPlayer.Name} placed {toneToPlace} at {FormatSpace(target)}");
                // Shadow: mirror the commit onto the parallel Session. Snapshot is
                // taken AFTER _rules mutated, so the hash the server will recompute
                // matches the hash we submit. A divergence here would mean the
                // rules adapter and GameRules produced different post-apply states
                // for the same action — the M6c3a canary.
                _shadowSink?.ShadowPlace(seatIndex, BuildCurrentSpecState(), target, toneToPlace);
                PlayPlacementTween(target, toneToPlace);
            }
            else
            {
                // Placement actually failed — drop the speculative snapshot.
                _undoStack.Pop();
                RefreshUndoButton();
            }
        }

        /// Flies a single counter from the placement ghost's cursor-tracked position to
        /// the target space. State has already been mutated; destination render is
        /// deferred to OnPlacementTweenComplete so the counter visibly arrives.
        private void PlayPlacementTween(SpaceId target, Tone tone)
        {
            var toView = FindSpaceView(target);
            Vector3 fromPos = (placementGhost != null && placementGhost.gameObject.activeInHierarchy)
                ? placementGhost.transform.position
                : (toView != null ? toView.transform.position : Vector3.zero);
            Vector3 toPos = toView != null ? toView.transform.position : fromPos;

            // Hide the ghost during the tween so the flying counter is the only visual;
            // OnPlacementTweenComplete calls UpdateStatusUI which re-shows the ghost with
            // the next tone (or hides it permanently once both tones are placed).
            placementGhost?.SetVisible(false);
            ClearHighlights();

            _moveInProgress = true;
            RefreshUndoButton();

            int light = tone == Tone.Light ? 1 : 0;
            int dark = tone == Tone.Dark ? 1 : 0;

            var overlayParent = ResolveOverlayParent(toView);
            MovingCounter.Play(overlayParent, fromPos, toPos, light, dark,
                MoveTweenDuration, OnPlacementTweenComplete, withPhantom: false);
        }

        private void OnPlacementTweenComplete()
        {
            _moveInProgress = false;
            RefreshBoards();
            UpdateStatusUI();
            if (_gameState.CurrentPhase == GamePhase.Placement)
            {
                HighlightPlacementTargets();
            }
            else
            {
                // Placement just flipped to Movement. SBE itself is skipped here
                // (placement can't deadend or win per design), but the new Movement
                // phase is a valid moment to auto-skip if the player's whole board
                // is locked.
                ClearHighlights();
                if (!MaybeAutoSkipTurn())
                    HighlightMovablePieces();
            }
            RefreshUndoButton();
        }

        private void HandleMovementClick(SpaceId clicked)
        {
            var currentPlayer = _gameState.GetCurrentPlayer();
            if (currentPlayer == null)
                return;

            if (_selectedSpace == null)
            {
                SelectMovementSource(clicked, currentPlayer.Id);
            }
            else
            {
                var from = _selectedSpace.Value;
                var stack = _gameState.GetBoard(from.BoardId)?.GetStack(from.Id);
                if (stack == null)
                {
                    ClearMovementSelection();
                    HighlightMovablePieces();
                    return;
                }

                if (_selectedReach != null && _selectedReach.ContainsKey(clicked))
                {
                    ExecuteStackMove(from, clicked);
                }
                else if (clicked.Equals(from))
                {
                    // Tapping the same source returns the in-hand counters to their origin.
                    DeselectWithReturnTween();
                }
                else
                {
                    // Re-target: queue the new source click to fire once the return
                    // tween lands, so the player sees the counters come home before the
                    // new stack gets picked up.
                    _pendingRetarget = clicked;
                    DeselectWithReturnTween();
                }
            }
        }

        private void SelectMovementSource(SpaceId clicked, int playerId)
        {
            var movablePieces = _rules.GetMovablePieces(_gameState, playerId);
            if (!movablePieces.Contains(clicked))
                return;

            var stack = _gameState.GetBoard(clicked.BoardId)?.GetStack(clicked.Id);
            if (stack == null)
                return;

            _pickedUpLight = stack.GetMovableCount(Tone.Light);
            _pickedUpDark = stack.GetMovableCount(Tone.Dark);
            if (_pickedUpLight + _pickedUpDark == 0)
                return;

            // _selectedTone kept for legacy call sites but not load-bearing — targets are
            // identical across movable tones since reachability is positional, not tone-bound.
            _selectedTone = _pickedUpLight > 0 ? Tone.Light : Tone.Dark;
            _selectedSpace = clicked;

            // Fade only the picked-up counters so the origin reads as "these are in the
            // player's hand," while any locked counter at the bottom stays opaque — it
            // didn't get picked up, so it shouldn't look spectral. The opaque in-hand
            // ghost becomes the anchor for where the counters are now.
            SetSourcePhantom(FindSpaceView(clicked), _pickedUpLight + _pickedUpDark);

            int maxSteps = _pickedUpLight + _pickedUpDark;
            _selectedReach = _rules.GetReachableTargets(_gameState, clicked, _selectedTone, maxSteps);
            _selectedReachMax = maxSteps;
            HighlightReachableSpaces(_selectedReach, maxSteps, _selectedTone);
            HighlightSelectedSource();
            NotifyInHandGhost();
        }

        private void ClearMovementSelection()
        {
            _selectedSpace = null;
            _pickedUpLight = 0;
            _pickedUpDark = 0;
            _selectedReach = null;
            _selectedReachMax = 0;
            ClearHighlights();
            NotifyInHandGhost();
            ClearSourcePhantom();
        }

        private void SetSourcePhantom(SpaceView view, int topCount)
        {
            if (view == null) return;
            if (_sourcePhantomView != null && _sourcePhantomView != view)
                ClearSourcePhantom();
            view.SetPhantomCounters(topCount, SourcePhantomAlpha);
            _sourcePhantomView = view;
        }

        private void ClearSourcePhantom()
        {
            if (_sourcePhantomView == null) return;
            _sourcePhantomView.ClearPhantomCounters();
            _sourcePhantomView = null;
        }

        private List<SpaceId> GetStackValidTargets(SpaceId from, TokenStack stack)
        {
            // Reachability is positional — a stack's valid targets are the union across
            // movable tones, but both tones yield the same adjacency/cross-board set
            // when they can move, so whichever is movable suffices.
            if (stack.CanMove(Tone.Light))
                return _rules.GetValidMoveTargets(_gameState, from, Tone.Light);
            if (stack.CanMove(Tone.Dark))
                return _rules.GetValidMoveTargets(_gameState, from, Tone.Dark);
            return new List<SpaceId>();
        }

        private void ExecuteStackMove(SpaceId from, SpaceId clicked)
        {
            if (networkMode == NetworkMode.Network)
            {
                ExecuteStackMoveNetwork(from, clicked);
                return;
            }

            // Resolve the path first. Single-hop destinations keep the direct tween path
            // for continuity; multi-hop destinations replay as chained single-step moves
            // (one MoveToken batch per hop) so the domain sees the same sequence it would
            // have seen had the player clicked each intermediate space manually.
            List<SpaceId> path;
            if (_selectedReach != null && _selectedReach.TryGetValue(clicked, out var dist) && dist > 1)
            {
                path = _rules.FindShortestPath(_gameState, from, clicked, _selectedTone, _selectedReachMax);
                if (path == null || path.Count == 0)
                {
                    // Reach claimed this space but no path resolved — bail to a clean state.
                    ClearMovementSelection();
                    HighlightMovablePieces();
                    return;
                }
            }
            else
            {
                path = new List<SpaceId> { clicked };
            }

            var fromView = FindSpaceView(from);
            var toView = FindSpaceView(clicked);

            int lightPickedUp = _pickedUpLight;
            int darkPickedUp = _pickedUpDark;

            PushMoveUndo(from, clicked, lightPickedUp, darkPickedUp);

            int lightCarried = lightPickedUp;
            int darkCarried = darkPickedUp;
            int lightLeftOrigin = 0;
            int darkLeftOrigin = 0;
            var hopOrigin = from;
            int successfulHops = 0;
            // Seat is latched BEFORE the first MoveToken call. GameRules.MoveToken
            // never transitions turns on its own (only EndTurn does), but taking
            // the reading once up front keeps the shadow submissions on the right
            // seat regardless of any future mid-move turn-change surprises.
            var moverForShadow = _gameState.GetCurrentPlayer();
            int moverSeatIndex = moverForShadow != null ? moverForShadow.Id - 1 : -1;

            // Per-waypoint stack sizes. Index 0 is the liftoff size; each subsequent
            // entry is the carried size after landing at path[hop-1] — which includes
            // any same-tone pickups and excludes counters lost to opposite-tone clashes.
            var waypointStacks = new List<(int light, int dark)> { (lightPickedUp, darkPickedUp) };

            for (int hop = 0; hop < path.Count; hop++)
            {
                var hopTarget = path[hop];
                int hopLight = 0;
                int hopDark = 0;
                for (int i = 0; i < lightCarried; i++)
                {
                    if (_rules.MoveToken(_gameState, hopOrigin, hopTarget, Tone.Light) == null) break;
                    hopLight++;
                    // One shadow submission per single-counter commit — matches the
                    // one-action-per-MoveToken shape the server rules adapter sees.
                    _shadowSink?.ShadowMove(moverSeatIndex, BuildCurrentSpecState(), hopOrigin, hopTarget, Tone.Light);
                }
                for (int i = 0; i < darkCarried; i++)
                {
                    if (_rules.MoveToken(_gameState, hopOrigin, hopTarget, Tone.Dark) == null) break;
                    hopDark++;
                    _shadowSink?.ShadowMove(moverSeatIndex, BuildCurrentSpecState(), hopOrigin, hopTarget, Tone.Dark);
                }

                if (hopLight + hopDark == 0) break;

                // Record how many left the original source at the first successful hop;
                // subsequent hops carry forward whatever survived ResolveEntry clashes
                // plus any same-tone counters picked up at each pass-through space.
                if (hop == 0)
                {
                    lightLeftOrigin = hopLight;
                    darkLeftOrigin = hopDark;
                }

                successfulHops = hop + 1;

                // Carried count for the next hop is the full stack sitting at hopTarget —
                // post-pickup, post-clash. Using board state here means a 3-stack passing
                // through a same-tone 2-stack leaves the intermediate holding 5 and hops
                // forward with 5, matching the reach-extension model in GetReachableTargets.
                var targetBoard = _gameState.GetBoard(hopTarget.BoardId);
                var targetStack = targetBoard?.GetStack(hopTarget.Id);
                lightCarried = targetStack?.LightCount ?? hopLight;
                darkCarried = targetStack?.DarkCount ?? hopDark;
                waypointStacks.Add((lightCarried, darkCarried));
                hopOrigin = hopTarget;
            }

            if (lightLeftOrigin + darkLeftOrigin == 0)
            {
                // Nothing landed anywhere — drop the speculative frame and roll the
                // source view + ghost back to their pre-pickup state.
                _undoStack.Pop();
                ClearMovementSelection();
                RefreshUndoButton();
                HighlightMovablePieces();
                return;
            }

            int lightMoved = lightLeftOrigin;
            int darkMoved = darkLeftOrigin;

            // Animation endpoint is wherever the chain actually terminated (final
            // successful hop), not necessarily the clicked destination. If the chain
            // failed partway, animate only up to the last successful hop.
            var animationEnd = hopOrigin;
            var mover = _gameState.GetCurrentPlayer();
            if (mover != null)
            {
                LogEvent($"{mover.Name} moved {FormatStackCounts(lightMoved, darkMoved)}: {FormatSpace(from)} → {FormatSpace(animationEnd)}");
            }
            ClearMovementSelection();
            UpdateStatusUI();
            if (fromView != null)
            {
                var postMoveSource = _gameState.GetBoard(from.BoardId)?.GetStack(from.Id);
                if (postMoveSource != null) fromView.UpdateTokenDisplay(postMoveSource);
            }

            _moveInProgress = true;
            RefreshUndoButton();

            var overlayParent = ResolveOverlayParent(fromView ?? toView);

            // Build the animation waypoints from source through each successful hop's
            // center. Single-hop collapses to the legacy two-point tween.
            Vector3 startPos = fromView != null ? fromView.transform.position : Vector3.zero;
            if (successfulHops <= 1)
            {
                var endView = FindSpaceView(animationEnd);
                Vector3 endPos = endView != null ? endView.transform.position : startPos;
                MovingCounter.Play(overlayParent, startPos, endPos, lightMoved, darkMoved,
                    MoveTweenDuration, OnMoveTweenComplete, withPhantom: false);
            }
            else
            {
                var waypoints = new List<Vector3> { startPos };
                for (int i = 0; i < successfulHops; i++)
                {
                    var hopView = FindSpaceView(path[i]);
                    waypoints.Add(hopView != null ? hopView.transform.position : startPos);
                }
                // Slice waypointStacks to the successful hops (index 0 + successfulHops).
                var visualStacks = new List<(int light, int dark)>();
                int maxVisualHops = Mathf.Min(successfulHops + 1, waypointStacks.Count);
                for (int i = 0; i < maxVisualHops; i++) visualStacks.Add(waypointStacks[i]);
                MovingCounter.PlayPath(overlayParent, waypoints, visualStacks,
                    MoveTweenDuration, OnMoveTweenComplete);
            }
        }

        /// Network-mode (submit-only) variant of ExecuteStackMove. Mirrors
        /// the Local path's structure — resolve shortest path, apply one
        /// MoveToken per counter per hop — but never mutates _gameState:
        /// the per-hop carried count is computed against a throwaway clone
        /// so we know how many single-counter SubmitMove calls to enqueue
        /// at each waypoint without affecting rendering. The server sees
        /// the same sequence of actions Local mode would have submitted
        /// via the shadow path and resolves authoritatively in ClientSeq
        /// order; if its own ResolveEntry outcomes differ from the local
        /// speculation at an intermediate hop, later hops will rules-fail
        /// server-side and the echoes will surface the actual landing
        /// position. No undo frame, no tween — echoes drive the re-render.
        private void ExecuteStackMoveNetwork(SpaceId from, SpaceId clicked)
        {
            int lightPickedUp = _pickedUpLight;
            int darkPickedUp = _pickedUpDark;
            if (lightPickedUp + darkPickedUp == 0)
            {
                ClearMovementSelection();
                HighlightMovablePieces();
                return;
            }

            var mover = _gameState.GetCurrentPlayer();
            if (mover == null)
            {
                ClearMovementSelection();
                HighlightMovablePieces();
                return;
            }

            // Resolve the path: single-hop collapses to the single-entry
            // list, multi-hop uses the rule-layer shortest-path search
            // exactly like Local mode.
            List<SpaceId> path;
            if (_selectedReach != null
                && _selectedReach.TryGetValue(clicked, out var dist)
                && dist > 1)
            {
                path = _rules.FindShortestPath(_gameState, from, clicked, _selectedTone, _selectedReachMax);
                if (path == null || path.Count == 0)
                {
                    ClearMovementSelection();
                    HighlightMovablePieces();
                    return;
                }
            }
            else
            {
                path = new List<SpaceId> { clicked };
            }

            int seatIndex = _localSeatId - 1;

            // Speculative resolve against a clone so per-hop carried counts
            // include ResolveEntry survivors + same-tone pickups at pass-
            // through spaces, matching what FindShortestPath's reach model
            // assumed. The clone never leaks — rendering stays on _gameState
            // and the echo reconciles any divergence from the server's
            // authoritative resolution.
            var speculative = _gameState.Clone();

            int totalLightSubmitted = 0;
            int totalDarkSubmitted = 0;
            int carriedLight = lightPickedUp;
            int carriedDark = darkPickedUp;
            var hopOrigin = from;
            SpaceId lastReachedHop = from;
            int successfulHops = 0;

            for (int hop = 0; hop < path.Count; hop++)
            {
                var hopTarget = path[hop];
                int hopLight = 0;
                int hopDark = 0;
                int landedLight = 0;
                int landedDark = 0;

                for (int i = 0; i < carriedLight; i++)
                {
                    var resolved = _rules.MoveToken(speculative, hopOrigin, hopTarget, Tone.Light);
                    if (resolved == null) break;
                    hopLight++;
                    if (resolved.Result != MoveResult.Clear) landedLight++;
                    if (_shadowSink != null
                        && _shadowSink.SubmitMove(seatIndex, hopOrigin, hopTarget, Tone.Light))
                    {
                        _pendingSubmissions++;
                        totalLightSubmitted++;
                    }
                    else if (Application.isEditor)
                    {
                        UnityEngine.Debug.LogWarning(
                            "[net] move submission dropped (sink null or not ready)");
                    }
                }
                for (int i = 0; i < carriedDark; i++)
                {
                    var resolved = _rules.MoveToken(speculative, hopOrigin, hopTarget, Tone.Dark);
                    if (resolved == null) break;
                    hopDark++;
                    if (resolved.Result != MoveResult.Clear) landedDark++;
                    if (_shadowSink != null
                        && _shadowSink.SubmitMove(seatIndex, hopOrigin, hopTarget, Tone.Dark))
                    {
                        _pendingSubmissions++;
                        totalDarkSubmitted++;
                    }
                    else if (Application.isEditor)
                    {
                        UnityEngine.Debug.LogWarning(
                            "[net] move submission dropped (sink null or not ready)");
                    }
                }

                if (hopLight + hopDark == 0) break;

                // Clash-only hop: counters were submitted (server will echo the
                // clash) but none of ours landed, so the player did not arrive
                // at hopTarget and cannot continue the path from here. Matches
                // IsSpaceControlled's Clear-excludes-control invariant; without
                // this, lastReachedHop would drift forward to a space the
                // player didn't actually reach and "partial" logs would lie.
                if (landedLight + landedDark == 0) break;

                // Next hop carries the full stack at hopTarget — post-clash,
                // post-pickup. Reading from the speculative clone keeps this
                // aligned with Local mode's carried-count math.
                var targetBoard = speculative.GetBoard(hopTarget.BoardId);
                var targetStack = targetBoard?.GetStack(hopTarget.Id);
                carriedLight = targetStack?.LightCount ?? hopLight;
                carriedDark = targetStack?.DarkCount ?? hopDark;
                hopOrigin = hopTarget;
                lastReachedHop = hopTarget;
                successfulHops = hop + 1;
            }

            if (totalLightSubmitted + totalDarkSubmitted > 0)
            {
                string destination = successfulHops == path.Count
                    ? FormatSpace(clicked)
                    : $"{FormatSpace(lastReachedHop)} (partial)";
                LogEvent($"{mover.Name} → submit move " +
                         $"L{totalLightSubmitted}+D{totalDarkSubmitted} " +
                         $"{FormatSpace(from)}→{destination} hops={successfulHops}");
                // Cache landed-narration keyed to mover.Name captured here —
                // turn rotates on the EndTurn echo that arrives after the
                // move echoes, so _gameState.GetCurrentPlayer() by emit time
                // may no longer point at the mover.
                _pendingEchoNarration = $"{mover.Name} moved " +
                    $"{FormatStackCounts(totalLightSubmitted, totalDarkSubmitted)}: " +
                    $"{FormatSpace(from)} → {FormatSpace(lastReachedHop)}";
            }

            // Clear the in-hand selection immediately — the player has
            // committed the action and can't re-target while echoes are in
            // flight. Highlights go away for the same reason; the next
            // echo-driven refresh will rehighlight based on the server's
            // post-apply state.
            ClearMovementSelection();
            RefreshUndoButton();
        }

        private void DeselectWithReturnTween()
        {
            if (!_selectedSpace.HasValue)
            {
                ClearMovementSelection();
                return;
            }
            if (_pickedUpLight + _pickedUpDark == 0)
            {
                // Nothing visually lifted — fall back to an instant clear.
                ClearMovementSelection();
                HighlightMovablePieces();
                return;
            }

            var from = _selectedSpace.Value;
            var fromView = FindSpaceView(from);
            int lightReturn = _pickedUpLight;
            int darkReturn = _pickedUpDark;

            Vector3 cursorPos = (inHandGhost != null)
                ? inHandGhost.transform.position
                : (fromView != null ? fromView.transform.position : Vector3.zero);
            Vector3 toPos = fromView != null ? fromView.transform.position : cursorPos;

            // Hand off the visual to the flying stack: hide the in-hand ghost and
            // clear target highlights so the player's eye follows the counters home.
            _pickedUpLight = 0;
            _pickedUpDark = 0;
            NotifyInHandGhost();
            ClearHighlights();

            _moveInProgress = true;
            RefreshUndoButton();

            var overlayParent = ResolveOverlayParent(fromView);
            MovingCounter.Play(overlayParent, cursorPos, toPos, lightReturn, darkReturn,
                MoveTweenDuration, OnReturnTweenComplete, withPhantom: false);
        }

        private void OnReturnTweenComplete()
        {
            _moveInProgress = false;
            _selectedSpace = null;
            ClearSourcePhantom();
            RefreshUndoButton();

            if (_gameState == null || _gameState.GameOver)
            {
                _pendingRetarget = null;
                return;
            }

            if (_pendingRetarget.HasValue)
            {
                var next = _pendingRetarget.Value;
                _pendingRetarget = null;
                HandleMovementClick(next);
            }
            else
            {
                HighlightMovablePieces();
            }
        }

        private void NotifyInHandGhost()
        {
            if (inHandGhost == null) return;
            inHandGhost.SetStack(_pickedUpLight, _pickedUpDark);
        }

        private void EnsureInHandGhost()
        {
            if (inHandGhost != null) return;
            // Auto-spawn under the Canvas so existing scenes work without a setup patch.
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                foreach (var presenter in _boardPresenters.Values)
                {
                    canvas = presenter.GetComponentInParent<Canvas>();
                    if (canvas != null) break;
                }
            }
            if (canvas == null) return;

            var go = new GameObject("InHandGhost", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(canvas.transform, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(48f, 48f);
            inHandGhost = go.AddComponent<InHandGhost>();
        }

        private void EnsureStatusBanner()
        {
            if (statusBanner != null) return;
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                foreach (var presenter in _boardPresenters.Values)
                {
                    canvas = presenter.GetComponentInParent<Canvas>();
                    if (canvas != null) break;
                }
            }
            if (canvas == null) return;

            var go = new GameObject("StatusBanner", typeof(RectTransform), typeof(CanvasGroup));
            var rt = (RectTransform)go.transform;
            rt.SetParent(canvas.transform, false);
            // Last child so the banner paints above boards/counters.
            rt.SetAsLastSibling();
            statusBanner = go.AddComponent<StatusBanner>();
        }

        private void EnsureStatusLog()
        {
            if (statusLog != null)
            {
                statusLog.SetVisible(showEventLog);
                return;
            }
            if (!showEventLog) return;

            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                foreach (var presenter in _boardPresenters.Values)
                {
                    canvas = presenter.GetComponentInParent<Canvas>();
                    if (canvas != null) break;
                }
            }
            if (canvas == null) return;

            var go = new GameObject("StatusLog", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(canvas.transform, false);
            rt.SetAsLastSibling();
            statusLog = go.AddComponent<StatusLog>();
        }

        private void EnsureIdentityBadge()
        {
            // Local mode's current-player rotates every turn, so a "you are
            // Player N" badge would lie. The HUD's existing "Player: X" line
            // is the honest affordance in hotseat; skip the badge entirely.
            if (networkMode != NetworkMode.Network) return;
            if (identityBadge != null) return;

            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                foreach (var presenter in _boardPresenters.Values)
                {
                    canvas = presenter.GetComponentInParent<Canvas>();
                    if (canvas != null) break;
                }
            }
            if (canvas == null) return;

            var go = new GameObject("IdentityBadge", typeof(RectTransform), typeof(CanvasGroup));
            var rt = (RectTransform)go.transform;
            rt.SetParent(canvas.transform, false);
            // First child of the canvas so banner/log paint above it. The
            // badge is a passive label — it must never eat clicks destined
            // for a space below.
            rt.SetAsFirstSibling();
            identityBadge = go.AddComponent<IdentityBadge>();
        }

        /// Runs SBE on the current state and narrates any eliminations or game-end. No-op
        /// during Placement phase per design — placement can't deadend or win by itself.
        private void RunStateBasedEffects()
        {
            if (_gameState == null) return;
            if (_gameState.CurrentPhase == GamePhase.Placement) return;
            var result = _gameState.ApplyStateBasedEffects();
            NarrateStateBasedEffects(result);
        }

        /// Emits a banner message for end-of-turn overflow trimming. Called with
        /// the player who JUST ended their turn (CurrentPlayer has already
        /// advanced by this point).
        private void NarrateOverflowCap(StateBasedEffectsResult result, Player endingPlayer)
        {
            if (result == null || result.OverflowTrims == null || result.OverflowTrims.Count == 0) return;
            int total = 0;
            foreach (var t in result.OverflowTrims) total += t.RemovedCount;
            var name = endingPlayer != null ? endingPlayer.Name : "Player";
            var noun = total == 1 ? "counter" : "counters";
            ShowBanner($"{name}: {total} {noun} cleared (overflow cap)");
            // Per-space detail goes to the log only — keeps the banner punchy while the
            // log carries enough info to retrace which spaces actually overflowed.
            foreach (var t in result.OverflowTrims)
            {
                LogEvent($"  ↳ {FormatSpace(t.Space)}: −{t.RemovedCount} {t.Tone}");
            }
        }

        /// Network-mode counterpart to the Local-mode narration path. Diffs a
        /// pre-apply snapshot against the newly-applied server state and emits
        /// banners for new eliminations, game-over/winner, and — on end-of-turn
        /// echoes — overflow trims on the ending player's board. Turn rotation
        /// (preCurrentPlayerId != post.CurrentPlayerId) is the discriminator
        /// for end-of-turn echoes; moves don't rotate the turn, so stack drops
        /// on a move echo are pickups (not overflow) and must not narrate.
        private void NarrateServerStateDiff(
            HashSet<int> preEliminated,
            int? preCurrentPlayerId,
            bool preGameOver,
            Dictionary<int, (int light, int dark)> preEndingBoardStacks)
        {
            if (_gameState == null) return;
            var result = new StateBasedEffectsResult();
            if (_gameState.Players != null)
            {
                foreach (var p in _gameState.Players)
                {
                    if (p.IsEliminated && !preEliminated.Contains(p.Id))
                        result.NewlyEliminatedPlayerIds.Add(p.Id);
                }
            }
            result.GameEnded = _gameState.GameOver && !preGameOver;
            result.WinnerId = _gameState.WinnerId;

            bool turnRotated = preCurrentPlayerId.HasValue
                && preCurrentPlayerId.Value != _gameState.CurrentPlayerId;
            Player endingPlayer = null;
            if (turnRotated && preEndingBoardStacks != null)
            {
                int endingPlayerId = preCurrentPlayerId.Value;
                var endingBoard = _gameState.Boards?.FirstOrDefault(b => b.PlayerId == endingPlayerId);
                if (endingBoard?.Spaces != null)
                {
                    foreach (var kv in endingBoard.Spaces)
                    {
                        if (!preEndingBoardStacks.TryGetValue(kv.Key, out var pre)) continue;
                        int lightDrop = pre.light - kv.Value.LightCount;
                        int darkDrop = pre.dark - kv.Value.DarkCount;
                        if (lightDrop > 0)
                            result.OverflowTrims.Add(new OverflowTrim
                            {
                                Space = new SpaceId(endingBoard.BoardId, kv.Key),
                                Tone = Tone.Light,
                                RemovedCount = lightDrop
                            });
                        if (darkDrop > 0)
                            result.OverflowTrims.Add(new OverflowTrim
                            {
                                Space = new SpaceId(endingBoard.BoardId, kv.Key),
                                Tone = Tone.Dark,
                                RemovedCount = darkDrop
                            });
                    }
                }
                endingPlayer = _gameState.Players?.FirstOrDefault(p => p.Id == endingPlayerId);
            }

            if (result.OverflowTrims.Count > 0)
                NarrateOverflowCap(result, endingPlayer);
            NarrateStateBasedEffects(result);
        }

        private void NarrateStateBasedEffects(StateBasedEffectsResult result)
        {
            if (result == null || !result.HasAnyEffect) return;
            foreach (var pid in result.NewlyEliminatedPlayerIds)
            {
                var p = _gameState.Players.FirstOrDefault(x => x.Id == pid);
                ShowBanner(p != null ? $"{p.Name} eliminated." : $"Player {pid} eliminated.");
            }
            if (result.GameEnded)
            {
                if (result.WinnerId.HasValue)
                {
                    var winner = _gameState.Players.FirstOrDefault(x => x.Id == result.WinnerId.Value);
                    ShowBanner(winner != null ? $"{winner.Name} wins!" : $"Player {result.WinnerId.Value} wins!");
                }
                else
                {
                    ShowBanner("Game Over.");
                }
            }
        }

        /// Narrates and ends the turn if the current player has no legal moves. Returns
        /// true if the turn was auto-skipped so the caller can skip its usual end-of-tick
        /// highlight pass (OnEndTurnClicked does its own).
        private bool MaybeAutoSkipTurn()
        {
            if (_rules == null || _gameState == null) return false;
            if (!_rules.ShouldAutoSkipTurn(_gameState)) return false;
            var p = _gameState.GetCurrentPlayer();
            ShowBanner(p != null ? $"{p.Name} has no legal moves — turn skipped." : "Turn skipped.");
            OnEndTurnClicked();
            return true;
        }

        private void ShowBanner(string message)
        {
            if (statusBanner != null) statusBanner.Enqueue(message);
            // Also append to the log so the persistent record is complete —
            // banner fades out, but a playtest viewer may want to scroll back
            // and re-read what happened.
            LogEvent(message);
        }

        /// Routine event recording — placements, moves, turn-ends, undos — routed
        /// to the persistent corner log rather than the fade-out banner. Critical
        /// moments still go through ShowBanner so they grab attention.
        private void LogEvent(string message)
        {
            if (!showEventLog) return;
            if (statusLog != null) statusLog.Append(message);
        }

        /// Wheel-color-named space reference. Center uses "[Player] Core"; every
        /// other space carries the board owner as a possessive prefix so cross-board
        /// references are unambiguous (e.g., "Alice's Rha Bridge"). Falls back to the
        /// raw "B{board}:{id}" form if state lookup fails — only happens before the
        /// game is initialized.
        private string FormatSpace(SpaceId id)
        {
            var board = _gameState?.GetBoard(id.BoardId);
            if (board == null || !board.SpaceMetadata.TryGetValue(id.Id, out var meta))
                return $"B{id.BoardId}:{id.Id}";

            var owner = _gameState.Players?.FirstOrDefault(p => p.Id == board.PlayerId);
            string ownerName = owner?.Name;

            if (meta.Type == SpaceType.Center)
                return SpaceNamer.Name(id.Id, meta, ownerName);

            string spaceName = SpaceNamer.Name(id.Id, meta);
            return string.IsNullOrEmpty(ownerName) ? spaceName : $"{ownerName}'s {spaceName}";
        }

        private static string FormatStackCounts(int light, int dark)
        {
            if (light > 0 && dark > 0) return $"{light}L+{dark}D";
            if (light > 0) return $"{light}L";
            if (dark > 0) return $"{dark}D";
            return "0";
        }

        private void EnsurePlacementGhost()
        {
            if (placementGhost != null) return;
            // Mirror the InHandGhost auto-spawn — scenes that never ran the setup utility
            // still get a working placement preview without a manual wiring step.
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                foreach (var presenter in _boardPresenters.Values)
                {
                    canvas = presenter.GetComponentInParent<Canvas>();
                    if (canvas != null) break;
                }
            }
            if (canvas == null) return;

            var go = new GameObject("PlacementGhost", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(canvas.transform, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(48f, 48f);
            placementGhost = go.AddComponent<PlacementGhost>();
        }

        private void HighlightSelectedSource()
        {
            if (!_selectedSpace.HasValue)
                return;

            foreach (var presenter in _boardPresenters.Values)
            {
                presenter.HighlightSelection(_selectedSpace);
            }
        }

        private void HighlightSpaces(List<SpaceId> spaces)
        {
            ClearHighlights();
            foreach (var kvp in _boardPresenters)
            {
                kvp.Value.HighlightValidMoves(spaces);
            }
        }

        private void HighlightReachableSpaces(Dictionary<SpaceId, int> distances, int maxDistance, Tone tone)
        {
            ClearHighlights();
            foreach (var kvp in _boardPresenters)
            {
                kvp.Value.HighlightValidMovesWithDistance(distances, maxDistance, tone);
            }
        }

        private void ClearHighlights()
        {
            foreach (var kvp in _boardPresenters)
            {
                kvp.Value.ClearAllStates();
            }
        }

        private void RefreshBoards()
        {
            foreach (var presenter in _boardPresenters.Values)
            {
                presenter.UpdateView();
            }
            RefreshEliminatedOverlays();
        }

        /// Drives BoardPresenter.SetEliminated from the authoritative Player list.
        /// Runs on every state refresh (local and Network) so elimination and any
        /// rare un-elimination path stay in sync with the model. Board stays
        /// playable — overlay is cosmetic only.
        private void RefreshEliminatedOverlays()
        {
            if (_gameState == null) return;
            foreach (var kvp in _boardPresenters)
            {
                var board = _gameState.GetBoard(kvp.Key);
                if (board == null) continue;
                var player = _gameState.Players.Find(p => p.Id == board.PlayerId);
                kvp.Value.SetEliminated(player != null && player.IsEliminated);
            }
        }

        private void HighlightPlacementTargets()
        {
            var currentPlayer = _gameState.GetCurrentPlayer();
            if (currentPlayer == null)
                return;

            var targets = _rules.GetValidPlacementTargets(_gameState, currentPlayer.Id);
            if (targets == null || targets.Count == 0)
            {
                ClearHighlights();
                return;
            }

            // Tone the placement ripple to whichever energy comes next: Light first, then
            // Dark, mirroring HandlePlacementClick's fixed order.
            Tone placementTone = _gameState.CurrentTurnPlacements.Exists(p => p.Tone == Tone.Light)
                ? Tone.Dark
                : Tone.Light;

            var playerBoard = _gameState.GetBoardForPlayer(currentPlayer.Id);
            if (playerBoard == null)
            {
                ClearHighlights();
                return;
            }

            // BFS hop distances from the player's core (space 0) so the pulse ripples
            // outward from the center during placement, matching the source-origin
            // ripple used during movement.
            var distancesFromCore = ComputeHopDistances(playerBoard, 0);
            var targetDistances = new Dictionary<SpaceId, int>();
            int maxDistance = 0;
            foreach (var target in targets)
            {
                if (target.BoardId != playerBoard.BoardId) continue;
                int dist = distancesFromCore.TryGetValue(target.Id, out var d) ? d : 1;
                if (dist <= 0) dist = 1;
                targetDistances[target] = dist;
                if (dist > maxDistance) maxDistance = dist;
            }
            if (targetDistances.Count == 0 || maxDistance <= 0)
            {
                ClearHighlights();
                return;
            }

            ClearHighlights();
            foreach (var kvp in _boardPresenters)
            {
                kvp.Value.HighlightValidMovesWithDistance(targetDistances, maxDistance, placementTone, uniformIntensity: true);
            }
        }

        /// BFS over a single board's adjacency, returning hop distance from `startSpaceId`
        /// to every reachable space (including the start itself at distance 0). Used by
        /// the placement ripple so targets pulse in rings outward from the core.
        private static Dictionary<int, int> ComputeHopDistances(BoardState board, int startSpaceId)
        {
            var distances = new Dictionary<int, int> { { startSpaceId, 0 } };
            var queue = new Queue<int>();
            queue.Enqueue(startSpaceId);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                int currentDist = distances[current];
                if (!board.Adjacency.TryGetValue(current, out var neighbors)) continue;
                foreach (var n in neighbors)
                {
                    if (distances.ContainsKey(n)) continue;
                    distances[n] = currentDist + 1;
                    queue.Enqueue(n);
                }
            }
            return distances;
        }

        private void HighlightMovablePieces()
        {
            if (_gameState.CurrentPhase != GamePhase.Movement)
                return;

            var currentPlayer = _gameState.GetCurrentPlayer();
            if (currentPlayer == null)
                return;

            var movable = _rules.GetMovablePieces(_gameState, currentPlayer.Id);
            // Source breathe, not destination pulse — readers can tell at a glance which
            // stacks they can pick up vs. where a selected stack can go.
            ClearHighlights();
            foreach (var presenter in _boardPresenters.Values)
            {
                presenter.HighlightMovableSources(movable);
            }
        }

        private void OnEndTurnClicked()
        {
            if (_gameState == null || _gameState.GameOver)
                return;

            if (_moveInProgress)
                return;

            // Network mode: same locks as OnSpaceClicked. The Space-key
            // shortcut in Update() would otherwise bypass this.
            if (networkMode == NetworkMode.Network)
            {
                if (_pendingSubmissions > 0) return;
                if (!IsLocalSeatsTurn()) return;
            }

            if (_gameState.CurrentPhase == GamePhase.Placement && !_gameState.IsPlacementComplete())
            {
                // Must place both tones before ending the turn.
                return;
            }

            ClearMovementSelection();

            var endingPlayer = _gameState.GetCurrentPlayer();
            // Capture the ending seat BEFORE EndTurn rotates CurrentPlayerId so
            // the shadow submission is addressed to the player who actually
            // ended their turn, not the next one up.
            int endingSeatIndex = endingPlayer != null ? endingPlayer.Id - 1 : -1;

            if (networkMode == NetworkMode.Network)
            {
                // Authority flip. No local EndTurn, no undo clear, no
                // hot-seat _localSeatId rotation — the echo carries the
                // rotated state and ApplyServerStateAndRefresh picks it up.
                int submitSeat = _localSeatId - 1;
                if (_shadowSink != null && _shadowSink.SubmitEndTurn(submitSeat))
                {
                    _pendingSubmissions++;
                    if (endingPlayer != null)
                        LogEvent($"{endingPlayer.Name} → submit end turn");
                    ClearHighlights();
                    RefreshUndoButton();
                }
                else if (Application.isEditor)
                {
                    UnityEngine.Debug.LogWarning(
                        "[net] end-turn submission dropped (sink null or not ready)");
                }
                return;
            }

            var endOfTurn = _gameState.EndTurn();
            NarrateOverflowCap(endOfTurn, endingPlayer);
            var nextPlayer = _gameState.GetCurrentPlayer();
            _shadowSink?.ShadowEndTurn(endingSeatIndex, BuildCurrentSpecState());

            // Hot-seat: the local seat follows the active turn so the same keyboard/mouse
            // drives whichever player is up. Online transport will stop firing this line
            // (or it becomes a no-op when the seat is pinned at session join).
            _localSeatId = _gameState.CurrentPlayerId;
            if (endingPlayer != null)
            {
                if (!_gameState.GameOver && nextPlayer != null && nextPlayer.Id != endingPlayer.Id)
                    LogEvent($"{endingPlayer.Name} ended turn → {nextPlayer.Name}");
                else if (!_gameState.GameOver)
                    LogEvent($"{endingPlayer.Name} ended turn");
            }

            // Turn boundaries invalidate undo history — the prior player cannot rewind
            // into the next player's turn.
            _undoStack.Clear();
            RefreshUndoButton();

            RefreshBoards();
            UpdateStatusUI();

            if (!_gameState.GameOver)
            {
                if (_gameState.CurrentPhase == GamePhase.Placement)
                {
                    HighlightPlacementTargets();
                }
                else
                {
                    HighlightMovablePieces();
                }
            }
        }

        private void PushPlacementUndo()
        {
            if (_gameState == null)
                return;
            _undoStack.Push(new UndoFrame { State = _gameState.Clone(), HasAnimation = false });
        }

        private void PushMoveUndo(SpaceId from, SpaceId to, int light, int dark)
        {
            if (_gameState == null)
                return;
            _undoStack.Push(new UndoFrame
            {
                State = _gameState.Clone(),
                HasAnimation = true,
                From = from,
                To = to,
                Light = light,
                Dark = dark,
            });
        }

        /// Local mid-turn rewind. Only valid when this client holds the current turn
        /// (IsLocalSeatsTurn()) — the undo stack is a per-seat, pre-submit buffer and
        /// cannot rewind commits made by another player. Under the future server-
        /// authoritative model even your own actions are server-committed by the time
        /// you see them, so cross-turn or cross-client rollback routes through
        /// RequestTakeback() instead; this path stays valid for pre-submit local-only
        /// action batching.
        private void OnUndoClicked()
        {
            if (_gameState == null)
                return;

            if (_moveInProgress)
                return;

            // Network mode: the undo button is repurposed as the takeback
            // trigger. Server-committed actions can't be popped from a local
            // buffer, so the click submits a 1-step takeback request and the
            // server decides (auto-grant same-turn, route for consent, or
            // reject). RequestTakeback itself gates on _pendingSubmissions,
            // sink readiness, and increments the UI lock.
            if (networkMode == NetworkMode.Network)
            {
                RequestTakeback();
                return;
            }

            if (_undoStack.Count == 0)
                return;

            // Controller-layer gate: undo only applies to the local seat's own
            // in-flight actions. Hot-seat satisfies this trivially (local seat tracks
            // current player); online mode will reject clicks during the remote turn.
            if (!IsLocalSeatsTurn())
                return;

            var frame = _undoStack.Pop();
            LogEvent(frame.HasAnimation
                ? $"Undo: {FormatStackCounts(frame.Light, frame.Dark)} {FormatSpace(frame.From)} ← {FormatSpace(frame.To)}"
                : "Undo: placement");

            // Cancel any in-flight selection so its drained source + ghost don't
            // linger through the reverse tween. The animation takes over from here.
            ClearMovementSelection();

            if (frame.HasAnimation && !_gameState.GameOver)
            {
                PlayReverseMoveTween(frame);
            }
            else
            {
                ApplyUndoState(frame.State);
            }

            RefreshUndoButton();
        }

        /// Online takeback entry point. Unlike OnUndoClicked (which pops from
        /// a local pre-submit buffer), a takeback request is addressed to the
        /// server because every action is already committed by the time the
        /// client sees its effect. The server decides policy: auto-grant
        /// same-turn, route to opponent for consent, or reject outright. The
        /// outcome lands as either an OnServerTakeback broadcast (granted —
        /// carries post-rewind state) or an OnServerTakebackReply (denied /
        /// pending). Returns silently in Local mode — undo already covers
        /// that path from a pre-submit buffer.
        public void RequestTakeback(int stepsRequested = 1, string reason = null)
        {
            if (networkMode != NetworkMode.Network)
            {
#if UNITY_EDITOR
                UnityEngine.Debug.LogWarning(
                    "GameController.RequestTakeback: ignored in Local mode — use Undo for pre-submit rewind.");
#endif
                return;
            }

            if (_shadowSink == null) return;

            // Hold off while a prior action is still in flight — the server
            // would see the takeback racing the pending echo and the ordering
            // is ambiguous. A user pressing takeback twice would otherwise
            // stack two submissions; this cap keeps the in-flight state
            // single-entry.
            if (_pendingSubmissions > 0) return;

            int seatIndex = _localSeatId - 1;
            if (_shadowSink.SubmitTakeback(seatIndex, stepsRequested, reason ?? string.Empty))
            {
                _pendingSubmissions++;
                LogEvent($"↶ submit takeback (steps={stepsRequested})");
                RefreshUndoButton();
            }
            else if (Application.isEditor)
            {
                UnityEngine.Debug.LogWarning(
                    "[net] takeback submission dropped (sink null or not ready)");
            }
        }

        private void PlayReverseMoveTween(UndoFrame frame)
        {
            var fromView = FindSpaceView(frame.From);
            var toView = FindSpaceView(frame.To);
            Vector3 startPos = toView != null ? toView.transform.position : Vector3.zero;
            Vector3 endPos = fromView != null ? fromView.transform.position : startPos;

            // Drain the moved counters off the destination view so the flying stack
            // isn't visually duplicated by the still-visible counters at the destination.
            // Capture moves restore their cleared opposing counters on landing via
            // CopyFrom — a brief pop-in is acceptable for that edge case.
            if (toView != null)
            {
                var destStack = _gameState.GetBoard(frame.To.BoardId)?.GetStack(frame.To.Id);
                if (destStack != null)
                {
                    var draining = destStack.Clone();
                    for (int i = 0; i < frame.Light; i++) draining.RemoveOne(Tone.Light);
                    for (int i = 0; i < frame.Dark; i++) draining.RemoveOne(Tone.Dark);
                    toView.UpdateTokenDisplay(draining);
                }
            }

            _moveInProgress = true;
            RefreshUndoButton();

            var overlayParent = ResolveOverlayParent(toView ?? fromView);
            var captured = frame;
            MovingCounter.Play(overlayParent, startPos, endPos, frame.Light, frame.Dark,
                MoveTweenDuration, () => OnUndoTweenComplete(captured), withPhantom: false);
        }

        private void OnUndoTweenComplete(UndoFrame frame)
        {
            _moveInProgress = false;
            ApplyUndoState(frame.State);
            RefreshUndoButton();
        }

        private void ApplyUndoState(GameState snapshot)
        {
            _gameState.CopyFrom(snapshot);
            RefreshBoards();
            UpdateStatusUI();

            if (!_gameState.GameOver)
            {
                if (_gameState.CurrentPhase == GamePhase.Placement)
                {
                    HighlightPlacementTargets();
                }
                else
                {
                    HighlightMovablePieces();
                }
            }
        }

        private void RefreshUndoButton()
        {
            if (undoButton != null)
            {
                // Mirror the OnUndoClicked gate so "button says yes" and
                // "controller says no" never disagree.
                //   Local mode: pre-submit undo stack must have entries and
                //     this seat must hold the current turn.
                //   Network mode: takeback request. Enable whenever a
                //     submission isn't already in flight and the sink is
                //     attached — turn ownership is a server policy call, not
                //     a client-side gate.
                bool canUndo = networkMode == NetworkMode.Local
                    ? _undoStack.Count > 0 && IsLocalSeatsTurn()
                    : _shadowSink != null && _pendingSubmissions == 0;
                undoButton.interactable = canUndo && !_moveInProgress;

                // The button's click handler routes to either OnUndoClicked's
                // local-buffer pop or RequestTakeback depending on mode. The
                // label follows the same split so the user sees what the
                // button will actually do rather than the legacy name baked
                // into the scene asset.
                var label = undoButton.GetComponentInChildren<Text>(includeInactive: true);
                if (label != null)
                {
                    string desired = networkMode == NetworkMode.Network ? "Takeback" : "Undo";
                    if (label.text != desired) label.text = desired;
                }
            }
            if (endTurnButton != null)
            {
                // End-turn must also respect the Network-mode pending-
                // submission lock. Placement-completeness and move-in-progress
                // gates are still handled inside OnEndTurnClicked itself.
                endTurnButton.interactable =
                    !(networkMode == NetworkMode.Network && _pendingSubmissions > 0);
            }
        }

        private void UpdateStatusUI()
        {
            gameHud?.UpdateHud(_gameState);
            placementGhost?.Refresh(_gameState, _localSeatId);
            UpdateIdentityBadge();
        }

        private void UpdateIdentityBadge()
        {
            if (identityBadge == null) return;
            // Hotseat has no stable "you" — suppress the badge entirely.
            if (networkMode != NetworkMode.Network)
            {
                identityBadge.SetVisible(false);
                return;
            }
            if (_gameState == null)
            {
                identityBadge.SetVisible(false);
                return;
            }
            var localPlayer = _gameState.Players?.FirstOrDefault(p => p.Id == _localSeatId);
            var name = localPlayer != null ? localPlayer.Name : $"Player{_localSeatId}";
            identityBadge.SetText($"You are {name}");
            identityBadge.SetVisible(true);
        }
    }
}
