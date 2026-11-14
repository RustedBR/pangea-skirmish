using System.Collections.Generic;
using NUnit.Framework;
using PangeaSkirmish;

/// <summary>GDD-024 etapa B: drops, baú, preço de venda.</summary>
public class LootTests
{
    [Test]
    public void Generate_ChestHasOnePerPlayerPlusOne_AndIsDeterministic()
    {
        var a = Loot.Generate(2, 0, 3, 42);
        Assert.AreEqual(4, a.Count, "sem feras abatidas: só o baú (3 jogadores + 1)");
        CollectionAssert.AreEqual(a, Loot.Generate(2, 0, 3, 42), "mesma semente, mesmos itens");
        foreach (var id in a) Assert.IsNotNull(ItemCatalog.Get(id), id);
    }

    [Test]
    public void BeastDrops_AreRare()
    {
        int extra = 0;
        for (int seed = 0; seed < 200; seed++) extra += Loot.Generate(1, 10, 1, seed).Count - 2;
        float rate = extra / 2000f;
        Assert.That(rate, Is.InRange(0.03f, 0.15f), $"~8% por fera (foi {rate:P0})");
    }

    [Test]
    public void DeepLayers_GiveBetterItems()
    {
        int epicShallow = 0, epicDeep = 0;
        for (int seed = 0; seed < 300; seed++)
        {
            var r = new System.Random(seed);
            if (Loot.RollRarity(r, 1) == Rarity.Epic) epicShallow++;
            if (Loot.RollRarity(new System.Random(seed), 6) == Rarity.Epic) epicDeep++;
        }
        Assert.AreEqual(0, epicShallow, "camadas 1–2 não dão épico");
        Assert.Greater(epicDeep, 30);
    }

    [Test]
    public void Sell_RemovesAndPaysByRarity()
    {
        Assert.Less(Loot.SellPrice(Rarity.Common), Loot.SellPrice(Rarity.Rare));
        Assert.Less(Loot.SellPrice(Rarity.Rare), Loot.SellPrice(Rarity.Epic));
        var stash = new List<string> { ItemCatalog.All[0].id };
        Assert.AreEqual(Loot.SellPrice(ItemCatalog.All[0]), Loot.Sell(stash, 0));
        Assert.AreEqual(0, stash.Count);
        Assert.AreEqual(0, Loot.Sell(stash, 0), "índice inválido não paga");
    }
}
