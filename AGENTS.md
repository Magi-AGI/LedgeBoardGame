# Repository Guidelines

## Project Structure & Module Organization
- Unity project lives in `LedgeBoardGame/`.
- Game code: `LedgeBoardGame/Assets/_Project/Scripts/` — key areas: `Models/`, `Rules/`, `Board/`, `UI/`, `Scripts/` (MonoBehaviours).
- Tests: `LedgeBoardGame/Assets/_Project/Scripts/Tests/EditMode/`.
- Scenes/Settings: `LedgeBoardGame/Assets/Scenes` and `LedgeBoardGame/Assets/Settings/`.
- Do not edit or commit generated folders: `LedgeBoardGame/Library/`, `LedgeBoardGame/Logs/`, `LedgeBoardGame/obj/`, `LedgeBoardGame/UserSettings/`.

## Build, Test, and Development Commands
- Unity version is pinned in `LedgeBoardGame/ProjectSettings/ProjectVersion.txt` (e.g., 6000.2.5f1). Open the folder in that Editor via Unity Hub.
- Run EditMode tests (Windows example):
  `"C:\\Program Files\\Unity\\Hub\\Editor\\6000.2.5f1\\Editor\\Unity.exe" -batchmode -quit -projectPath "%CD%\\LedgeBoardGame" -runTests -testPlatform EditMode -logFile "LedgeBoardGame\\Logs\\editmode.log" -testResults "LedgeBoardGame\\Logs\\results.xml"`
- Optional compile for IDE feedback: `dotnet build LedgeBoardGame/LedgeBoardGame.sln` (requires the pinned Unity installed so references resolve).

## Coding Style & Naming Conventions
- C# 9, 4-space indentation. Keep files UTF-8.
- Public types/methods/properties: PascalCase. Private fields: `_camelCase`. Locals/parameters: camelCase.
- Keep domain logic in `Models/` and `Rules/`; keep Unity-specific behaviour in `Scripts/`. Avoid scene dependencies in core logic.

## Testing Guidelines
- NUnit via Unity Test Framework; tests live under `.../Tests/EditMode/` and end with `*Tests.cs`.
- Add tests with each behavior change; prefer deterministic, pure C# tests.
- Run via the Unity Test Runner or the CLI command above. Store logs/results in `LedgeBoardGame/Logs/`.

## Commit & Pull Request Guidelines
- Use concise subjects; prefer Conventional Commits (e.g., `feat:`, `fix:`, `chore:`, `test:`) with optional scope (e.g., `fix(rules): ...`).
- In PRs, link issues, summarize changes, include screenshots/GIFs for scene/UI changes, and state how you tested.
- Ensure tests pass and there are no new C# warnings before requesting review.

## Dependency Management
- This project uses **[MagiUnityDependencyManager](../MagiUnityDependencyManager)** for package management.
- **Edit `depfile.yaml`** (not `manifest.json`) to modify dependencies.
- After editing `depfile.yaml`, run:
  ```powershell
  cd ../MagiUnityDependencyManager
  ./magi-deps.ps1 apply -ProjectPath ../LedgeBoardGame/LedgeBoardGame
  ./magi-deps.ps1 verify -ProjectPath ../LedgeBoardGame/LedgeBoardGame -Strict
  ```
- `Packages/manifest.json` is **GENERATED** - changes will be overwritten.

## Security & Configuration Tips
- Unity packages are version-controlled in `depfile.yaml`; avoid version drift without discussion.
- Do not commit large binaries or generated folders; rely on `.gitignore` already present in the repo.
