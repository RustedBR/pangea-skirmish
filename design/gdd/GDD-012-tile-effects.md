# GDD-012: Tile Effects

| Field | Value |
|-------|-------|
| **Status** | Implemented |
| **Systems** | TileEffects, GridManager |
| **Priority** | Medium |

## Overview

Tile effects are terrain modifications caused by spells. They persist for multiple rounds, interact with each other (fire + water = extinguish), and affect units that move through or stand on them. The `TileEffectManager` tracks all active effects and handles their lifecycle.

## Core Mechanics

### Effect Types

| Effect | Visual | Behavior | Duration |
|--------|--------|----------|----------|
| Fire | Orange circle overlay | Damages units on tile each round | 3 rounds |
| Water | Blue circle overlay | Changes tile to water terrain | Until replaced |
| Wind | Light blue circle overlay | Pushes units in direction when stepped on | 3 rounds |
| ManaOrb | Purple circle overlay | Restores mana to unit that picks it up | Until consumed |

### Effect Lifecycle

```
Apply Effect
├── Check existing effect at cell
│   ├── Fire + Fire → Stack potency, refresh duration
│   ├── Fire + Water → Both extinguish
│   ├── Water + Fire → Fire extinguished
│   └── Other → Replace existing effect
├── Create overlay sprite
└── Register in dictionary

End of Round
├── For each Fire effect:
│   ├── Damage units standing on tile
│   └── Decrement rounds left
├── For each Wind effect:
│   └── Decrement rounds left
├── For each Water effect:
│   └── Decrement rounds left (restore original tile on expiry)
└── Remove expired effects

Unit Movement
├── Check each cell along path:
│   ├── Fire → Take damage (once per move)
│   ├── ManaOrb → Restore mana, remove orb
│   └── Wind at destination → Push unit N tiles
└── Check final position for wind push
```

### Interaction Rules

| Interaction | Result |
|-------------|--------|
| Fire + Fire | Potency stacks, duration = max(current, default) |
| Fire + Water | Both removed |
| Water + Fire | Fire removed |
| Wind + Unit (on step) | Unit pushed N tiles in wind direction |
| ManaOrb + Unit (on step) | Mana restored, orb consumed |
| Earth + Stone tile | Elevation +1 (permanent) |

### Visual Rendering

- **Overlays**: Colored circles with configurable alpha
- **Sorting**: `(x + y) × 4 + 3` (above terrain, below units)
- **Alpha**: `tileEffectOverlayAlpha` (default 0.65)
- **Fallback**: Generated circles when sprite sheets unavailable

### Grid Integration

- **Tile change**: Water effect changes the underlying tile index
- **Tile restore**: When water expires, original tile index is restored
- **Remap**: When grid expands, all effects are remapped to new coordinates
- **Clear**: All effects removed on battle end

## Dependencies

- `TileEffects.cs` — Effect management
- `GridManager.cs` — Grid queries and modifications
- `GameTuning.cs` — Effect parameters

## Open Questions

- [ ] Should there be poison (damage over time to unit)?
- [ ] Should ice slow units?
- [ ] Should lightning chain between adjacent units?

## Tuning Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| fireTileDamageFactor | 0.4 | Fire damage per tick |
| fireTileDurationRounds | 3 | Fire duration |
| windTileDurationRounds | 3 | Wind duration |
| windPushTiles | 1 | Tiles wind pushes |
| waterTileIndex | 16 | Water tile atlas index |
| earthStoneTileIndex | 8 | Stone tile atlas index |
| earthRaiseAmount | 1 | Elevation increase per Earth spell |
| orbManaFactor | 1.0 | Mana conversion factor |
| tileEffectAnimFps | 6 | Overlay animation FPS |
| tileEffectOverlayAlpha | 0.65 | Overlay transparency |
