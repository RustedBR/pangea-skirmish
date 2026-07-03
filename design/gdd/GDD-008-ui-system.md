# GDD-008: UI System (HUD)

| Field | Value |
|-------|-------|
| **Status** | Implemented |
| **Systems** | BattleHUD, BattleLabel, FloatingLabel, UiSkin |
| **Priority** | Medium |

## Overview

The HUD follows a **Final Fantasy Tactics aesthetic** — blue gradient windows with light borders. The UI is built procedurally in code (no UXML/UI Toolkit) using Unity's legacy UI system. All theming colors come from GameTuning for easy iteration.

## Core Mechanics

### Layout

```
┌─────────────────────────────────────┐
│  [Phase: Round 3]    [Timer: 12s]  │  ← Top Bar
├───────┬─────────────────────┬───────┤
│       │                     │       │
│ LOG   │                     │ CMD   │
│       │     BATTLEFIELD     │       │
│       │                     │       │
├───────┤                     ├───────┤
│ STATUS│                     │ BONUS │
│       │                     │       │
├───────┴─────────────────────┴───────┤
│  [Move] [Attack] [Spell] [Confirm] │  ← Action Sequence Bar
└─────────────────────────────────────┘
```

### UI Panels

| Panel | Position | Content |
|-------|----------|---------|
| TopBar | Top center | Phase name, Timer, Camera mode indicator |
| BattleLog | Left | Scrollable battle history (sections per round) |
| StatusPanel | Bottom-left | Unit portrait, HP bar, AP/BAP, attributes |
| CommandMenu | Bottom-right | Cascading action menus |
| ActionSeqBar | Bottom center | Action sequence chips (Move/Attack/Spell) |
| ManaStepper | Contextual | Mana injection UI for spells |
| PromptPanel | Contextual | Bonus action confirmation |
| EndPanel | Center | Victory/Defeat screen |

### Command Menu (Cascading)

```
COMANDOS
├── 1 - Ações
│   ├── 1 - Mover
│   └── 2 - Atacar
│       ├── 1 - Atacar Unidade
│       └── 2 - Atacar Tile
├── 2 - Bônus
│   ├── 1 - Concentrar
│   └── 2 - Incremento
├── 3 - Magia
│   ├── 1 - Físico
│   ├── 2 - Mágico
│   ├── 3 - Fogo
│   ├── 4 - Água
│   ├── 5 - Ar
│   └── 6 - Terra
│       └── [Spell Type]
│           ├── 1 - Self
│           ├── 2 - Unidade
│           └── 3 - Tile
│               └── [Mana Stepper]
│                   ├── − / + (mana amount)
│                   ├── Potência display
│                   ├── Conjurar
│                   └── Voltar
└── Confirmar plano
    ├── Desfazer
    └── Limpar
```

### Visual Theme

- **Window gradient**: Dark blue (#1C2650) → Very dark (#050A1C)
- **Border**: Light blue (#80A4EB)
- **Buttons**: Blue pill shapes with 9-slice sprite
- **Font**: LegacyRuntime.ttf (built-in)
- **Text colors**: Per-team (player blue, enemy red, system gray)

### Battle Labels (In-World)

- **Damage numbers**: Float upward and fade (red for normal, gold for critical)
- **Miss labels**: Gray "MISS" text
- **Attack announcements**: "⚔ UnitName" above attacker
- **Sequence numbers**: Numbered markers on planned action positions
- **Stacking**: Labels in same area stack vertically

### World Indicators

- **Active unit marker**: Green arrow above selected unit
- **Target marker**: Red arrow above attack target
- **Bob animation**: Markers gently bob up and down

### UI Skin System

- **Source**: BDragon1727 kit sprites (runtime slicing)
- **Buttons**: 9-slice capsule frames with tint
- **Window frames**: Generated pixel-art border (contorno preto + realce)
- **Fallback**: Solid color rectangles when skin disabled

## Dependencies

- `BattleHUD.cs` — Main HUD construction and updates
- `BattleLabel.cs` — In-world damage/attack labels
- `FloatingLabel.cs` — Initiative roll labels
- `UiSkin.cs` — Runtime sprite slicing and caching
- `GameTuning.cs` — All UI colors and dimensions

## Open Questions

- [ ] Should the HUD scale for mobile?
- [ ] Should there be a minimap?
- [ ] Should tooltips show detailed stat formulas?

## Tuning Parameters

All UI parameters are in GameTuning under "HUD: TEMA", "LABELS", and "PLANEJAMENTO" headers.
