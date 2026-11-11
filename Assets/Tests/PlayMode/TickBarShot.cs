using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PangeaSkirmish;
using PangeaSkirmish.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>Renderiza a HUD de batalha com uma fila e eventos e salva Temp/tickbar.png.</summary>
public class TickBarShot
{
    [UnityTest]
    public IEnumerator TickBar_RendersUnifiedRows()
    {
        LogAssert.ignoreFailingMessages = true;
        foreach (var old in Object.FindObjectsByType<Unit>(FindObjectsSortMode.None)) Object.Destroy(old.gameObject);
        foreach (var old in Object.FindObjectsByType<GridManager>(FindObjectsSortMode.None)) Object.Destroy(old.gameObject);
        yield return null;

        var tuning = Resources.Load<GameTuning>("GameTuning");
        RuntimeTuning.Active = tuning;
        AttributeStats.Formulas = tuning.statFormulas;
        var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        cam.orthographic = true; cam.backgroundColor = new Color(0.10f, 0.11f, 0.15f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        var grid = new GameObject("GridManager").AddComponent<GridManager>();
        grid.width = 12; grid.height = 12; grid.Build();

        Unit Make(string n, Team t, Vector2Int a, ulong owner)
        {
            var u = new GameObject(n).AddComponent<Unit>();
            u.unitName = n; u.team = t; u.weaponId = "IronSword"; u.ownerId = owner;
            u.stats = new UnitStatBlock { STR = 4, VIT = 4, DEX = 3, AGI = 3 }.ToAttributeStats();
            u.Init(grid, a, Color.red, "Sprites/TinyTactics/Warrior");
            return u;
        }
        var hero = Make("Jogador", Team.Player, new Vector2Int(2, 3), 0);
        var beast = Make("Fera 1", Team.Enemy, new Vector2Int(8, 6), BeastAI.OwnerId);
        var beast2 = Make("Fera 2", Team.Enemy, new Vector2Int(9, 8), BeastAI.OwnerId);
        var units = new List<Unit> { hero, beast, beast2 };

        var hud = PangeaScreen.Spawn<BattleHUD>("BattleHUD");
        yield return null; yield return null;
        hud.ShowUnitInfo(hero);
        hud.SetActionBarVisible(true, hero); // o PlanningController faz isto ao começar o plano

        // Fase de plano do jogador (blocos clicáveis) + fileiras das feras ocupadas.
        beast.actionSequence.Add(new ScheduledAction { Type = ActionType.Move });
        beast.plannedPath.Add(new Vector2Int(7, 6));
        beast.actionSequence.Add(new ScheduledAction { Type = ActionType.Attack });
        beast.plannedAttacks.Add(new PlannedAttack { Mode = AttackMode.Unit, TargetUnit = hero });
        beast.BuildQueueFromPlan();
        hud.UpdateTimeline(3, units, u => u == hero);
        hud.SetActionSequence(
            new List<ActionType> { ActionType.Move, ActionType.Move, ActionType.Attack, ActionType.Attack },
            new List<ScheduledAction> {
                new ScheduledAction { Type = ActionType.Move }, new ScheduledAction { Type = ActionType.Move },
                new ScheduledAction { Type = ActionType.Attack }, new ScheduledAction { Type = ActionType.Attack, IsBonus = true } });
        hud.LogAction("> Fera 1 -> Jogador: 6  (11/32 HP)", beast);
        for (int i = 0; i < 5; i++) yield return null;
        // Clica no bloco "3 Atacar" do plano: deve abrir o menu de incremento.
        var root = hud.GetComponent<UIDocument>().rootVisualElement;
        VisualElement atk = null;
        root.Query<Label>().ForEach(l => { if (atk == null && l.text.Contains("Atacar") && l.text.StartsWith("3")) atk = l.parent; });
        Assert.IsNotNull(atk, "bloco de ataque do plano não encontrado");
        using (var e = ClickEvent.GetPooled()) { e.target = atk; atk.SendEvent(e); }
        for (int i = 0; i < 5; i++) yield return null;
        yield return new WaitForEndOfFrame();

        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "Temp", "tickbar.png"), tex.EncodeToPNG());
        Debug.Log("[TickBarShot] ok " + tex.width + "x" + tex.height);
    }
}
