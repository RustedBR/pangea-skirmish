using NUnit.Framework;
using PangeaSkirmish;
using UnityEngine;

/// <summary>Feras de tamanhos diferentes (teste 2026-11-13): pequena 1×1, média 2×2, grande 4×4.</summary>
public class BeastSizeTests
{
    [SetUp] public void SetUp() { RuntimeTuning.Active = Resources.Load<GameTuning>("GameTuning"); }

    [Test]
    public void Sizes_Footprint_Strength_AndReach()
    {
        Assert.AreEqual(1, BeastAI.FootprintOf(BeastAI.BeastSize.Small));
        Assert.AreEqual(2, BeastAI.FootprintOf(BeastAI.BeastSize.Medium));
        Assert.AreEqual(4, BeastAI.FootprintOf(BeastAI.BeastSize.Large));
        var s = BeastAI.BuildPreset(0, 1, 30, BeastAI.BeastSize.Small);
        var l = BeastAI.BuildPreset(0, 1, 30, BeastAI.BeastSize.Large);
        Assert.AreEqual(1, s.stats.Footprint); Assert.AreEqual(4, l.stats.Footprint);
        Assert.AreEqual(s.stats.STR * 4, l.stats.STR, 2f, "grande é ~4× mais forte (arredondamento)");
        Assert.AreEqual(s.stats.DEX, l.stats.DEX, "mas não mais rápida");
        Assert.AreEqual(2, l.stats.AttackRange);
        StringAssert.StartsWith("Fera Grande", l.presetName);
    }

    [Test]
    public void EachLayer_MixesSizes()
    {
        var seen = new System.Collections.Generic.HashSet<BeastAI.BeastSize>();
        for (int i = 0; i < 3; i++) seen.Add(BeastAI.SizeFor(i, 1));
        Assert.AreEqual(3, seen.Count, "três feras seguidas = os três tamanhos");
    }
}
