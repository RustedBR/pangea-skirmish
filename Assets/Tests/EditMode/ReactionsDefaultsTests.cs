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
        Assert.AreEqual(0.25f, t.statFormulas.armorPerVIT, 0.0001f, "VIT padrão dá 1/4 da defesa de antes");
        Assert.AreEqual(0.5f, t.blockFlatPerVIT, 0.0001f, "a reação Bloquear mantém metade da VIT");
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
