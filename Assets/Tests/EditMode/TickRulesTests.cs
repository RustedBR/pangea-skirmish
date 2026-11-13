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
    public void Stamina_PlanCostIsCommittedWhenQueueIsBuilt()
    {
        _u.stats.STR = 4f; _u.stats.VIT = 4f;
        _u.currentStamina = _u.stats.MaxStamina;
        int max = _u.currentStamina;
        Assert.AreEqual(Mathf.RoundToInt(_t.staminaBase + 8 * _t.staminaPerBodyPoint), max);

        _u.actionSequence.Add(new ScheduledAction { Type = ActionType.Attack, IsBonus = true });
        _u.RecomputeTickBudget();
        Assert.AreEqual(max - _t.staminaPowerStrike, _u.remainingBAP, "golpe poderoso reserva Stamina no plano");

        _u.BuildQueueFromPlan();
        Assert.AreEqual(max - _t.staminaPowerStrike, _u.currentStamina, "o gasto vale ao comprometer a fila");
        _u.GainStamina(999);
        Assert.AreEqual(max, _u.currentStamina, "não passa do máximo");
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
    public void Increment_NextAttack_SpendsStaminaAndLengthensIt()
    {
        _u.stats.STR = 4f; _u.stats.VIT = 4f; _u.stats.AttackTicks = 2;
        _u.currentStamina = _u.stats.MaxStamina;
        int before = _u.currentStamina;
        _u.plannedAttacks.Add(new PlannedAttack { Mode = AttackMode.Tile });
        _u.actionSequence.Add(new ScheduledAction { Type = ActionType.Attack, Index = 0 });
        _u.BuildQueueFromPlan();
        int dur = _u.queue[0].Duration;

        Assert.IsTrue(_u.TryIncrementNext(0), "golpe poderoso na próxima ação");
        Assert.AreEqual(before - _t.staminaPowerStrike, _u.currentStamina);
        Assert.AreEqual(dur + _t.ticksPowerStrikeExtra, _u.queue[0].Duration);
        Assert.IsFalse(_u.TryIncrementNext(0), "não duplica o golpe poderoso");

        Assert.IsTrue(_u.TryIncrementNext(1), "mirar ainda pode");
        Assert.IsTrue(_u.queue[0].IsAimed);
        Assert.AreEqual(before - _t.staminaPowerStrike - _t.staminaAim, _u.currentStamina);
    }

    [Test]
    public void Increment_Refused_WhenStartedWalkingOrBroke()
    {
        _u.stats.STR = 4f; _u.stats.VIT = 4f;
        _u.currentStamina = _u.stats.MaxStamina;
        _u.plannedAttacks.Add(new PlannedAttack { Mode = AttackMode.Tile });
        _u.actionSequence.Add(new ScheduledAction { Type = ActionType.Attack, Index = 0 });
        _u.BuildQueueFromPlan();
        _u.queue[0].Elapsed = 1;
        Assert.IsFalse(_u.TryIncrementNext(0), "ação já começou");

        _u.queue[0].Elapsed = 0;
        _u.currentStamina = 0;
        Assert.IsFalse(_u.TryIncrementNext(1), "sem Stamina");

        _u.queue.Clear();
        _u.queue.Add(new QueuedAction { Type = ActionType.Move, Duration = 1 });
        _u.currentStamina = 99;
        Assert.IsFalse(_u.TryIncrementNext(0), "só ataques");
    }

    [Test]
    public void Agi_AboveSix_KeepsWalkCapButSpeedsStaminaRegen()
    {
        _u.stats.AGI = 6f;
        Assert.AreEqual(_t.maxTilesPerMove, _u.stats.MoveStep, "com AGI 6 a andada já está no teto");
        int regen6 = _u.stats.StaminaRegenTicks;
        _u.stats.AGI = 12f;
        Assert.AreEqual(_t.maxTilesPerMove, _u.stats.MoveStep, "o teto de andada continua");
        Assert.Less(_u.stats.StaminaRegenTicks, regen6, "AGI acima de 6 acelera a Stamina");
        Assert.AreEqual(1, _u.stats.StaminaRegenTicks, "AGI 12: 1 ponto por tick");
    }

    [Test]
    public void Agi_IsNotCappedAtSixWhenBuildingStats()
    {
        var block = new UnitStatBlock { AGI = 14f };
        Assert.AreEqual(14f, block.ToAttributeStats().AGI);
    }

    [Test]
    public void Stamina_IsScarce()
    {
        _u.stats.STR = 8f; _u.stats.VIT = 8f; // 16 pontos de corpo
        Assert.LessOrEqual(_u.stats.MaxStamina, 6, "Stamina máxima baixo (era 11 com os mesmos pontos)");
    }

    [Test]
    public void Armor_IsFlat_OnePerVit()
    {
        // GDD-020 §7: defesa fixa, 1 por ponto de VIT, descontada uma vez no total do golpe.
        var T = Tuning.Get();
        AttributeStats.Formulas = T.statFormulas;
        Assert.AreEqual(0f, T.statFormulas.armorPctPerVIT, "sem redução percentual");
        _u.stats.VIT = 8f;
        Assert.AreEqual(2, _u.stats.PhysicalDefense, "1/4 de defesa por ponto de VIT");
        var atk = new AttributeStats();
        T.statFormulas.damageVariance = 0f;
        try { Assert.AreEqual(18, atk.RollDamage(20, true, _u.stats), "20 de golpe − 2 de VIT"); }
        finally { T.statFormulas.damageVariance = Resources.Load<GameTuning>("GameTuning").statFormulas.damageVariance; }
    }

    [Test]
    public void Unarmed_AttackIsAsSlowAsAMediumWeapon()
    {
        Assert.AreEqual(2, Tuning.Get().ticksUnarmed, "feras não podem atacar o dobro de vezes que uma espada");
    }

    [Test]
    public void PowerStrike_MultipliesDamage_NotJustTicks()
    {
        _u.stats.STR = 4f; _u.stats.WeaponDamage = 5; _u.stats.ShapeTiles = 3;   // arma de 3 tiles (GDD-020)
        int b = _u.stats.PhysicalDamage;
        int power = AttackResolver.PowerDamage(_u, true, 0);
        Assert.AreEqual(Mathf.RoundToInt(b * Tuning.Get().powerStrikeMult), power);
        Assert.Greater(power, b + 4, "mais que o antigo bônus fixo de +4");
        Assert.AreEqual(b + 3, AttackResolver.PowerDamage(_u, false, 3), "a mira soma, sem multiplicar");
    }
}
