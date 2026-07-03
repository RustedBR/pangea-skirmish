# GDD-003: Magic System

| Field | Value |
|-------|-------|
| **Status** | Implemented |
| **Systems** | SpellTypes, SpellResolver, SpellVfx, TileEffects |
| **Priority** | Core |

## Overview

Pangea Skirmish features a **6-element magic system** where spells interact with the terrain and units in tactical ways. Each element has a unique stat pair for potency, a distinct behavior, and visual effects. Spells cost mana and action points, with potency scaling based on invested mana.

## Core Mechanics

### Elements

| Element | Stat Pair | Color | Behavior |
|---------|-----------|-------|----------|
| Physical | DEX + STR | Gold | Self-buff (STR/VIT increase) |
| Magic | INT + WIS | Purple | Self-shield (absorbs damage) |
| Fire | INT + VIT | Orange | Tile: Burns units standing on it |
| Water | VIT + INT | Blue | Tile: Creates water terrain |
| Air | AGI + INT | Light Blue | Tile: Pushes units; Projectile: knockback |
| Earth | VIT + STR | Brown | Tile: Raises terrain elevation |

### Spell Types

| Type | Mana Cost | Target | Description |
|------|-----------|--------|-------------|
| Self | 2 MP | Caster | Buffs the caster (Physical/Magic) or grants elemental resistance |
| Unit | 3 MP | Enemy/Ally | Projectile that flies to target, deals damage, may push |
| Tile | 4 MP | Terrain | Creates terrain effect at target location |

### Potency Formula

```
potency = (pair.a + pair.b) × basePotency × conduitMult × affinityBonus
```

Where:
- `pair.a + pair.b` = Sum of the two linked attributes
- `basePotency` = Element-specific multiplier (default 1.0)
- `conduitMult` = Weapon's spellPowerMult (1.0 if no conduit)
- `affinityBonus` = +25% if weapon element matches spell element

### Range Formula

```
range = mana × spellRangePerMana + spellRangeBase + conduitRangeBonus
```

- `spellRangePerMana`: 1 tile per mana spent (default)
- `spellRangeBase`: 0 (default)
- `conduitRangeBonus`: From weapon definition

### Mana Investment

The player chooses how much mana to inject into a spell via a stepper UI:
- More mana = higher potency AND longer range
- Mana is spent on cast, cannot be refunded
- Concentrate bonus action recovers 3 mana

### Elemental Interactions

| Interaction | Result |
|-------------|--------|
| Fire + Water | Both extinguish (fire removed) |
| Water + Fire | Fire tile extinguished |
| Fire on Fire | Potency stacks, duration refreshed |
| Wind + Unit | Pushes unit N tiles in wind direction |
| Earth on Stone | Raises tile elevation by 1 |
| Mana Orb + Unit | Unit recovers mana, orb consumed |

### Spell Effects by Element

#### Physical (Self Buff)
- Grants `PhysicalMight` status: +STR and +VIT proportional to potency
- Duration: 3 rounds
- Stack rule: Takes highest value (AddOrReplace)

#### Magic (Self Shield)
- Grants `MagicShield` status: Absorbs damage up to potency × shieldCapacityFactor
- Shield absorbs damage before HP
- Duration: Until depleted or battle ends

#### Fire (Tile)
- Creates fire overlay on tile
- Damages units standing on it each round: potency × fireTileDamageFactor
- Duration: 3 rounds
- Stacks potency with existing fire

#### Water (Tile)
- Changes tile appearance to water terrain
- Extinguishes existing fire on that tile
- Duration: Until replaced

#### Air (Tile/Projectile)
- **Tile**: Creates wind effect that pushes units N tiles when they step on it
- **Projectile**: Deals damage + knockback (1 tile) on hit

#### Earth (Tile)
- Raises terrain elevation by 1
- Creates stone tile if empty
- Lasts until end of battle (permanent terrain change)

### Conduit System

Weapons act as spell conduits:
- **spellPowerMult**: Multiplies spell potency (1.0 = no bonus)
- **elementAffinity**: Bonus +25% when matching spell element
- **spellRangeBonus**: Extra range tiles

## Dependencies

- `SpellTypes.cs` — SpellElement enum, SpellBook formulas
- `SpellResolver.cs` — Spell resolution logic
- `SpellVfx.cs` — Visual effects (projectiles, impacts, auras)
- `TileEffects.cs` — Tile effect management
- `GameTuning.cs` — All spell tuning parameters

## Open Questions

- [ ] Should there be a "spell cooldown" system?
- [ ] Can allies be targeted with offensive spells?
- [ ] Should there be spell combinations (Fire + Water = Steam)?

## Tuning Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| spellApCost | 1 | AP cost per spell |
| spellSelfManaCost | 2 | Mana for Self spells |
| spellUnitManaCost | 3 | Mana for Unit spells |
| spellTileManaCost | 4 | Mana for Tile spells |
| concentrateManaGain | 3 | Mana recovered via Concentrate |
| spellRangePerMana | 1 | Tiles of range per mana |
| conduitAffinityBonus | 0.25 | +25% damage on element match |
| spellMinDamage | 1 | Minimum spell damage |
| spellResistFactor | 0.5 | Resist potency multiplier |
| spellBuffDurationRounds | 3 | Duration of elemental buffs |
| physicalBuffStrFactor | 0.5 | STR bonus fraction |
| physicalBuffVitFactor | 0.5 | VIT bonus fraction |
| shieldCapacityFactor | 1.0 | Shield absorption multiplier |
| fireTileDamageFactor | 0.4 | Fire damage per tick |
| fireTileDurationRounds | 3 | Fire duration |
| windTileDurationRounds | 3 | Wind duration |
| windPushTiles | 1 | Tiles wind pushes |
| airProjectilePushTiles | 1 | Projectile knockback |
| spellProjectileSpeed | 14 | Projectile velocity |
| wallImpactDamage | 2 | Damage when pushed into wall |
| spellProjectileArc | 0.6 | Parabolic arc height |
