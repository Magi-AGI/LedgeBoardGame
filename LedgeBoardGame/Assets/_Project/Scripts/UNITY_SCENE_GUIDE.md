# Unity Scene Setup Guide for Ledge Board Game

## Overview
This guide helps Unity AI Assistant create the visual layer for the Ledge board game. The core domain logic is complete in `Assets/_Project/Scripts/`. Your task is to create the Unity presentation layer following Magi project conventions.

## Current Architecture

### Completed Domain Layer (DO NOT MODIFY)
- **Location**: `Assets/_Project/Scripts/`
- **Assembly**: `Magi.LedgeBoardGame`
- **Namespace**: `Magi.LedgeBoardGame`
- **Dependencies**:
  - `Unity.Mathematics` - Math library
  - `Unity.Burst` - High-performance compiler
  - `Unity.InputSystem` - New input system
  - `Magi.UnityTools` - Shared utility library
- **Key Classes**:
  - `TokenStack`: Manages token counts and resolution
  - `BoardState`: Stores board spaces, adjacency, metadata
  - `GameState`: Manages players, turns, phases
  - `GameRules`: Validates and executes moves
  - `BoardGraphBuilder`: Creates board adjacency graph

### Unity Presentation Layer (TO BE IMPLEMENTED)
- **Location**: `Assets/_Project/Scripts/` (alongside domain)
- **Assembly**: `Magi.LedgeBoardGame` (same assembly)

## Board Layout Specifications

### Space Organization (49 spaces total)
```
- Center (1): Space ID 0
- Inner Ring Split (12):
  - Bridge spaces (6): IDs 1-6 (adjacent to center)
  - Wall spaces (6): IDs 7-12 (NOT adjacent to center)
- Ring 2 (12): IDs 13-24
- Ring 3 (18): IDs 25-42
- Outer Added (6): IDs 43-48
- Ledge spaces (12): IDs 37-48 (overlapping with Ring 3/Outer)
```

### Visual Layout
- 12 wedges radiating from center
- Split inner ring: alternating bridge/wall half-hexes
- Ledge spaces on rim with color labels: Ela, Biz, Yun, Jutu, Glei, Sace, Rha, Dau, Wim, Pfi, Quae, Vei

## Scene Setup Tasks

### 1. Create Main Scene Structure

**IMPORTANT: This is a 2D board game. Use Canvas and UI elements, NOT 3D GameObjects.**

```
Scene Root
├── Main Camera (Configure for 2D)
│   - Clear Flags: Solid Color
│   - Background: Dark gray (#2C2C34)
│   - Orthographic: Yes
│   - Size: 10
│
├── Canvas - Game Board (Screen Space - Camera)
│   ├── GameController.cs (attached here)
│   ├── Board Container (RectTransform)
│   │   ├── Board_Player1 (RectTransform)
│   │   │   ├── BoardPresenter.cs
│   │   │   ├── Background (Image - dark board color)
│   │   │   └── Spaces (RectTransform container)
│   │   │       ├── Space_0 (Button + Image)
│   │   │       │   ├── TokenText (TextMeshPro)
│   │   │       │   └── LockIcon (Image - initially disabled)
│   │   │       └── ... (49 total spaces as UI elements)
│   │   └── Board_Player2 (similar structure)
│   │
│   └── UI Overlay (RectTransform)
│       ├── TurnBanner (TextMeshPro)
│       ├── PhaseIndicator (TextMeshPro)
│       ├── PassButton (Button)
│       └── UndoButton (Button)
│
└── EventSystem (Required for UI interaction)
```

**Setup Steps:**
1. Create a new 2D URP scene
2. Add a Canvas set to "Screen Space - Camera"
3. Set Canvas Scaler to "Scale With Screen Size" (1920x1080 reference)
4. Create board spaces as UI Buttons with Image components
5. Use RectTransform for positioning, not Transform

### 2. Create BoardPresenter Component
```csharp
// Location: Assets/_Project/Scripts/Board/BoardPresenter.cs
using UnityEngine;
using System.Collections.Generic;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Config;

namespace Magi.LedgeBoardGame.Board
{
    public class BoardPresenter : MonoBehaviour
    {
        [SerializeField] private BoardLayoutConfig layoutConfig;
        [SerializeField] private SpaceView spaceViewPrefab;

        private BoardState boardState;
        private Dictionary<int, SpaceView> spaceViews;

        public void Initialize(BoardState state) { }
        public void UpdateView() { }
        public void HighlightValidMoves(List<SpaceId> spaces) { }
    }
}
```

### 3. Create SpaceView Component
```csharp
// Location: Assets/_Project/Scripts/Board/SpaceView.cs
using UnityEngine;
using TMPro;
using Magi.LedgeBoardGame.Models;

namespace Magi.LedgeBoardGame.Board
{
    public class SpaceView : MonoBehaviour
    {
    [SerializeField] private TextMeshPro lightCountText;
    [SerializeField] private TextMeshPro darkCountText;
    [SerializeField] private GameObject lockIndicator;
    [SerializeField] private GameObject highlightEffect;

    private int spaceId;
    private SpaceMeta metadata;

        public void SetData(int id, SpaceMeta meta, TokenStack stack) { }
        public void UpdateTokenDisplay(TokenStack stack) { }
        public void SetHighlight(bool active) { }
    }
}
```

