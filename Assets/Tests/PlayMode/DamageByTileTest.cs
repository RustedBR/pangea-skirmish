using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PangeaSkirmish;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// GDD-020 resolvido de verdade (AttackResolver/SpellResolver/Unit): dano por tile, esquiva que tira
/// tiles, mira livre, Interpor dividindo tiles, bloqueio que desequilibra, aliados fora da forma,
/// postura com os máximos. Fórmulas sem sorte (acerto 100%, sem crítico, sem variância).
/// </summary>
public class DamageByTileTest
{
    private GridManager _grid;
    private readonly List<Unit> _units = new List<Unit>();
    private StatFormulas _saved;
    private GameTuning _t;

    private IEnumerator Setup()
    {
        LogAssert.ignoreFailingMessages = true;
        foreach (var o in Object.FindObjectsByType<Unit>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        foreach (var o in Object.FindObjectsByType<GridManager>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        yield return null;
        _t = Resources.Load<GameTuning>("GameTuning");
        RuntimeTuning.Active = _t;
        _saved = _t.statFormulas;
        var f = JsonUtility.FromJson<StatFormulas>(JsonUtility.ToJson(_saved));
        f.baseHitChance = 1f; f.critPerDEX = 0f; f.dodgePerAGI = 0f; f.damageVariance = 0f;
        AttributeStats.Formulas = f;
        _grid = new GameObject("GridManager").AddComponent<GridManager>();
        _grid.width = 30; _grid.height = 30; _grid.Build();
        _units.Clear();
        UnitRegistry.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        AttributeStats.Formulas = _saved;
        foreach (var u in _units) if (u != null) Object.Destroy(u.gameObject);
    }

    private Unit Make(string n, Team t, Vector2Int a, string weapon, float str = 5, float vit = 0, float agi = 0)
    {
        var u = new GameObject(n).AddComponent<Unit>();
        u.unitName = n; u.team = t; u.weaponId = weapon;
        u.stats = new UnitStatBlock { STR = str, VIT = vit, DEX = 0, AGI = agi, INT = 0, WIS = 0 }.ToAttributeStats();
        u.Init(_grid, a, Color.red, "Sprites/TinyTactics/Warrior");
        u.currentStamina = 20;
        UnitRegistry.Register((uint)(_units.Count + 1), u);
        _units.Add(u);
        return u;
    }

    [UnityTest]
    public IEnumerator CenteredAxeSweep_HitsSixTiles_AndDamageIsPerTile()
    {
        yield return Setup();
        var axe = Make("Machado", Team.Player, new Vector2Int(10, 6), "IronAxe", str: 0);
        var foe = Make("Fera", Team.Enemy, new Vector2Int(10, 10), "Claws");
        int hp = foe.currentHP;
        var log = AttackResolver.ResolveAttack(axe, new PlannedAttack { Mode = AttackMode.Unit, TargetUnit = foe }, _units);
        StringAssert.Contains("6 tiles", log);
        Assert.AreEqual(5 * 6, hp - foe.currentHP, "5 por tile × 6 tiles, sem defesa");
    }

    [UnityTest]
    public IEnumerator Vit_IsFlatDefense_OnceOnTheTotal()
    {
        yield return Setup();
        var sword = Make("Espada", Team.Player, new Vector2Int(10, 6), "IronSword", str: 0);
        var tank = Make("Tanque", Team.Enemy, new Vector2Int(10, 10), "Claws", vit: 4);
        int hp = tank.currentHP;
        AttackResolver.ResolveAttack(sword, new PlannedAttack { Mode = AttackMode.Unit, TargetUnit = tank }, _units);
        Assert.AreEqual(6 * 3 - 1, hp - tank.currentHP, "18 − 1 (VIT 4 × 0,25), uma vez");
    }

    [UnityTest]
    public IEnumerator FreeAim_EdgeTile_HitsLess_ButGetsTheBonus()
    {
        yield return Setup();
        var bow = Make("Arco", Team.Player, new Vector2Int(10, 2), "ShortBow", str: 0);
        var foe = Make("Fera", Team.Enemy, new Vector2Int(10, 10), "Claws");
        int hp = foe.currentHP;
        // Mira livre num canto da fera: 1 tile, +25%.
        AttackResolver.ResolveAttack(bow, new PlannedAttack { Mode = AttackMode.Tile, TargetTile = new Vector2Int(10, 10) }, _units);
        Assert.AreEqual(Mathf.RoundToInt(9 * _t.freeAimDamageMult), hp - foe.currentHP, "1 tile × 9 × 1,25");
        // Mira livre no chão vazio: não pega ninguém.
        int hp2 = foe.currentHP;
        var log = AttackResolver.ResolveAttack(bow, new PlannedAttack { Mode = AttackMode.Tile, TargetTile = new Vector2Int(20, 20) }, _units);
        Assert.AreEqual(hp2, foe.currentHP);
        Assert.IsTrue(log == null || log.Contains("não pegou") || log.Contains("fora de alcance"), log);
    }

    [UnityTest]
    public IEnumerator Dodge_RemovesTilesByAgi_SingleTileBecomesAMiss()
    {
        yield return Setup();
        var axe = Make("Machado", Team.Player, new Vector2Int(10, 6), "IronAxe", str: 0);
        var foe = Make("Ágil", Team.Enemy, new Vector2Int(10, 10), "Claws", agi: 6);
        foe.dodgeReactTiles = AttackResolver.DodgeTiles(foe);
        Assert.AreEqual(2, foe.dodgeReactTiles, "AGI 6 → 2 tiles");
        int hp = foe.currentHP;
        AttackResolver.ResolveAttack(axe, new PlannedAttack { Mode = AttackMode.Unit, TargetUnit = foe }, _units);
        Assert.AreEqual(5 * 4, hp - foe.currentHP, "6 tiles − 2 da esquiva = 4");

        var bow = Make("Arco", Team.Player, new Vector2Int(10, 2), "ShortBow", str: 0);
        foe.dodgeReactTiles = AttackResolver.DodgeTiles(foe);
        int hp2 = foe.currentHP;
        AttackResolver.ResolveAttack(bow, new PlannedAttack { Mode = AttackMode.Unit, TargetUnit = foe }, _units);
        Assert.AreEqual(hp2, foe.currentHP, "golpe de 1 tile esquivado vira erro");
    }

    [UnityTest]
    public IEnumerator AlliesInTheShape_AreNotHit()
    {
        yield return Setup();
        var axe = Make("Machado", Team.Player, new Vector2Int(10, 6), "IronAxe", str: 0);
        var ally = Make("Aliado", Team.Player, new Vector2Int(10, 10), "IronSword");
        int hp = ally.currentHP;
        AttackResolver.ResolveAttack(axe, new PlannedAttack { Mode = AttackMode.Tile, TargetTile = new Vector2Int(11, 11) }, _units);
        Assert.AreEqual(hp, ally.currentHP);
    }

    [UnityTest]
    public IEnumerator Interpose_SplitsTheTiles()
    {
        yield return Setup();
        var axe = Make("Machado", Team.Player, new Vector2Int(10, 6), "IronAxe", str: 0);
        var foe = Make("Protegido", Team.Enemy, new Vector2Int(10, 10), "Claws");
        var guard = Make("Guarda", Team.Enemy, new Vector2Int(13, 10), "Claws");
        int hpFoe = foe.currentHP, hpGuard = guard.currentHP;
        AttackResolver.ResolveAttack(axe, new PlannedAttack { Mode = AttackMode.Unit, TargetUnit = foe }, _units, guard, foe);
        Assert.AreEqual(5 * 3, hpFoe - foe.currentHP, "protegido fica com metade (3 de 6)");
        Assert.AreEqual(5 * 3, hpGuard - guard.currentHP, "quem interpôs leva a outra metade");
    }

    [UnityTest]
    public IEnumerator Block_ThatAbsorbsEverything_Staggers()
    {
        yield return Setup();
        var axe = Make("Machadinha", Team.Player, new Vector2Int(10, 6), "Hatchet", str: 0);   // 3 × 3 = 9
        var foe = Make("Muralha", Team.Enemy, new Vector2Int(10, 10), "IronSword", vit: 4);
        foe.blockFlat = 20;
        int hp = foe.currentHP;
        AttackResolver.ResolveAttack(axe, new PlannedAttack { Mode = AttackMode.Unit, TargetUnit = foe }, _units);
        Assert.AreEqual(hp, foe.currentHP, "bloqueio absorveu tudo");
        Assert.IsTrue(AttackResolver.Last.AbsorbedAll, "sinaliza para desequilibrar o atacante");
    }

    [UnityTest]
    public IEnumerator AreaSpell_DamageIsPerTileInTheArea()
    {
        yield return Setup();
        var mage = Make("Maga", Team.Player, new Vector2Int(2, 2), "ArcaneStaff", str: 0);
        mage.stats.INT = 6;
        var full = Make("Inteiro", Team.Enemy, new Vector2Int(12, 12), "Claws", vit: 15);   // vida para não morrer (VIT não reduz magia)
        var ex = SpellCatalog.Get("Explosion");
        Assert.Greater(ex.area, 0);
        int amount = SpellResolver.Amount(mage, ex);
        int hp = full.currentHP;
        // Centro da área no centro da fera (a mira usa o anchor do tile + fp/2).
        SpellResolver.Resolve(mage, new PlannedSpell { SpellId = "Explosion", TargetTile = full.anchor }, _units, _grid, null);
        int dFull = hp - full.currentHP;
        Assert.AreEqual(Mathf.Max(1, amount - full.stats.MagicDefense), dFull, "fera inteira na área: dano cheio");

        var edge = Make("Ponta", Team.Enemy, new Vector2Int(20, 20), "Claws", vit: 15);
        int r = SpellResolver.AreaRadius(ex);
        int hpE = edge.currentHP;
        // Área centrada a (r+1) células do canto da fera: só a quina entra.
        var center = edge.anchor + new Vector2Int(-r, -r);
        SpellResolver.Resolve(mage, new PlannedSpell { SpellId = "Explosion", TargetTile = center - new Vector2Int(1, 1) }, _units, _grid, null);
        int dEdge = hpE - edge.currentHP;
        Assert.Less(dEdge, dFull, "pegar só a ponta dá menos dano que pegar inteiro");
        Assert.Greater(dEdge, 0);
    }

    [UnityTest]
    public IEnumerator Posture_Serene_GivesCurrentAndMax_SwitchCutsOnlyAboveMax()
    {
        yield return Setup();
        var u = Make("Serena", Team.Player, new Vector2Int(5, 5), "WoodenStaff");
        u.stats.WIS = 4;
        u.currentMana = u.stats.MaxMana;
        int max0 = u.stats.MaxMana;
        u.SetPosture(Posture.Serene);
        int max1 = u.stats.MaxMana;
        Assert.Greater(max1, max0);
        Assert.AreEqual(max1, u.currentMana, "ganho de máximo entra também no atual");
        u.SetPosture(Posture.Neutral);
        Assert.AreEqual(max0, u.stats.MaxMana);
        Assert.AreEqual(max0, u.currentMana, "acima do novo máximo: cortado no máximo");

        u.SetPosture(Posture.Serene);
        u.currentMana = max0 - 3;
        u.SetPosture(Posture.Neutral);
        Assert.AreEqual(max0 - 3, u.currentMana, "abaixo do máximo normal: não perde nada");
    }
}
