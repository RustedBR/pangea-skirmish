using System.Collections.Generic;
using NUnit.Framework;
using PangeaSkirmish;
using UnityEngine;

/// <summary>GDD-020: formas de arma, dano por tile, custo misto, defesa fixa e posturas.</summary>
public class AttackShapeTests
{
    private GameTuning _t;

    [SetUp]
    public void SetUp()
    {
        _t = Resources.Load<GameTuning>("GameTuning");
        RuntimeTuning.Active = _t;
        AttributeStats.Formulas = _t.statFormulas;
    }

    [TearDown] public void TearDown() { AttributeStats.Formulas = null; }

    [TestCase("Hatchet", 3)]
    [TestCase("IronAxe", 6)]
    [TestCase("WoodenSword", 2)]
    [TestCase("IronSword", 3)]
    [TestCase("WoodenStaff", 3)]
    [TestCase("Scepter", 4)]
    [TestCase("ApprenticeWand", 4)]
    [TestCase("ArcaneStaff", 3)]
    [TestCase("ShortBow", 1)]
    [TestCase("LongBow", 1)]
    public void CenteredShape_HitsTheApprovedTiles(string weapon, int tiles)
    {
        Assert.AreEqual(tiles, AttackShape.CenteredTiles(WeaponCatalog.Get(weapon)), weapon);
    }

    [Test]
    public void Table_TotalsMatchTheGdd()
    {
        // total = dano/tile × tiles centralizado, antes de atributos (balanceamento do simulador, 2026-11-13).
        var expected = new Dictionary<string, int> {
            { "Hatchet", 18 }, { "IronAxe", 36 }, { "WoodenSword", 24 }, { "IronSword", 24 }, { "WoodenStaff", 12 },
            { "Scepter", 12 }, { "ApprenticeWand", 16 }, { "ArcaneStaff", 12 }, { "ShortBow", 15 }, { "LongBow", 25 } };
        foreach (var kv in expected)
        {
            var w = WeaponCatalog.Get(kv.Key);
            Assert.AreEqual(kv.Value, w.damage * AttackShape.CenteredTiles(w), kv.Key);
        }
    }

    [Test]
    public void ShapeRotates_WithTheStrikeDirection()
    {
        var axe = WeaponCatalog.Get("Hatchet");                 // arco 3×1
        var aim = new Vector2Int(10, 10);
        var fromSouth = AttackShape.Cells(axe, new Vector2Int(10, 4), aim);
        var fromWest  = AttackShape.Cells(axe, new Vector2Int(4, 10), aim);
        CollectionAssert.AreEquivalent(new[] { new Vector2Int(9, 10), new Vector2Int(10, 10), new Vector2Int(11, 10) }, fromSouth, "golpe de baixo: fileira horizontal");
        CollectionAssert.AreEquivalent(new[] { new Vector2Int(10, 9), new Vector2Int(10, 10), new Vector2Int(10, 11) }, fromWest, "golpe da esquerda: fileira vertical");
    }

    [Test]
    public void TilesOn_CountsOnlyTheFootprint()
    {
        var cells = new List<Vector2Int> { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(5, 5) };
        Assert.AreEqual(2, AttackShape.TilesOn(cells, new Vector2Int(0, 0), 3));
        Assert.AreEqual(0, AttackShape.TilesOn(cells, new Vector2Int(10, 10), 3));
    }

    [Test]
    public void DamagePerTile_ScalesByPercent_AndPhysicalDamageIsCentered()
    {
        var st = new UnitStatBlock { STR = 5, VIT = 5, DEX = 5, AGI = 5, INT = 5, WIS = 5 }.ToAttributeStats();
        WeaponDef.ApplyTo(st, WeaponCatalog.Get("IronSword"), _t);
        Assert.AreEqual(WeaponCatalog.Get("IronSword").damage * (1f + 5f * _t.statFormulas.dmgPctPerAttr), st.DamagePerTile, 0.001f);
        Assert.AreEqual(Mathf.RoundToInt(st.DamagePerTile * 3), st.PhysicalDamage);
        var bow = new UnitStatBlock { STR = 5, VIT = 5, DEX = 5, AGI = 5, INT = 5, WIS = 5 }.ToAttributeStats();
        WeaponDef.ApplyTo(bow, WeaponCatalog.Get("LongBow"), _t);
        Assert.IsTrue(bow.dexScalesDamage);
        Assert.AreEqual(WeaponCatalog.Get("LongBow").damage * (1f + 5f * _t.statFormulas.dmgPctPerAttr), bow.DamagePerTile, 0.001f);
    }

    [Test]
    public void PhysicalSpell_SplitsCost_StaminaRoundsDown()
    {
        var fury = SpellCatalog.Get("Fury");
        Assert.AreEqual(3, fury.cost);
        Assert.AreEqual(1, fury.StaminaPart);
        Assert.AreEqual(2, fury.ManaPart);
        var leap = SpellCatalog.Get("Leap");
        Assert.AreEqual(1, leap.StaminaPart); Assert.AreEqual(1, leap.ManaPart);
        var ember = SpellCatalog.Get("Ember");
        Assert.AreEqual(0, ember.StaminaPart); Assert.AreEqual(ember.cost, ember.ManaPart);
    }

    [Test]
    public void Posture_AddsAndRemovesAttributes()
    {
        var s = new AttributeStats { STR = 5, VIT = 5, AGI = 5, DEX = 5, INT = 5, WIS = 5 };
        PostureRules.Apply(s, Posture.Serene, +1);
        Assert.AreEqual(5 + _t.postureBonus, s.WIS);
        Assert.AreEqual(5 - _t.posturePenalty, s.STR);
        PostureRules.Apply(s, Posture.Serene, -1);
        Assert.AreEqual(5f, s.WIS); Assert.AreEqual(5f, s.STR);
    }

    [Test]
    public void AttackShape_DescribesTheShape()
    {
        Assert.AreEqual("varredura 3×2", AttackShape.Describe(WeaponCatalog.Get("IronAxe")));
        Assert.AreEqual("arco 3×1", AttackShape.Describe(WeaponCatalog.Get("IronSword")));
        Assert.AreEqual("linha 3", AttackShape.Describe(WeaponCatalog.Get("WoodenStaff")));
        Assert.AreEqual("1 tile", AttackShape.Describe(WeaponCatalog.Get("LongBow")));
    }
}
