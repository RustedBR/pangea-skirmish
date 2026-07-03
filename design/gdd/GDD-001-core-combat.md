# GDD-001: Core Combat System

| Field | Value |
|-------|-------|
| **Status** | Implemented |
| **Systems** | RoundManager, PlanningController, AttackResolver, SpellResolver, CharacterConfig |
| **Priority** | Core |

## Overview

Pangea Skirmish uses a **semi-action turn-based combat** system inspired by Final Fantasy Tactics and Into the Breach. Each round has structured phases: Initiative → Planning (timed, 15s + 2s grace) → Action (Move → Attack → Spell → Resolving → GameOver). Units act simultaneously during the action phase, resolving in initiative order.

## Core Mechanics

### Round Structure

```
Round N
├── Initiative Phase: Roll 1d20 + AGI + DEX for each unit
├── Planning Phase: 15s timer (+ 2s grace) — player assigns actions to units
│   ├── Pick actions per unit: Move, Attack, Spell, Bonus Actions
│   └── Confirm all plans
├── Action Phase: Actions execute in initiative order
│   ├── Slot 1 (Move): All planned moves resolve simultaneously
│   ├── Slot 2 (Attack): Attacks resolve in initiative order
│   ├── Slot 3 (Spell): Spells resolve in initiative order
│   └── Resolving: Tile effects tick, status effects tick
└── GameOver check: If one team is wiped, battle ends
```

### Initiative

- Roll: `1d20 + AGI + DEX`
- Determines action order within each slot
- Tie-breaker: re-roll between tied units
- Camera focuses on initiative contest at round start

### Planning Phase

- **Duration**: 15 seconds + 2 seconds grace period
- **Timer**: Visual countdown on HUD, warning color at <5s
- **Actions per unit**: Each unit gets a sequence of actions:
  - **Move**: Pick destination tile (costs MoveBudget based on Footprint + AGI)
  - **Attack**: Pick target unit or tile (costs 1 AP)
  - **Spell**: Pick spell + target (costs 1 AP + mana)
  - **Bonus Actions** (BAP):
    - **Power Strike** (+3 damage): Costs BAP
    - **Quick Step** (+1 tile movement): Costs BAP
    - **Concentrate** (recover 3 mana): Costs BAP
- **Undo/Clear**: Player can undo individual actions or clear all plans

### Action Resolution

Actions execute sequentially per slot:
1. **Move slot**: All moves resolve simultaneously (collision resolved by fresh initiative)
2. **Attack slot**: Attacks resolve in initiative order
3. **Spell slot**: Spells resolve in initiative order
4. **Bonus actions** consume BAP and execute after the main action

### Action Points

- **AP (Action Points)**: Derived from AGI. Base 1 + floor(AGI/5)
- **BAP (Bonus Action Points)**: Derived from DEX. Base 1 + floor(DEX/5)
- **Min AP floor**: Always at least 1 AP per round

### Attack Resolution

Three attack modes:
1. **Auto (AI)**: AI picks target automatically
2. **Unit**: Direct target selection — deals damage based on weapon + STR modifier
3. **Tile**: Positional attack — deals weapon damage + STR × tileAttackStrMultiplier to all units in the tile

Damage formula:
```
baseDamage = weapon.damage + STR
mitigation = target.VIT (partial, varies by attack type)
finalDamage = max(minDamageFloor, baseDamage - mitigation)
```

Critical hits: Roll 1d20 + DEX vs target's defense. On critical: ×2 damage, screen shake, flash.

### Win/Lose Conditions

- **Win**: All enemy units are defeated
- **Lose**: All player units are defeated
- **Draw**: (Not implemented yet)

## Dependencies

- `RoundManager.cs` — Round state machine
- `PlanningController.cs` — Player planning input
- `AttackResolver.cs` — Attack resolution
- `SpellResolver.cs` — Spell resolution
- `GameTuning.cs` — All tuning parameters

## Open Questions

- [ ] Should there be a "retreat" option?
- [ ] How does friendly fire work?
- [ ] Should initiative ties be broken by luck (1d20 re-roll) or by stat?

## Tuning Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| planningTime | 15s | Planning phase duration |
| planningGraceSeconds | 2s | Extra time after timer expires |
| minActionPoints | 1 | Floor for AP per round |
| minDamageFloor | 1 | Minimum damage after mitigation |
| tileAttackStrMultiplier | 1.0 | STR fraction added to tile attack |
| unarmedDamage | 1 | Damage without weapon |
| unarmedRange | 1 | Range without weapon |
