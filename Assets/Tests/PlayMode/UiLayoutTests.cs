using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PangeaSkirmish;
using PangeaSkirmish.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Escala, encaixe, espaçamento e contraste das telas (2026-11-14), na escala do PC e na do celular.
/// Atenção especial aos menus da esquerda (escolhas, reação, grade de ações) e à tela de magias.
/// </summary>
public class UiLayoutTests
{
    private static readonly Vector2Int Pc = new Vector2Int(1920, 1080), Phone = new Vector2Int(1120, 630);
    private PanelSettings _panel; private Vector2Int _keepRes;

    [SetUp] public void SetUp() { _panel = Resources.Load<PanelSettings>(PangeaScreen.PanelSettingsResource); _keepRes = _panel.referenceResolution; }
    [TearDown] public void TearDown() { _panel.referenceResolution = _keepRes; Battlefield.Clean(); RuntimeMultiplayerSession.IsMultiplayer = false; }

    private static IEnumerator Settle() { for (int i = 0; i < 4; i++) yield return null; }

    private static void Report(List<string> bad)
    {
        foreach (var b in bad) Debug.Log("[UiAudit] " + b);
        Assert.IsEmpty(bad, $"{bad.Count} problema(s) de UI:\n" + string.Join("\n", bad.GetRange(0, Mathf.Min(25, bad.Count))));
    }

    private IEnumerator Menus(Vector2Int res, List<string> bad)
    {
        _panel.referenceResolution = res;
        string tag = res == Pc ? "PC" : "celular";
        foreach (var make in new System.Func<PangeaScreen>[]
                 {
                     () => PangeaScreen.Spawn<MainMenuScreen>("MainMenuScreen"),
                     () => PangeaScreen.Spawn<ProfileScreen>("ProfileScreen"),
                     () => PangeaScreen.Spawn<CharCreationHUD>("CharCreationHUD"),
                 })
        {
            var s = make();
            yield return Settle();
            bad.AddRange(UiAudit.Run(s.GetComponent<UIDocument>().rootVisualElement, $"{s.GetType().Name}/{tag}"));
            Object.Destroy(s.gameObject);
            yield return null;
        }
    }

    [UnityTest]
    public IEnumerator Menus_FitAndRead()
    {
        LogAssert.ignoreFailingMessages = true;
        Battlefield.Clean(); yield return null;
        var bad = new List<string>();
        yield return Menus(Pc, bad);
        yield return Menus(Phone, bad);
        Report(bad);
    }

    private IEnumerator Battle(Vector2Int res, List<string> bad)
    {
        _panel.referenceResolution = res;
        string tag = res == Pc ? "PC" : "celular";
        var b = new Battlefield(); yield return b.Build();
        var hero = b.Add("Escudeiro", Team.Player, new Vector2Int(6, 6), "IronSword", 0);
        b.Add("Fera", Team.Enemy, new Vector2Int(12, 8), "Claws", 1);
        var planner = new GameObject("Planner").AddComponent<PlanningController>();
        planner.Setup(b.Grid, b.Cam, b.Units); planner.SetHUD(b.Hud); planner.Begin(hero);
        b.Hud.ShowUnitInfo(hero);
        b.Hud.SetActionBarVisible(true, hero);
        b.Hud.SetSpellMenuUnit(hero);
        yield return Settle();
        var root = b.Hud.GetComponent<UIDocument>().rootVisualElement;
        bad.AddRange(UiAudit.Run(root, $"Batalha/{tag}"));

        // Menu de escolha à esquerda (reação, incremento, itens do grupo usam o mesmo).
        b.Hud.ShowChoiceTitled("REAGIR", "Escudeiro pode reagir (Stamina 5).\nSe o tempo acabar: Bloquear",
            ("Esquivar\nanda até 2 tiles para fora do golpe · 2 Stamina", () => { }),
            ("Bloquear\nsegura 6 de dano; se absorver tudo, desequilibra · 1 Stamina", () => { }),
            ("Não reagir", () => { }));
        yield return Settle();
        bad.AddRange(UiAudit.Run(root, $"Batalha+escolha/{tag}"));
        b.Hud.HideReactionMenu();

        // Tela de magias.
        b.Hud.ToggleSpellMenu();
        yield return Settle();
        bad.AddRange(UiAudit.Run(root, $"Batalha+magias/{tag}"));
        b.Hud.ToggleSpellMenu();
        planner.End();
        Object.Destroy(planner.gameObject);
        Battlefield.Clean();
        yield return null;
    }

    [UnityTest]
    public IEnumerator BattleHud_FitsAndReads_IncludingLeftMenus()
    {
        LogAssert.ignoreFailingMessages = true;
        var bad = new List<string>();
        yield return Battle(Pc, bad);
        yield return Battle(Phone, bad);
        Report(bad);
    }

    [UnityTest]
    public IEnumerator SpellMenu_GroupsSearchAndFixedDescription()
    {
        LogAssert.ignoreFailingMessages = true;
        var b = new Battlefield(); yield return b.Build();
        var hero = b.Add("Maga", Team.Player, new Vector2Int(6, 6), "ArcaneStaff", 0);
        b.Hud.SetSpellMenuUnit(hero);
        b.Hud.ToggleSpellMenu();
        yield return Settle();
        var menu = b.Hud.GetComponent<UIDocument>().rootVisualElement.Q<VisualElement>("spell-menu");
        Assert.IsNotNull(menu);
        Assert.AreEqual(SpellCatalog.All().Length, menu.Query<Button>(className: "bh-spellblock").ToList().Count, "toda magia tem um bloco");
        var search = menu.Q<TextField>("spell-search");
        search.value = "agua"; yield return Settle();
        Assert.AreEqual(DisplayStyle.None, menu.Q<Button>("spell-Ember").resolvedStyle.display, "busca sem acento filtra");
        search.value = ""; yield return Settle();
        float top = menu.worldBound.yMin;
        foreach (var id in new[] { "Explosion", "Ember" })
        {
            var bl = menu.Q<Button>("spell-" + id);
            using (var e = PointerEnterEvent.GetPooled()) { e.target = bl; bl.SendEvent(e); }
            yield return Settle();
            Assert.AreEqual(top, menu.worldBound.yMin, 0.5f, "a descrição não move o painel");
        }
    }

    [UnityTest]
    public IEnumerator ScreenPoint_MapsToPanel_WithYFlip()
    {
        LogAssert.ignoreFailingMessages = true;
        Battlefield.Clean(); yield return null;
        var s = PangeaScreen.Spawn<MainMenuScreen>("MainMenuScreen");
        yield return Settle();
        var root = s.GetComponent<UIDocument>().rootVisualElement;
        var btn = root.Q<Button>("btn-options");
        var c = btn.worldBound.center;
        float k = Screen.width / root.panel.visualTree.layout.width;
        var px = new Vector2(c.x * k, Screen.height - c.y * k);
        Assert.Less(Vector2.Distance(c, UIHelper.ScreenToPanel(root.panel, px)), 2f);
        Assert.IsTrue(UIHelper.IsPositionOverClickableUI(px));
        Object.Destroy(s.gameObject);
    }
}
