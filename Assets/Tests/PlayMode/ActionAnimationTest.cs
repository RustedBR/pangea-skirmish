using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PangeaSkirmish;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>Animações por ação: golpe por arma, flecha, conjuração, projétil de magia, reações, morte e alma.</summary>
public class ActionAnimationTest
{
    private GridManager _grid; private Camera _cam;

    private Unit Spawn(string name, string defId, Team team, Vector2Int anchor, Color col, string weapon)
    {
        var u = new GameObject(name).AddComponent<Unit>();
        u.unitName = name; u.definitionId = defId; u.team = team; u.weaponId = weapon;
        u.stats = new UnitStatBlock { STR = 3, VIT = 3, DEX = 3, AGI = 3, INT = 5, WIS = 3 }.ToAttributeStats();
        u.Init(_grid, anchor, col, "Sprites/TinyTactics/Warrior");
        return u;
    }

    private IEnumerator Setup()
    {
        LogAssert.ignoreFailingMessages = true;
        foreach (var old in Object.FindObjectsByType<Unit>(FindObjectsSortMode.None)) Object.Destroy(old.gameObject);
        foreach (var old in Object.FindObjectsByType<GridManager>(FindObjectsSortMode.None)) Object.Destroy(old.gameObject);
        foreach (var old in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) Object.Destroy(old.gameObject);
        foreach (var fx in Object.FindObjectsByType<ActionFx>(FindObjectsSortMode.None)) Object.Destroy(fx.gameObject);   // efeitos do teste anterior
        foreach (var m in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)) if (m.transform.parent == null) Object.Destroy(m.gameObject);
        yield return null;
        var tuning = Resources.Load<GameTuning>("GameTuning");
        RuntimeTuning.Active = tuning; AttributeStats.Formulas = tuning.statFormulas;
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        _cam = camGo.AddComponent<Camera>();
        _cam.orthographic = true; _cam.clearFlags = CameraClearFlags.SolidColor; _cam.backgroundColor = new Color(0.10f, 0.11f, 0.15f);
        var ctrl = camGo.AddComponent<CameraController>();
        _grid = new GameObject("GridManager").AddComponent<GridManager>();
        _grid.width = 22; _grid.height = 14; _grid.Build();
        ctrl.Configure(_cam, _grid.CellToWorld(new Vector2Int(10, 6)), 6.2f);
        ActionFx.ResetCounters();
    }

    private Texture2D Capture()
    {
        var rt = new RenderTexture(1280, 720, 24);
        _cam.targetTexture = rt; _cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
        RenderTexture.active = null; _cam.targetTexture = null;
        return tex;
    }

    private static IEnumerator Wait(float s) { for (float t = 0; t < s; t += Time.deltaTime) yield return null; }

    [UnityTest]
    public IEnumerator Styles_FollowTheWeapon_AndBowShootsAnArrow()
    {
        yield return Setup();
        var blue = new Color(0.2f, 0.45f, 0.9f);
        var sword = Spawn("Guerreiro", "warrior", Team.Player, new Vector2Int(2, 8), blue, "IronSword");
        var axe   = Spawn("Tanque", "tank", Team.Player, new Vector2Int(6, 8), blue, "IronAxe");
        var bow   = Spawn("Arqueiro", "archer", Team.Player, new Vector2Int(10, 8), blue, "LongBow");
        var staff = Spawn("Mago", "mage", Team.Player, new Vector2Int(14, 8), blue, "ArcaneStaff");
        var beast = Spawn("Fera 1", "monster", Team.Enemy, new Vector2Int(10, 2), new Color(0.85f, 0.2f, 0.2f), "");
        for (int i = 0; i < 5; i++) yield return null;

        Assert.AreEqual(UnitModel3D.AttackStyle.Slash,  sword.Model3D.Style);
        Assert.AreEqual(UnitModel3D.AttackStyle.Chop,   axe.Model3D.Style);
        Assert.AreEqual(UnitModel3D.AttackStyle.Shoot,  bow.Model3D.Style);
        Assert.AreEqual(UnitModel3D.AttackStyle.Thrust, staff.Model3D.Style);
        Assert.AreEqual(UnitModel3D.AttackStyle.Bite,   beast.Model3D.Style);

        foreach (var u in new[] { sword, axe, bow, staff, beast })
            u.Model3D.PlayAttack(0.6f, beast.transform.position, power: u == axe);
        yield return null;
        Assert.AreEqual("attack", sword.Model3D.CurrentAction);
        yield return Wait(0.24f);                    // no meio do preparo/golpe
        File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "docs", "anim-attacks.png"), Capture().EncodeToPNG());
        yield return Wait(0.25f);
        Assert.GreaterOrEqual(ActionFx.Spawned, 1, "o arco dispara uma flecha");
        Assert.AreEqual("Arrow", ActionFx.LastModel);
        yield return Wait(0.6f);
        Assert.AreEqual("", sword.Model3D.CurrentAction, "o golpe termina e volta ao repouso");
    }

    [UnityTest]
    public IEnumerator Cast_Reactions_Death_AndSoul()
    {
        yield return Setup();
        var blue = new Color(0.2f, 0.45f, 0.9f);
        var mage  = Spawn("Mago", "mage", Team.Player, new Vector2Int(3, 8), blue, "ArcaneStaff");
        var tank  = Spawn("Tanque", "tank", Team.Player, new Vector2Int(8, 8), blue, "IronSword");
        var rogue = Spawn("Arqueiro", "archer", Team.Player, new Vector2Int(13, 8), blue, "ShortBow");
        var beast = Spawn("Fera 1", "monster", Team.Enemy, new Vector2Int(8, 2), new Color(0.85f, 0.2f, 0.2f), "");
        for (int i = 0; i < 5; i++) yield return null;

        // Conjurando Explosão: pose de conjuração com orbe; a magia Física não conjura.
        mage.queue.Add(new QueuedAction { Type = ActionType.Spell, Duration = 3, Elapsed = 1,
                                          Spell = new PlannedSpell { SpellId = "Explosion", TargetTile = beast.anchor } });
        tank.PlayReactionAnim(ReactionKind.Block);
        rogue.PlayReactionAnim(ReactionKind.Dodge);
        yield return Wait(0.15f);
        Assert.AreEqual("cast", mage.Model3D.CurrentAction);
        Assert.AreEqual("block", tank.Model3D.CurrentAction);
        Assert.AreEqual("dodge", rogue.Model3D.CurrentAction);
        Assert.IsNotNull(GameObject.Find("SpellOrb"), "orbe do elemento enquanto conjura");
        File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "docs", "anim-cast-react.png"), Capture().EncodeToPNG());

        // Solta a magia: projétil (Fogo → labareda) e impacto.
        mage.queue.Clear();
        ActionFx.ResetCounters();
        ActionFx.PlaySpell(mage, new PlannedSpell { SpellId = "Explosion", TargetTile = beast.anchor }, _grid);
        // Checa antes de avançar o tempo: um frame longo pode levar o projétil até o impacto.
        Assert.AreEqual("Flame", ActionFx.LastModel, "Explosão (Fogo + Ar) lança labareda");
        Assert.AreEqual("release", mage.Model3D.CurrentAction);
        yield return Wait(0.4f);
        Assert.GreaterOrEqual(ActionFx.Spawned, 3, "projétil + impacto de Fogo + impacto de Ar");
        Assert.IsNull(GameObject.Find("SpellOrb"), "o orbe some quando a conjuração acaba");

        // Dano: recuo. Morte: tomba e a alma desce.
        mage.TakeDamage(1);              // (o Tanque ainda está com o escudo erguido: o bloqueio tem prioridade)
        Assert.AreEqual("hit", mage.Model3D.CurrentAction);
        ActionFx.ResetCounters();
        beast.TakeDamage(9999);
        yield return Wait(0.2f);
        Assert.AreEqual("death", beast.Model3D.CurrentAction);
        yield return Wait(0.35f);
        Assert.AreEqual("Soul", ActionFx.LastModel, "a alma sai da cabeça ao morrer");
        File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "docs", "anim-death-soul.png"), Capture().EncodeToPNG());

        // Salto: o modelo parte de onde estava.
        var from = rogue.transform.position;
        rogue.TeleportTo(rogue.anchor + new Vector2Int(0, -4));
        rogue.Model3D.PlayLeap(from);
        yield return null;
        Assert.AreEqual("leap", rogue.Model3D.CurrentAction);
    }
}
