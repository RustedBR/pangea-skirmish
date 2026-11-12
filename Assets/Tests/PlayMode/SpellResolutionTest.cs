using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PangeaSkirmish;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>Resolução das magias (GDD-019 passo 5): dano, status, cura, zonas, muralha, preparos e interrupção.</summary>
public class SpellResolutionTest
{
    private GridManager _grid;
    private SpellField _field;
    private List<Unit> _all;

    private Unit Make(string n, Team t, Vector2Int a, ulong owner)
    {
        var u = new GameObject(n).AddComponent<Unit>();
        u.unitName = n; u.team = t; u.weaponId = "WoodenStaff"; u.ownerId = owner;
        u.stats = new UnitStatBlock { STR = 3, VIT = 4, DEX = 3, AGI = 3, INT = 6, WIS = 4 }.ToAttributeStats();
        u.Init(_grid, a, Color.red, "Sprites/TinyTactics/Warrior");
        _all.Add(u);
        return u;
    }

    private IEnumerator Setup()
    {
        LogAssert.ignoreFailingMessages = true;
        foreach (var old in Object.FindObjectsByType<Unit>(FindObjectsSortMode.None)) Object.Destroy(old.gameObject);
        foreach (var old in Object.FindObjectsByType<GridManager>(FindObjectsSortMode.None)) Object.Destroy(old.gameObject);
        yield return null;
        var tuning = Resources.Load<GameTuning>("GameTuning");
        RuntimeTuning.Active = tuning;
        AttributeStats.Formulas = tuning.statFormulas;
        BattleRng.Seed(1234);
        _grid = new GameObject("GridManager").AddComponent<GridManager>();
        _grid.width = 30; _grid.height = 30; _grid.Build();
        _field = new SpellField(); SpellField.Current = _field;
        _all = new List<Unit>();
    }

    private List<string> Cast(Unit c, string id, Vector2Int tile, Unit target = null) =>
        SpellResolver.Resolve(c, new PlannedSpell { SpellId = id, TargetTile = tile, TargetUnit = target }, _all, _grid, _field);

    [UnityTest]
    public IEnumerator Damage_Status_Heal_Drain()
    {
        yield return Setup();
        var mage = Make("Maga", Team.Player, new Vector2Int(2, 2), 0);
        var ally = Make("Aliado", Team.Player, new Vector2Int(2, 6), 0);
        var foe  = Make("Fera", Team.Enemy, new Vector2Int(6, 2), BeastAI.OwnerId);

        foe.currentHP = 9999;            // com o dano rebalanceado, Brasa + Jato matariam o alvo no meio do teste
        int hp = foe.currentHP;
        Cast(mage, "Ember", foe.anchor, foe);
        Assert.Less(foe.currentHP, hp, "Brasa causa dano");
        Assert.Greater(foe.burningTicks, 0, "Brasa aplica Queimando");
        int afterHit = foe.currentHP;
        SpellResolver.TickEffects(_all, _field);
        Assert.AreEqual(afterHit - foe.burningDamage, foe.currentHP, "Queimando dói por tick");

        Cast(mage, "WaterJet", foe.anchor, foe);
        Assert.AreEqual(0, foe.burningTicks, "Água apaga o fogo");
        Assert.Greater(foe.soakedTicks, 0, "Encharcado");
        Assert.AreEqual(Mathf.Max(1, foe.stats.MoveStep - 1), foe.EffectiveMoveStep, "Encharcado tira 1 de andada");

        ally.currentHP = 5;
        Cast(mage, "Balm", ally.anchor);
        Assert.Greater(ally.currentHP, 5, "Bálsamo cura aliado");
        int foeHpBeforeBalm = foe.currentHP;
        Cast(mage, "Balm", foe.anchor);
        Assert.AreEqual(foeHpBeforeBalm, foe.currentHP, "Bálsamo não cura inimigo");

        foe.currentMana = 10; mage.currentMana = 0;
        Cast(mage, "MindTide", foe.anchor, foe);
        Assert.Less(foe.currentMana, 10, "Maré mental drena");
        Assert.Greater(mage.currentMana, 0, "e devolve metade");
    }

