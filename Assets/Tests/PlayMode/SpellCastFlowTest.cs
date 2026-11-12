using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PangeaSkirmish;
using PangeaSkirmish.UI;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Conjuração de ponta a ponta pelo RoundManager.ActionRoutine (o caminho do jogo, não só o
/// resolvedor): plano de UMA magia só, magia longa atravessando pontos de decisão de outras
/// unidades, e o plano passando pelo PlanWire como no multiplayer.
/// </summary>
public class SpellCastFlowTest
{
    private GridManager _grid; private BattleHUD _hud; private RoundManager _rm; private List<Unit> _units;
    private MethodInfo _actionRoutine; private FieldInfo _tick, _field;

    private Unit Make(string n, Team t, Vector2Int a, string weapon, int teamId, ulong owner, int dex = 0, int intel = 6)
    {
        var u = new GameObject(n).AddComponent<Unit>();
        u.unitName = n; u.team = t; u.weaponId = weapon; u.teamId = teamId; u.ownerId = owner;
        u.stats = new UnitStatBlock { STR = 3, VIT = 4, DEX = dex, AGI = 3, INT = intel, WIS = 6 }.ToAttributeStats();
        u.Init(_grid, a, Color.red, "Sprites/TinyTactics/Warrior");
        _units.Add(u);
        UnitRegistry.Register((uint)_units.Count, u);     // como o PlacementSync faz: o PlanWire referencia alvos por id
        return u;
    }

    private IEnumerator Setup()
    {
        LogAssert.ignoreFailingMessages = true;
        foreach (var o in Object.FindObjectsByType<Unit>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        foreach (var o in Object.FindObjectsByType<GridManager>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        foreach (var o in Object.FindObjectsByType<BattleHUD>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        foreach (var o in Object.FindObjectsByType<RoundManager>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        yield return null;
        var tuning = Resources.Load<GameTuning>("GameTuning");
        RuntimeTuning.Active = tuning; AttributeStats.Formulas = tuning.statFormulas;
        RuntimeMultiplayerSession.IsMultiplayer = true;
        RuntimeMultiplayerSession.CurrentConfig = new RoomConfigData { gameMode = GameModes.Coop };
        BattleRng.Seed(4321);
        var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        _grid = new GameObject("GridManager").AddComponent<GridManager>();
        _grid.width = 30; _grid.height = 30; _grid.Build();
        _hud = PangeaScreen.Spawn<BattleHUD>("BattleHUD");
        yield return null; yield return null;
        UnitRegistry.Clear();
        _units = new List<Unit>();
        _rm = new GameObject("RM").AddComponent<RoundManager>();
        var T = typeof(RoundManager); const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
        T.GetField("_units", F).SetValue(_rm, _units);
        T.GetField("_grid", F).SetValue(_rm, _grid);
        T.GetField("_hud", F).SetValue(_rm, _hud);
        T.GetField("_cam", F).SetValue(_rm, cam);
        _tick = T.GetField("_globalTick", F);
        _field = T.GetField("_spellField", F);
        SpellField.Current = (SpellField)_field.GetValue(_rm);
        _actionRoutine = T.GetMethod("ActionRoutine", F);
    }

    private IEnumerator Step() { yield return _rm.StartCoroutine((IEnumerator)_actionRoutine.Invoke(_rm, null)); yield return null; }

    private static void PlanSpell(Unit u, string id, Vector2Int tile, Unit target = null)
    {
        u.plannedSpells.Add(new PlannedSpell { SpellId = id, TargetTile = tile, TargetUnit = target });
        u.actionSequence.Add(new ScheduledAction { Type = ActionType.Spell, Index = u.plannedSpells.Count - 1 });
        u.RecomputeTickBudget();
    }

    private static void PlanWaitLike(Unit u, Vector2Int dest)
    {
        u.plannedPath.Add(dest); u.plannedMoveCount = 1; u.plannedAnchor = dest;
        u.actionSequence.Add(new ScheduledAction { Type = ActionType.Move, Index = 0 });
        u.RecomputeTickBudget();
    }

    /// <summary>Passa o plano pelo fio como o lockstep faz (JSON), e aplica de volta.</summary>
    private static void RoundTripThroughWire(Unit u)
    {
        string json = JsonUtility.ToJson(UnitPlanWire.FromUnit(u));
        var back = JsonUtility.FromJson<UnitPlanWire>(json);
        u.ResetPlan();
        UnitPlanWire.ApplyToUnit(back, u);
        u.RecomputeTickBudget();
    }

    [UnityTest]
    public IEnumerator SpellOnlyPlan_LongCast_ResolvesAcrossDecisionPoints()
    {
        yield return Setup();
        var mage  = Make("Maga", Team.Player, new Vector2Int(4, 10), "ArcaneStaff", 0, 0);
        var ally  = Make("Aliado", Team.Player, new Vector2Int(4, 20), "IronSword", 0, 1);
        var dummy = Make("Alvo", Team.Enemy, new Vector2Int(10, 10), "IronSword", 1, 2);   // inimigo controlado "por jogador": não age
        dummy.currentHP = dummy.stats.MaxHP; int hp0 = dummy.currentHP;
        mage.currentMana = mage.stats.MaxMana; int mana0 = mage.currentMana;
        for (int i = 0; i < 3; i++) yield return null;

        // Plano de UMA ação: Explosão (3 ticks com DEX 0). O aliado anda 1 tick por decisão.
        var explosion = SpellCatalog.Get("Explosion");
        Assert.AreEqual(3, TickRules.Duration(mage, ActionType.Spell, false, explosion));
        Assert.IsTrue(mage.CanFitSpell(explosion, out string why), why);
        PlanSpell(mage, "Explosion", dummy.anchor, dummy);
        RoundTripThroughWire(mage);
        Assert.AreEqual(1, mage.plannedSpells.Count, "a magia sobrevive ao PlanWire");
        Assert.AreEqual("Explosion", mage.plannedSpells[0].SpellId);
        Assert.AreSame(dummy, mage.plannedSpells[0].TargetUnit);

        int ticks = 0;
        for (int round = 0; round < 6 && dummy.currentHP == hp0; round++)
        {
            if (!ally.IsBusy) { ally.ResetPlan(); PlanWaitLike(ally, ally.anchor + new Vector2Int(round % 2 == 0 ? 1 : -1, 0)); }
            if (!dummy.IsBusy) dummy.ResetPlan();
            if (!mage.IsBusy && round > 0) mage.ResetPlan();
            yield return Step();
            ticks = (int)_tick.GetValue(_rm);
        }
        Assert.Less(dummy.currentHP, hp0, $"a Explosão deveria ter acertado (ticks={ticks}, fila da maga={mage.queue.Count})");
        Assert.AreEqual(3, ticks, "resolve no 3º tick");
        Assert.AreEqual(mana0 - explosion.cost + Mathf.FloorToInt(3f / mage.stats.ManaRegenTicks), mage.currentMana, "Mana gasta uma vez (mais a regeneração dos 3 ticks)");
        Assert.IsFalse(mage.IsBusy, "a maga fica livre depois de conjurar");
    }

    [UnityTest]
    public IEnumerator Magma_CreatesZone_AndBurnsOverTicks()
    {
        yield return Setup();
        var mage  = Make("Maga", Team.Player, new Vector2Int(4, 10), "ArcaneStaff", 0, 0);
        var dummy = Make("Alvo", Team.Enemy, new Vector2Int(8, 10), "IronSword", 1, 2);
        mage.currentMana = mage.stats.MaxMana;
        int hp0 = dummy.currentHP;
        for (int i = 0; i < 3; i++) yield return null;

        PlanSpell(mage, "Magma", dummy.anchor);
        RoundTripThroughWire(mage);
        var field = (SpellField)_field.GetValue(_rm);
        for (int round = 0; round < 8; round++)
        {
            if (!dummy.IsBusy) dummy.ResetPlan();
            if (!mage.IsBusy && round > 0) mage.ResetPlan();
            yield return Step();
            if (field.Zones.Count > 0 && dummy.currentHP < hp0) break;
        }
        Assert.Less(dummy.currentHP, hp0, "o Magma deveria queimar quem está em cima");
    }

    [UnityTest]
    public IEnumerator PlannerEdits_KeepSpellListsConsistent()
    {
        yield return Setup();
        var mage  = Make("Maga", Team.Player, new Vector2Int(4, 10), "ArcaneStaff", 0, 0);
        var dummy = Make("Alvo", Team.Enemy, new Vector2Int(8, 10), "IronSword", 1, 2);
        mage.currentMana = mage.stats.MaxMana;
        yield return null;
        var planner = new GameObject("Planner").AddComponent<PlanningController>();
        planner.Setup(_grid, Camera.main, _units);
        planner.SetHUD(_hud);
        planner.Begin(mage);
        const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
        var P = typeof(PlanningController);
        var append = P.GetMethod("AppendSpell", F);
        void Add(string id) => append.Invoke(planner, new object[] { SpellCatalog.Get(id), dummy, dummy.anchor });

        // Desfazer tira a magia das DUAS listas.
        Add("Ember");
        P.GetMethod("OnUndoClick", F).Invoke(planner, null);
        Assert.AreEqual(0, mage.actionSequence.Count);
        Assert.AreEqual(0, mage.plannedSpells.Count, "Desfazer remove a magia planejada");
        Assert.AreEqual(0, mage.reservedMana, "e devolve a Mana reservada");

        // Limpar funciona com plano só de magia.
        Add("Ember"); Add("Gust");
        Assert.AreEqual(4, mage.reservedMana);
        P.GetMethod("OnClearClick", F).Invoke(planner, null);
        Assert.AreEqual(0, mage.actionSequence.Count, "Limpar funciona com plano só de magia");
        Assert.AreEqual(0, mage.plannedSpells.Count);
        Assert.AreEqual(0, mage.reservedMana);
    }

    [UnityTest]
    public IEnumerator Balance_ReachGrowsWithInt_SpellsAndBowsHitLikeWeapons()
    {
        yield return Setup();
        var T = Tuning.Get();
        var novice = Make("Novata", Team.Player, new Vector2Int(2, 2), "ArcaneStaff", 0, 0, intel: 2);
        var sage   = Make("Sabia", Team.Player, new Vector2Int(2, 8), "ArcaneStaff", 0, 1, intel: 10);
        var ember = SpellCatalog.Get("Ember"); var magma = SpellCatalog.Get("Magma");
        // Alcance: base × mult + INT × porINT. Mais INT = mais longe.
        int conduit = WeaponCatalog.Get("ArcaneStaff").spellRangeBonus;      // o Cajado Arcano soma alcance às magias
        Assert.AreEqual(Mathf.RoundToInt(ember.range * T.spellRangeMult + 2 * T.spellRangePerINT) + conduit, SpellResolver.Reach(novice, ember));
        Assert.Greater(SpellResolver.Reach(sage, ember), SpellResolver.Reach(novice, ember), "INT aumenta o alcance");
        Assert.Greater(SpellResolver.Reach(novice, magma), 3 + 1, "Magma deixa de ser corpo a corpo (uma unidade tem 3 células)");

        // Potência: uma Brasa de INT 6 fica na casa de um golpe de espada de STR 6.
        var mage = Make("Maga", Team.Player, new Vector2Int(2, 14), "ArcaneStaff", 0, 2, intel: 6);
        var sword = Make("Guerreiro", Team.Player, new Vector2Int(8, 2), "IronSword", 0, 3);
        sword.stats.STR = 6; 
        int emberDmg = SpellResolver.Amount(mage, ember), swordDmg = sword.stats.PhysicalDamage;
        Assert.That(emberDmg, Is.InRange(swordDmg * 0.7f, swordDmg * 1.5f), $"Brasa {emberDmg} (com Cajado Arcano ×1,3) vs espada {swordDmg}");

        // Arcos: escalam com a DEX (antes: Arco Longo = 1 de dano fixo).
        var shortBow = Make("ArqCurto", Team.Player, new Vector2Int(8, 8), "ShortBow", 0, 4, dex: 6);
        var longBow  = Make("ArqLongo", Team.Player, new Vector2Int(8, 14), "LongBow", 0, 5, dex: 6);
        Assert.IsTrue(longBow.stats.dexScalesDamage, "arco escala com DEX");
        Assert.IsFalse(sword.stats.dexScalesDamage, "espada continua na STR");
        Assert.Greater(longBow.stats.PhysicalDamage, 20, "Arco Longo (3 ticks) bate forte por disparo");
        Assert.Greater(longBow.stats.PhysicalDamage, shortBow.stats.PhysicalDamage, "arma pesada bate mais por disparo");
        float shortPerTick = shortBow.stats.PhysicalDamage / (float)TickRules.Duration(shortBow, ActionType.Attack, false);
        float swordPerTick = swordDmg / (float)TickRules.Duration(sword, ActionType.Attack, false);
        Assert.That(shortPerTick, Is.InRange(swordPerTick * 0.5f, swordPerTick * 1.6f), $"Arco Curto {shortPerTick}/tick vs espada {swordPerTick}/tick");
    }

    [TearDown]
    public void TearDown() { RuntimeMultiplayerSession.IsMultiplayer = false; }
}
