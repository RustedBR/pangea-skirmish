using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PangeaSkirmish;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>Reações inspiradas no PF2e (GDD-017): Bloquear fixo, Interceptar e Passo reativo.</summary>
public class ReactionsTest
{
    private RoundManager _rm;
    private GridManager _grid;
    private readonly List<Unit> _units = new List<Unit>();

    private IEnumerator Setup()
    {
        LogAssert.ignoreFailingMessages = true;
        foreach (var o in Object.FindObjectsByType<Unit>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        foreach (var o in Object.FindObjectsByType<GridManager>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        foreach (var o in Object.FindObjectsByType<RoundManager>(FindObjectsSortMode.None)) Object.Destroy(o);
        yield return null;
        var tuning = Resources.Load<GameTuning>("GameTuning");
        RuntimeTuning.Active = tuning; AttributeStats.Formulas = tuning.statFormulas;
        _grid = new GameObject("GridManager").AddComponent<GridManager>();
        _grid.width = 20; _grid.height = 20; _grid.Build();
        _units.Clear();
        _rm = new GameObject("RM").AddComponent<RoundManager>();
        var f = typeof(RoundManager);
        f.GetField("_units", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_rm, _units);
        f.GetField("_grid", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_rm, _grid);
        yield return null;
    }

    private Unit Make(string n, Team t, Vector2Int a, string weapon, float str = 4, float vit = 4, float agi = 3)
    {
        var u = new GameObject(n).AddComponent<Unit>();
        u.unitName = n; u.team = t; u.weaponId = weapon;
        u.stats = new UnitStatBlock { STR = str, VIT = vit, DEX = 3, AGI = agi }.ToAttributeStats();
        u.Init(_grid, a, Color.red, "Sprites/TinyTactics/Warrior");
        _units.Add(u);
        return u;
    }

    private object Call(string name, params object[] args) =>
        typeof(RoundManager).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_rm, args);

    [Test]
    public void BlockAmount_IsFlat_VitPlusWeaponHardness()
    {
        var tuning = Resources.Load<GameTuning>("GameTuning");
        var u = new GameObject("B").AddComponent<Unit>();
        u.weaponId = "IronSword";
        u.stats = new AttributeStats { VIT = 8f };
        int expected = Mathf.Max(1, Mathf.RoundToInt(8f * tuning.blockFlatPerVIT) + WeaponCatalog.Get("IronSword").blockHardness);
        Assert.AreEqual(expected, RoundManager.BlockAmount(u));
        u.stats.VIT = 16f;
        Assert.Greater(RoundManager.BlockAmount(u), expected, "mais VIT bloqueia mais, em valor fixo");
        Object.DestroyImmediate(u.gameObject);
    }

    [UnityTest]
    public IEnumerator Intercept_AllyAdjacentWithMoreHp_TakesTheHitForAWoundedTarget()
    {
        yield return Setup();
        var hurt = Make("Ferido", Team.Player, new Vector2Int(5, 6), "IronSword");
        var guard = Make("Guarda", Team.Player, new Vector2Int(8, 6), "IronSword");
        var foe = Make("Inimigo", Team.Enemy, new Vector2Int(12, 6), "IronSword", str: 6);
        hurt.currentHP = 3;                       // golpe fatal
        guard.currentStamina = guard.stats.MaxStamina; guard.remainingReactions = 1;

        var picked = (Unit)Call("FindInterceptor", hurt, foe);
        Assert.AreSame(guard, picked, "o aliado adjacente com mais vida intercepta");

        hurt.currentHP = hurt.stats.MaxHP;       // sem risco: ninguém se joga na frente
        Assert.IsNull(Call("FindInterceptor", hurt, foe));
    }

    [UnityTest]
    public IEnumerator Intercept_NeedsAdjacencyAndStamina()
    {
        yield return Setup();
        var hurt = Make("Ferido", Team.Player, new Vector2Int(2, 6), "IronSword");
        var far = Make("Longe", Team.Player, new Vector2Int(14, 6), "IronSword");
        var foe = Make("Inimigo", Team.Enemy, new Vector2Int(12, 12), "IronSword");
        hurt.currentHP = 3; far.currentStamina = far.stats.MaxStamina; far.remainingReactions = 1;
        Assert.IsNull(Call("FindInterceptor", hurt, foe), "longe demais não intercepta");
    }

    [UnityTest]
    public IEnumerator Step_FindsATileOutsideTheAttackersRange()
    {
        yield return Setup();
        var runner = Make("Veloz", Team.Player, new Vector2Int(5, 6), "IronSword", agi: 6);
        var beast = Make("Fera", Team.Enemy, new Vector2Int(8, 6), "Claws");    // desarmada: alcance 1
        Assert.AreEqual(1, beast.stats.AttackRange);
        var dest = (Vector2Int?)Call("FindStepTile", runner, beast);
        Assert.IsTrue(dest.HasValue, "há um tile para sair do alcance de uma fera de alcance 1");
        int gap = GridManager.FootprintGap(dest.Value, runner.stats.Footprint, beast.anchor, beast.stats.Footprint);
        Assert.Greater(gap, beast.stats.AttackRange);
    }
}
