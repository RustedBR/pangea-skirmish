using System.Collections.Generic;
using NUnit.Framework;
using PangeaSkirmish;
using UnityEngine;

/// <summary>Progressão, Coop, loot, presets da sala, ajustes de rig e volume (2026-11-14).</summary>
public class ProgressionTests
{
    private string _presetsBackup;
    private const string PresetsKey = "pangea.roompresets.v1";

    [SetUp] public void SetUp() { TestKit.LoadTuning(); _presetsBackup = PlayerPrefs.GetString(PresetsKey, ""); PlayerPrefs.DeleteKey(PresetsKey); }
    [TearDown] public void TearDown() { if (_presetsBackup.Length > 0) PlayerPrefs.SetString(PresetsKey, _presetsBackup); else PlayerPrefs.DeleteKey(PresetsKey); }

    private static CharacterPreset Char(float each, int level) => new CharacterPreset
    {
        level = level, presetName = "C", weaponId = "IronSword",
        stats = new UnitStatBlock { STR = each, VIT = each, DEX = each, AGI = each, INT = each, WIS = each },
    };

    [Test]
    public void Levels_15PointsPlus5_LinearXp()
    {
        Assert.AreEqual(15, PlayerProfile.BudgetFor(Char(2.5f, 1)));
        Assert.AreEqual(30, PlayerProfile.BudgetFor(Char(5, 4)));
        var c = Char(2.5f, 1);
        Assert.AreEqual(2, PlayerProfile.AddXp(c, 20 + 40 + 5), "20 × nível por nível");
        Assert.AreEqual(3, c.level); Assert.AreEqual(5, c.xp);
        var old = Char(5, 0);
        PlayerProfile.MigrateLevel(old);
        Assert.AreEqual(4, old.level, "personagem antigo de 30 pontos vira nível 4");
    }

    [Test]
    public void AboveLevel2_OnlyAddsPoints()
    {
        var lvl3 = Char(2.5f, 3);
        var add = PlayerProfile.Clone(lvl3); add.stats.STR += 5;
        Assert.AreEqual("", PlayerProfile.WhyEditBlocked(lvl3, add));
        var respec = PlayerProfile.Clone(lvl3); respec.stats.STR -= 1; respec.stats.VIT += 1;
        Assert.AreNotEqual("", PlayerProfile.WhyEditBlocked(lvl3, respec));
        var swap = PlayerProfile.Clone(lvl3); swap.weaponId = "ShortBow";
        Assert.AreNotEqual("", PlayerProfile.WhyEditBlocked(lvl3, swap));
        Assert.AreEqual("", PlayerProfile.WhyEditBlocked(Char(2.5f, 2), swap), "nível 2 edita livre");
    }

    [Test]
    public void CoopRewards_GrowWithLayer_AndNightIsHarder()
    {
        var (xp1, gold1) = RoundManager.CoopReward(1, 3, cleared: true);
        var (xp3, gold3) = RoundManager.CoopReward(3, 3, cleared: true);
        Assert.AreEqual(3 * xp1, xp3); Assert.AreEqual(3 * gold1, gold3);
        Assert.Greater(DayNight.BeastMult(DayNight.Night), DayNight.BeastMult(DayNight.Dusk));
        Assert.Greater(DayNight.BeastMult(DayNight.Dusk), DayNight.BeastMult(DayNight.Day));
    }

    [Test]
    public void Loot_ChestDeterministic_SellAndCompare()
    {
        var a = Loot.Generate(2, 0, 3, 42);
        Assert.AreEqual(4, a.Count, "baú: 1 por personagem + 1");
        CollectionAssert.AreEqual(a, Loot.Generate(2, 0, 3, 42));
        Assert.Less(Loot.SellPrice(Rarity.Common), Loot.SellPrice(Rarity.Epic));
        var stash = new List<string> { ItemCatalog.All[0].id };
        Assert.AreEqual(Loot.SellPrice(ItemCatalog.All[0]), Loot.Sell(stash, 0));
        Assert.AreEqual(0, stash.Count);
        StringAssert.Contains("não usa nada", Loot.Details(ItemCatalog.All[0], null));
    }

    [Test]
    public void RoomPresets_SaveReplaceLoadDelete()
    {
        var t = Object.Instantiate(Resources.Load<GameTuning>("GameTuning"));
        t.maxQueueTicks = 7;
        Assert.IsTrue(RoomPresets.Save(" Rápido ", GameModes.PvpTeams, 6, 25f, t));
        Assert.IsFalse(RoomPresets.Save("  ", 0, 2, 10f, t));
        Assert.AreEqual("Rápido", RoomPresets.All()[0].name);
        Assert.AreEqual(7, RoomPresets.TuningOf(RoomPresets.All()[0]).maxQueueTicks);
        Assert.IsTrue(RoomPresets.Save("rápido", GameModes.Coop, 3, 30f, t), "mesmo nome substitui");
        Assert.AreEqual(1, RoomPresets.All().Count);
        RoomPresets.Delete("rápido");
        Assert.AreEqual(0, RoomPresets.All().Count);
    }

    [Test]
    public void RigOffsets_ApplyAndClear()
    {
        var asset = ScriptableObject.CreateInstance<RigOffsets>();
        asset.Set("Mage", "Head", new Vector3(0, 0.05f, 0), new Vector3(10, 0, 0), Vector3.one * 1.1f);
        RigOffsets.SetCache(asset);
        var go = new GameObject("rig");
        try
        {
            RigOffsets.Apply(go.transform, "Mage", "Head");
            Assert.AreEqual(0.05f, go.transform.localPosition.y, 1e-4f);
            RigOffsets.Apply(go.transform, "Mage", "ArmL");
            Assert.AreEqual(Vector3.zero, go.transform.localPosition);
            asset.Set("Mage", "Head", Vector3.zero, Vector3.zero, Vector3.one);
            Assert.IsNull(asset.Find("Mage", "Head"), "ajuste zerado some");
        }
        finally { Object.DestroyImmediate(go); RigOffsets.SetCache(null); }
    }

    [Test]
    public void Volume_CurveInDecibels_AndLowCeiling()
    {
        Assert.AreEqual(0f, GameSettings.SliderToGain(0f, 1f));
        Assert.AreEqual(0.1f, GameSettings.SliderToGain(0.5f, 1f), 0.001f, "50% do slider ≈ −20 dB");
        Assert.Less(GameSettings.MusicMaxGain, 0.5f, "música no 100% bem abaixo do volume cheio");
        float a = GameSettings.SliderToGain(0.3f, 1f), b = GameSettings.SliderToGain(0.4f, 1f);
        float c = GameSettings.SliderToGain(0.9f, 1f), d = GameSettings.SliderToGain(1f, 1f);
        Assert.AreEqual(b / a, d / c, 1e-4f, "cada passo do slider muda o volume na mesma proporção");
    }
}
