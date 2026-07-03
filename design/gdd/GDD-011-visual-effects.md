# GDD-011: Visual Effects (VFX)

| Field | Value |
|-------|-------|
| **Status** | Implemented |
| **Systems** | SpellVfx, BattleLabel, ScreenFlash, MaterialFactory |
| **Priority** | Low |

## Overview

Visual effects provide feedback for combat actions. The system includes spell projectiles (arcing flight), impact explosions, buff auras, damage labels (floating numbers), and screen flash effects. All VFX use sprite sheets from the BDragon1727 kit with runtime fallbacks.

## Core Mechanics

### Spell VFX

| Type | Behavior | Source |
|------|----------|--------|
| Projectile | Arcs from caster to target | Element-specific sprite sheet |
| Impact | Explosion burst at target | Impact sheet |
| Self Buff | Aura rising from caster | Aura sheet |
| Tile VFX | Burst at tile position | Impact sheet |

### Projectile Flight

- **Path**: Parabolic arc (configurable height via `spellProjectileArc`)
- **Speed**: `spellProjectileSpeed` (default 14 units/s)
- **Rotation**: Sprite rotates to face flight direction
- **Frame**: Fixed frame by default (avoids color flickering)

### Damage Labels

- **Normal damage**: Red number, rises and fades
- **Critical damage**: Gold number, larger scale, rises higher
- **Miss**: Gray "MISS" text
- **Stacking**: Labels in same area stack vertically (configurable radius)

### Screen Flash

| Type | Color | Trigger |
|------|-------|---------|
| Critical | White | Critical hit landed |
| Player damage | Red | Player unit takes critical damage |
| Generic | White | General feedback |

### Initiative Labels

- **Floating text**: Shows d20 roll + modifiers above units during initiative phase
- **Font**: LegacyRuntime.ttf
- **Billboard**: Always faces camera

## Dependencies

- `SpellVfx.cs` — Spell visual effects
- `BattleLabel.cs` — In-world damage/attack labels
- `FloatingLabel.cs` — Initiative roll labels
- `ScreenFlash.cs` — Screen flash effects
- `MaterialFactory.cs` — Material creation

## Open Questions

- [ ] Should there be particle effects?
- [ ] Should VFX have sound effects同步?
- [ ] Should there be screen shake for all hits (not just crits)?

## Tuning Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| spellProjectileSpeed | 14 | Projectile velocity |
| spellProjectileArc | 0.6 | Arc height |
| spellProjectileScale | 1.5 | Projectile sprite scale |
| spellVfxScale | 2.0 | Impact/aura sprite scale |
| spellVfxFps | 12 | Animation FPS |
| spellCastAnimDuration | 0.5s | Cast animation duration |
| spellImpactPause | 0.35s | Pause after impact |
| spellVfxDebugLogs | true | Debug logging |
| spellProjectileFrame | 0 | Fixed frame (-1 = animate all) |
| damageRiseHeight | 0.7 | Damage label rise distance |
| damageRiseDuration | 0.35s | Damage label rise time |
| critRiseHeight | 1.0 | Critical label rise distance |
| critRiseDuration | 0.5s | Critical label rise time |
| missRiseHeight | 0.6 | Miss label rise distance |
| missRiseDuration | 0.3s | Miss label rise time |
| critLabelScale | 1.35 | Critical label scale multiplier |
| flashDurationCrit | 0.25s | Critical flash duration |
| flashIntensityCrit | 0.3 | Critical flash intensity |
| flashDurationRed | 0.2s | Red flash duration |
| flashIntensityRed | 0.35 | Red flash intensity |
