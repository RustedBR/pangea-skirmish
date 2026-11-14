using NUnit.Framework;
using PangeaSkirmish;

/// <summary>GDD-023: incrementos de magia escolhidos ao planejar, pagos em Mana.</summary>
public class SpellBoostTests
{
    [Test]
    public void NoBoost_ReturnsCatalogSpell()
    {
        var ember = SpellCatalog.Get("Ember");
        Assert.AreSame(ember, SpellBoosts.Apply(ember, 0));
        Assert.AreSame(ember, new PlannedSpell { SpellId = "Ember" }.Def);
    }

    [Test]
    public void Power_RaisesAmountCostAndTicks_WithoutTouchingCatalog()
    {
        var ember = SpellCatalog.Get("Ember");
        var p = SpellBoosts.Apply(ember, (int)SpellBoost.Power);
        Assert.AreEqual(ember.baseAmount * 1.5f, p.baseAmount, 0.001f);
        Assert.AreEqual(ember.potFactor * 1.5f, p.potFactor, 0.001f);
        Assert.AreEqual(ember.ticksBase + 1, p.ticksBase);
        Assert.AreEqual(ember.ManaPart + 2, p.ManaPart, "Potência custa 2 de Mana");
        Assert.AreEqual(0, ember.boostMana, "o catálogo não muda");
    }

    [Test]
    public void Effect_Range_Quick_Combine()
    {
        var ember = SpellCatalog.Get("Ember");
        int flags = (int)(SpellBoost.Effect | SpellBoost.Range | SpellBoost.Quick);
        var b = new PlannedSpell { SpellId = "Ember", Boosts = flags }.Def;
        Assert.AreEqual(ember.durationTicks + 2, b.durationTicks);
        Assert.Greater(b.range, ember.range);
        Assert.AreEqual(System.Math.Max(1, ember.ticksBase - 1), b.ticksBase);
        Assert.AreEqual(ember.ManaPart + 1 + 1 + 2, b.ManaPart);
        Assert.AreEqual(4, SpellBoosts.TotalCost(flags));
    }

    [Test]
    public void Area_OnlyForAreaSpells()
    {
        var ember = SpellCatalog.Get("Ember");
        Assert.IsFalse(SpellBoosts.Allowed(ember, SpellBoost.Area), "Brasa é de alvo único");
        var b = SpellBoosts.Apply(ember, (int)SpellBoost.Area);
        Assert.AreEqual(ember.area, b.area, "incremento não permitido não muda nada");
        Assert.AreEqual(ember.ManaPart, b.ManaPart, "e não cobra");
        foreach (var s in SpellCatalog.All())
            if (s.area > 0) { Assert.AreEqual(s.area + 1, SpellBoosts.Apply(s, (int)SpellBoost.Area).area); return; }
        Assert.Fail("nenhuma magia de área no catálogo");
    }

    [Test]
    public void PhysicalSpell_BoostPaidInMana()
    {
        var fury = SpellCatalog.Get("Fury");
        var b = SpellBoosts.Apply(fury, (int)SpellBoost.Quick | (int)SpellBoost.Power);
        Assert.AreEqual(fury.StaminaPart, b.StaminaPart, "Stamina não muda");
    }
}
