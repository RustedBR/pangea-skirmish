# GDD-002: Units & Classes

| Field | Value |
|-------|-------|
| **Status** | Implemented |
| **Systems** | Unit, UnitDefinition, CharacterConfig, GameTuning, MapData |
| **Priority** | Core |

## Overview

Units are the core entities on the battlefield. Each unit has 6 core attributes that derive all combat stats (HP, Mana, Damage, Initiative, MoveBudget, AP, BAP). Units belong to classes that define their starting attribute distribution and default weapons.

## Core Mechanics

### Attributes

| Attribute | Abbrev | Primary Effect | Secondary Effect |
|-----------|--------|----------------|------------------|
| Strength | STR | Melee damage, tile attack bonus | — |
| Vitality | VIT | Max HP (+2 per point) | Physical defense |
| Dexterity | DEX | Critical chance, BAP (+1 per 5 points) | Initiative |
| Agility | AGI | Initiative, MoveBudget, AP, animation speed | — |
| Intelligence | INT | Spell power (Magic/Fire/Air elements) | Mana cost efficiency |
| Wisdom | WIS | Max Mana (+1 per point), spell power (Water/Earth) | Concentration effectiveness |

### Derived Stats

| Stat | Formula | Description |
|------|---------|-------------|
| MaxHP | 10 + 2 × VIT | Hit points |
| MaxMana | 5 + 1 × WIS | Mana pool |
| Damage | weapon.damage + STR | Base attack damage |
| Initiative | 1d20 + AGI + DEX | Round action order |
| MoveBudget | Footprint + AGI | Tiles a unit can move |
| AP | 1 + floor(AGI/5) | Actions per round |
| BAP | 1 + floor(DEX/5) | Bonus actions per round |

### Footprint

- **Footprint**: Size of the unit on the grid (default: 3)
- Determines collision area and movement cost
- Larger units are harder to maneuver but may have stat advantages
- Footprint collision uses Chebyshev distance (diagonal counting)

### Classes

| Class | STR | VIT | DEX | AGI | INT | WIS | Footprint | AttackRange | Default Weapon |
|-------|-----|-----|-----|-----|-----|-----|-----------|-------------|----------------|
| Guerreiro | 8 | 10 | 2 | 3 | 1 | 1 | 3 | 1 | Hatchet |
| Ladino | 3 | 5 | 8 | 10 | 1 | 1 | 3 | 1 | — |
| Arqueiro | 5 | 5 | 10 | 7 | 1 | 2 | 3 | 3 | ShortBow |
| Mago | 1 | 4 | 2 | 4 | 10 | 8 | 3 | 4 | WoodenStaff |
| Goblin | 3 | 3 | 3 | 3 | 1 | 1 | 2 | 1 | — |

### Weapons

| ID | Name | Damage | Range | Notes |
|----|------|--------|-------|-------|
| Hatchet | Machado | 4 | 1 | Default Guerreiro |
| IronAxe | Machado de Ferro | 7 | 1 | — |
| WoodenSword | Espada de Madeira | 3 | 1 | — |
| IronSword | Espada de Ferro | 5 | 1 | — |
| WoodenStaff | Cajado | 2 | 2 | Default Mago |
| Scepter | Cetro | 3 | 2 | — |
| ShortBow | Arco Curto | 3 | 3 | Default Arqueiro |
| LongBow | Arco Longo | 5 | 4 | — |
| ApprenticeWand | Varinha | 2 | 3 | — |
| ArcaneStaff | Cajado Arcano | 6 | 4 | — |

### Unit Visual Representation

- **Sprites**: 8-direction isometric sprites (TinyTactics kit)
- **Tinting**: Enemy units use `enemyTint` (darker)
- **Footprint diamond**: Colored per team (blue/red)
- **Body collider**: Clickable area for selection
- **World bars**: HP/MP bars floating above units
- **Markers**: Arrow indicators for active/targeted units

## Dependencies

- `Unit.cs` — Unit MonoBehaviour
- `UnitDefinition.cs` — ScriptableObject for unit type
- `GameTuning.cs` — Attribute limits, weapon catalog
- `MapData.cs` — ClassCatalog, WeaponCatalog, UnitPlacement

## Open Questions

- [ ] Should there be a leveling system?
- [ ] How many total classes are planned?
- [ ] Should weapons have special properties (element, critical bonus)?

## Tuning Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| attributeMin | 1 | Minimum attribute value in editor |
| attributeMax | 20 | Maximum attribute value in editor |
| spriteScalePerFootprint | 1.12 | Visual scale relative to footprint |
| footprintScalePerFootprint | 0.95 | Footprint diamond scale |
