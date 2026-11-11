using NUnit.Framework;
using PangeaSkirmish;

/// <summary>Regressão: a armadura que reduz o dano é a do DEFENSOR, não a do atacante (bug até 2026-11-11).</summary>
[TestFixture]
public class ArmorTargetTests
{
    [SetUp]    public void SetUp()    { AttributeStats.Formulas = new StatFormulas { damageVariance = 0f, armorPctPerVIT = 0.08f, armorPctMax = 0.6f }; }
    [TearDown] public void TearDown() { AttributeStats.Formulas = null; }

    [Test]
    public void DefenderVit_ReducesDamage()
    {
        var atk = new AttributeStats { VIT = 0f };
        var soft = new AttributeStats { VIT = 0f };
        var tank = new AttributeStats { VIT = 8f };
        int vsSoft = atk.RollDamage(20, true, soft);
        int vsTank = atk.RollDamage(20, true, tank);
        Assert.Less(vsTank, vsSoft, "VIT do defensor reduz o dano recebido");
    }

    [Test]
    public void AttackerVit_DoesNotWeakenOwnHit()
    {
        var target = new AttributeStats { VIT = 2f };
        var frail = new AttributeStats { VIT = 0f };
        var sturdy = new AttributeStats { VIT = 8f };
        Assert.AreEqual(frail.RollDamage(20, true, target), sturdy.RollDamage(20, true, target),
                        "a VIT de quem ataca não muda o dano do golpe");
    }
}
