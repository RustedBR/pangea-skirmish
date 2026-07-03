# GDD-004: Grid & Map System

| Field | Value |
|-------|-------|
| **Status** | Implemented |
| **Systems** | GridManager, TileDatabase, TileEffects, MapData, IsoConfig, SandboxController |
| **Priority** | Core |

## Overview

Pangea Skirmish uses an **isometric 2D grid** with per-cell elevation, a footprint-based collision system, and a built-in map editor (Sandbox). The grid supports dynamic expansion via spells (Physical → InsertLine, Earth → Expand).

## Core Mechanics

### Grid Structure

- **Tile shape**: Isometric diamond (64×32 px atlas, 32px blocks)
- **Default size**: 20×20 tiles
- **Coordinate system**: (x, y) where x = column, y = row
- **Origin**: Top-left, y increases downward
- **Elevation**: Per-cell height (0–3 levels), affects rendering and movement cost

### Tile Types (Atlas Palette)

| Index | Name | Height | Notes |
|-------|------|--------|-------|
| 0 | Grama | 0 | Default terrain |
| 4 | Terra | 0 | — |
| 8 | Pedra | 0 | Can be raised by Earth spells |
| 12 | Areia | 0 | — |
| 16 | Água | 0 | Created by Water spells |
| 20 | Grama escura | 0 | — |
| 24 | Caminho | 0 | — |
| -1 | Apagar | 0 | Void tile (not rendered) |

### Footprint System

- **Footprint**: Size of unit on grid (default 3 = covers 3×3 cells roughly)
- **Collision**: Rectangle-based overlap check using Chebyshev distance
- **Gap calculation**: `ChebyshevDistance(unitA.anchor, unitB.anchor) - footprintA/2 - footprintB/2`
- **Movement**: Units move tile-by-tile, consuming MoveBudget
- **Footprint rendering**: Colored diamond overlay under units

### Pathfinding & Movement

- **Pathfinding**: A* with isometric heuristics
- **Movement cost**: `moveCostPerTile × tiles + turnCost × directionChanges + heightCostMultiplier × elevationChanges`
- **MoveBudget**: Derived from `Footprint + AGI`
- **Collision resolution**: When two units try to occupy overlapping space, resolved by fresh initiative roll

### Elevation

- **Height levels**: 0–3 per cell
- **Visual**: Side faces rendered with darker colors for depth
- **Movement cost**: `heightCostMultiplier` (default 1.5×) per level change
- **Cliff faces**: Auto-generated cliff sprites at plateau edges

### Tile Effects

| Effect | Behavior | Duration |
|--------|----------|----------|
| Fire | Damages units on tile each round | 3 rounds |
| Water | Changes tile to water terrain | Until replaced |
| Wind | Pushes units that step on it | 3 rounds |
| ManaOrb | Restores mana to unit that picks it up | Until consumed |

### Map Data Structure

```csharp
MapData {
    string mapName;
    int width, height;
    int[] tileIndices;    // idx = x * height + y
    int[] heights;        // idx = x * height + y
    bool[] voidCells;     // idx = x * height + y
    List<UnitPlacement> units;
}
```

### Dynamic Grid Expansion

- **InsertLine (Physical spell)**: Inserts a new row/column at a position
- **Expand (Sandbox)**: Grows grid by adding border tiles
- **Remap**: All units and tile effects are remapped to new coordinates after expansion

### Tile Database

- **Atlas**: TinyTactics tileset (512×416, 64×64 composite tiles)
- **Block extraction**: 32×32 individual blocks from composite quadrants
- **PPU**: 16 (32px → 2 world units)
- **Pivot**: (0.5, 0.75) — center of top diamond face

## Dependencies

- `GridManager.cs` — Grid logic, pathfinding, rendering
- `TileDatabase.cs` — Atlas sprite loading
- `TileEffects.cs` — Tile effect management
- `MapData.cs` — Map serialization
- `IsoConfig.cs` — Isometric configuration
- `SandboxController.cs` — Map editor

## Open Questions

- [ ] Should there be fog of war?
- [ ] How large can maps get? (Currently max 50×50)
- [ ] Should elevation affect line of sight?

## Tuning Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| moveCostPerTile | 1.0 | Movement cost per tile |
| turnCost | 0.5 | Cost for direction change |
| heightCostMultiplier | 1.5 | Elevation movement cost multiplier |
| sandboxDefaultMapSize | 20 | Default new map size |
| maxMapSize | 50 | Maximum map dimension |
| maxTileHeight | 3 | Maximum elevation level |