    [UnityTest]
    public IEnumerator AreaSkipsAllies_AndTargetFollowsUnit()
    {
        yield return Setup();
        var mage = Make("Maga", Team.Player, new Vector2Int(2, 2), 0);
        var ally = Make("Aliado", Team.Player, new Vector2Int(9, 2), 0);
        var foe  = Make("Fera", Team.Enemy, new Vector2Int(9, 6), BeastAI.OwnerId);
        int allyHp = ally.currentHP, foeHp = foe.currentHP;
        Cast(mage, "Explosion", new Vector2Int(9, 4));
        Assert.AreEqual(allyHp, ally.currentHP, "área não acerta aliado");
        Assert.Less(foe.currentHP, foeHp, "área acerta inimigo");

        // Alvo saiu do tile, mas segue ao alcance: a magia o segue.
        var foe2 = Make("Fera2", Team.Enemy, new Vector2Int(5, 2), BeastAI.OwnerId);
        var aimed = foe2.anchor;
        foe2.TeleportTo(new Vector2Int(5, 5));
        int hp2 = foe2.currentHP;
        Cast(mage, "Ember", aimed, foe2);
        Assert.Less(foe2.currentHP, hp2, "alvo por unidade");
    }

    [UnityTest]
    public IEnumerator Zones_Lava_Mud_Barrier()
    {
        yield return Setup();
        var mage = Make("Maga", Team.Player, new Vector2Int(2, 2), 0);
        var foe  = Make("Fera", Team.Enemy, new Vector2Int(6, 2), BeastAI.OwnerId);

        Cast(mage, "Magma", foe.anchor);
        Assert.AreEqual(1, _field.Zones.Count);
        int hp = foe.currentHP;
        SpellResolver.TickEffects(_all, _field);
        Assert.Less(foe.currentHP, hp, "lava dói");
        int mageHp = mage.currentHP;
        Assert.AreEqual(mageHp, mage.currentHP, "conjurador não se queima");
        for (int i = 0; i < 6; i++) SpellResolver.TickEffects(_all, _field);
        Assert.AreEqual(0, _field.Zones.Count, "zona expira");

        Cast(mage, "Mud", foe.anchor);
        Assert.AreEqual(1, foe.EffectiveMoveStep, "Lama: andada 1");
        _field.Clear();

        // Muralha entre a maga e a fera absorve o projétil.
        Cast(mage, "Rampart", new Vector2Int(4, 2));
        var wall = _field.Zones[0];
        int wallHp = wall.Hp; hp = foe.currentHP;
        Cast(mage, "Ember", foe.anchor, foe);
        Assert.AreEqual(hp, foe.currentHP, "muralha protege");
        Assert.Less(wall.Hp, wallHp, "muralha leva o dano");
    }

    [UnityTest]
    public IEnumerator Physical_Preps_And_Leap()
    {
        yield return Setup();
        var hero = Make("Herói", Team.Player, new Vector2Int(2, 2), 0);
        Cast(hero, "Fury", hero.anchor);
        Assert.Greater(hero.furyReady, 0f);
        int normal = AttackResolver.PowerDamage(hero, false, 0);
        int fury = SpellResolver.ApplyFury(hero, normal, 0);
        Assert.AreEqual(Mathf.RoundToInt(hero.stats.PhysicalDamage * 2f), fury, "Fúria dobra");
        Assert.AreEqual(0f, hero.furyReady, "Fúria se consome");

        var foe = Make("Fera", Team.Enemy, new Vector2Int(6, 2), BeastAI.OwnerId);
        Cast(hero, "TrueAim", hero.anchor);
        Assert.IsTrue(SpellResolver.TryTrueAim(hero, foe, out var res));
        Assert.AreNotEqual(HitResult.Miss, res, "Olho certeiro não erra");

        Cast(foe, "StoneSkin", foe.anchor);
        Assert.Greater(foe.stoneSkinTicks, 0);
        Assert.LessOrEqual(SpellResolver.StoneSkin(foe, 20, physical: true), 20);

        var dest = hero.anchor + new Vector2Int(0, 6);
        Cast(hero, "Leap", dest);
        Assert.AreEqual(dest, hero.anchor, "Salto pousa no destino");
    }

    [UnityTest]
    public IEnumerator Elemental_Interrupted_By_Damage()
    {
        yield return Setup();
        var mage = Make("Maga", Team.Player, new Vector2Int(2, 2), 0);
        mage.queue.Clear();
        mage.queue.Add(new QueuedAction { Type = ActionType.Spell, Spell = new PlannedSpell { SpellId = "Explosion" }, Duration = 3 });
        int limit = SpellResolver.InterruptThreshold(mage);
        mage.TakeDamage(limit - 1);
        Assert.IsFalse(mage.queue[0].Interrupted, "abaixo do limite mantém a concentração");
        mage.TakeDamage(1);
        Assert.IsTrue(mage.queue[0].Interrupted, "no limite interrompe");

        mage.queue.Clear();
        mage.queue.Add(new QueuedAction { Type = ActionType.Spell, Spell = new PlannedSpell { SpellId = "Fury" }, Duration = 1 });
        mage.TakeDamage(limit * 2);
        Assert.IsFalse(mage.queue[0].Interrupted, "magia Física nunca é interrompida");
    }
}