### 4. Create GameController
```csharp
// Location: Assets/_Project/Scripts/Game/GameController.cs
using UnityEngine;
using System.Collections.Generic;
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Rules;
using Magi.LedgeBoardGame.Config;
using Magi.LedgeBoardGame.Board;
using Magi.LedgeBoardGame.Builder;

namespace Magi.LedgeBoardGame.Game
{
    public class GameController : MonoBehaviour
    {
    [SerializeField] private BoardLayoutConfig layoutConfig;
    [SerializeField] private BoardPresenter boardPresenterPrefab;

    private GameState gameState;
    private GameRules rules;
    private Dictionary<int, BoardPresenter> boardPresenters;

    void Start()
    {
        // Initialize game with 2-4 players
        // Create BoardState using BoardGraphBuilder
        // Instantiate BoardPresenters for each player
        }
    }
}
```

### 5. Visual Requirements

#### Space Rendering
- **Shape**: Hexagonal spaces (except split inner ring: half-hexes)
  - Use flat 2D sprites or UI elements, NOT 3D models
  - Each space should be a clickable area approximately 50-60 pixels wide
- **Token Display**:
  - Show light/dark counts as TEXT using TextMeshPro
  - Format: "L:2 D:1" or use separate text fields
  - Lock indicator: Simple sprite overlay or border color change
  - Bottom tone indicator: Tint the space background slightly
- **Ledge Spaces**: Colored borders matching their color label
  - Use outline shader or colored sprite frame
- **Highlighting**:
  - Valid placement targets: Green outline or glow
  - Valid move targets: Blue outline or glow
  - Selected piece: Yellow outline or pulse animation

#### Materials and Textures Needed

**DO NOT USE:**
- Terrain or terrain layers (this is a 2D board game)
- Complex 3D materials or shaders
- Physically-based rendering materials

