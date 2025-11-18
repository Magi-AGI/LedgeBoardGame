# Ledge Board Game — Digital Implementation Plan

This plan translates the Ledge digital spec into a concrete, testable Unity 6 implementation within `Magi-AGI/LedgeBoardGame/LedgeBoardGame`.

## Goals
- Playable 2–4 player Ledge with correct rules: placement, stack/lock/clear, adjacency, ledge crossing, elimination.
- Deterministic, UI‑agnostic rules engine with unit tests.
- Portable, engine‑agnostic game spec (JSON or similar) describing state shape, moves, phases, and end‑conditions that both Unity and other hosts (e.g., boardgame.io style engines) can consume.
- Simple 2D URP presentation, clear input, and pass‑and‑play. Hooks for optional online multiplayer later.

## Architecture Overview
- Core‑first domain layer (pure C#) for rules and state — no `UnityEngine` dependencies.
- Thin Unity layer for rendering, input, and orchestration. Presenters map domain state → views.
- Scriptable config defines board layout and ledge color mapping. Graph is constructed at runtime.

## Portable Game Spec (JSON/YAML)
- Define a portable game spec inspired by boardgame.io concepts:
  - `G` (game state shape): players, boards, spaces, stacks, current player, phase, elimination flags.
  - `moves`: named operations (`placeToken`, `moveToken`, `endTurn`) with typed parameters.
  - `phases`: `placement`, `movement`, each with allowed moves and turn constraints (e.g., min/max moves, placement requirements).
  - `endIf` / `victory`: conditions for elimination and winner resolution.
  - `config`: constants such as board size, ledge colors, movement limits, and version / ruleset identifiers.
- Represent this spec as JSON (or YAML) checked into the repo alongside C# code:
  - `Specs/core/turn-based-spec.schema.json` — generic, game‑agnostic turn‑based schema.
  - `Specs/ledge/ledge-game.v1.json` — Ledge‑specific spec instance that conforms to the core schema.
  - Optional derived assets (e.g., `BoardLayoutConfig` ScriptableObject) generated from the Ledge spec for Unity.
- Treat the spec as the source of truth; C# (`GameState`, `GameRules`) and any future boardgame.io implementation are consumers.

### Spec Integration in C# Domain
- Add a small, Unity‑free spec loader in the domain layer:
  - Types to represent the core schema and the Ledge instance (e.g., `TurnBasedSpec`, `LedgeGameSpec`).
  - Loader APIs to read `Specs/ledge/ledge-game.v1.json` from disk or a JSON string.
- Replace hard‑coded configuration values with spec‑driven ones where practical:
  - Board configuration: ring membership and `ledgeColors` come from the spec and are used by `BoardGraphBuilder` and `GameState.GenerateCrossBoardLedgeEdges()`.
  - Phases and moves: verify that `GamePhase` and the C# move set align with the spec’s `phases` and `moves` and add mapping helpers if needed.
- Introduce DTOs for the spec’s `G` container to support round‑trip serialization:
  - `SpecGameState` wrapping `players`, `ctx` (`currentPlayer`, `phase`, `turnNumber`), and a Ledge‑specific `data` payload.
  - Conversion helpers `SpecGameState.FromGameState(GameState, LedgeGameSpec)` and `GameState FromSpecGameState(SpecGameState, LedgeGameSpec)`.

## Core Data Model (Domain)
The C# model mirrors the JSON `G` shape closely so it can be serialized/deserialized losslessly.

- `Tone`: `Light`, `Dark`.
- `SpaceType`: `center`, `inner-bridge`, `inner-stop`, `ring2`, `ring3`, `outer-added`, `ledge`.
- `SpaceMeta`: `{ type, ringIndex, wedgeIndex (0–11), isHalf?, colorLabel? }`.
- `SpaceId`: `{ boardId:int, spaceId:int }` (stable ints per board).
- `TokenStack`: `{ light:int, dark:int, bottomTone?:Tone }` with helpers: `IsLocked(tone)`, `IsStack(tone)`.
- `BoardState`: map `spaceId → TokenStack`, adjacency list (hex + special edges to center + cross‑board ledges), metadata per `spaceId`.
- `GameState`: players, list of `BoardState`, `currentPlayer`, `phase` (`Place`, `Move`), elimination flags, history (for undo‑within‑turn).
- `Move`: `{ from:SpaceId, to:SpaceId, tone:Tone }` plus resolution result (`Lock|Stack|Clear`).
- Serialization:
  - Compact JSON matching the spec’s `G` schema (including `version` and `rulesetId`).
  - Deterministic ordering to ease testing and cross‑engine comparison.

## Rules Engine (Domain)
Rules are implemented in C# but parameterised by the portable spec where practical (e.g., move names, phase constraints), keeping behavior deterministic and pure.

- Placement phase (own board only): place 1 light and 1 dark; resolve immediately.
- Movement phase (repeat until pass):
  - Only non‑bottom tokens in a stack may move; bottom token is always locked.
  - Step to an adjacent space (or across ledge mapping if on a `ledge` and stacked).
  - Resolve entry per step:
    - Empty → Lock (token becomes new bottom; set/keep `bottomTone`).
    - Same tone → Stack (increment; bottom remains locked; upper tokens movable).
    - Opposite tone → Clear (remove one‑for‑one). If a single token remains on otherwise empty space → it is locked; if ≥2 remain of same tone → stacked with bottom locked.
- Adjacency: standard hex neighbors, plus `inner-bridge ↔ center` special edges.
- Ledge crossing: from a `ledge` with a stack, edges to matching color ledges on all opponent boards.
- Control semantics: on your turn you control tokens on your board; while traversing other boards during the chain, you control tokens on spaces you enter.
- Elimination: if a dark ends locked on an opponent center after clearing light, that player is eliminated; last remaining wins.
- Invariants to assert in engine: moving onto empty always locks; only non‑bottom moves; cross‑board edges only from stacked tokens on `ledge`.

### Move Declarations (Spec View)
- JSON `moves` section declares:
  - `"placeToken"`: params `{ boardId, spaceId, tone }`, allowed in `placement` phase only, constraints `ownBoardOnly`, `oneLightAndOneDarkPerTurn`.
  - `"moveToken"`: params `{ from, to, tone }`, allowed in `movement` phase, constraints `sourceMustBeMovable`, `destinationMustBeAdjacentOrCrossBoard`.
  - `"endTurn"`: params `{}`, allowed in both phases, advances `currentPlayer` when placement is complete or player chooses to pass in movement.
- C# `GameRules` exposes corresponding methods but uses the spec’s configuration (e.g., which moves are valid in each phase) rather than hard‑coding those relationships.

## Unity Layer
- Rendering (2D URP):
  - Procedural 2D layout: 12 wedges; split inner ring modeled as distinct clickable halves; center node.
  - Per‑space visuals: light/dark counts, lock indicator, simple chip icons or numeric badges; ledge spaces labeled and colored.
  - Multi‑board layout for 2–4 players; consistent ledge orientation across boards.
- Input (New Input System):
  - Placement UI: tone selection (or two sequential placements), show valid targets, click to place.
  - Movement UI: click a movable stack → highlight legal destinations → click to move. Buttons: Pass, Undo (within current turn chain).
- Orchestration:
  - `GameController` owns `GameState`, turn rotation, phase, and elimination.
  - `BoardPresenter` builds/updates GameObjects from `BoardState` and metadata.
  - `SpaceView` handles hover, selection, and click forwarding.
  - Lightweight animations (tween move, pop on clear, pulse on lock) without blocking logic.
- UX: turn banner, legal‑move highlights, move log, minimal tutorial overlay.

Unity never talks directly to the JSON spec; it talks to the C# domain types. Spec → C# mapping happens in a single place (e.g., a `GameSpecLoader` in the domain layer) so other engines can reuse the same spec without Unity dependencies.

## Project Structure (Unity 6 Best Practices)
- Folders
  - `Assets/Ledge/Core` — domain code only (asmdef `Ledge.Core`).
  - `Assets/Ledge/Runtime` — MonoBehaviours, presenters, bootstrap (asmdef `Ledge.Runtime`).
  - `Assets/Ledge/Configs` — ScriptableObjects for board layout/theme.
  - `Assets/Ledge/UI`, `Assets/Ledge/Art`, `Assets/Ledge/Shaders`.
  - `Assets/Ledge/Tests` — EditMode (domain) and PlayMode (interaction) tests.
- Assembly definitions
  - `Ledge.Core` has no Unity references; `Ledge.Runtime` depends on `Ledge.Core`.
- Unity 6 conventions
  - Use `FindFirstObjectByType`, avoid `Resources.Load`, wire via `[SerializeField]`.
  - URP 2D shaders/materials; keep shaders self‑contained; prefer simple HLSL/ShaderGraph compatible with URP 2D.

## Board Generation
- JSON spec for board layout:
  - Defines 12 wedges and 49 spaces per board with `spaceId`, `SpaceMeta` (`type`, `ringIndex`, `wedgeIndex`, `isHalf`, `colorLabel`), and any visual hints.
  - Lists adjacency relationships either implicitly (via ring/wedge rules) or explicitly for tricky cases (split inner ring, center connections).
- `BoardLayoutConfig` (ScriptableObject):
  - Generated from (or validated against) the JSON layout spec.
  - Provides Unity‑friendly data but does not redefine rules.
- `BoardGraphBuilder` (domain helper):
  - Builds adjacency using the same rules as the spec (axial `(q,r)` for rings plus explicit edges).
  - During `GameState` init, generates cross‑board ledge edges by color labels across opponents.

## Testing
- EditMode unit tests (domain):
  - Lock trail: move across 3 empties → three locks left behind.
  - Clear chain: stacked dark clears layered light with correct residual locks.
  - Double cross: light peel then dark pivot across matching ledge(s).
  - Multiplayer dogpile: two boards route through a third in one chain.
  - Invariants: empty→lock, only non‑bottom moves, cross only from ledge stacks.
- PlayMode tests: basic interaction flows (place → move → pass, elimination path).

### Spec‑Driven Scenario Tests
- For each major scenario, add a JSON file in a `Scenarios/` folder:
  - Initial `G` (game state), as per the spec.
  - Sequence of moves `{ type, args }`.
  - Expected final `G` and winner / phase.
- C# tests:
  - Load scenario JSON, replay through `GameRules`, and assert final `GameState` matches expected JSON.
  - Round‑trip `GameState` → JSON → `GameState` and compare to ensure the model is portable.
  - Assert that board configuration derived from `ledge-game.v1.json` (rings, 49 spaces, ledge colors) matches what `BoardGraphBuilder` and `BoardState` produce.

## Multiplayer Path (Optional Phase 2)
- Phase 1: local pass‑and‑play only.
- Phase 2: online via Netcode for GameObjects (NGO):
  - Server‑authoritative `GameState`; clients send intents; state snapshots/diffs serialized from domain model.
  - Add `com.unity.netcode.gameobjects` and Unity Transport when starting this phase.

## Build & CI
- Keep URP/Input settings under version control.
- Simple CI: run EditMode tests and PlayMode smoke; ensure domain deterministic tests are green.
- Targets: Desktop (Win/macOS) first; later WebGL with UI tuning.

## Milestones & Deliverables
- M0: Scaffolding (Deliverables)
  - Folder structure + asmdefs (`Ledge.Core`, `Ledge.Runtime`).
  - `BoardLayoutConfig`, `BoardGraphBuilder` (domain + minimal SO).
  - Portable game spec v1 (`Specs/core/turn-based-spec.schema.json` and `Specs/ledge/ledge-game.v1.json`) describing `G`, `moves`, `phases`, `endIf`, board layout.
  - Domain: `TokenStack`, `BoardState`, `GameState`, rules, JSON serialization aligned with the spec.
  - EditMode tests for invariants + 4 spec‑driven scenarios — all green.
- M1: Single‑Board Gameplay
  - 2D rendering for one board; placement + move chain + pass; legal highlights; basic animations.
  - HUD (turn banner, pass/undo). Local 2‑player on one board for dev.
- M2: Multi‑Board + Ledge Crossing
  - 2–4 boards layout; cross‑board ledge mapping by color; control semantics while traversing.
  - Elimination + end‑of‑game flow.
- M3: UX Polish
  - Clearer visuals for lock vs. stack; feedback on clear/stack; undo‑within‑turn; tutorial overlay.
- M4: Optional Online
  - NGO + Transport; authoritative server; minimal lobby; state sync.
- M5: Packaging
  - Desktop builds; smoke tests; README with controls and rules quick reference.

## Open Questions
- Visual style: minimalist badges vs. chip sprites?
- Undo scope: limited to current turn chain only?
- AI player: out of scope now or desired later?

## Next Actions
1) Create asmdefs and folder structure under `Assets/Ledge/*`.
2) Finalize and iterate on `Specs/core/turn-based-spec.schema.json` and `Specs/ledge/ledge-game.v1.json` describing `G`, `moves`, `phases`, `endIf`, and board layout.
3) Implement a small domain loader that reads the Ledge spec and maps it onto `GameState`, `GameRules`, and `BoardGraphBuilder`, with JSON serialization that matches the spec, plus spec‑driven tests (M0).
4) Replace remaining hard‑coded config values (ledge colors, phase names, board constants) with spec‑driven mappings and add sanity checks that the code’s layout matches the spec.
5) Add `BoardLayoutConfig` + `BoardGraphBuilder` wired from the spec, then build a minimal scene with a single board.
6) Implement placement/movement UI + highlights; verify invariants via tests and manual play.
