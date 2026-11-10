using NUnit.Framework;
using PangeaSkirmish;
using UnityEngine;

/// <summary>Semi-turno por ticks (GDD-014): durações, orçamento e fila.</summary>
[TestFixture]
public class TickRulesTests
{
    private GameObject _go;
    private Unit _u;
    private GameTuning _t;

    [SetUp]
    public void SetUp()
    {
        AttributeStats.Formulas = new StatFormulas();
        _t = Tuning.Get();
        _go = new GameObject("TickUnit");
        _u = _go.AddComponent<Unit>();
        _u.stats = new AttributeStats { AGI = 0f };
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_go);
        AttributeStats.Formulas = null;
    }

    [Test]
    public void Duration_WithZeroAgi_EqualsBaseTicks()
    {
        Assert.AreEqual(_t.ticksMove,        TickRules.Duration(_u, ActionType.Move, false));
        Assert.AreEqual(_t.ticksAttack,      TickRules.Duration(_u, ActionType.Attack, false));
        Assert.AreEqual(_t.ticksPowerStrike, TickRules.Duration(_u, ActionType.Attack, true));
    }

    [Test]
    public void Duration_HighAgi_IsFasterButNeverBelowOne()
    {
        _u.stats.AGI = _t.tickAgiFactor; // fator 0.5
        Assert.Less(TickRules.Duration(_u, ActionType.Attack, true), _t.ticksPowerStrike);
        _u.stats.AGI = 1000f;
        Assert.AreEqual(1, TickRules.Duration(_u, ActionType.Attack, true));
    }

    [Test]
    public void Wait_DoesNotScaleWithAgi()
    {
        _u.stats.AGI = 1000f;
        Assert.AreEqual(Mathf.Max(1, _t.ticksWait), TickRules.Duration(_u, ActionType.Wait, false));
    }

    [Test]
    public void Budget_IsMaxQueueMinusPlannedTicks()
    {
        _u.actionSequence.Add(new ScheduledAction { Type = ActionType.Attack });
        _u.RecomputeTickBudget();
        Assert.AreEqual(_t.maxQueueTicks - _t.ticksAttack, _u.remainingAP);
    }

    [Test]
    public void EmptyPlan_BecomesWaitAndMakesUnitBusy()
    {
        Assert.IsFalse(_u.IsBusy);
        _u.BuildQueueFromPlan();
        Assert.IsTrue(_u.IsBusy);
        Assert.AreEqual(ActionType.Wait, _u.queue[0].Type);
    }

    [Test]
    public void BuildQueue_CopiesMoveDestinationsByValue()
    {
        _u.plannedPath.Add(new Vector2Int(3, 4));
        _u.actionSequence.Add(new ScheduledAction { Type = ActionType.Move, Index = 0 });
        _u.BuildQueueFromPlan();
        _u.plannedPath.Clear(); // carry-over não pode depender da lista
        Assert.AreEqual(new Vector2Int(3, 4), _u.queue[0].Dest);
        Assert.AreEqual(_t.ticksMove, _u.QueueTicksRemaining);
    }

    [Test]
    public void DelayQueue_AddsTicksAtFront()
    {
        _u.actionSequence.Add(new ScheduledAction { Type = ActionType.Attack });
        _u.BuildQueueFromPlan();
        int before = _u.QueueTicksRemaining;
        _u.DelayQueue(1);
        Assert.AreEqual(before + 1, _u.QueueTicksRemaining);
        Assert.AreEqual(ActionType.Wait, _u.queue[0].Type);
    }
}
