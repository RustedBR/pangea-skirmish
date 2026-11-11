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
        foreach (var old in Object.FindObjectsByType<Unit>(FindObjectsSortMode.None)) Object.Destroy(old.gameObject);
        foreach (var old in Object.FindObjectsByType<GridManager>(FindObjectsSortMode.None)) Object.Destroy(old.gameObject);
        foreach (var old in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) Object.Destroy(old.gameObject);
        yield return null;

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

    // ---------------------------------------------------------------- IntentBuilder (GDD-016 §5)

    private static int CountOf(Unit u, ActionType t)
    {
        int n = 0;
        foreach (var a in u.actionSequence) if (a.Type == t) n++;
        return n;
    }

    [UnityTest]
    public IEnumerator Charge_WalksThenFillsWithAttacks()
    {
        yield return Setup();
        var hero = Spawn("Heroi", Team.Player, new Vector2Int(1, 6), false);
        var foe = Spawn("Alvo", Team.Enemy, new Vector2Int(1 + 3 + hero.stats.AttackRange + 2, 6), false);

        Assert.IsTrue(IntentBuilder.Charge(hero, foe, _units, _grid));

        Assert.Greater(CountOf(hero, ActionType.Move), 0, "precisa andar para alcançar");
        Assert.Greater(CountOf(hero, ActionType.Attack), 0, "deveria atacar com o tempo que sobra");
        Assert.AreEqual(ActionType.Attack, hero.actionSequence[hero.actionSequence.Count - 1].Type);
        Assert.LessOrEqual(hero.PlannedTicks(), Tuning.Get().maxQueueTicks);
        Assert.Less(hero.remainingAP, TickRules.Duration(hero, ActionType.Attack, false), "gasta todo o tempo que couber");
    }

    [UnityTest]
    public IEnumerator HitAndRun_AttacksOnceThenWalksBack()
    {
        yield return Setup();
        var hero = Spawn("Heroi", Team.Player, new Vector2Int(2, 6), false);
        var foe = Spawn("Alvo", Team.Enemy, new Vector2Int(2 + 3 + hero.stats.AttackRange + 2, 6), false);
        var home = hero.anchor;

        Assert.IsTrue(IntentBuilder.HitAndRun(hero, foe, _units, _grid));

        Assert.AreEqual(1, CountOf(hero, ActionType.Attack), "exatamente um golpe");
        Assert.AreEqual(ActionType.Move, hero.actionSequence[hero.actionSequence.Count - 1].Type, "termina recuando");
        int atkIdx = hero.actionSequence.FindIndex(a => a.Type == ActionType.Attack);
        Vector2Int strikeFrom = hero.plannedPath[hero.actionSequence[atkIdx - 1].Index];
        int dStrike = Mathf.Abs(strikeFrom.x - home.x) + Mathf.Abs(strikeFrom.y - home.y);
        int dEnd = Mathf.Abs(hero.plannedAnchor.x - home.x) + Mathf.Abs(hero.plannedAnchor.y - home.y);
        Assert.Less(dEnd, dStrike, "termina mais perto de casa do que de onde golpeou");
    }

    [UnityTest]
    public IEnumerator Retreat_IncreasesDistanceFromEnemies()
    {
        yield return Setup();
        var hero = Spawn("Heroi", Team.Player, new Vector2Int(6, 6), false);
        var foe = Spawn("Alvo", Team.Enemy, new Vector2Int(9, 6), false);
        int before = GridManager.FootprintGap(hero.anchor, 3, foe.anchor, 3);

        Assert.IsTrue(IntentBuilder.Retreat(hero, _units, _grid));

        Assert.AreEqual(0, CountOf(hero, ActionType.Attack));
        Assert.Greater(GridManager.FootprintGap(hero.plannedAnchor, 3, foe.anchor, 3), before);
    }

    [UnityTest]
    public IEnumerator MoveTo_ReachesNearbyCell()
    {
        yield return Setup();
        var hero = Spawn("Heroi", Team.Player, new Vector2Int(2, 2), false);
        var dest = new Vector2Int(2 + hero.stats.MoveStep, 2);

        Assert.IsTrue(IntentBuilder.MoveTo(hero, dest, _units, _grid));
        Assert.AreEqual(dest, hero.plannedAnchor);
    }

    [UnityTest]
    public IEnumerator HitAndRun_AlreadyInRange_BacksAwayAfterTheHit()
    {
        yield return Setup();
        var hero = Spawn("Heroi", Team.Player, new Vector2Int(5, 6), false);
        var foe = Spawn("Alvo", Team.Enemy, new Vector2Int(8, 6), false);
        int before = GridManager.FootprintGap(hero.anchor, 3, foe.anchor, 3);

        Assert.IsTrue(IntentBuilder.HitAndRun(hero, foe, _units, _grid));

        Assert.AreEqual(ActionType.Attack, hero.actionSequence[0].Type, "não precisa andar para bater");
        Assert.AreEqual(1, CountOf(hero, ActionType.Attack));
        Assert.Greater(GridManager.FootprintGap(hero.plannedAnchor, 3, foe.anchor, 3), before, "afasta-se depois do golpe");
    }

    [UnityTest]
    public IEnumerator Beast_AttacksAsSlowlyAsAMediumWeapon()
    {
        yield return Setup();
        var beast = Spawn("Fera", Team.Enemy, new Vector2Int(8, 6), true);   // arma "Claws" (fora do catálogo)
        var hero  = Spawn("Heroi", Team.Player, new Vector2Int(2, 6), false); // IronSword (média)
        Assert.AreEqual(hero.stats.AttackTicks, beast.stats.AttackTicks, "mesma cadência que uma arma média");
        Assert.GreaterOrEqual(TickRules.Duration(beast, ActionType.Attack, false), 2);
    }
}
