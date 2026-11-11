using NUnit.Framework;
using PangeaSkirmish;
using UnityEngine;

/// <summary>Mana por tick, Concentrar e embotamento dos sentidos (GDD-019 §4).</summary>
[TestFixture]
public class ManaTests
{
    private GameObject _go;
    private Unit _u;
    private GameTuning _t;

    [SetUp]
    public void SetUp()
    {
        AttributeStats.Formulas = new StatFormulas();
        _t = Tuning.Get();
        _go = new GameObject("ManaUnit");
        _u = _go.AddComponent<Unit>();
        _u.stats = new AttributeStats { WIS = 4f, DEX = 0f, AttackRange = 3 };
        _u.currentHP = 10;
        _u.currentMana = _u.stats.MaxMana;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_go);
        AttributeStats.Formulas = null;
    }

    [Test]
    public void MaxMana_FollowsFormula()
    {
        var f = AttributeStats.Formulas;
        Assert.AreEqual(Mathf.RoundToInt(f.manaBase + 4f * f.manaPerWIS), _u.stats.MaxMana);
    }

    [Test]
    public void GainMana_ClampsToMax()
    {
        _u.currentMana = 0;
        _u.GainMana(999);
        Assert.AreEqual(_u.stats.MaxMana, _u.currentMana);
        _u.GainMana(-999);
        Assert.AreEqual(0, _u.currentMana);
    }

    [Test]
    public void Concentrate_GivesTuningAmount()
    {
        Assert.AreEqual(_t.concentrateMana, _u.stats.ConcentrateMana);
    }

    [Test]
    public void Dulled_BelowFraction_LosesRangeAndHit()
    {
        int max = _u.stats.MaxMana;
        _u.currentMana = max;
        Assert.IsFalse(_u.IsDulled, "Mana cheia: sentidos normais");
        Assert.AreEqual(3, _u.EffectiveAttackRange);
        Assert.AreEqual(_u.stats.HitChance, _u.EffectiveHitChance, 1e-4f);

        _u.currentMana = 0;
        Assert.IsTrue(_u.IsDulled, "Mana zerada: embotado");
        Assert.AreEqual(3 - _t.dullRangePenalty, _u.EffectiveAttackRange);
        Assert.AreEqual(Mathf.Clamp01(_u.stats.HitChance - _t.dullHitPenalty), _u.EffectiveHitChance, 1e-4f);
    }

    [Test]
    public void Dulled_MeleeRangeNeverBelowOne()
    {
        _u.stats.AttackRange = 1;
        _u.currentMana = 0;
        Assert.IsTrue(_u.IsDulled);
        Assert.AreEqual(1, _u.EffectiveAttackRange, "corpo a corpo não perde alcance");
    }

    [Test]
    public void Dulled_ThresholdIsStrictlyBelowFraction()
    {
        int max = _u.stats.MaxMana;
        int threshold = Mathf.CeilToInt(max * _t.dullManaFraction);
        _u.currentMana = threshold;
        Assert.IsFalse(_u.IsDulled, "exatamente no limite não embota");
        _u.currentMana = threshold - 1;
        Assert.IsTrue(_u.IsDulled, "abaixo do limite embota");
    }
}
