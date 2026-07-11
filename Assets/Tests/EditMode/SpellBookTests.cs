using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using PangeaSkirmish;

[TestFixture]
public class SpellBookTests
{
    private GameObject casterGo;
    private Unit caster;
    private GameTuning tuning;

    [SetUp]
    public void SetUp()
    {
        casterGo = new GameObject("Caster");
        caster = casterGo.AddComponent<Unit>();
        caster.stats = new AttributeStats
        {
            INT = 4f, WIS = 3f, VIT = 2f, STR = 1f, AGI = 5f, DEX = 1f
        };
        tuning = ScriptableObject.CreateInstance<GameTuning>();
        // Bases decididas com Marcus 2026-07-10.
        tuning.spellPotencyBaseDamage = 0.15f;
        tuning.spellPotencyBaseBuff = 0.75f;
        tuning.spellMinDamage = 1;
        tuning.conduitAffinityBonus = 0f;
        RuntimeTuning.Active = tuning;
    }

    [TearDown]
    public void TearDown()
    {
        RuntimeTuning.Active = null;
        Object.DestroyImmediate(casterGo);
    }

    [Test]
    public void DamagePotency_FireElement_UsesINTAndVIT()
    {
        var element = SpellElement.Fire;
        int potency = SpellBook.DamagePotency(caster, element, 1);
        // Fire: INT (4) + VIT (2) = 6 → 1 × 0.15 × 6 = 0.9 → round 1 (min)
        Assert.AreEqual(1, potency);
    }

    [Test]
    public void DamagePotency_WaterElement_UsesVITAndINT()
    {
        var element = SpellElement.Water;
        int potency = SpellBook.DamagePotency(caster, element, 1);
        // Water: VIT (2) + INT (4) = 6 → 1 × 0.15 × 6 = 0.9 → round 1 (min)
        Assert.AreEqual(1, potency);
    }

    [Test]
    public void DamagePotency_MagicElement_UsesINTAndWIS()
    {
        var element = SpellElement.Magic;
        int potency = SpellBook.DamagePotency(caster, element, 1);
        // Magic: INT (4) + WIS (3) = 7 → 1 × 0.15 × 7 = 1.05 → round 1
        Assert.AreEqual(1, potency);
    }

    [Test]
    public void DamagePotency_RespectsMinimumDamage()
    {
        caster.stats = new AttributeStats { INT = 1f, WIS = 1f };
        var element = SpellElement.Magic;
        int potency = SpellBook.DamagePotency(caster, element, 1);
        // Raw: (1+1) × 0.15 = 0.3 → round 0 → max(min=1, 0) = 1
        Assert.GreaterOrEqual(potency, tuning.spellMinDamage);
    }

    [Test]
    public void DamagePotency_ScalesWithManaPower()
    {
        caster.stats = new AttributeStats { INT = 4f, WIS = 3f };
        var element = SpellElement.Magic; // INT+WIS = 7
        int pot1 = SpellBook.DamagePotency(caster, element, 1); // 7 × 0.15 = 1.05 → 1
        int pot3 = SpellBook.DamagePotency(caster, element, 3); // 7 × 0.15 × 3 = 3.15 → 3
        Assert.AreEqual(1, pot1);
        Assert.AreEqual(3, pot3);
    }

    [Test]
    public void BuffPotency_Self_UsesHigherBase()
    {
        caster.stats = new AttributeStats { VIT = 10f, STR = 10f };
        var element = SpellElement.Earth; // VIT+STR = 20
        int buff = SpellBook.BuffPotency(caster, element, 1); // 20 × 0.75 × 1 = 15
        Assert.AreEqual(15, buff);
    }

    [Test]
    public void SpellRange_DependsOnMana_WithoutConduit()
    {
        caster.weaponId = null; // no conduit
        int mana = 5;
        int range = SpellBook.SpellRange(caster, mana);
        // Range = mana × spellRangePerMana + 0 (no conduit)
        // = 5 × 1 = 5
        Assert.AreEqual(5, range);
    }

    [Test]
    public void ElementName_Fire_ReturnsCorrectName()
    {
        Assert.AreEqual("Fogo", SpellBook.ElementName(SpellElement.Fire));
    }

    [Test]
    public void ElementName_Water_ReturnsCorrectName()
    {
        Assert.AreEqual("Água", SpellBook.ElementName(SpellElement.Water));
    }

    [Test]
    public void ElementName_Air_ReturnsCorrectName()
    {
        Assert.AreEqual("Ar", SpellBook.ElementName(SpellElement.Air));
    }

    [Test]
    public void ElementName_Earth_ReturnsCorrectName()
    {
        Assert.AreEqual("Terra", SpellBook.ElementName(SpellElement.Earth));
    }

    [Test]
    public void ElementColor_Fire_ReturnsColor()
    {
        Color color = SpellBook.ElementColor(SpellElement.Fire);
        Assert.AreNotEqual(Color.clear, color);
    }

    [Test]
    public void BuffPotency_AirElement_UsesAGIAndINT()
    {
        caster.stats = new AttributeStats { AGI = 5f, INT = 3f };
        var element = SpellElement.Air;
        int buff = SpellBook.BuffPotency(caster, element, 1); // (5+3) × 0.75 = 6
        Assert.AreEqual(6, buff);
    }

    [Test]
    public void BuffPotency_EarthElement_UsesVITAndSTR()
    {
        caster.stats = new AttributeStats { VIT = 4f, STR = 3f };
        var element = SpellElement.Earth;
        int buff = SpellBook.BuffPotency(caster, element, 1); // (4+3) × 0.75 = 5.25 → 5
        Assert.AreEqual(5, buff);
    }
}
