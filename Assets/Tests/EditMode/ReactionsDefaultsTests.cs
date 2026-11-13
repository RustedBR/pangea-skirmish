using NUnit.Framework;
using PangeaSkirmish;
using UnityEngine;

/// <summary>Ajustes após os testes iniciais (2026-11-12): VIT a 1/4, bloqueio igual, decisão de 1 minuto.</summary>
public class ReactionsDefaultsTests
{
    [Test]
    public void Vit_Defense_IsAQuarter_BlockKeepsItsNumbers()
    {
        var t = Resources.Load<GameTuning>("GameTuning");
        // Balanceamento do simulador (2026-11-13): 0,5 por VIT e bloqueio 0,75 por VIT.
        Assert.AreEqual(0.5f, t.statFormulas.armorPerVIT, 0.0001f);
        Assert.AreEqual(0.75f, t.blockFlatPerVIT, 0.0001f);
    }

    [Test]
    public void DecisionTime_DefaultsToOneMinute()
    {
        var t = Resources.Load<GameTuning>("GameTuning");
        Assert.AreEqual(60f, t.planningTime);
        Assert.AreEqual(60f, t.decisionTime);
        Assert.AreEqual(60f, new RoomConfigData().planningTime, "sala nova começa com 1 minuto");
    }
}
