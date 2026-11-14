using NUnit.Framework;
using PangeaSkirmish;
using UnityEngine;

/// <summary>Feedback 2026-11-14: incremento das físicas, recursos no fim do plano e Salto movendo o plano.</summary>
public class PlanProjectionTests
{
    private Unit _u;

    [SetUp]
    public void SetUp()
    {
        RuntimeTuning.Active = Resources.Load<GameTuning>("GameTuning");
        _u = new GameObject("Plano").AddComponent<Unit>();
        _u.stats = new UnitStatBlock { STR = 3, VIT = 3, DEX = 3, AGI = 3, INT = 5, WIS = 5 }.ToAttributeStats();
        _u.anchor = new Vector2Int(2, 2); _u.plannedAnchor = _u.anchor;
    }

    [TearDown]
    public void TearDown() { Object.DestroyImmediate(_u.gameObject); }

    private void AddSpell(string id, Vector2Int tile, int boosts = 0)
    {
        _u.plannedSpells.Add(new PlannedSpell { SpellId = id, TargetTile = tile, Boosts = boosts });
        _u.actionSequence.Add(new ScheduledAction { Type = ActionType.Spell, Index = _u.plannedSpells.Count - 1 });
        _u.RecomputePlannedAnchor();
        _u.RecomputeTickBudget();
    }

    [Test]
    public void PhysicalSpells_CanBeBoosted()
    {
        var fury = SpellCatalog.Get("Fury");
        Assert.IsTrue(SpellBoosts.Allowed(fury, SpellBoost.Power));
        Assert.AreEqual(fury.multiplier * 1.3f, SpellBoosts.Apply(fury, (int)SpellBoost.Power).multiplier, 0.001f);
        var leap = SpellCatalog.Get("Leap");
        Assert.IsTrue(SpellBoosts.Allowed(leap, SpellBoost.Range));
        Assert.AreEqual(leap.multiplier + 1f, SpellBoosts.Apply(leap, (int)SpellBoost.Range).multiplier, 0.001f);
        var skin = SpellCatalog.Get("StoneSkin");
        Assert.IsTrue(SpellBoosts.Allowed(skin, SpellBoost.Effect));
        Assert.IsTrue(SpellBoosts.Allowed(skin, SpellBoost.Power));
        Assert.AreEqual(2, SpellBoosts.Apply(skin, (int)SpellBoost.Effect).durationTicks);
    }

    [Test]
    public void ConcentrateBeforeSpell_CountsForMana()
    {
        _u.currentMana = 0; _u.currentStamina = _u.stats.MaxStamina;
        _u.actionSequence.Add(new ScheduledAction { Type = ActionType.Concentrate });
        _u.RecomputeTickBudget();
        Assert.GreaterOrEqual(_u.ProjectedManaEnd, _u.stats.ConcentrateMana, "Concentrar entra na projeção");
        var ember = SpellCatalog.Get("Ember");
        Assert.IsTrue(_u.CanFitSpell(ember, out string why), "Concentrar e depois Brasa: " + why);
        AddSpell("Ember", new Vector2Int(5, 5));
        Assert.IsTrue(_u.ProjectPlan().ok);
        Assert.AreEqual(_u.AvailableMana, _u.ProjectedManaEnd, "Mana livre = Mana no fim do plano");
    }

    [Test]
    public void SpellWithoutMana_StillRefused()
    {
        _u.currentMana = 0;
        _u.RecomputeTickBudget();
        Assert.IsFalse(_u.CanFitSpell(SpellCatalog.Get("Ember"), out _));
    }

    [Test]
    public void Leap_MovesPlannedAnchor_AndUndoRestores()
    {
        _u.currentMana = _u.stats.MaxMana; _u.currentStamina = _u.stats.MaxStamina;
        var dest = new Vector2Int(2, 6);
        AddSpell("Leap", dest);
        Assert.AreEqual(dest, _u.plannedAnchor, "o resto do plano parte do destino do Salto");
        CollectionAssert.AreEqual(new[] { dest }, _u.PlannedStops());
        _u.actionSequence.RemoveAt(0); _u.plannedSpells.RemoveAt(0);
        _u.RecomputePlannedAnchor();
        Assert.AreEqual(_u.anchor, _u.plannedAnchor);
    }

    [Test]
    public void LockedSpell_CentersOnTargetBody_NotCasterSize()
    {
        var beast = new GameObject("Fera").AddComponent<Unit>();
        try
        {
            beast.stats = new UnitStatBlock { STR = 3, VIT = 3, DEX = 3, AGI = 3, INT = 1, WIS = 1, Footprint = 1 }.ToAttributeStats();
            beast.anchor = new Vector2Int(10, 10);
            beast.currentHP = 10;   // viva: a trava só vale para alvo vivo
            var sp = new PlannedSpell { SpellId = "Ember", TargetTile = beast.anchor, TargetUnit = beast };
            Assert.AreEqual(new Vector2Int(10, 10), SpellResolver.AimCenter(_u, sp), "fera 1×1: o centro é ela, não anchor + 1");
            CollectionAssert.AreEqual(new[] { new Vector2Int(10, 10) }, SpellResolver.PreviewCells(_u, sp), "prévia = o corpo do alvo");
            beast.stats.Footprint = 4;
            Assert.AreEqual(16, SpellResolver.PreviewCells(_u, sp).Count, "fera grande: 4×4");
            Assert.AreEqual(new Vector2Int(12, 12), SpellResolver.AimCenter(_u, sp));
            // A trava segue o alvo: prévia na posição planejada dele.
            Assert.Contains(new Vector2Int(20, 20), SpellResolver.PreviewCells(_u, sp, new Vector2Int(20, 20)));
            var tileOnly = new PlannedSpell { SpellId = "Ember", TargetTile = new Vector2Int(4, 4) };
            Assert.AreEqual(new Vector2Int(5, 5), SpellResolver.AimCenter(_u, tileOnly), "mirada em tile: tile + metade de quem conjura (3)");
        }
        finally { Object.DestroyImmediate(beast.gameObject); }
    }
}
