using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
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
        private const float MoveTweenDuration = 0.28f;

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
        }

        private void OnDestroy()
        {
            SpaceClickedEvent.Unregister(OnSpaceClicked);
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

                presenterInstance.Initialize(board);
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
            // before the current chip has landed.
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
            // Destination was held at its pre-move state during the tween so the chips
            // read as arriving — now that they've landed, catch every board up to state.
            RefreshBoards();
            if (_gameState == null || _gameState.GameOver)
            {
                RefreshUndoButton();
                return;
            }
            HighlightMovablePieces();
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
                    RefreshBoards();
                    UpdateStatusUI();
                    if (_gameState.CurrentPhase == GamePhase.Placement)
                    {
                        HighlightPlacementTargets();
                    }
                    else
                    {
                        ClearHighlights();
                        HighlightMovablePieces();
                    }
                    RefreshUndoButton();
                }
                else
                {
                    // Placement actually failed — drop the speculative snapshot.
                    _undoStack.Pop();
                    RefreshUndoButton();
                }
            }
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

                var targets = GetStackValidTargets(from, stack);
                if (targets.Contains(clicked))
                {
                    ExecuteStackMove(from, clicked);
                }
                else if (clicked.Equals(from))
                {
                    // Tapping the same source returns the in-hand chips to their origin.
                    DeselectWithReturnTween();
                }
                else
                {
                    // Re-target: queue the new source click to fire once the return
                    // tween lands, so the player sees the chips come home before the
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

            // Drain the source view immediately so the in-hand ghost + drained space
            // read as "chips have been lifted." Model stays untouched; rules still see
            // the full stack for MoveToken.
            var fromView = FindSpaceView(clicked);
            if (fromView != null)
            {
                var draining = stack.Clone();
                for (int i = 0; i < _pickedUpLight; i++) draining.RemoveOne(Tone.Light);
                for (int i = 0; i < _pickedUpDark; i++) draining.RemoveOne(Tone.Dark);
                fromView.UpdateTokenDisplay(draining);
            }

            var targets = GetStackValidTargets(clicked, stack);
            HighlightSpaces(targets);
            HighlightSelectedSource();
            NotifyInHandGhost();
        }

        private void ClearMovementSelection()
        {
            var prior = _selectedSpace;
            _selectedSpace = null;
            _pickedUpLight = 0;
            _pickedUpDark = 0;
            ClearHighlights();
            NotifyInHandGhost();

            // Restore the source view if we drained it on pickup and are clearing
            // without a return tween (e.g., failed execute, undo, end-turn).
            if (prior.HasValue && _gameState != null)
            {
                var view = FindSpaceView(prior.Value);
                var stack = _gameState.GetBoard(prior.Value.BoardId)?.GetStack(prior.Value.Id);
                if (view != null && stack != null) view.UpdateTokenDisplay(stack);
            }
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
            var fromView = FindSpaceView(from);
            var toView = FindSpaceView(clicked);

            // Flying stack originates at the cursor — that's where the chips visually
            // live once the source has drained on pickup. Fall back to the source view
            // if the in-hand ghost isn't wired up (editor setups without auto-spawn).
            Vector3 fromPos = (inHandGhost != null)
                ? inHandGhost.transform.position
                : (fromView != null ? fromView.transform.position : Vector3.zero);
            Vector3 toPos = toView != null ? toView.transform.position : Vector3.zero;

            int lightToMove = _pickedUpLight;
            int darkToMove = _pickedUpDark;

            PushMoveUndo(from, clicked, lightToMove, darkToMove);

            int lightMoved = 0;
            int darkMoved = 0;
            for (int i = 0; i < lightToMove; i++)
            {
                if (_rules.MoveToken(_gameState, from, clicked, Tone.Light) == null) break;
                lightMoved++;
            }
            for (int i = 0; i < darkToMove; i++)
            {
                if (_rules.MoveToken(_gameState, from, clicked, Tone.Dark) == null) break;
                darkMoved++;
            }

            if (lightMoved + darkMoved == 0)
            {
                // Nothing landed — drop the speculative frame and roll the source view
                // + ghost back to their pre-pickup state.
                _undoStack.Pop();
                ClearMovementSelection();
                RefreshUndoButton();
                HighlightMovablePieces();
                return;
            }

            ClearMovementSelection();

            // Source view is already drained from SelectMovementSource; this repaint
            // only matters if rules partially rejected (shouldn't happen today) so the
            // view matches the model.
            if (fromView != null)
            {
                var postMoveFrom = _gameState.GetBoard(from.BoardId)?.GetStack(from.Id);
                if (postMoveFrom != null) fromView.UpdateTokenDisplay(postMoveFrom);
            }
            UpdateStatusUI();

            _moveInProgress = true;
            RefreshUndoButton();
            var overlayParent = ResolveOverlayParent(fromView ?? toView);
            // No phantom on the forward tween — the drained source already reads as
            // "these chips have been lifted," so a translucent copy would double up.
            MovingCounter.Play(overlayParent, fromPos, toPos, lightMoved, darkMoved,
                MoveTweenDuration, OnMoveTweenComplete, withPhantom: false);
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
            // clear target highlights so the player's eye follows the chips home.
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

            var prior = _selectedSpace;
            _selectedSpace = null;

            if (prior.HasValue && _gameState != null)
            {
                var view = FindSpaceView(prior.Value);
                var stack = _gameState.GetBoard(prior.Value.BoardId)?.GetStack(prior.Value.Id);
                if (view != null && stack != null) view.UpdateTokenDisplay(stack);
            }

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
            HighlightSpaces(targets);
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

            _gameState.EndTurn();

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

            // Drain the moved chips off the destination view so the flying stack
            // isn't visually duplicated by the still-visible chips at the destination.
            // Capture moves restore their cleared opposing chips on landing via
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
