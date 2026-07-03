# GDD-010: Save/Load System

| Field | Value |
|-------|-------|
| **Status** | Implemented (partial) |
| **Systems** | SaveSystem, SaveLoadUI |
| **Priority** | Low |

## Overview

The save system uses JSON serialization via `JsonUtility` with 3 manual save slots plus an autosave slot. Saves capture unit stats, grid state, and basic battle context. The system is currently partial — tile effects and status effects are not yet serialized.

## Core Mechanics

### Save Slots

| Slot | Type | Description |
|------|------|-------------|
| 0 | Autosave | Auto-saved at round start |
| 1 | Manual | Player save slot 1 |
| 2 | Manual | Player save slot 2 |
| 3 | Manual | Player save slot 3 |

### Saved Data

```csharp
SaveData {
    int roundNumber;
    string activeTeam;      // "Player" or "Enemy"
    int mapWidth, mapHeight;
    int[] tileIndices;
    int[] heights;
    UnitSaveData[] units;   // Per-unit: position, HP, mana, stats, team
}
```

### What's Saved

- ✅ Round number
- ✅ Active team
- ✅ Map dimensions and terrain
- ✅ Unit positions, HP, mana, stats
- ✅ Unit teams

### What's NOT Saved (TODO)

- ❌ Tile effects (fire, water, wind)
- ❌ Status effects (buffs, shields)
- ❌ Spell cooldowns
- ❌ Initiative order

### Serialization

- Format: JSON via `JsonUtility.ToJson()`
- Storage: `Application.persistentDataPath`
- File naming: `save_slot_{n}.json`

## Dependencies

- `SaveSystem.cs` — Save/load logic
- `SaveLoadUI.cs` — Save/load UI

## Open Questions

- [ ] Should autosave happen at other times (end of round, before boss)?
- [ ] Should there be save compression?
- [ ] Should saves be cloud-synced?

## Tuning Parameters

None — save system has no tuning parameters.
