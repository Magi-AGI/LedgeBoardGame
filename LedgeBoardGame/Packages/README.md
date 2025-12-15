# Package Management

## IMPORTANT: manifest.json is Generated

**DO NOT EDIT `manifest.json` DIRECTLY**

This file is generated from `../../depfile.yaml` using [MagiUnityDependencyManager](../../../MagiUnityDependencyManager).

## Making Changes to Dependencies

1. Edit `../../depfile.yaml` in the project root
2. Run the apply command:
   ```powershell
   cd ../../../MagiUnityDependencyManager
   ./magi-deps.ps1 apply -ProjectPath ../LedgeBoardGame/LedgeBoardGame
   ```
3. Verify before committing:
   ```powershell
   ./magi-deps.ps1 verify -ProjectPath ../LedgeBoardGame/LedgeBoardGame -Strict
   ```

## Why This Approach?

- **Single Source of Truth**: `depfile.yaml` drives all package configuration
- **Policy Enforcement**: Automatic validation of dependency rules
- **Consistency**: Same approach across all Magi Unity projects
- **Reproducibility**: Lock file verification ensures builds are deterministic

See [MagiUnityDependencyManager README](../../../MagiUnityDependencyManager/README.md) for full documentation.
