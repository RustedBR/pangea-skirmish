using NUnit.Framework;
using PangeaSkirmish;
using UnityEngine;

/// <summary>Regras do combate em vigor (2026-11-14): dano, defesa, esquiva, reações, ticks e magias.</summary>
public class CombatRulesTests
{
    private GameTuning _t;
    [SetUp] public void SetUp() => _t = TestKit.LoadTuning();

    // ── Dano ──
    [Test]
    public void Melee_ScalesWithStr_At6PercentPerPoint()
    {
        var st = TestKit.Stats(str: 10, dex: 0);
        WeaponDef.ApplyTo(st, WeaponCatalog.Get("IronSword"), _t);
        Assert.IsTrue(st.strScalesDamage);
        Assert.AreEqual(0.06f, _t.statFormulas.dmgPctPerAttr, 1e-4f, "STR: 6% por ponto");
        Assert.AreEqual(WeaponCatalog.Get("IronSword").damage * (1f + 10 * 0.06f), st.DamagePerTile, 0.05f);   // os 80% de 1 mão entram em Equipment.ApplyTo
    }

    [Test]
    public void Ranged_ScalesWithDex_At2PercentPerPoint()
    {
        var st = TestKit.Stats(str: 0, dex: 10);
        WeaponDef.ApplyTo(st, WeaponCatalog.Get("LongBow"), _t);
        Assert.IsTrue(st.dexScalesDamage);
        Assert.AreEqual(0.02f, _t.statFormulas.dexDmgPctPerAttr, 1e-4f, "DEX à distância: 2% por ponto (STR segue 6%)");
        Assert.AreEqual(WeaponCatalog.Get("LongBow").damage * (1f + 10 * 0.02f), st.DamagePerTile, 0.05f);
    }

    [Test]
    public void OneHandedWeapon_Deals80Percent()
    {
        Assert.AreEqual(0.8f, _t.oneHandDamageMult, 1e-4f);
        Assert.AreEqual(0.5f, _t.offhandDamageMult, 1e-4f);
    }

    [Test]
    public void Defense_GrowsWithVit_AndWis()
    {
        Assert.Greater(TestKit.Stats(vit: 12).PhysicalDefense, TestKit.Stats(vit: 2).PhysicalDefense);
        Assert.Greater(TestKit.Stats(wis: 12).MagicDefense, TestKit.Stats(wis: 2).MagicDefense);
    }

    // ── Esquiva e reações ──
    [Test]
    public void Dodge_IsFractionOfTiles_CappedAt50Percent()
    {
        var u = TestKit.Unit(agi: 6);
        try
        {
            Assert.AreEqual(0.30f, AttackResolver.DodgeFrac(u), 1e-4f, "5% por AGI");
            u.stats.AGI = 30;
            Assert.AreEqual(0.50f, AttackResolver.DodgeFrac(u), 1e-4f, "teto 50%");
            Assert.AreEqual(3, AttackResolver.DodgeTilesFor(u, 6), "metade dos tiles do golpe");
        }
        finally { Object.DestroyImmediate(u.gameObject); }
    }

    [Test]
    public void ReactionCosts_AndCounterStrength()
    {
        Assert.AreEqual(2, Unit.ReactionCost(ReactionKind.Dodge), "esquivar");
        Assert.AreEqual(1, Unit.ReactionCost(ReactionKind.Block), "bloquear");
        Assert.AreEqual(3, Unit.ReactionCost(ReactionKind.CounterAttack), "contra-atacar");
        Assert.AreEqual(1, Unit.ReactionCost(ReactionKind.TrueAim), "olho certeiro");
        Assert.AreEqual(0.6f, _t.aooDamageMult, 1e-4f, "contra-ataque com 60% do dano");
        Assert.AreEqual(0.60f, _t.trueAimCritBonus, 1e-4f);
    }