**USE INSTEAD:**
1. **Board Background**: Simple solid color or subtle gradient
   - Color: Dark gray (#2C2C34) or dark blue (#1A1A2E)
   - Can be a simple Canvas with Image component

2. **Space Sprites** (create as simple shapes):
   - **Regular Hex**: White outline, transparent fill
   - **Half Hex**: White outline, transparent fill (for split inner ring)
   - **Center Space**: Larger hex or circle, special highlight
   - Size: 64x64 or 128x128 pixels
   - Format: PNG with transparency

3. **Token Indicators** (UI elements, not 3D objects):
   - Light token icon: White circle sprite (16x16 px)
   - Dark token icon: Black circle sprite (16x16 px)
   - Lock icon: Simple padlock sprite (16x16 px)
   - OR just use text display

4. **Highlight Effects**:
   - Create as UI Image borders or overlays
   - Use Unity's built-in UI effects (Outline, Shadow)
   - No need for custom shaders

#### Board Layout Algorithm
```csharp
// Hexagonal positioning for wedge w, ring r
float angle = w * 30f * Mathf.Deg2Rad; // 12 wedges = 30° each
float radius = GetRadiusForRing(r);     // Increase by ring
Vector2 position = new Vector2(
    radius * Mathf.Cos(angle),
    radius * Mathf.Sin(angle)
);
```

### 6. Prefab Creation Guide

#### Space Prefab (UI Button)
```
SpaceButton (Prefab)
├── Button Component
│   - Transition: Color Tint
│   - Normal Color: White with 50% alpha
│   - Highlighted: White with 80% alpha
│   - Selected: Yellow
│   - Pressed: White
├── Image Component
│   - Sprite: Hex outline sprite (or use UI circle)
│   - Color: White
│   - Raycast Target: True
├── SpaceView.cs Component
└── Children:
    ├── TokenDisplay (TextMeshPro)
    │   - Text: "L:0 D:0"
    │   - Font Size: 14
    │   - Alignment: Center
    ├── LockIcon (Image)
    │   - Sprite: Lock icon or "🔒"
    │   - Active: False by default
    └── HighlightBorder (Image)
        - Sprite: Hex outline
        - Color: Green/Blue/Yellow
        - Active: False by default
```

#### Board Colors for Ledge Spaces
```csharp
// Define these colors for the 12 ledge spaces
Dictionary<string, Color> ledgeColors = new Dictionary<string, Color>
{
    { "Ela", Color.red },
    { "Biz", Color.blue },
    { "Yun", Color.yellow },
    { "Jutu", Color.green },
    { "Glei", Color.cyan },
    { "Sace", Color.magenta },
    { "Rha", new Color(1f, 0.5f, 0f) }, // Orange
    { "Dau", new Color(0.5f, 0f, 0.5f) }, // Purple
    { "Wim", new Color(0f, 1f, 0.5f) }, // Lime
    { "Pfi", new Color(1f, 0.75f, 0.8f) }, // Pink
    { "Quae", new Color(0.5f, 0.5f, 0.5f) }, // Gray
    { "Vei", new Color(0.6f, 0.4f, 0.2f) } // Brown
};
```

### 7. Input Handling
```csharp
// Using New Input System (already referenced in Magi.LedgeBoardGame.asmdef)
public class InputHandler : MonoBehaviour
{
    // Placement Phase:
    // 1. Click to select tone (Light/Dark)
    // 2. Click valid space to place

    // Movement Phase:
    // 1. Click space with movable tokens
    // 2. Show valid destinations
    // 3. Click destination to move
}
```

### 7. Multi-Board Layout
For 2-4 players, arrange boards in a grid:
```
2 Players: Side by side
[Board1] [Board2]

3 Players: Triangle
   [Board1]
[Board2][Board3]

4 Players: Square
[Board1][Board2]
[Board3][Board4]
```

## Implementation Order

1. **Basic Scene Setup**
   - Create GameController
   - Add single board with 49 spaces
   - Use BoardGraphBuilder.CreateHexagonalBoard() for adjacency

2. **Token Visualization**
   - Display token counts
   - Show lock states
   - Color ledge spaces

3. **Placement Phase**
   - Click to place light and dark tokens
   - Validate placement through GameRules
   - Auto-transition to movement phase

4. **Movement Phase**
   - Highlight movable pieces
   - Show valid destinations on selection
   - Execute moves through GameRules

5. **Game Flow**
   - Turn rotation
   - Pass button functionality
   - Win/elimination detection

## Testing the Scene

### Quick Test Setup
```csharp
using Magi.LedgeBoardGame.Models;
using Magi.LedgeBoardGame.Builder;

[ContextMenu("Test Placement")]
private void TestPlacement()
{
    var players = new List<Player> {
        new Player(1, "Test1", 0),
        new Player(2, "Test2", 1)
    };
    gameState = new GameState(players);

    // Use BoardGraphBuilder to create boards
    var builder = BoardGraphBuilder.CreateHexagonalBoard();
    // Apply to BoardState...
}
```

### Verify Core Mechanics
1. Place 1 light + 1 dark → auto-switch to movement
2. Move stacked token → leaves lock trail
3. Clear opposite tones → correct resolution
4. Cannot move locked bottom tokens
5. Ledge crossing only with stacks (count ≥ 2)

## Important Notes

- **DO NOT modify** the domain logic in `Runtime/Models/`, `Runtime/Rules/`, `Runtime/Builder/` - it's complete and tested
- **Follow Magi namespace conventions**: All code should be under `Magi.LedgeBoardGame`
- **Use BoardGraphBuilder** for adjacency - don't manually create edges
- **Reference BoardLayoutConfig** ScriptableObject for space positions
- **GameRules handles** all validation - don't duplicate logic in UI
- **TokenStack.ResolveEntry()** handles Lock/Stack/Clear automatically

## ScriptableObject Setup

Create a BoardLayoutConfig asset:
1. Right-click in Project → Create → Ledge → Board Layout Config
2. Click "Generate Default Layout" in the context menu
3. This provides all space definitions and adjacency

## Color Scheme Suggestion
- Light tokens: White/Cream (#F8F8F0)
- Dark tokens: Black/Charcoal (#2C2C2C)
- Board spaces: Neutral gray (#808080)
- Ledge colors: Use the 12 distinct colors defined above
- Highlights: Green (valid), Blue (targets), Yellow (selected)

## Common Issues and Solutions

### DO NOT:
- Create Terrain or TerrainLayers (this is a 2D board game)
- Use 3D models for spaces (use UI elements)
- Create complex material shaders (use simple UI sprites)
- Generate ProBuilder meshes (use UI Images/Buttons)
- Use NavMesh (no pathfinding needed)

### IF YOU SEE ERRORS:
1. **"Magi.UnityTools not found"**
   - The MagiUnityTools package is referenced but optional
   - You can temporarily remove it from the assembly definition if needed

2. **"Cannot find BoardLayoutConfig"**
   - Create one: Right-click → Create → Ledge → Board Layout Config
   - Click "Generate Default Layout" in Inspector

3. **Spaces not clickable**
   - Ensure EventSystem exists in scene
   - Check that Canvas has GraphicRaycaster component
   - Verify Button components have "Interactable" checked

## Quick Start Checklist

✅ Create 2D URP Scene
✅ Add Canvas (Screen Space - Camera)
✅ Add EventSystem
✅ Create 49 UI Button prefabs for spaces
✅ Use TextMeshPro for token counts
✅ Position spaces using RectTransform
✅ Wire up GameController to Canvas
✅ Test with 2 players first

This guide provides everything needed to create the Unity visual layer on top of the completed domain logic. The core game rules are fully implemented - focus on presentation and input handling.