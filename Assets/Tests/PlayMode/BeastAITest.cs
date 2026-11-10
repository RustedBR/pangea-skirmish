using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PangeaSkirmish;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>IA das feras (GDD-015 §4): aproxima, ataca no alcance e é determinística.</summary>
public class BeastAITest
{
    private GridManager _grid;
    private readonly List<Unit> _units = new List<Unit>();

    private IEnumerator Setup()
    {
        LogAssert.ignoreFailingMessages = true;
        var tuning = Resources.Load<GameTuning>("GameTuning");
        RuntimeTuning.Active = tuning;
        AttributeStats.Formulas = tuning.statFormulas;
        _grid = new GameObject("GridManager").AddComponent<GridManager>();
        _grid.width = 16; _grid.height = 16;
        _grid.Build();
        for (int x = 0; x < 16; x++) for (int y = 0; y < 16; y++) _grid.SetCell(x, y, "grass_tile_full", 0, "");
        _units.Clear();
        yield return null;
    }

    private Unit Spawn(string name, Team team, Vector2Int anchor, bool beast)
    {
        var u = new GameObject(name).AddComponent<Unit>();
        u.unitName = name; u.team = team;
        u.weaponId = beast ? "Claws" : "IronSword";
        if (beast) u.ownerId = BeastAI.OwnerId;
        u.stats = new UnitStatBlock { STR = 4, VIT = 4, DEX = 2, AGI = 3 }.ToAttributeStats();
        u.Init(_grid, anchor, Color.red, "Sprites/TinyTactics/Warrior");
        _units.Add(u);
        return u;
    }

    [UnityTest]
    public IEnumerator FarTarget_BeastMovesCloser()
    {
        yield return Setup();
        var hero = Spawn("Heroi", Team.Player, new Vector2Int(1, 6), false);
        var beast = Spawn("Fera", Team.Enemy, new Vector2Int(12, 6), true);
        int before = GridManager.FootprintGap(beast.anchor, 3, hero.anchor, 3);

        BeastAI.Plan(beast, _units, _grid);

        Assert.Greater(beast.plannedMoveCount, 0, "a fera deveria andar");
        int after = GridManager.FootprintGap(beast.plannedAnchor, 3, hero.anchor, 3);
        Assert.Less(after, before, "a fera deveria terminar mais perto do alvo");
        Assert.LessOrEqual(beast.PlannedTicks(), Tuning.Get().maxQueueTicks, "plano não cabe na fila");
    }

    [UnityTest]
    public IEnumerator AdjacentTarget_BeastAttacksWithoutMoving()
    {
        yield return Setup();
        var hero = Spawn("Heroi", Team.Player, new Vector2Int(4, 6), false);
        var beast = Spawn("Fera", Team.Enemy, new Vector2Int(7, 6), true);

        BeastAI.Plan(beast, _units, _grid);

        Assert.AreEqual(0, beast.plannedMoveCount);
        Assert.Greater(beast.plannedAttacks.Count, 0, "a fera deveria atacar");
        Assert.AreSame(hero, beast.plannedAttacks[0].TargetUnit);
    }

    [UnityTest]
    public IEnumerator SameState_SamePlan()
    {
        yield return Setup();
        Spawn("Heroi", Team.Player, new Vector2Int(1, 3), false);
        Spawn("Heroi2", Team.Player, new Vector2Int(1, 10), false);
        var beast = Spawn("Fera", Team.Enemy, new Vector2Int(11, 6), true);

        BeastAI.Plan(beast, _units, _grid);
        var first = new List<Vector2Int>(beast.plannedPath);
        BeastAI.Plan(beast, _units, _grid);

        CollectionAssert.AreEqual(first, beast.plannedPath, "o mesmo estado deve gerar o mesmo plano");
    }

    [UnityTest]
    public IEnumerator NextLayer_RestoresPartiallyAndRevives()
    {
        yield return Setup();
        var hurt = Spawn("Ferido", Team.Player, new Vector2Int(1, 2), false);
        var dead = Spawn("Caido", Team.Player, new Vector2Int(1, 8), false);
        int max = hurt.stats.MaxHP;
        hurt.currentHP = 1;
        hurt.TeleportTo(new Vector2Int(9, 9));
        hurt.BuildQueueFromPlan();
        dead.TakeDamage(9999);
        Assert.IsTrue(dead.IsDead);

        hurt.RestoreForNextLayer(0.30f, 0.50f, 0.25f);
        dead.RestoreForNextLayer(0.30f, 0.50f, 0.25f);

        Assert.AreEqual(1 + Mathf.RoundToInt(max * 0.30f), hurt.currentHP, "cura deve ser parcial");
        Assert.Less(hurt.currentHP, max, "descer tem peso: não cura tudo");
        Assert.AreEqual(new Vector2Int(1, 2), hurt.anchor, "volta ao ponto de partida");
        Assert.IsFalse(hurt.IsBusy, "fila limpa");
        Assert.IsFalse(dead.IsDead, "unidade caída volta de pé");
        Assert.AreEqual(Mathf.Max(1, Mathf.RoundToInt(dead.stats.MaxHP * 0.25f)), dead.currentHP);
        Assert.IsTrue(dead.gameObject.activeSelf);
    }
}
