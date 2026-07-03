# Unit Tests

This folder contains unit tests for Pangea Skirmish using Unity Test Framework (NUnit).

## Running Tests

### Unity Editor
1. Open Window → General → Test Runner
2. Select EditMode tab
3. Click Run All

### Command Line
```bash
# EditMode tests
Unity -batchmode -nographics -runTests -testPlatform EditMode -testResults results.xml
```

## Test Files

### GridManagerTests.cs
Tests for grid coordinate conversion and footprint collision detection:
- `CellToWorld` - Grid to world position conversion
- `WorldToCell` - World to grid position conversion
- `FootprintsOverlap` - Collision detection between units
- `FootprintGap` - Distance calculation between units

### AttributeStatsTests.cs
Tests for stat calculations:
- `MaxHP` - Health calculation from VIT
- `MaxMana` - Mana calculation from WIS
- `PhysicalDamage` - Damage from STR + weapon
- `MagicDamage` - Damage from INT
- `Initiative` - Turn order from AGI + DEX
- `Accuracy` - Hit chance from DEX + weapon
- `Evasion` - Dodge chance from AGI - footprint
- `SpellCritChance` - Critical chance from INT
- `SpellPotency` - Spell power from INT + WIS
- `HitsToKill` - Expected hits to defeat enemy

### SpellBookTests.cs
Tests for magic system:
- `Potency` - Spell damage calculation for each element
- `SpellRange` - Range with conduit bonus
- `SpellCritChance` - Critical chance from WIS
- `ElementPair` - Attribute pairs for each element
- `AffinityBonus` - Bonus when weapon matches spell element

## Notes

- Tests use `NUnit.Framework` for assertions
- Tests use `UnityEngine` for MonoBehaviour operations
- GameObjects are cleaned up in TearDown methods
- Static state (like `RuntimeTuning.Active`) is reset between tests
