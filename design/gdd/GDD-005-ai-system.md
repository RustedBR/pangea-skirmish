# GDD-005: AI System

| Field | Value |
|-------|-------|
| **Status** | Implemented |
| **Systems** | SimpleEnemyAI, GameTuning |
| **Priority** | High |

## Overview

The enemy AI uses a **per-unit parameter system** where each enemy unit has individual aggression, attack preference, intelligence, and survival instinct values. The AI evaluates possible actions (move, attack, spell) and scores them based on weighted criteria.

## Core Mechanics

### AI Parameters (Per Unit)

| Parameter | Range | Default | Description |
|-----------|-------|---------|-------------|
| aggression | 0–1 | 0.7 | 0 = waits, 1 = always advances toward player |
| attackPreference | 0–1 | 0.6 | 0 = prefers repositioning, 1 = attacks whenever possible |
| intelligence | 0–1 | 0.5 | 0 = random decisions, 1 = optimal targets/positions |
| survivalInstinct | 0–1 | 0.3 | 0 = ignores own HP, 1 = retreats very early |

### Global AI Parameters (GameTuning)

| Parameter | Default | Description |
|-----------|---------|-------------|
| aiSurvivalHpThreshold | 0.6 | HP fraction to trigger survival mode |
| aiFlankRange | 4 | Tiles for flanking maneuvers |
| aiAggressiveMaxGap | 2 | Max gap when aggressive |
| aiCautiousGapOffset | 1 | Extra tiles when cautious |
| aiFlankScoreBonus | 1 | Score bonus for diagonal flanking |
| aiKillabilityWeight | 0.4 | Weight for targeting killable enemies |
| aiReactionDelay | 0.3s | Cosmetic delay before "thinking" |

### Decision Flow

```
For each AI unit:
1. Evaluate survival mode (HP < threshold × survivalInstinct)
2. If survival: find safest tile (max distance from threats)
3. If normal:
   a. Find all reachable tiles within MoveBudget
   b. For each tile, evaluate:
      - Distance to nearest enemy (aggression-weighted)
      - Flanking positions (diagonal bonus)
      - Killability of targets from this position
   c. Pick highest-scoring position
4. From chosen position:
   a. If attackPreference > 0.5: attack if target in range
   b. If spell available and mana > threshold: consider spell
   c. Otherwise: move to chosen position
```

### Target Scoring

```
score = (1 / distance) × aggression
      + killabilityBonus × killabilityWeight
      + flankBonus (if diagonal approach)
      + intelligence × optimalPositionBonus
```

### Survival Mode

When `currentHP / maxHP < aiSurvivalHpThreshold × survivalInstinct`:
- AI ignores attack preferences
- Focuses on moving to safest position (max distance from all enemies)
- May use self-buff spells (Physical Might, Magic Shield) if available
- Will not voluntarily enter melee range

### Spell Usage

- AI considers spells when `aiUseSpells = true` and mana available
- Minimum attribute pair sum: `aiSpellMinPair` (default 8)
- Mana fraction per cast: `aiSpellManaFraction` (default 0.34)
- Self-buff priority: When HP < 50%, prioritize self-buffs over attacks
- Concentrate: Used when mana < `aiConcentrationThreshold`

### Flanking

- Diagonal positions adjacent to target receive `aiFlankScoreBonus`
- Range limited by `aiFlankRange`
- Intelligence scales how aggressively AI seeks flanking positions

## Dependencies

- `SimpleEnemyAI.cs` — AI decision logic
- `GameTuning.cs` — AI parameters
- `Unit.cs` — Unit stats and state
- `PlanningController.cs` — Planning integration

## Open Questions

- [ ] Should there be difficulty levels that scale AI parameters?
- [ ] Should AI communicate/coordinate between units?
- [ ] Should there be AI "personalities" (aggressive, defensive, balanced)?

## Tuning Parameters

All AI parameters are in GameTuning under "IA DO INIMIGO" header.
