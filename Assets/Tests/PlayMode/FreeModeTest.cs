using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PangeaSkirmish;
using PangeaSkirmish.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>Modo Livre: adicionar, remover, reordenar e trocar destino de ações do plano.</summary>
public class FreeModeTest
{
    private PlanningController _pc;
    private Unit _hero, _foe;
    private readonly MethodInfo _appendMove = typeof(PlanningController).GetMethod("FreeAppendMove", BindingFlags.NonPublic | BindingFlags.Instance);
    private readonly MethodInfo _appendAtk  = typeof(PlanningController).GetMethod("FreeAppendAttack", BindingFlags.NonPublic | BindingFlags.Instance);
    private readonly MethodInfo _block      = typeof(PlanningController).GetMethod("OnBlockAction", BindingFlags.NonPublic | BindingFlags.Instance);
    private readonly MethodInfo _retarget   = typeof(PlanningController).GetMethod("RetargetMove", BindingFlags.NonPublic | BindingFlags.Instance);

    private IEnumerator Setup()
    {
        LogAssert.ignoreFailingMessages = true;
        foreach (var o in Object.FindObjectsByType<Unit>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        foreach (var o in Object.FindObjectsByType<GridManager>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        foreach (var o in Object.FindObjectsByType<PlanningController>(FindObjectsSortMode.None)) Object.Destroy(o);
        yield return null;
        var tuning = Resources.Load<GameTuning>("GameTuning");
        RuntimeTuning.Active = tuning; AttributeStats.Formulas = tuning.statFormulas;
        var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        var grid = new GameObject("GridManager").AddComponent<GridManager>();
        grid.width = 20; grid.height = 20; grid.Build();
        Unit Make(string n, Team t, Vector2Int a)
        {
            var u = new GameObject(n).AddComponent<Unit>();
            u.unitName = n; u.team = t; u.weaponId = "IronSword";
            u.stats = new UnitStatBlock { STR = 4, VIT = 4, DEX = 3, AGI = 4 }.ToAttributeStats();
            u.Init(grid, a, Color.red, "Sprites/TinyTactics/Warrior");
            return u;
        }
        _hero = Make("Heroi", Team.Player, new Vector2Int(2, 6));
        _foe = Make("Alvo", Team.Enemy, new Vector2Int(12, 6));
        var hud = PangeaScreen.Spawn<BattleHUD>("BattleHUD");
        yield return null; yield return null;
        _pc = new GameObject("Planner").AddComponent<PlanningController>();
        var units = new List<Unit> { _hero, _foe };
        _pc.Setup(grid, cam, units);
        _pc.SetHUD(hud);
        _pc.Begin(_hero);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Append_Move_Reachable_AddsAndFarIsRefused()
    {
        yield return Setup();
        int step = _hero.stats.MoveStep;
        _appendMove.Invoke(_pc, new object[] { new Vector2Int(2 + step, 6) });
        Assert.AreEqual(1, _hero.plannedMoveCount);
        Assert.AreEqual(new Vector2Int(2 + step, 6), _hero.plannedAnchor);

        _appendMove.Invoke(_pc, new object[] { new Vector2Int(2 + step + step + 3, 6) });
        Assert.AreEqual(1, _hero.plannedMoveCount, "andada longe demais deve ser recusada");
    }

    [UnityTest]
    public IEnumerator Append_Attack_OnceOrFillsRemainingTicks()
    {
        yield return Setup();
        _appendAtk.Invoke(_pc, new object[] { _foe, false });
        Assert.AreEqual(1, _hero.plannedAttacks.Count);
        int before = _hero.remainingAP;
        _appendAtk.Invoke(_pc, new object[] { _foe, true });
        Assert.Greater(_hero.plannedAttacks.Count, 1, "duplo clique preenche o tempo");
        Assert.Less(_hero.remainingAP, TickRules.Duration(_hero, ActionType.Attack, false), "não sobra tempo para outro ataque");
        Assert.Less(_hero.remainingAP, before);
    }

    [UnityTest]
    public IEnumerator Remove_FirstMove_ShiftsIndicesAndAnchor()
    {
        yield return Setup();
        _appendMove.Invoke(_pc, new object[] { new Vector2Int(4, 6) });
        _appendMove.Invoke(_pc, new object[] { new Vector2Int(6, 6) });
        _appendAtk.Invoke(_pc, new object[] { _foe, false });
        Assert.AreEqual(3, _hero.actionSequence.Count);

        _block.Invoke(_pc, new object[] { 0, 12 });   // remove o 1º andar

        Assert.AreEqual(2, _hero.actionSequence.Count);
        Assert.AreEqual(1, _hero.plannedMoveCount);
        Assert.AreEqual(1, _hero.plannedPath.Count);
        Assert.AreEqual(0, _hero.actionSequence[0].Index, "o 2º andar passa a ser o índice 0");
        Assert.AreEqual(new Vector2Int(6, 6), _hero.plannedAnchor);
    }

    [UnityTest]
    public IEnumerator Reorder_SwapsActionsAndRecomputesAnchor()
    {
        yield return Setup();
        _appendMove.Invoke(_pc, new object[] { new Vector2Int(4, 6) });
        _appendMove.Invoke(_pc, new object[] { new Vector2Int(6, 6) });

        _block.Invoke(_pc, new object[] { 1, 10 });   // 2º andar vem antes do 1º

        Assert.AreEqual(1, _hero.actionSequence[0].Index);
        Assert.AreEqual(new Vector2Int(4, 6), _hero.plannedAnchor, "o último destino agora é (4,6)");
    }

    [UnityTest]
    public IEnumerator Retarget_ChangesDestinationOfThatMove()
    {
        yield return Setup();
        _appendMove.Invoke(_pc, new object[] { new Vector2Int(4, 6) });
        _retarget.Invoke(_pc, new object[] { 0, new Vector2Int(3, 8) });
        Assert.AreEqual(new Vector2Int(3, 8), _hero.plannedPath[0]);
        Assert.AreEqual(new Vector2Int(3, 8), _hero.plannedAnchor);
    }

    [UnityTest]
    public IEnumerator HudPointerDown_BlocksMapClickInSameFrame()
    {
        yield return Setup();
        var hud = Object.FindFirstObjectByType<BattleHUD>();
        var root = hud.GetComponent<UIDocument>().rootVisualElement;
        var any = root.Q<VisualElement>("intents-panel");
        Assert.IsNotNull(any, "painel de intenções");
        using (var e = PointerDownEvent.GetPooled())
        {
            e.target = any;
            any.SendEvent(e);
        }
        var f = typeof(BattleHUD).GetField("_uiDownFrame", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.AreEqual(Time.frameCount, (int)f.GetValue(hud), "o toque consumido pela HUD deve ser registrado");
    }
}
