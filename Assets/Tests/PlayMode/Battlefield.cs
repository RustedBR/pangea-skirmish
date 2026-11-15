using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using PangeaSkirmish;
using PangeaSkirmish.UI;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Campo de batalha mínimo para os testes (2026-11-14): grade, HUD e RoundManager de verdade,
/// unidades criadas à mão e um round executado de uma vez (Step).
/// </summary>
public class Battlefield
{
    public GridManager Grid; public BattleHUD Hud; public RoundManager Round; public Camera Cam;
    public readonly List<Unit> Units = new List<Unit>();
    private MethodInfo _actionRoutine;
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Clean()
    {
        foreach (var o in Object.FindObjectsByType<Unit>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        foreach (var o in Object.FindObjectsByType<GridManager>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        foreach (var o in Object.FindObjectsByType<RoundManager>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        foreach (var o in Object.FindObjectsByType<PangeaScreen>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        foreach (var o in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        UnitRegistry.Clear();
        Time.timeScale = 1f; Time.captureDeltaTime = 0f;
    }

    public IEnumerator Build(int size = 30)
    {
        LogAssert.ignoreFailingMessages = true;
        Clean();
        yield return null;
        var tuning = Resources.Load<GameTuning>("GameTuning");
        RuntimeTuning.Active = tuning; AttributeStats.Formulas = tuning.statFormulas;
        RuntimeMultiplayerSession.IsMultiplayer = true;
        RuntimeMultiplayerSession.CurrentConfig = new RoomConfigData { gameMode = GameModes.Coop };
        BattleRng.Seed(4321);
        Cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        Grid = new GameObject("GridManager").AddComponent<GridManager>();
        Grid.width = size; Grid.height = size; Grid.Build();
        Hud = PangeaScreen.Spawn<BattleHUD>("BattleHUD");
        yield return null; yield return null;
        Round = new GameObject("RM").AddComponent<RoundManager>();
        var T = typeof(RoundManager);
        T.GetField("_units", F).SetValue(Round, Units);
        T.GetField("_grid", F).SetValue(Round, Grid);
        T.GetField("_hud", F).SetValue(Round, Hud);
        T.GetField("_cam", F).SetValue(Round, Cam);
        SpellField.Current = (SpellField)T.GetField("_spellField", F).GetValue(Round);
        _actionRoutine = T.GetMethod("ActionRoutine", F);
    }

    /// <summary>Unidade de jogador (ou inimiga) controlada à mão; teamId separa os lados.</summary>
    public Unit Add(string name, Team team, Vector2Int at, string weapon, int teamId, float agi = 3, float intel = 6)
    {
        var u = new GameObject(name).AddComponent<Unit>();
        u.unitName = name; u.team = team; u.weaponId = weapon; u.teamId = teamId; u.ownerId = (ulong)teamId;
        u.stats = new UnitStatBlock { STR = 3, VIT = 4, DEX = 0, AGI = agi, INT = intel, WIS = 6 }.ToAttributeStats();
        u.Init(Grid, at, Color.red, "Sprites/TinyTactics/Warrior");
        u.currentHP = u.stats.MaxHP; u.currentMana = u.stats.MaxMana; u.currentStamina = u.stats.MaxStamina;
        Units.Add(u);
        UnitRegistry.Register((uint)Units.Count, u);
        return u;
    }

    /// <summary>Roda um round inteiro (os ticks do round) como o jogo faz depois de todos confirmarem.</summary>
    public IEnumerator Step()
    {
        yield return Round.StartCoroutine((IEnumerator)_actionRoutine.Invoke(Round, null));
        yield return null;
    }

    public object Call(string method, params object[] args) => typeof(RoundManager).GetMethod(method, F).Invoke(Round, args);

    // ── Planos ──
    public static void Begin(Unit u) => u.BeginPlanning();
    public static void Wait(Unit u, int times = 1) { for (int i = 0; i < times; i++) u.actionSequence.Add(new ScheduledAction { Type = ActionType.Wait }); u.RecomputeTickBudget(); }
    public static void Move(Unit u, Vector2Int dest)
    {
        u.plannedPath.Add(dest); u.plannedMoveCount++;
        u.actionSequence.Add(new ScheduledAction { Type = ActionType.Move, Index = u.plannedPath.Count - 1 });
        u.RecomputePlannedAnchor(); u.RecomputeTickBudget();
    }
    public static void Attack(Unit u, Unit target)
    {
        u.plannedAttacks.Add(new PlannedAttack { Mode = AttackMode.Unit, TargetUnit = target });
        u.actionSequence.Add(new ScheduledAction { Type = ActionType.Attack, Index = u.plannedAttacks.Count - 1 });
        u.RecomputeTickBudget();
    }
    public static void Cast(Unit u, string spell, Unit target)
    {
        u.plannedSpells.Add(new PlannedSpell { SpellId = spell, TargetUnit = target, TargetTile = target != null ? target.anchor : u.anchor });
        u.actionSequence.Add(new ScheduledAction { Type = ActionType.Spell, Index = u.plannedSpells.Count - 1 });
        u.RecomputeTickBudget();
    }

    /// <summary>Esperas para que uma ação de <paramref name="later"/> ticks termine junto com uma de <paramref name="first"/>.</summary>
    public static int Align(Unit waiter, int first, int later)
    {
        int wt = TickRules.Duration(waiter, ActionType.Wait, false);
        return (first - later) % wt == 0 && first >= later ? (first - later) / wt : -1;
    }
}
