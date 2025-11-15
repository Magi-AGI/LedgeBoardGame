## LedgeBoardGame – Manual Unity Setup for Playable Scene

This document describes how to wire the existing scripts into a playable Unity scene. It separates what you must do in the editor (development time) from what is created and driven at runtime by the code.

---

### Development-Time Setup (Editor / Inspector)

#### 1. Scene & UI Basics

- Create or open a 2D UI scene.
- Ensure the scene has:
  - A `Canvas` (Screen Space – Overlay is fine) with a `GraphicRaycaster`.
  - An `EventSystem` in the hierarchy so UI clicks work.

#### 2. GameController GameObject

- Create an empty GameObject named `GameController` at the root.
- Add the `GameController` component (`Magi.LedgeBoardGame.GameController` in `Assets/_Project/Scripts/Scripts/GameController.cs`).
- You will assign these serialized fields in the inspector:
  - `boardPresenterPrefab`
  - `endTurnButton`
  - `statusText`
  - `phaseText`
  - `currentPlayerText`

#### 3. SpaceView Prefab

Create a prefab that will represent a single board space.

- Under the Canvas, create a UI → Image and name it `SpaceView`.
- On the root `SpaceView` object, ensure it has:
  - `RectTransform`
  - `Image` (for the cell visual; keep Raycast Target enabled)
  - `SpaceView` component (`Magi.LedgeBoardGame.Board.SpaceView`)
  - (Optional) `Button` component for visual feedback; clicks are handled via `IPointerClickHandler` on `SpaceView`.

- Under the root `SpaceView` GameObject, create children:
  - `LightText` – UI → Text for the light token count.
  - `DarkText` – UI → Text for the dark token count.
  - `LockIndicator` – UI → Image (small icon or overlay to show a locked stack).
  - `HighlightEffect` – UI → Image (outline or glow; default inactive) to show selection/valid targets.

- In the `SpaceView` inspector, wire up the fields:
  - `lightCountText` → `LightText`’s `Text` component.
  - `darkCountText` → `DarkText`’s `Text` component.
  - `lockIndicator` → `LockIndicator` GameObject.
  - `highlightEffect` → `HighlightEffect` GameObject.

- Turn this GameObject into a prefab (e.g. `Assets/Prefabs/SpaceView.prefab`) and remove the instance from the scene; it will be instantiated at runtime.

#### 4. BoardPresenter Prefab

Create a prefab that will own all 49 `SpaceView` instances for a single board.

- Under the Canvas, create a UI → Panel (or empty UI GameObject) named `BoardPresenter`.
- Ensure it has a `RectTransform` and add the `BoardPresenter` component (`Magi.LedgeBoardGame.Board.BoardPresenter`).
- In the `BoardPresenter` inspector:
  - Assign `spaceViewPrefab` → the `SpaceView` prefab you created.
- You do **not** manually add 49 children; `BoardPresenter.Initialize` will create one `SpaceView` per space at runtime.
- Turn this into a prefab (e.g. `Assets/Prefabs/BoardPresenter.prefab`) and remove the instance from the scene.

#### 5. HUD / UI Elements

On the Canvas, add basic HUD elements:

- A `Button` named `EndTurnButton`:
  - Child `Text`: “End Turn”.
- Three `Text` components:
  - `PhaseText` – shows the current phase (`Placement` / `Movement`).
  - `CurrentPlayerText` – shows the active player (e.g., `Player: Player1`).
  - `StatusText` – shows instructions, e.g.:
    - “Place one Light and one Dark token.”
    - “Select a movable stack, then a valid destination.”
    - “Game Over – Winner: Player X”

Assign these in the `GameController` inspector:

- `boardPresenterPrefab` → the `BoardPresenter` prefab.
- `endTurnButton` → the `EndTurnButton` instance.
- `phaseText` → `PhaseText`.
- `currentPlayerText` → `CurrentPlayerText`.
- `statusText` → `StatusText`.

You do not need to add any manual OnClick listeners to the End Turn button; `GameController` attaches its handler in `Start()`.

---

### Runtime Behavior (Created by Code)

Once the above wiring is in place, hitting Play will let the existing code drive everything:

#### GameController

- In `Start()` (`GameController.cs`):
  - Creates a `GameState` with two players (`Player1` on board 0, `Player2` on board 1).
  - Constructs `GameRules`.
  - Instantiates one `BoardPresenter` per `BoardState` from `boardPresenterPrefab`.
  - Calls `BoardPresenter.Initialize(board)` for each board.
  - Registers to `SpaceClickedEvent` so it is notified when a `SpaceView` is clicked.
  - Wires `endTurnButton.onClick` to its internal `OnEndTurnClicked` method.
  - Calls `UpdateStatusUI()` to populate the HUD.

#### BoardPresenter

- `Initialize(BoardState state)`:
  - Clears existing children.
  - Creates one `SpaceView` instance per entry in `state.SpaceMetadata`.
  - Names each instance `Space_XX_Type` (where `XX` is the space id).
  - Calls `SpaceView.SetData(spaceId, meta, stack)` so each view displays the correct token counts and lock state.
  - `UpdateView()` refreshes token counts and clears highlights from the underlying `BoardState`.
  - `HighlightValidMoves(List<SpaceId>)` toggles the `highlightEffect` on matching spaces for that board.

#### SpaceView

- Implements `IPointerClickHandler`:
  - On click, calls `SpaceClickedEvent.Raise(this)`.
  - `GameController` listens to this event to interpret clicks.
- `SetData(int id, SpaceMeta meta, TokenStack stack)`:
  - Stores space id and metadata.
  - Updates light/dark counts and lock indicator from the given `TokenStack`.
  - Clears any highlights.
- `UpdateTokenDisplay(TokenStack stack)`:
  - Shows light/dark counts in the child `Text` components.
  - Enables/disables the `lockIndicator` based on the stack’s lock state.
- `SetHighlight(bool active)`:
  - Shows or hides `highlightEffect` to indicate valid moves/targets or selection.

#### Game Flow

- **Placement Phase**:
  - On click, `GameController` uses `GameRules.CanPlaceToken` / `PlaceToken` to place first a Light, then a Dark token on valid spaces of the current player’s board.
  - Uses `GetValidPlacementTargets` to highlight where the player can place.
  - After both placements, `GameState` switches to Movement phase and the HUD text updates.

- **Movement Phase**:
  - First click on a highlighted, movable space selects a source stack; `GetMovablePieces` controls what counts as movable.
  - Valid destinations are obtained via `GetValidMoveTargets` (including cross-board ledges when allowed) and highlighted.
  - Second click on a highlighted destination calls `MoveToken`, updates the stacks, and refreshes the board views.

- **End Turn Button**:
  - Calls `_gameState.EndTurn()`:
    - Clears current turn moves/placements.
    - Advances to the next non-eliminated player.
    - Resets placement flags and phase to Placement.
    - Checks eliminations and sets `GameOver` / `WinnerId` when appropriate.
  - Refreshes all `BoardPresenter` views.
  - Updates phase/player/status text accordingly.
  - Disables the button when the game is over and shows a winner message (or “No Winner”).

With this setup, you should be able to enter Play mode, see two boards (one per player) with 49 clickable spaces each, place Light/Dark tokens in the placement phase, move tokens in the movement phase, and advance turns with the End Turn button while the HUD reflects the current game state.

