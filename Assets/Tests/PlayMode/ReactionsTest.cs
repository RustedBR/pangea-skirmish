using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PangeaSkirmish;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>Reações (GDD-020 §8): Bloquear fixo, Interpor qualquer aliado colado, custos por reação.</summary>
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
    public IEnumerator Interpose_AnyAdjacentAlly_WithStamina()
    {
        yield return Setup();
        var hurt = Make("Aliado", Team.Player, new Vector2Int(5, 6), "IronSword");
        var guard = Make("Guarda", Team.Player, new Vector2Int(8, 6), "IronSword");
        var foe = Make("Inimigo", Team.Enemy, new Vector2Int(12, 6), "IronSword", str: 6);
        guard.currentStamina = guard.stats.MaxStamina;
        var list = (System.Collections.Generic.List<Unit>)Call("FindInterceptors", hurt, foe);
        Assert.Contains(guard, list, "aliado que chega colado com uma andada pode interpor");
        guard.currentStamina = Unit.ReactionCost(ReactionKind.Intercept) - 1;
        list = (System.Collections.Generic.List<Unit>)Call("FindInterceptors", hurt, foe);
        Assert.IsFalse(list.Contains(guard), "sem Stamina para Interpor (2), não interpõe");
    }

    [UnityTest]
    public IEnumerator Intercept_NeedsAdjacencyAndStamina()
    {
        yield return Setup();
        var hurt = Make("Ferido", Team.Player, new Vector2Int(2, 6), "IronSword");
        var far = Make("Longe", Team.Player, new Vector2Int(14, 6), "IronSword");
        var foe = Make("Inimigo", Team.Enemy, new Vector2Int(12, 12), "IronSword");
        far.currentStamina = far.stats.MaxStamina;
        var list = (System.Collections.Generic.List<Unit>)Call("FindInterceptors", hurt, foe);
        Assert.IsFalse(list.Contains(far), "longe demais para chegar com uma andada não intercepta");
    }

    [Test]
    public void ReactionCosts_PerKind()
    {
        Assert.AreEqual(1, Unit.ReactionCost(ReactionKind.Dodge));
        Assert.AreEqual(1, Unit.ReactionCost(ReactionKind.Block));
        Assert.AreEqual(3, Unit.ReactionCost(ReactionKind.CounterAttack));
        Assert.AreEqual(2, Unit.ReactionCost(ReactionKind.Intercept));
        Assert.AreEqual(18f, Resources.Load<GameTuning>("GameTuning").reactionChoiceTime, "18 s para decidir a reação");
    }
}
