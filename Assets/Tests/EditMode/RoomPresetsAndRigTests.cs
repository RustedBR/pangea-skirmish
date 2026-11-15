using NUnit.Framework;
using PangeaSkirmish;
using UnityEngine;

/// <summary>2026-11-14: presets nomeados da sala e ajustes de rig.</summary>
public class RoomPresetsAndRigTests
{
    private string _backup;
    private const string Key = "pangea.roompresets.v1";

    [SetUp] public void SetUp() { _backup = PlayerPrefs.GetString(Key, ""); PlayerPrefs.DeleteKey(Key); }
    [TearDown] public void TearDown() { if (_backup.Length > 0) PlayerPrefs.SetString(Key, _backup); else PlayerPrefs.DeleteKey(Key); }

    [Test]
    public void Preset_SavesModeRulesAndTuning_AndLoadsBack()
    {
        var t = Object.Instantiate(Resources.Load<GameTuning>("GameTuning"));
        t.maxQueueTicks = 7; t.freeAimDamageMult = 1.9f;
        Assert.IsTrue(RoomPresets.Save("  Rápido  ", GameModes.PvpTeams, 6, 25f, t));
        Assert.IsFalse(RoomPresets.Save("   ", 0, 2, 10f, t), "sem nome não salva");
        var all = RoomPresets.All();
        Assert.AreEqual(1, all.Count);
        Assert.AreEqual("Rápido", all[0].name, "nome sem espaços nas pontas");
        Assert.AreEqual(6, all[0].maxPlayers); Assert.AreEqual(25f, all[0].planningTime);
        var back = RoomPresets.TuningOf(all[0]);
        Assert.AreEqual(7, back.maxQueueTicks); Assert.AreEqual(1.9f, back.freeAimDamageMult, 1e-4f);

        Assert.IsTrue(RoomPresets.Save("rápido", GameModes.Coop, 3, 30f, t), "mesmo nome substitui");
        Assert.AreEqual(1, RoomPresets.All().Count);
        Assert.AreEqual(GameModes.Coop, RoomPresets.All()[0].gameMode);
        RoomPresets.Delete("RÁPIDO".ToLowerInvariant());
        Assert.AreEqual(0, RoomPresets.All().Count);
    }

    [Test]
    public void RigOffsets_SetFindAndApply()
    {
        var asset = ScriptableObject.CreateInstance<RigOffsets>();
        asset.Set("Warrior", "Weapon", new Vector3(0.1f, 0f, 0f), new Vector3(0f, 0f, 15f), Vector3.one);
        Assert.IsNotNull(asset.Find("Warrior", "Weapon"));
        asset.Set("Warrior", "Weapon", Vector3.zero, Vector3.zero, Vector3.one);
        Assert.IsNull(asset.Find("Warrior", "Weapon"), "ajuste zerado apaga a entrada");

        asset.Set("Mage", "Head", new Vector3(0f, 0.05f, 0f), new Vector3(10f, 0f, 0f), Vector3.one * 1.1f);
        RigOffsets.SetCache(asset);
        var go = new GameObject("rig");
        RigOffsets.Apply(go.transform, "Mage", "Head");
        Assert.AreEqual(0.05f, go.transform.localPosition.y, 1e-4f);
        Assert.AreEqual(1.1f, go.transform.localScale.x, 1e-4f);
        RigOffsets.Apply(go.transform, "Mage", "ArmL");
        Assert.AreEqual(Vector3.zero, go.transform.localPosition, "peça sem ajuste fica zerada");
        Object.DestroyImmediate(go);
        RigOffsets.SetCache(null);
    }
}
