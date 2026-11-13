using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PangeaSkirmish;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>Retorno de combate: números e palavras flutuantes, barras acima da cabeça e som por ação.</summary>
public class BattleFeedbackTest
{
    private GridManager _grid; private Camera _cam; private List<Unit> _all; private SpellField _field;

    private Unit Spawn(string name, string defId, Team team, Vector2Int anchor, Color col, string weapon, int vit = 4)
    {
        var u = new GameObject(name).AddComponent<Unit>();
        u.unitName = name; u.definitionId = defId; u.team = team; u.weaponId = weapon;
        u.stats = new UnitStatBlock { STR = 4, VIT = vit, DEX = 3, AGI = 3, INT = 6, WIS = 4 }.ToAttributeStats();
        u.Init(_grid, anchor, col, "Sprites/TinyTactics/Warrior");
        _all.Add(u);
        return u;
    }

    private IEnumerator Setup()
    {
        LogAssert.ignoreFailingMessages = true;
        foreach (var old in Object.FindObjectsByType<Unit>(FindObjectsSortMode.None)) Object.Destroy(old.gameObject);
        foreach (var old in Object.FindObjectsByType<GridManager>(FindObjectsSortMode.None)) Object.Destroy(old.gameObject);
        foreach (var old in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) Object.Destroy(old.gameObject);
        foreach (var old in Object.FindObjectsByType<BattleLabel>(FindObjectsSortMode.None)) Object.Destroy(old.gameObject);
        foreach (var fx in Object.FindObjectsByType<ActionFx>(FindObjectsSortMode.None)) Object.Destroy(fx.gameObject);
        yield return null;
        var tuning = Resources.Load<GameTuning>("GameTuning");
        RuntimeTuning.Active = tuning; AttributeStats.Formulas = tuning.statFormulas;
        BattleRng.Seed(99);
        if (AudioManager.I == null) new GameObject("AudioManager").AddComponent<AudioManager>();
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        _cam = camGo.AddComponent<Camera>();
        _cam.orthographic = true; _cam.clearFlags = CameraClearFlags.SolidColor; _cam.backgroundColor = new Color(0.10f, 0.11f, 0.15f);
        var ctrl = camGo.AddComponent<CameraController>();
        _grid = new GameObject("GridManager").AddComponent<GridManager>();
        _grid.width = 22; _grid.height = 14; _grid.Build();
        ctrl.Configure(_cam, _grid.CellToWorld(new Vector2Int(10, 6)), 8f);   // perto do zoom real da batalha (10)
        _all = new List<Unit>(); _field = new SpellField(); SpellField.Current = _field;
        BattleFeedback.Cam = _cam; BattleFeedback.Reset();
    }

