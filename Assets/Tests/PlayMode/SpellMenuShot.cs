using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PangeaSkirmish;
using PangeaSkirmish.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>Abre o menu de Magia da HUD (GDD-019 §6) com uma magia já na fila e salva Temp/spellmenu.png.</summary>
public class SpellMenuShot
{
    [UnityTest]
    public IEnumerator SpellMenu_OpensWithBothGroups_AndSpellBlockInRow()
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

        Unit Make(string n, Team t, Vector2Int a, ulong owner, int intel)
        {
            var u = new GameObject(n).AddComponent<Unit>();
            u.unitName = n; u.team = t; u.weaponId = "WoodenStaff"; u.ownerId = owner;
            u.stats = new UnitStatBlock { STR = 2, VIT = 3, DEX = 4, AGI = 3, INT = intel, WIS = 4 }.ToAttributeStats();
            u.Init(grid, a, Color.red, "Sprites/TinyTactics/Warrior");
            return u;
        }
        var hero  = Make("Maga", Team.Player, new Vector2Int(2, 3), 0, 6);
        var beast = Make("Fera 1", Team.Enemy, new Vector2Int(5, 4), BeastAI.OwnerId, 0);
        var units = new List<Unit> { hero, beast };

        foreach (var oldHud in Object.FindObjectsByType<BattleHUD>(FindObjectsSortMode.None)) Object.Destroy(oldHud.gameObject);
        yield return null;
        var hud = PangeaScreen.Spawn<BattleHUD>("BattleHUD");
        yield return null; yield return null;
        hud.ShowUnitInfo(hero);
        hud.SetActionBarVisible(true, hero);
        hud.SetSpellMenuUnit(hero);

        // Plano: andar, Brasa na fera, Fúria (Física). Mostra bloco Elemental (cor Fogo, interrompível) e Físico.
        hero.currentStamina = hero.stats.MaxStamina; hero.currentMana = hero.stats.MaxMana;
        hero.plannedPath.Add(new Vector2Int(3, 3));
        hero.plannedSpells.Add(new PlannedSpell { SpellId = "Ember", TargetUnit = beast, TargetTile = beast.anchor });
        hero.plannedSpells.Add(new PlannedSpell { SpellId = "Fury", TargetTile = hero.anchor });
        hero.actionSequence.Add(new ScheduledAction { Type = ActionType.Move, Index = 0 });
        hero.actionSequence.Add(new ScheduledAction { Type = ActionType.Spell, Index = 0 });
        hero.actionSequence.Add(new ScheduledAction { Type = ActionType.Spell, Index = 1 });
        hero.RecomputeTickBudget();
        Assert.AreEqual(2 + 1, hero.PlannedManaCost(), "Brasa custa 2 de Mana e a Fúria (custo 2, física) 1 de Mana");
        Assert.AreEqual(hero.currentStamina - 1, hero.remainingBAP, "Fúria reserva 1 de Stamina (metade, arredondada para baixo)");

        hud.UpdateTimeline(1, units, u => u == hero);
        hud.SetActionSequence(new List<ActionType> { ActionType.Move, ActionType.Spell, ActionType.Spell },
                              new List<ScheduledAction>(hero.actionSequence));
        for (int i = 0; i < 3; i++) yield return null;

        var root = hud.GetComponent<UIDocument>().rootVisualElement;
        Label ember = null;
        root.Query<Label>().ForEach(l => { if (ember == null && l.text.Contains("Brasa")) ember = l; });
        Assert.IsNotNull(ember, "bloco da Brasa na fila do plano");

        // Abre o menu (o botão Magia agora fica na barra de ações, GDD-020 §9).
        hud.ToggleSpellMenu();
        for (int i = 0; i < 3; i++) yield return null;

        var menu = root.Q<VisualElement>("spell-menu");
        Assert.IsNotNull(menu, "menu de magia aberto");
        int spellButtons = menu.Query<Button>(className: "bh-spellblock").ToList().Count;
        var explosion = menu.Q<Button>("spell-Explosion");
        Assert.AreEqual(19, spellButtons, "19 magias na grade");
        Assert.IsNotNull(explosion);
        // Agrupadas por elemento: a Explosão (Fogo + Ar) fica no grupo de Fogo.
        Assert.AreEqual(ElementInfo.Name(SpellElement.Fire), explosion.parent.parent.Q<Label>(className: "bh-spellgroup-name").text);

        // Busca sem acento e por elemento: "agua" acha as magias de Água; "zzz" esconde tudo.
        var search = menu.Q<TextField>("spell-search");
        search.value = "agua"; yield return null;
        int visible = 0;
        menu.Query<Button>(className: "bh-spellblock").ForEach(b => { if (b.resolvedStyle.display == DisplayStyle.Flex) visible++; });
        Assert.Greater(visible, 0, "busca por elemento acha magias");
        Assert.Less(visible, 19, "e filtra as outras");
        Assert.AreEqual(DisplayStyle.None, menu.Q<Button>("spell-Ember").resolvedStyle.display, "Brasa (Fogo) sai da busca por água");
        search.value = ""; yield return null;

        // A descrição tem altura fixa: trocar de magia não move o painel.
        float topBefore = menu.worldBound.yMin;
        // Hover na Explosão: a linha de baixo mostra o efeito.
        using (var e = PointerEnterEvent.GetPooled()) { e.target = explosion; explosion.SendEvent(e); }
        yield return null;
        bool hoverShown = false;
        menu.Query<Label>().ForEach(l => { if (l.text.Contains("de dano de Fogo em quem estiver inteiro")) hoverShown = true; });
        Assert.IsTrue(hoverShown, "hover escreve o efeito da magia");
        var emberBlock = menu.Q<Button>("spell-Ember");
        using (var e = PointerEnterEvent.GetPooled()) { e.target = emberBlock; emberBlock.SendEvent(e); }
        for (int i = 0; i < 2; i++) yield return null;
        Assert.AreEqual(topBefore, menu.worldBound.yMin, 0.5f, "a tela de magia não pula com a descrição");
        for (int i = 0; i < 2; i++) yield return null;
        yield return new WaitForEndOfFrame();

        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "Temp", "spellmenu.png"), tex.EncodeToPNG());
        Debug.Log("[SpellMenuShot] ok " + tex.width + "x" + tex.height);
    }
}
