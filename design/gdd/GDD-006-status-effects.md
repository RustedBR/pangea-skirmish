# GDD-006: Status Effects

| Field | Value |
|-------|-------|
| **Status** | Implemented |
| **Systems** | StatusEffects, GameTuning |
| **Priority** | Medium |

## Overview

Status effects provide temporary buffs and debuffs to units. The system supports three types: Physical Might (stat boost), Magic Shield (damage absorption), and Element Resist (elemental resistance). Effects use an "AddOrReplace" stack rule — multiple applications take the highest value.

## Core Mechanics

### Effect Types

| Type | Stat Modified | Duration | Source |
|------|---------------|----------|--------|
| PhysicalMight | +STR, +VIT | 3 rounds | Physical Self spell |
| MagicShield | Absorbs damage | Until depleted | Magic Self spell |
| ElementResist | +Resistance to element | 3 rounds | Elemental Self spell |

### PhysicalMight

- **STR bonus**: potency × physicalBuffStrFactor (default 0.5)
- **VIT bonus**: potency × physicalBuffVitFactor (default 0.5)
- **Duration**: physicalBuffDurationRounds (default 3)
- **Stack rule**: AddOrReplace — takes highest STR/VIT values

### MagicShield

- **Absorption**: potency × shieldCapacityFactor (default 1.0)
- **Behavior**: Shield absorbs damage before HP
- **Duration**: Until absorption is depleted or battle ends
- **Visual**: Purple shield overlay on unit

### Element Resist

- **Resistance**: potency × spellResistFactor (default 0.5)
- **Duration**: spellBuffDurationRounds (default 3)
- **Stack rule**: AddOrReplace — takes highest resistance value
- **Applies to**: The element of the Self spell cast

### Stack Rules

All status effects follow "AddOrReplace":
- If a new effect of the same type exists, replace if new value > old value
- If new value ≤ old value, ignore the new application
- Different effect types stack independently

## Dependencies

- `StatusEffects.cs` — Effect definitions and management
- `GameTuning.cs` — Duration and potency parameters

## Open Questions

- [ ] Should there be debuffs (poison, slow, etc.)?
- [ ] Should effects have visual indicators on the unit sprite?
- [ ] Can effects be dispelled?

## Tuning Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| spellResistFactor | 0.5 | Resist potency multiplier |
| spellBuffDurationRounds | 3 | Duration of elemental buffs |
| physicalBuffDurationRounds | 3 | Physical buff duration |
| physicalBuffStrFactor | 0.5 | STR bonus fraction |
| physicalBuffVitFactor | 0.5 | VIT bonus fraction |
| shieldCapacityFactor | 1.0 | Shield absorption multiplier |
