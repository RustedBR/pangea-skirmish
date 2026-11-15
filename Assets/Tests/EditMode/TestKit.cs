using PangeaSkirmish;
using UnityEngine;

/// <summary>Base comum da suíte EditMode (2026-11-14): tuning real carregado e unidades soltas, sem cena.</summary>
public static class TestKit
{
    public static GameTuning LoadTuning()
    {
        var t = Resources.Load<GameTuning>("GameTuning");
        RuntimeTuning.Active = t;
        AttributeStats.Formulas = t.statFormulas;
        return t;
    }

    public static AttributeStats Stats(float str = 5, float vit = 5, float dex = 5, float agi = 5, float intel = 5, float wis = 5) =>
        new UnitStatBlock { STR = str, VIT = vit, DEX = dex, AGI = agi, INT = intel, WIS = wis }.ToAttributeStats();

    /// <summary>Unidade sem grade nem modelo (só regras). Destrua com Object.DestroyImmediate.</summary>
    public static Unit Unit(string name = "U", Vector2Int? at = null, int footprint = 3, float agi = 5, float intel = 5)
    {
        var u = new GameObject(name).AddComponent<Unit>();
        u.unitName = name;
        u.stats = new UnitStatBlock { STR = 5, VIT = 5, DEX = 5, AGI = agi, INT = intel, WIS = 5, Footprint = footprint }.ToAttributeStats();
        u.anchor = at ?? new Vector2Int(2, 2); u.plannedAnchor = u.anchor;
        u.currentHP = u.stats.MaxHP; u.currentMana = u.stats.MaxMana; u.currentStamina = u.stats.MaxStamina;
        return u;
    }
}