    // ── Magias ──
    [Test]
    public void SpellValues_At75Percent_AndFreeAimBonus()
    {
        Assert.AreEqual(0.75f, _t.spellValueMult, 1e-4f);
        var u = TestKit.Unit(intel: 6);
        try
        {
            var locked = new PlannedSpell { SpellId = "Ember", TargetUnit = u, TargetTile = u.anchor };
            var tile = new PlannedSpell { SpellId = "Ember", TargetTile = new Vector2Int(9, 9) };
            Assert.IsFalse(SpellResolver.IsFreeAim(locked));
            Assert.IsTrue(SpellResolver.IsFreeAim(tile), "magia de dano mirada num tile = mira livre");
            Assert.AreEqual(Mathf.RoundToInt(SpellResolver.Amount(u, tile.Def) * _t.freeAimDamageMult), SpellResolver.AmountFor(u, tile));
        }
        finally { Object.DestroyImmediate(u.gameObject); }
    }

    [Test]
    public void SpellDurations_AreInRounds()
    {
        foreach (var s in SpellCatalog.All())
            if (s.durationRounds > 0)
                Assert.AreEqual(s.durationRounds * _t.ticksPerRound, SpellResolver.DurationTicks(s), s.id);
        StringAssert.Contains("round", SpellResolver.DurationText(SpellCatalog.Get("Ember")));
    }

    [Test]
    public void PhysicalSpells_SplitCost_HalfStamina()
    {
        var leap = SpellCatalog.Get("Leap");
        Assert.AreEqual(leap.cost / 2, leap.StaminaPart);
        Assert.AreEqual(leap.cost - leap.cost / 2, leap.ManaPart);
        Assert.AreEqual(0, SpellCatalog.Get("Ember").StaminaPart, "elemental: só Mana");
    }

    [Test]
    public void Boosts_CopyTheSpell_AndChargeMana()
    {
        var ember = SpellCatalog.Get("Ember");
        var b = SpellBoosts.Apply(ember, (int)(SpellBoost.Power | SpellBoost.Effect));
        Assert.AreNotSame(ember, b);
        Assert.AreEqual(ember.durationRounds + SpellBoosts.EffectRounds, b.durationRounds);
        Assert.AreEqual(ember.ManaPart + SpellBoosts.Cost(SpellBoost.Power) + SpellBoosts.Cost(SpellBoost.Effect), b.ManaPart);
        Assert.AreEqual(0, ember.boostMana, "o catálogo não muda");
        Assert.IsTrue(SpellBoosts.Allowed(SpellCatalog.Get("Fury"), SpellBoost.Power), "físicas também incrementam");
        Assert.IsTrue(SpellBoosts.Allowed(SpellCatalog.Get("Leap"), SpellBoost.Range));
    }

    [Test]
    public void LockedSpell_CentersOnTarget_WithItsOwnSize()
    {
        var caster = TestKit.Unit("Maga");
        var beast = TestKit.Unit("Fera", new Vector2Int(10, 10), footprint: 1);
        try
        {
            var sp = new PlannedSpell { SpellId = "Ember", TargetTile = beast.anchor, TargetUnit = beast };
            Assert.AreEqual(beast.anchor, SpellResolver.AimCenter(caster, sp));
            CollectionAssert.AreEqual(new[] { beast.anchor }, SpellResolver.PreviewCells(caster, sp));
        }
        finally { Object.DestroyImmediate(caster.gameObject); Object.DestroyImmediate(beast.gameObject); }
    }

    // ── Planejamento ──
    [Test]
    public void Plan_CountsConcentrateAndCarriedAction()
    {
        var u = TestKit.Unit();
        try
        {
            u.currentMana = 0;
            u.BeginPlanning();
            u.actionSequence.Add(new ScheduledAction { Type = ActionType.Concentrate });
            u.RecomputeTickBudget();
            Assert.IsTrue(u.CanFitSpell(SpellCatalog.Get("Ember"), out string why), "Concentrar antes paga a Brasa: " + why);

            u.ResetPlan(); u.currentMana = u.stats.MaxMana;
            u.queue.Add(new QueuedAction { Type = ActionType.Move, Dest = new Vector2Int(2, 6), Duration = 2, Elapsed = 1 });
            u.BeginPlanning();
            Assert.AreEqual(1, u.carriedTicks);
            Assert.AreEqual(_t.maxQueueTicks - 1, u.remainingAP, "planeja os ticks que sobram depois da ação em andamento");
            Assert.AreEqual(new Vector2Int(2, 6), u.plannedAnchor, "e parte de onde ela termina");
        }
        finally { Object.DestroyImmediate(u.gameObject); }
    }

