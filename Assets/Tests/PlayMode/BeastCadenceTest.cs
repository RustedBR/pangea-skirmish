using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PangeaSkirmish;
using PangeaSkirmish.UI;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Roda a resolução de ticks de verdade (RoundManager.ActionRoutine) com uma fera contra um alvo
/// parado e conta quantos golpes ela dá por tick.
/// </summary>
public class BeastCadenceTest
{
    [UnityTest]
    public IEnumerator Beast_NeverHitsMoreThanOncePerTick_AndFollowsItsWeaponCadence()
    {
        LogAssert.ignoreFailingMessages = true;
        foreach (var o in Object.FindObjectsByType<Unit>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        foreach (var o in Object.FindObjectsByType<GridManager>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        foreach (var o in Object.FindObjectsByType<BattleHUD>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        foreach (var o in Object.FindObjectsByType<RoundManager>(FindObjectsSortMode.None)) Object.Destroy(o);
        yield return null;

        var tuning = Resources.Load<GameTuning>("GameTuning");
        RuntimeTuning.Active = tuning; AttributeStats.Formulas = tuning.statFormulas;
        RuntimeMultiplayerSession.IsMultiplayer = true;               // caminho sem StartRound recursivo
        RuntimeMultiplayerSession.CurrentConfig = new RoomConfigData { gameMode = GameModes.Coop };

        var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        var grid = new GameObject("GridManager").AddComponent<GridManager>();
        grid.width = 20; grid.height = 20; grid.Build();
        var hud = PangeaScreen.Spawn<BattleHUD>("BattleHUD");
        yield return null; yield return null;

        Unit Make(string n, Team t, Vector2Int a, string weapon, int teamId, ulong owner)
        {
            var u = new GameObject(n).AddComponent<Unit>();
            u.unitName = n; u.team = t; u.weaponId = weapon; u.teamId = teamId; u.ownerId = owner;
            u.stats = new UnitStatBlock { STR = 4, VIT = 4, DEX = 3, AGI = 3 }.ToAttributeStats();
            u.Init(grid, a, Color.red, "Sprites/TinyTactics/Warrior");
            return u;
        }
        var hero = Make("Heroi", Team.Player, new Vector2Int(5, 6), "IronSword", 0, 0);
        var beast = Make("Fera", Team.Enemy, new Vector2Int(8, 6), "Claws", 1, BeastAI.OwnerId);
        hero.currentHP = 99999; hero.currentInstinct = 0;             // sem reação: não abre menu
        var units = new List<Unit> { hero, beast };

        var rm = new GameObject("RM").AddComponent<RoundManager>();
        var T = typeof(RoundManager); const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
        T.GetField("_units", F).SetValue(rm, units);
        T.GetField("_grid", F).SetValue(rm, grid);
        T.GetField("_hud", F).SetValue(rm, hud);
        T.GetField("_cam", F).SetValue(rm, cam);
        T.GetField("_playerUnit", F).SetValue(rm, hero);
        var tickField = T.GetField("_globalTick", F);
        var actionRoutine = T.GetMethod("ActionRoutine", F);

        var hitsPerTick = new Dictionary<int, int>();
        System.Action<Unit, int, bool> onHit = (v, amt, crit) =>
        {
            if (v != hero) return;
            int tk = (int)tickField.GetValue(rm);
            hitsPerTick[tk] = hitsPerTick.TryGetValue(tk, out var n) ? n + 1 : 1;
        };
        Unit.OnDamageTaken += onHit;

        for (int i = 0; i < 14 && !beast.IsDead; i++)
        {
            yield return rm.StartCoroutine((IEnumerator)actionRoutine.Invoke(rm, null));
            yield return null;
        }
        Unit.OnDamageTaken -= onHit;
        RuntimeMultiplayerSession.IsMultiplayer = false;

        int totalHits = 0, totalTicks = (int)tickField.GetValue(rm);
        foreach (var kv in hitsPerTick)
        {
            totalHits += kv.Value;
            Assert.LessOrEqual(kv.Value, 1, $"a fera bateu {kv.Value}x no tick {kv.Key}");
        }
        Debug.Log($"[BeastCadenceTest] {totalHits} golpes em {totalTicks} ticks; por tick: " +
                  string.Join(", ", new SortedDictionary<int, int>(hitsPerTick)));
        Assert.Greater(totalHits, 0, "a fera deveria ter atacado");
        int attackTicks = TickRules.Duration(beast, ActionType.Attack, false);
        Assert.GreaterOrEqual(attackTicks, 2);
        // No máximo um golpe a cada 'attackTicks' ticks (+1 de folga pela fila já em andamento).
        Assert.LessOrEqual(totalHits, totalTicks / attackTicks + 1,
            $"{totalHits} golpes em {totalTicks} ticks com ataque de {attackTicks} ticks");
    }
}
