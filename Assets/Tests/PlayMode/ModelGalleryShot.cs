using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PangeaSkirmish;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>Coloca em cena as 4 classes, as 10 armas, as 3 feras e a Muralha, confere os modelos e salva Temp/models_gallery.png.</summary>
public class ModelGalleryShot
{
    private static Unit Spawn(string name, string defId, Team team, Vector2Int anchor, Color col, GridManager grid, string weapon)
    {
        var u = new GameObject(name).AddComponent<Unit>();
        u.unitName = name; u.definitionId = defId; u.team = team; u.weaponId = weapon;
        u.stats = new UnitStatBlock { STR = 3, VIT = 3, DEX = 3, AGI = 3, INT = 3, WIS = 3 }.ToAttributeStats();
        u.Init(grid, anchor, col, "Sprites/TinyTactics/Warrior");
        return u;
    }

    [UnityTest]
    public IEnumerator AllModels_SpawnWithTheRightMesh()
    {
        LogAssert.ignoreFailingMessages = true;
        foreach (var old in Object.FindObjectsByType<Unit>(FindObjectsSortMode.None)) Object.Destroy(old.gameObject);
        foreach (var old in Object.FindObjectsByType<GridManager>(FindObjectsSortMode.None)) Object.Destroy(old.gameObject);
        foreach (var old in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) Object.Destroy(old.gameObject);
        yield return null;

        var tuning = Resources.Load<GameTuning>("GameTuning");
        RuntimeTuning.Active = tuning;
        AttributeStats.Formulas = tuning.statFormulas;

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.10f, 0.11f, 0.15f);
        var ctrl = camGo.AddComponent<CameraController>();
        var grid = new GameObject("GridManager").AddComponent<GridManager>();
        grid.width = 24; grid.height = 20; grid.Build();
        ctrl.Configure(cam, grid.CellToWorld(new Vector2Int(11, 9)), 8.5f);

        var blue = new Color(0.2f, 0.45f, 0.9f); var red = new Color(0.85f, 0.2f, 0.2f);
        // Fileira 1: as quatro classes pela definição.
        var war  = Spawn("Guerreiro", "warrior", Team.Player, new Vector2Int(1, 15), blue, grid, "IronSword");
        var tank = Spawn("Tanque", "tank", Team.Player, new Vector2Int(5, 15), blue, grid, "IronAxe");
        var arc  = Spawn("Arqueiro", "archer", Team.Player, new Vector2Int(9, 15), blue, grid, "LongBow");
        var mag  = Spawn("Mago", "mage", Team.Player, new Vector2Int(13, 15), blue, grid, "ArcaneStaff");
        // Fileira 2: sem definição — a arma escolhe a classe; cobre as outras armas.
        string[] weapons = { "Hatchet", "WoodenSword", "ShortBow", "WoodenStaff", "Scepter", "ApprenticeWand" };
        var byWeapon = new List<Unit>();
        for (int i = 0; i < weapons.Length; i++)
            byWeapon.Add(Spawn("Jogador" + (char)('A' + i), "", Team.Player, new Vector2Int(1 + i * 4, 10), blue, grid, weapons[i]));
        // Fileira 3: as três feras (o número do nome alterna os tipos).
        var b1 = Spawn("Fera 1", "monster", Team.Enemy, new Vector2Int(2, 4), red, grid, "");
        var b2 = Spawn("Fera 2", "monster", Team.Enemy, new Vector2Int(8, 4), red, grid, "");
        var b3 = Spawn("Fera 3", "monster", Team.Enemy, new Vector2Int(14, 4), red, grid, "");

        // Muralha e zonas de magia.
        var field = new SpellField(); SpellField.Current = field;
        var all = new List<Unit> { war, tank, arc, mag, b1, b2, b3 };
        SpellResolver.Resolve(mag, new PlannedSpell { SpellId = "Rampart", TargetTile = new Vector2Int(18, 15) }, all, grid, field);
        SpellResolver.Resolve(mag, new PlannedSpell { SpellId = "Magma", TargetTile = new Vector2Int(19, 9) }, all, grid, field);
        for (int i = 0; i < 20; i++) yield return null;

        Assert.AreEqual("Warrior", war.GetComponent<UnitModel3D>().ModelName);
        Assert.AreEqual("Tank",    tank.GetComponent<UnitModel3D>().ModelName);
        Assert.AreEqual("Archer",  arc.GetComponent<UnitModel3D>().ModelName);
        Assert.AreEqual("Mage",    mag.GetComponent<UnitModel3D>().ModelName);
        Assert.AreEqual("IronSword",   war.GetComponent<UnitModel3D>().WeaponModelName);
        Assert.AreEqual("ArcaneStaff", mag.GetComponent<UnitModel3D>().WeaponModelName);
        Assert.IsNotNull(World3D.FindPart(tank.transform, "Shield"), "o Tanque carrega escudo");
        for (int i = 0; i < weapons.Length; i++)
            Assert.AreEqual(weapons[i], byWeapon[i].GetComponent<UnitModel3D>().WeaponModelName, "arma com modelo próprio");
        Assert.AreEqual("Warrior", byWeapon[0].GetComponent<UnitModel3D>().ModelName, "machado → Guerreiro");
        Assert.AreEqual("Warrior", byWeapon[1].GetComponent<UnitModel3D>().ModelName, "espada → Guerreiro");
        Assert.AreEqual("Archer",  byWeapon[2].GetComponent<UnitModel3D>().ModelName, "arco → Arqueiro");
        Assert.AreEqual("Mage",    byWeapon[3].GetComponent<UnitModel3D>().ModelName, "cajado → Mago");
        Assert.AreEqual("Monster",        b1.GetComponent<UnitModel3D>().ModelName);
        Assert.AreEqual("MonsterStalker", b2.GetComponent<UnitModel3D>().ModelName);
        Assert.AreEqual("MonsterAlpha",   b3.GetComponent<UnitModel3D>().ModelName);
        Assert.IsNotNull(GameObject.Find("WallBlock"), "a Muralha usa o bloco 3D");
        foreach (var m in new[] { "Arrow", "SpellOrb", "Flame", "RockShard", "WaterDrop", "Gust", "HealLeaf", "Soul" })
            Assert.IsTrue(World3D.Has(m), "modelo de magia/ação ausente: " + m);

        var rt = new RenderTexture(1600, 900, 24);
        cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
        RenderTexture.active = null; cam.targetTexture = null;
        // Fora de Temp/: o Unity apaga Temp ao fechar (rodadas headless perdiam a captura).
        File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "docs", "models-3d-ingame.png"), tex.EncodeToPNG());
        field.Clear(); SpellField.Current = null;
    }
}
