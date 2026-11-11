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
        _u.stats = new AttributeStats { AGI = 0f, DEX = 0f };
        _u.currentHP = 10; // viva (IsDead = HP <= 0)
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_go);
        AttributeStats.Formulas = null;
    }

    [Test]
    public void Duration_WithZeroDex_EqualsWeaponWeight()
    {
        _u.stats.DEX = 0f;
        _u.stats.AttackTicks = 3; // arma pesada
        Assert.AreEqual(_t.ticksMove, TickRules.Duration(_u, ActionType.Move, false));
        Assert.AreEqual(3, TickRules.Duration(_u, ActionType.Attack, false));
        Assert.AreEqual(3 + _t.ticksPowerStrikeExtra, TickRules.Duration(_u, ActionType.Attack, true));
    }

    [Test]
    public void Dex_SpeedsUpActions_ButNotWalking()
    {
        _u.stats.AttackTicks = 3;
        _u.stats.DEX = _t.tickDexFactor; // fator 0.5
        Assert.Less(TickRules.Duration(_u, ActionType.Attack, false), 3);
        _u.stats.DEX = 1000f;
        Assert.AreEqual(1, TickRules.Duration(_u, ActionType.Attack, true));
        Assert.AreEqual(_t.ticksMove, TickRules.Duration(_u, ActionType.Move, false), "andar não depende da DEX");
    }

    [Test]
    public void Agi_DoesNotSpeedUpActions_ButExtendsWalk()
    {
        _u.stats.DEX = 0f; _u.stats.AttackTicks = 2;
        _u.stats.AGI = 0f;
        int slow = TickRules.Duration(_u, ActionType.Attack, false);
        int shortWalk = _u.stats.MoveStep;
        _u.stats.AGI = 8f;
        Assert.AreEqual(slow, TickRules.Duration(_u, ActionType.Attack, false));
        Assert.Greater(_u.stats.MoveStep, shortWalk);
        _u.stats.AGI = 1000f;
        Assert.AreEqual(_t.maxTilesPerMove, _u.stats.MoveStep);
    }

    [Test]
    public void Wait_DoesNotScale()
    {
        _u.stats.DEX = 1000f;
        Assert.AreEqual(Mathf.Max(1, _t.ticksWait), TickRules.Duration(_u, ActionType.Wait, false));
    }

    [Test]
    public void Instinct_PlanCostIsCommittedWhenQueueIsBuilt()
    {
        _u.stats.STR = 4f; _u.stats.VIT = 4f;
        _u.currentInstinct = _u.stats.MaxInstinct;
        int max = _u.currentInstinct;
        Assert.AreEqual(Mathf.RoundToInt(_t.instinctBase + 8 * _t.instinctPerBodyPoint), max);

        _u.actionSequence.Add(new ScheduledAction { Type = ActionType.Attack, IsBonus = true });
        _u.RecomputeTickBudget();
        Assert.AreEqual(max - _t.instinctPowerStrike, _u.remainingBAP, "golpe poderoso reserva Instinto no plano");

        _u.BuildQueueFromPlan();
        Assert.AreEqual(max - _t.instinctPowerStrike, _u.currentInstinct, "o gasto vale ao comprometer a fila");
        _u.GainInstinct(999);
        Assert.AreEqual(max, _u.currentInstinct, "não passa do máximo");
    }

    [Test]
    public void Budget_IsMaxQueueMinusPlannedTicks()
    {
        _u.actionSequence.Add(new ScheduledAction { Type = ActionType.Attack });
        _u.RecomputeTickBudget();
        Assert.AreEqual(_t.maxQueueTicks - TickRules.Duration(_u, ActionType.Attack, false), _u.remainingAP);
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

    [Test]
    public void Increment_NextAttack_SpendsInstinctAndLengthensIt()
    {
        _u.stats.STR = 4f; _u.stats.VIT = 4f; _u.stats.AttackTicks = 2;
        _u.currentInstinct = _u.stats.MaxInstinct;
        int before = _u.currentInstinct;
        _u.plannedAttacks.Add(new PlannedAttack { Mode = AttackMode.Tile });
        _u.actionSequence.Add(new ScheduledAction { Type = ActionType.Attack, Index = 0 });
        _u.BuildQueueFromPlan();
        int dur = _u.queue[0].Duration;

        Assert.IsTrue(_u.TryIncrementNext(0), "golpe poderoso na próxima ação");
        Assert.AreEqual(before - _t.instinctPowerStrike, _u.currentInstinct);
        Assert.AreEqual(dur + _t.ticksPowerStrikeExtra, _u.queue[0].Duration);
        Assert.IsFalse(_u.TryIncrementNext(0), "não duplica o golpe poderoso");

        Assert.IsTrue(_u.TryIncrementNext(1), "mirar ainda pode");
        Assert.IsTrue(_u.queue[0].IsAimed);
        Assert.AreEqual(before - _t.instinctPowerStrike - _t.instinctAim, _u.currentInstinct);
    }

    [Test]
    public void Increment_Refused_WhenStartedWalkingOrBroke()
    {
        _u.stats.STR = 4f; _u.stats.VIT = 4f;
        _u.currentInstinct = _u.stats.MaxInstinct;
        _u.plannedAttacks.Add(new PlannedAttack { Mode = AttackMode.Tile });
        _u.actionSequence.Add(new ScheduledAction { Type = ActionType.Attack, Index = 0 });
        _u.BuildQueueFromPlan();
        _u.queue[0].Elapsed = 1;
        Assert.IsFalse(_u.TryIncrementNext(0), "ação já começou");

        _u.queue[0].Elapsed = 0;
        _u.currentInstinct = 0;
        Assert.IsFalse(_u.TryIncrementNext(1), "sem Instinto");

        _u.queue.Clear();
        _u.queue.Add(new QueuedAction { Type = ActionType.Move, Duration = 1 });
        _u.currentInstinct = 99;
        Assert.IsFalse(_u.TryIncrementNext(0), "só ataques");
    }

    [Test]
    public void Agi_IsCappedWhenBuildingStats()
    {
        var block = new UnitStatBlock { AGI = 14f };
        Assert.AreEqual(CharacterConfig.AgiMax, block.ToAttributeStats().AGI, "AGI acima do teto não rende");
        Assert.AreEqual(6, CharacterConfig.AgiMax);
    }

    [Test]
    public void Agi6_AlreadyReachesMaxWalk()
    {
        _u.stats.AGI = CharacterConfig.AgiMax;
        Assert.AreEqual(_t.maxTilesPerMove, _u.stats.MoveStep, "com AGI 6 a andada já está no máximo");
    }

    [Test]
    public void Instinct_IsScarce()
    {
        _u.stats.STR = 8f; _u.stats.VIT = 8f; // 16 pontos de corpo
        Assert.LessOrEqual(_u.stats.MaxInstinct, 6, "Instinto máximo baixo (era 11 com os mesmos pontos)");
    }
}