    [Test]
    public void PowerStrike_AlwaysAddsItsTick_WhateverTheDex()
    {
        var u = TestKit.Unit();
        try
        {
            foreach (float dex in new[] { 0f, 3f, 5f, 8f, 12f, 20f })
            {
                u.stats = TestKit.Stats(dex: dex);
                int normal = TickRules.Duration(u, ActionType.Attack, false);
                int power = TickRules.Duration(u, ActionType.Attack, true);
                Assert.AreEqual(normal + _t.ticksPowerStrikeExtra, power, $"DEX {dex}: o arredondamento da DEX não pode engolir o tick do golpe poderoso");
            }
        }
        finally { Object.DestroyImmediate(u.gameObject); }
    }

    [Test]
    public void LastAction_CanCrossIntoNextRound_ButNothingStartsAfterTheLine()
    {
        var u = TestKit.Unit();
        try
        {
            u.BeginPlanning();
            int cap = _t.maxQueueTicks;
            // Enche o round até sobrar 1 tick.
            while (u.remainingAP > 1) { u.actionSequence.Add(new ScheduledAction { Type = ActionType.Wait }); u.RecomputeTickBudget(); }
            Assert.AreEqual(1, u.remainingAP);
            Assert.IsTrue(u.CanFit(ActionType.Attack), "1 tick livre basta: o ataque começa neste round e termina no próximo");
            u.actionSequence.Add(new ScheduledAction { Type = ActionType.Attack, Index = 0 });
            u.RecomputeTickBudget();
            Assert.LessOrEqual(u.remainingAP, 0);
            Assert.IsTrue(u.PlanStartsInRound(), "a última ação começa dentro do round");
            Assert.IsFalse(u.CanFit(ActionType.Wait), "depois de atravessar a linha não entra mais nada");

            var tooLate = new System.Collections.Generic.List<ScheduledAction>(u.actionSequence) { new ScheduledAction { Type = ActionType.Wait } };
            Assert.IsFalse(u.PlanStartsInRound(tooLate), "uma ação que começaria depois da linha é recusada");
            Assert.Greater(u.PlannedTicks() + u.carriedTicks, cap - 1);
        }
        finally { Object.DestroyImmediate(u.gameObject); }
    }

    [Test]
    public void AttackTicks_OneHand1_TwoHand2_LongBow3_DexDoesNotChange()
    {
        var u = TestKit.Unit();
        try
        {
            foreach (var w in WeaponCatalog.All())
                foreach (float dex in new[] { 0f, 10f, 25f })
                {
                    u.weaponId = w.id;
                    u.stats = TestKit.Stats(dex: dex);
                    WeaponDef.ApplyTo(u.stats, w, _t);
                    int expected = w.id == "LongBow" ? 3 : Equipment.IsTwoHanded(w) ? 2 : 1;
                    Assert.AreEqual(expected, TickRules.Duration(u, ActionType.Attack, false), $"{w.id} com DEX {dex}");
                }
        }
        finally { Object.DestroyImmediate(u.gameObject); }
    }

    [Test]
    public void Dex_ShortensSpellsAndConcentrate_InSteps_NeverBelowOne()
    {
        var u = TestKit.Unit();
        try
        {
            int step = _t.dexPointsPerTick;
            u.stats = TestKit.Stats(dex: step - 1);
            Assert.AreEqual(_t.ticksConcentrate, TickRules.Duration(u, ActionType.Concentrate, false), "abaixo do degrau não muda");
            u.stats = TestKit.Stats(dex: step);
            Assert.AreEqual(Mathf.Max(1, _t.ticksConcentrate - 1), TickRules.Duration(u, ActionType.Concentrate, false), "cada degrau tira 1 tick");
            u.stats = TestKit.Stats(dex: step * 20);
            Assert.AreEqual(1, TickRules.Duration(u, ActionType.Concentrate, false), "nunca abaixo de 1");
            Assert.AreEqual(_t.ticksMove, TickRules.Duration(u, ActionType.Move, false), "andar não escala com DEX");
        }
        finally { Object.DestroyImmediate(u.gameObject); }
    }
}
