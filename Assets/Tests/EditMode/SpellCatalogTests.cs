using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PangeaSkirmish;

/// <summary>Catálogo de magias (GDD-019): 4 Físicas + 15 Elementais, montagem explícita, custos sem mistura.</summary>
[TestFixture]
public class SpellCatalogTests
{
    [Test]
    public void Has19Spells_4Physical_15Elemental()
    {
        var all = SpellCatalog.All();
        Assert.AreEqual(19, all.Length);
        Assert.AreEqual(4,  SpellCatalog.OfKind(SpellKind.Physical).Count());
        Assert.AreEqual(15, SpellCatalog.OfKind(SpellKind.Elemental).Count());
    }

    [Test]
    public void Ids_AreUniqueAndResolvable()
    {
        var all = SpellCatalog.All();
        var ids = new HashSet<string>();
        foreach (var s in all)
        {
            Assert.IsFalse(string.IsNullOrEmpty(s.id), "id vazio");
            Assert.IsTrue(ids.Add(s.id), $"id duplicado: {s.id}");
            Assert.AreSame(s, SpellCatalog.Get(s.id));
        }
        Assert.IsNull(SpellCatalog.Get("NaoExiste"));
    }

    [Test]
    public void Elemental_5Pure_10Pairs_CoveringEveryPair()
    {
        var el = SpellCatalog.OfKind(SpellKind.Elemental).ToList();
        Assert.AreEqual(5,  el.Count(s => s.IsPure));
        Assert.AreEqual(10, el.Count(s => s.IsPair));
        var five = new[] { SpellElement.Magic, SpellElement.Fire, SpellElement.Water, SpellElement.Air, SpellElement.Earth };
        foreach (var e in five)
            Assert.AreEqual(1, el.Count(s => s.IsPure && s.PrimaryElement == e), $"pura de {e}");
        var pairs = new HashSet<string>();
        foreach (var s in el.Where(s => s.IsPair))
        {
            Assert.AreNotEqual(s.elements[0], s.elements[1], $"{s.id}: par com o mesmo elemento");
            var key = string.Join("+", s.elements.OrderBy(x => (int)x));
            Assert.IsTrue(pairs.Add(key), $"par repetido: {key}");
        }
        Assert.AreEqual(10, pairs.Count, "todos os 10 pares distintos");
        foreach (var s in el) Assert.IsFalse(s.elements.Contains(SpellElement.Physical), $"{s.id}: Elemental não usa Físico");
    }

    [Test]
    public void Physical_OnePerBodyAttribute_AndOnlyPhysicalElement()
    {
        var ph = SpellCatalog.OfKind(SpellKind.Physical).ToList();
        CollectionAssert.AreEquivalent(new[] { Attr.STR, Attr.AGI, Attr.VIT, Attr.DEX }, ph.Select(s => s.scalesWith).ToList());
        foreach (var s in ph)
        {
            Assert.AreEqual(1, s.elements.Length, $"{s.id}: Física tem só [Physical]");
            Assert.AreEqual(SpellElement.Physical, s.elements[0]);
            Assert.AreEqual(1, s.ticksBase, $"{s.id}: preparo de 1 tick");
        }
    }

    [Test]
    public void Costs_PositiveAndWithinBudget()
    {
        foreach (var s in SpellCatalog.All())
        {
            Assert.Greater(s.cost, 0, $"{s.id}: custo > 0");
            Assert.GreaterOrEqual(s.ticksBase, 1, $"{s.id}: ticks ≥ 1");
            if (s.IsPhysical) Assert.LessOrEqual(s.cost, 3, $"{s.id}: Estâmina é escassa");
            else Assert.LessOrEqual(s.cost, 5, $"{s.id}: cabe numa Mana de WIS 0 (6)");
        }
    }

    [Test]
    public void Amount_ScalesWithPot_AndPureMagicPierces()
    {
        var ember = SpellCatalog.Get("Ember");
        Assert.AreEqual(4, ember.Amount(0));
        Assert.AreEqual(12, ember.Amount(8));
        Assert.AreEqual(0.5f, SpellCatalog.ArmorPierce(SpellCatalog.Get("ArcaneSight")), 1e-4f, "Mágico puro ignora metade da armadura");
        Assert.AreEqual(0.5f, SpellCatalog.ArmorPierce(SpellCatalog.Get("ArcaneFlame")), 1e-4f);
        Assert.AreEqual(0f,   SpellCatalog.ArmorPierce(ember), 1e-4f);
    }

    [Test]
    public void Montage_TextShowsHowItWasBuilt()
    {
        Assert.AreEqual("Fogo + Ar", SpellCatalog.Get("Explosion").MontageText());
        Assert.AreEqual("Água", SpellCatalog.Get("WaterJet").MontageText());
        Assert.AreEqual("Físico (STR)", SpellCatalog.Get("Fury").MontageText());
    }
}
