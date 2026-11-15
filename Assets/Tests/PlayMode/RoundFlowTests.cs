using System.Collections;
using NUnit.Framework;
using PangeaSkirmish;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>Ordem do tick e rounds (2026-11-14): magias → ataques → andadas, fuga por AGI,
/// esquiva com dano primeiro e ação que atravessa o round.</summary>
public class RoundFlowTests
{
    private Battlefield _b;
    [UnitySetUp] public IEnumerator SetUp() { _b = new Battlefield(); yield return _b.Build(); }
    [TearDown] public void TearDown() { Battlefield.Clean(); RuntimeMultiplayerSession.IsMultiplayer = false; }

    /// <summary>O alvo termina uma andada no mesmo tick em que o golpe (ou magia) termina.</summary>
    private IEnumerator SameTick(Unit actor, ActionType type, Unit target, string spell, Vector2Int away)
    {
        int d = type == ActionType.Spell ? TickRules.Duration(actor, ActionType.Spell, false, SpellCatalog.Get(spell)) : TickRules.Duration(actor, type, false);
        int mv = TickRules.Duration(target, ActionType.Move, false);
        int waits = Battlefield.Align(target, d, mv);
        Assume.That(waits >= 0, $"alinhar ação ({d}) e andada ({mv})");
        Battlefield.Begin(actor); Battlefield.Begin(target);
        if (type == ActionType.Spell) Battlefield.Cast(actor, spell, target); else Battlefield.Attack(actor, target);
        Battlefield.Wait(target, waits);
        Battlefield.Move(target, away);
        yield return _b.Step();
    }

    [UnityTest]
    public IEnumerator SpellLandsBeforeMove()
    {
        var mage = _b.Add("Maga", Team.Player, new Vector2Int(4, 10), "ArcaneStaff", 0);
        var foe = _b.Add("Alvo", Team.Enemy, new Vector2Int(9, 10), "IronSword", 1);
        int hp = foe.currentHP; var start = foe.anchor;
        yield return SameTick(mage, ActionType.Spell, foe, "StoneShard", start + new Vector2Int(0, 2));
        Assert.Less(foe.currentHP, hp, "a magia acertou");
        Assert.AreEqual(start, foe.anchor, "pegou antes da andada, e a Lasca de pedra a cancelou");
    }

    [UnityTest]
    public IEnumerator AttackLandsBeforeMove_UnlessTargetIsMoreAgile()
    {
        var atk = _b.Add("Espada", Team.Player, new Vector2Int(10, 6), "IronSword", 0, agi: 3);
        var foe = _b.Add("Alvo", Team.Enemy, new Vector2Int(10, 10), "IronSword", 1, agi: 3);
        int hp = foe.currentHP;
        yield return SameTick(atk, ActionType.Attack, foe, null, foe.anchor + new Vector2Int(0, foe.EffectiveMoveStep));
        Assert.Less(foe.currentHP, hp, "AGI empatada: o golpe pega antes de ele sair");

        var fast = _b.Add("Ágil", Team.Enemy, new Vector2Int(20, 10), "IronSword", 1, agi: 8);
        var atk2 = _b.Add("Espada 2", Team.Player, new Vector2Int(20, 6), "IronSword", 0, agi: 3);
        foe.currentHP = 0; foe.gameObject.SetActive(false);
        int hp2 = fast.currentHP;
        yield return SameTick(atk2, ActionType.Attack, fast, null, fast.anchor + new Vector2Int(0, fast.EffectiveMoveStep));
        Assume.That(GridManager.FootprintGap(atk2.anchor, atk2.stats.Footprint, fast.anchor, fast.stats.Footprint) > atk2.EffectiveAttackRange);
        Assert.AreEqual(hp2, fast.currentHP, "mais ágil que o atacante: sai antes do golpe");
    }

    [UnityTest]
    public IEnumerator Dodge_DamageFirst_ThenStepsTwiceAsFast()
    {
        var foe = _b.Add("Ágil", Team.Enemy, new Vector2Int(10, 10), "IronSword", 1, agi: 10);
        var atk = _b.Add("Atacante", Team.Player, new Vector2Int(10, 4), "IronSword", 0);
        yield return null;
        var start = foe.anchor; var startPos = foe.transform.position;
        var cells = new System.Collections.Generic.List<Vector2Int>();
        for (int x = 9; x <= 13; x++) for (int y = 9; y <= 13; y++) cells.Add(new Vector2Int(x, y));
        yield return _b.Round.StartCoroutine((IEnumerator)_b.Call("DodgeMove", foe, cells, atk));
        Assert.AreNotEqual(start, foe.anchor, "a esquiva já vale para o dano");
        Assert.AreEqual(startPos, foe.transform.position, "mas o corpo ainda não andou");
        float t0 = Time.time;
        yield return _b.Round.StartCoroutine((IEnumerator)_b.Call("PlayPendingDodges"));
        var T = Tuning.Get();
        float normal = Mathf.Max(T.moveDurationMin, T.moveDurationBase / Mathf.Max(1f, foe.stats.AGI));
        Assert.LessOrEqual(Time.time - t0, normal / 2f + 0.1f, "passo 2× mais rápido");
        Assert.Less(Vector3.Distance(_b.Grid.AnchorToWorldCenter(foe.anchor, foe.stats.Footprint), foe.transform.position), 0.05f);
    }

    [UnityTest]
    public IEnumerator ActionCrossingTheRound_StillLetsTheUnitPlan()
    {
        var u = _b.Add("Herói", Team.Player, new Vector2Int(4, 4), "IronSword", 0);
        _b.Add("Fera longe", Team.Enemy, new Vector2Int(25, 25), "Claws", 1);   // só um lado vivo encerra o round no 1º tick
        int max = Tuning.Get().maxQueueTicks;
        u.queue.Add(new QueuedAction { Type = ActionType.Wait, Duration = max + 1 });   // atravessa 1 tick do round seguinte
        yield return _b.Step();
        Battlefield.Begin(u);
        Assert.IsTrue(u.CanPlanThisRound, $"ainda planeja no round seguinte (carried {u.carriedTicks}, fila {u.queue.Count}, resta {u.QueueTicksRemaining}, max {max})");
        Assert.AreEqual(max - u.carriedTicks, u.remainingAP);
        var dest = u.anchor + new Vector2Int(0, 2);
        Battlefield.Move(u, dest);
        yield return _b.Step();
        Assert.AreEqual(dest, u.anchor, "a andada planejada depois da ação antiga aconteceu");
    }
}
