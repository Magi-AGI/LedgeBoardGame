using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Models.Spec;
using Magi.LedgeBoardGame.Rules;
using Magi.LedgeBoardGame.Board;

namespace Magi.LedgeBoardGame
{
    public class GameController : MonoBehaviour
    {
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
        [Tooltip("When on, records placements/moves/turn-ends/undos to the on-screen log panel. Leave on for playtest/video; turn off to hide the panel during normal play.")]
        [SerializeField] private bool showEventLog = true;
        [SerializeField] private Tone defaultMovementTone = Tone.Light;

        private GameState _gameState;
        private GameRules _rules;
        private readonly Dictionary<int, BoardPresenter> _boardPresenters = new Dictionary<int, BoardPresenter>();
        private SpaceId? _selectedSpace;
        private Tone _selectedTone = Tone.Light;
        private int _pickedUpLight;
        private int _pickedUpDark;
        private readonly Stack<UndoFrame> _undoStack = new Stack<UndoFrame>();
        private bool _moveInProgress;
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

        private void Start()
        {
            var players = new List<Player>
            {
                new Player(1, "Player1", 0),
                new Player(2, "Player2", 1)
            };

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

            if (multiBoardLayout == null)
            {
                multiBoardLayout = GetComponent<Board.MultiBoardLayout>();
                if (multiBoardLayout == null)
                {
                    multiBoardLayout = gameObject.AddComponent<Board.MultiBoardLayout>();
                }
            }

            CreateBoardPresenters();

            EnsureInHandGhost();
            EnsurePlacementGhost();
            EnsureStatusBanner();
            EnsureStatusLog();

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

            foreach (var board in _gameState.Boards)
            {
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

            // Update multi-board layout positions if present
            if (multiBoardLayout != null)
            {
                multiBoardLayout.Refresh();
            }

            gameHud?.UpdateHud(_gameState);
        }

        private void OnSpaceClicked(SpaceView view)
        {
            if (view == null || _gameState == null)
                return;

            // Gate clicks during a move-tween so the player can't queue a second move
            // before the current counter has landed.
            if (_moveInProgress)
                return;

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

            if (_rules.CanPlaceToken(_gameState, target, toneToPlace))
            {
                PushPlacementUndo();
                var move = _rules.PlaceToken(_gameState, target, toneToPlace);
                if (move != null)
                {
                    LogEvent($"{currentPlayer.Name} placed {toneToPlace} at {FormatSpace(target)}");
                    PlayPlacementTween(target, toneToPlace);
                }
                else
                {
                    // Placement actually failed — drop the speculative snapshot.
                    _undoStack.Pop();
                    RefreshUndoButton();
                }
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
                }
                for (int i = 0; i < darkCarried; i++)
                {
                    if (_rules.MoveToken(_gameState, hopOrigin, hopTarget, Tone.Dark) == null) break;
                    hopDark++;
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

            if (_gameState.CurrentPhase == GamePhase.Placement && !_gameState.IsPlacementComplete())
            {
                // Must place both tones before ending the turn.
                return;
            }

            ClearMovementSelection();

            var endingPlayer = _gameState.GetCurrentPlayer();
            var endOfTurn = _gameState.EndTurn();
            NarrateOverflowCap(endOfTurn, endingPlayer);
            var nextPlayer = _gameState.GetCurrentPlayer();
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

        private void OnUndoClicked()
        {
            if (_undoStack.Count == 0 || _gameState == null)
                return;

            if (_moveInProgress)
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
                undoButton.interactable = _undoStack.Count > 0 && !_moveInProgress;
            }
        }

        private void UpdateStatusUI()
        {
            gameHud?.UpdateHud(_gameState);
            placementGhost?.Refresh(_gameState);
        }
    }
}
