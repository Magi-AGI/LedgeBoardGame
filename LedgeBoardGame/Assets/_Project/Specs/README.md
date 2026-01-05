## Game Spec Layout

This folder contains an engine‑agnostic game specification that is designed to be extracted into a separate repository later.

The structure is split into:

- `core/turn-based-spec.schema.json`  
  A generic, game‑agnostic schema describing how turn‑based game specs are structured (players, phases, moves, state container `G`, end conditions).

- `ledge/ledge-game.v1.json`  
  The Ledge‑specific ruleset expressed using the core schema (phases, moves, board layout config, victory conditions).

Unity, C#, and any future engines (e.g., JavaScript/boardgame.io) should treat these JSON files as the source of truth for rules and structure, and load/validate against the core schema rather than re‑encoding game concepts directly in code.