    private void Capture(string file)
    {
        var rt = new RenderTexture(1280, 720, 24);
        _cam.targetTexture = rt; _cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
        RenderTexture.active = null; _cam.targetTexture = null;
        File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "docs", file), tex.EncodeToPNG());
    }

    private static IEnumerator Wait(float s) { for (float t = 0; t < s; t += Time.deltaTime) yield return null; }
    private List<string> Cast(Unit c, string id, Vector2Int tile, Unit target = null) =>
        SpellResolver.Resolve(c, new PlannedSpell { SpellId = id, TargetTile = tile, TargetUnit = target }, _all, _grid, _field);

    [UnityTest]
    public IEnumerator Numbers_Words_AndBarsAboveHeads()
    {
        yield return Setup();
        var blue = new Color(0.2f, 0.45f, 0.9f);
        var mage = Spawn("Mago", "mage", Team.Player, new Vector2Int(3, 8), blue, "ArcaneStaff");
        var tank = Spawn("Tanque", "tank", Team.Player, new Vector2Int(8, 8), blue, "IronSword", vit: 8);
        var ally = Spawn("Arqueiro", "archer", Team.Player, new Vector2Int(13, 8), blue, "ShortBow");
        var foe  = Spawn("Fera 1", "monster", Team.Enemy, new Vector2Int(8, 3), new Color(0.85f, 0.2f, 0.2f), "");
        for (int i = 0; i < 5; i++) yield return null;

        // Pips de Stamina: um por ponto, apagam ao gastar.
        Assert.AreEqual(tank.stats.MaxStamina, tank.WorldStaminaPipsLit);
        tank.currentStamina = 1;
        yield return null;
        Assert.AreEqual(1, tank.WorldStaminaPipsLit, "pips acompanham a Stamina");

        // Barra de conjuração só enquanto conjura Elemental.
        Assert.IsFalse(mage.WorldCastBarVisible);
        mage.queue.Add(new QueuedAction { Type = ActionType.Spell, Duration = 3, Elapsed = 2, Spell = new PlannedSpell { SpellId = "Explosion", TargetTile = foe.anchor } });
        yield return null;
        Assert.IsTrue(mage.WorldCastBarVisible, "barra de conjuração visível");

        // Dano que não interrompe ensina a regra; estados aparecem em texto.
        mage.TakeDamage(1);
        CollectionAssert.Contains(BattleFeedback.Log, "kept");
        Cast(mage, "Ember", foe.anchor, foe);
        CollectionAssert.Contains(BattleFeedback.Log, "status");
        ally.currentHP = 4;
        Cast(mage, "Balm", ally.anchor);
        CollectionAssert.Contains(BattleFeedback.Log, "heal");
        foe.currentMana = 8;
        Cast(mage, "MindTide", foe.anchor, foe);
        CollectionAssert.Contains(BattleFeedback.Log, "mana");
        BattleFeedback.Block(tank, 5);
        BattleFeedback.Miss(ally);
        BattleFeedback.Interrupted(mage, 12, 9);
        yield return null; yield return null;
        StringAssert.Contains("Queimando", foe.WorldStatusText);
        Assert.Greater(Object.FindObjectsByType<BattleLabel>(FindObjectsSortMode.None).Length, 5, "rótulos flutuantes criados");
        yield return Wait(0.12f);
        Capture("feedback-numbers.png");

        // Rótulos somem sozinhos.
        yield return Wait(2.2f);
        Assert.AreEqual(0, Object.FindObjectsByType<BattleLabel>(FindObjectsSortMode.None).Length, "rótulos se destroem");
    }

    [UnityTest]
    public IEnumerator Sounds_FollowTheAction()
    {
        yield return Setup();
        var blue = new Color(0.2f, 0.45f, 0.9f);
        var sword = Spawn("Guerreiro", "warrior", Team.Player, new Vector2Int(2, 8), blue, "IronSword");
        var axe   = Spawn("Tanque", "tank", Team.Player, new Vector2Int(6, 8), blue, "IronAxe");
        var bow   = Spawn("Arqueiro", "archer", Team.Player, new Vector2Int(10, 8), blue, "LongBow");
        var staff = Spawn("Mago", "mage", Team.Player, new Vector2Int(14, 8), blue, "ArcaneStaff");
        var beast = Spawn("Fera 1", "monster", Team.Enemy, new Vector2Int(10, 2), new Color(0.85f, 0.2f, 0.2f), "");
        for (int i = 0; i < 3; i++) yield return null;

        Assert.AreEqual("sfx_slash",  sword.AttackSfxName());
        Assert.AreEqual("sfx_chop",   axe.AttackSfxName());
        Assert.AreEqual("sfx_bow",    bow.AttackSfxName());
        Assert.AreEqual("sfx_thrust", staff.AttackSfxName());
        Assert.AreEqual("sfx_bite",   beast.AttackSfxName());
        Assert.AreEqual("sfx_spell_fire",  ActionFx.SfxFor(SpellCatalog.Get("Explosion")));
        Assert.AreEqual("sfx_spell_water", ActionFx.SfxFor(SpellCatalog.Get("Balm")));
        Assert.AreEqual("sfx_wall",        ActionFx.SfxFor(SpellCatalog.Get("Rampart")));
        Assert.AreEqual("sfx_flex",        ActionFx.SfxFor(SpellCatalog.Get("Fury")));
        Assert.AreEqual("sfx_leap",        ActionFx.SfxFor(SpellCatalog.Get("Leap")));

        // Todo som referenciado existe em Resources/Audio.
        foreach (var n in new[] { "sfx_slash", "sfx_chop", "sfx_bow", "sfx_thrust", "sfx_bite", "sfx_block", "sfx_cast_start",
                                  "sfx_spell_fire", "sfx_spell_water", "sfx_spell_earth", "sfx_spell_air", "sfx_spell_magic",
                                  "sfx_flex", "sfx_leap", "sfx_interrupt", "sfx_soul", "sfx_burn", "sfx_wall", "sfx_spell_pick",
                                  "sfx_heal", "sfx_miss", "sfx_dodge" })
            Assert.IsNotNull(AudioManager.I.Named(n), "som ausente: " + n);

        ActionFx.PlaySpell(staff, new PlannedSpell { SpellId = "Gust", TargetTile = beast.anchor }, _grid);
        Assert.AreEqual("sfx_spell_air", AudioManager.LastNamed);
        BattleFeedback.Block(axe, 3);
        Assert.AreEqual("sfx_block", AudioManager.LastNamed);
    }

    [UnityTest]
    public IEnumerator Grid_AdjacentUnits_AndAttackMarks()
    {
        yield return Setup();
        _cam.GetComponent<CameraController>().Configure(_cam, _grid.CellToWorld(new Vector2Int(9, 7)), 5.2f);
        var blue = new Color(0.2f, 0.45f, 0.9f); var red = new Color(0.85f, 0.2f, 0.2f);
        // Quatro unidades coladas (âncoras a 3 células: os footprints 3×3 se encostam).
        var a = Spawn("Guerreiro", "warrior", Team.Player, new Vector2Int(6, 6), blue, "IronSword");
        var b = Spawn("Tanque", "tank", Team.Player, new Vector2Int(6, 9), blue, "IronAxe");
        var c = Spawn("Fera 1", "monster", Team.Enemy, new Vector2Int(9, 6), red, "");
        var d = Spawn("Fera 2", "monster", Team.Enemy, new Vector2Int(9, 9), red, "");
        var archer = Spawn("Arqueiro", "archer", Team.Player, new Vector2Int(1, 2), blue, "LongBow");
        var far = Spawn("Fera 3", "monster", Team.Enemy, new Vector2Int(15, 2), red, "");
        for (int i = 0; i < 5; i++) yield return null;

        // Corpo a corpo: marca os tiles que a FORMA pega (espada: arco 3), sem trilha (estão colados).
        ActionFx.ResetGroundMarks();
        ActionFx.MarkAttack(_grid, a, new PlannedAttack { Mode = AttackMode.Unit, TargetUnit = c });
        Assert.AreEqual(3, ActionFx.GroundMarks, "arco 3×1 da espada marcado, sem trilha entre unidades coladas");
        // À distância: alvo + trilha no chão entre os dois.
        ActionFx.ResetGroundMarks();
        ActionFx.MarkAttack(_grid, archer, new PlannedAttack { Mode = AttackMode.Unit, TargetUnit = far });
        Assert.Greater(ActionFx.GroundMarks, 1 + 5, "1 tile do arco + trilha do disparo até o alvo");
        // Magia em área: a área inteira na cor do elemento.
        ActionFx.ResetGroundMarks();
        ActionFx.MarkSpell(_grid, archer, new PlannedSpell { SpellId = "Explosion", TargetTile = d.anchor });
        Assert.GreaterOrEqual(ActionFx.GroundMarks, 9, "área da Explosão (raio 1 = 3×3)");
        yield return Wait(0.12f);
        Capture("grid-adjacent.png");
        yield return Wait(Tuning.Get().spellMarkSeconds + 0.4f);   // marcas duram 1,9 s (golpe) e 2,4 s (magia)
        Assert.IsNull(GameObject.Find("GroundMark"), "as marcas somem sozinhas");
    }
}
