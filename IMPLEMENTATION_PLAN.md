# Ledge Board Game — Digital Implementation Plan

This plan translates the Ledge digital spec into a concrete, testable Unity 6 implementation within `Magi-AGI/LedgeBoardGame/LedgeBoardGame`.

## Goals
- Playable 2–4 player Ledge with correct rules: placement, stack/lock/clear, adjacency, ledge crossing, elimination.
- Deterministic, UI‑agnostic rules engine with unit tests.
- Simple 2D URP presentation, clear input, and pass‑and‑play. Hooks for optional online multiplayer later.

## Architecture Overview
- Core‑first domain layer (pure C#) for rules and state — no `UnityEngine` dependencies.
- Thin Unity layer for rendering, input, and orchestration. Presenters map domain state → views.
- Scriptable config defines board layout and ledge color mapping. Graph is constructed at runtime.

## Core Data Model (Domain)
- `Tone`: `Light`, `Dark`.
- `SpaceType`: `center`, `inner-bridge`, `inner-stop`, `ring2`, `ring3`, `outer-added`, `ledge`.
- `SpaceMeta`: `{ type, ringIndex, wedgeIndex (0–11), isHalf?, colorLabel? }`.
- `SpaceId`: `{ boardId:int, spaceId:int }` (stable ints per board).
- `TokenStack`: `{ light:int, dark:int, bottomTone?:Tone }` with helpers: `IsLocked(tone)`, `IsStack(tone)`.
- `BoardState`: map `spaceId → TokenStack`, adjacency list (hex + special edges to center + cross‑board ledges), metadata per `spaceId`.
- `GameState`: players, list of `BoardState`, `currentPlayer`, `phase` (`Place`, `Move`), elimination flags, history (for undo‑within‑turn).
- `Move`: `{ from:SpaceId, to:SpaceId, tone:Tone }` plus resolution result (`Lock|Stack|Clear`).
- Serialization: compact JSON with version key for saves/network.

## Rules Engine (Domain)
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
- `BoardLayoutConfig` (ScriptableObject):
  - 12 wedges definition; per‑space `SpaceMeta` including `type`, `ringIndex`, `wedgeIndex`, `isHalf`, `colorLabel` for ledges.
  - Export stable `spaceId` ordering.
- `BoardGraphBuilder` (domain helper):
  - Build adjacency with axial `(q,r)` for rings; explicit edges for split inner halves and `inner-bridge ↔ center`.
  - During `GameState` init, generate cross‑board ledge edges by color labels across opponents.

## Testing
- EditMode unit tests (domain):
  - Lock trail: move across 3 empties → three locks left behind.
  - Clear chain: stacked dark clears layered light with correct residual locks.
  - Double cross: light peel then dark pivot across matching ledge(s).
  - Multiplayer dogpile: two boards route through a third in one chain.
  - Invariants: empty→lock, only non‑bottom moves, cross only from ledge stacks.
- PlayMode tests: basic interaction flows (place → move → pass, elimination path).

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
  - Domain: `TokenStack`, `BoardState`, `GameState`, rules, JSON serialization.
  - EditMode tests for invariants + 4 scenarios — all green.
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
2) Implement domain model and rules with tests (M0).
3) Add `BoardLayoutConfig` + builder, then wire a minimal scene with a single board.
4) Implement placement/movement UI + highlights; verify invariants via tests and manual play.
