using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using PangeaSkirmish;
using PangeaSkirmish.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// GDD-020 na HUD de verdade (PlanningController + BattleHUD): barra de ações, fantasmas 3D,
/// contorno do alcance, área de andada, formas planejadas, prévia da mira livre, menu de reação
/// e catálogo de magias. Salva capturas em docs/hud-*.png.
/// </summary>
public class HudProjectionShot
{
    private GridManager _grid; private Camera _cam; private List<Unit> _all;

    private Unit Spawn(string name, string defId, Team team, Vector2Int anchor, Color col, string weapon, ulong owner)
    {
        var u = new GameObject(name).AddComponent<Unit>();
        u.unitName = name; u.definitionId = defId; u.team = team; u.weaponId = weapon; u.ownerId = owner;
        u.stats = new UnitStatBlock { STR = 6, VIT = 5, DEX = 4, AGI = 5, INT = 5, WIS = 5 }.ToAttributeStats();
        u.Init(_grid, anchor, col, "Sprites/TinyTactics/Warrior");
        UnitRegistry.Register((uint)(_all.Count + 1), u);
        _all.Add(u);
        return u;
    }

    private static object Call(object o, string name, params object[] args) =>
        o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public).Invoke(o, args);

    private static IEnumerator Shot(string file)
    {
        for (int i = 0; i < 4; i++) yield return null;
        yield return new WaitForEndOfFrame();
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "docs", file), tex.EncodeToPNG());
        Object.Destroy(tex);
    }

    private static void Hover(VisualElement el)
    {
        using (var e = PointerEnterEvent.GetPooled()) { e.target = el; el.SendEvent(e); }
    }
    private static void Leave(VisualElement el)
    {
        using (var e = PointerLeaveEvent.GetPooled()) { e.target = el; el.SendEvent(e); }
    }

    [UnityTest]
    public IEnumerator Planning_Hud_Projection_Reactions_Spells()
    {
        LogAssert.ignoreFailingMessages = true;
        foreach (var old in Object.FindObjectsByType<Unit>(FindObjectsSortMode.None)) Object.Destroy(old.gameObject);
        foreach (var old in Object.FindObjectsByType<GridManager>(FindObjectsSortMode.None)) Object.Destroy(old.gameObject);
        foreach (var old in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) Object.Destroy(old.gameObject);
        foreach (var old in Object.FindObjectsByType<BattleHUD>(FindObjectsSortMode.None)) Object.Destroy(old.gameObject);
        foreach (var old in Object.FindObjectsByType<BattleLabel>(FindObjectsSortMode.None)) Object.Destroy(old.gameObject);
        yield return null;
        var tuning = Resources.Load<GameTuning>("GameTuning");
        RuntimeTuning.Active = tuning; AttributeStats.Formulas = tuning.statFormulas;
        UnitRegistry.Clear();
        if (AudioManager.I == null) new GameObject("AudioManager").AddComponent<AudioManager>();
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        _cam = camGo.AddComponent<Camera>();
        _cam.orthographic = true; _cam.clearFlags = CameraClearFlags.SolidColor; _cam.backgroundColor = new Color(0.10f, 0.11f, 0.15f);
        var ctrl = camGo.AddComponent<CameraController>();
        _grid = new GameObject("GridManager").AddComponent<GridManager>();
        _grid.width = 24; _grid.height = 18; _grid.Build();
        ctrl.Configure(_cam, _grid.CellToWorld(new Vector2Int(10, 8)), 9f);
        _all = new List<Unit>();
        BattleFeedback.Cam = _cam;

        var blue = tuning.playerTeamColor; var red = tuning.enemyTeamColor;
        var hero = Spawn("Escudeiro", "warrior", Team.Player, new Vector2Int(3, 7), blue, "IronAxe", 0);
        var f1 = Spawn("Fera 1", "monster", Team.Enemy, new Vector2Int(12, 6), red, "", BeastAI.OwnerId);
        var f2 = Spawn("Fera 2", "monster", Team.Enemy, new Vector2Int(12, 9), red, "", BeastAI.OwnerId);
        hero.currentStamina = hero.stats.MaxStamina; hero.currentMana = hero.stats.MaxMana;
        for (int i = 0; i < 4; i++) yield return null;

        var hud = PangeaScreen.Spawn<BattleHUD>("BattleHUD");
        yield return null; yield return null;
        var planner = new GameObject("Planner").AddComponent<PlanningController>();
        planner.Setup(_grid, _cam, _all);
        planner.SetHUD(hud);
        planner.Begin(hero);
        hud.UpdateTimeline(1, _all, u => u == hero);
        for (int i = 0; i < 3; i++) yield return null;

        var root = hud.GetComponent<UIDocument>().rootVisualElement;
        var bar = root.Q<VisualElement>("action-bar");
        Assert.IsNotNull(bar, "barra de ações criada");
        Assert.AreEqual(DisplayStyle.Flex, bar.resolvedStyle.display, "barra visível no planejamento");
        foreach (var id in new[] { "attack", "aim", "spell", "concentrate", "breathe", "wait", "posture", "reaction" })
            Assert.IsNotNull(root.Q<Button>("abtn-" + id), "botão " + id);

        // Plano: anda duas vezes, ataca a Fera 1 centralizado; postura Agressiva; reação padrão Bloquear; respira 1×.
        Call(planner, "FreeAppendMove", new Vector2Int(6, 7));
        Call(planner, "FreeAppendMove", new Vector2Int(8, 7));
        Call(planner, "CommitAttackUnit", f1);
        Call(planner, "CyclePosture", 1);
        hero.defaultReaction = ReactionKind.Block;
        Call(planner, "OnBreatheClick");
        Call(planner, "ShowSelfAttackRange");
        hud.RefreshActionBar();
        var ghosts = (List<GameObject>)typeof(PlanningController).GetField("_modelGhosts", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(planner);
        Assert.AreEqual(2, ghosts.Count, "um fantasma 3D por parada do caminho");
        var shapes = (List<GameObject>)typeof(PlanningController).GetField("_planShapeGhosts", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(planner);
        Assert.AreEqual(6, shapes.Count, "varredura 3×2 do ataque planejado marcada no chão");
        Assert.AreEqual(Posture.Aggressive, hero.plannedPosture);
        Hover(root.Q<Button>("abtn-posture"));
        yield return Shot("hud-plan.png");
        Leave(root.Q<Button>("abtn-posture"));

        // Mira livre: a forma sob o cursor pega as duas feras, com o dano previsto em cada uma.
        // (Em jogo o cursor sobre o mapa mantém a prévia; aqui não há mouse, então ela é chamada direto, em Idle.)
        Call(planner, "SetBaseRange", hero.plannedAnchor, hero.EffectiveAttackRange, tuning.attackOutlineColor, false);
        Call(planner, "ShowCursorShape", new Vector2Int(12, 8), true);
        var labels = (List<BattleLabel>)typeof(PlanningController).GetField("_cursorShapeLabels", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(planner);
        Assert.AreEqual(2, labels.Count, "prévia de dano nas duas feras dentro da forma");
        yield return Shot("hud-free-aim.png");
        Call(planner, "SetMode", PlanMode.Idle);

        // Menu de reação com custos e efeito de cada uma.
        hud.ShowReactionMenu(hero, new List<ReactionKind> { ReactionKind.Dodge, ReactionKind.Block }, 18f, _ => { });
        var rtext = root.Q<Label>("reaction-text").text;
        StringAssert.Contains("Bloquear", rtext, "mostra a reação padrão");
        yield return Shot("hud-reaction.png");
        hud.HideReactionMenu();

        // Janela de incremento (mesmo lugar da reação), com o dano de agora → depois.
        hud.ShowChoiceTitled("INCREMENTAR", "Escudeiro vai atacar em Fera 1: ~26 de dano.\n<size=12><color=#9fb3d9>Stamina 5</color></size>",
            ("<b>Golpe poderoso</b>   ~26 → <b>~39</b> de dano\n<size=12><color=#9fb3d9>2 Stamina · +1 tick</color></size>", null),
            ("<b>Mirar</b>   ~26 → <b>~31</b> de dano, mais acerto\n<size=12><color=#9fb3d9>1 Stamina</color></size>", null),
            ("Não incrementar", null));
        yield return Shot("hud-increment.png");
        hud.HideReactionMenu();

        // Catálogo de magias com a descrição de números reais.
        hud.ToggleSpellMenu();
        for (int i = 0; i < 2; i++) yield return null;
        Button explosion = null;
        root.Q<VisualElement>("spell-menu").Query<Button>().ForEach(b => { if (b.text.Contains("Explosão")) explosion = b; });
        Assert.IsNotNull(explosion);
        Hover(explosion);
        yield return Shot("hud-spells.png");
        StringAssert.Contains("5×5", SpellResolver.Describe(hero, SpellCatalog.Get("Explosion")), "área real no texto");
        StringAssert.Contains("Mana + 1 Stamina", SpellCatalog.Get("Fury").CostText);

        // Inspeção com ícones (compacta e expandida) e placas sobre as unidades.
        hud.ToggleSpellMenu();
        f1.burningTicks = 2; f1.burningDamage = 4; f1.soakedTicks = 3;
        hero.SetPosture(Posture.Aggressive); hero.currentMana = 2; hero.currentStamina = 1;
        hud.ShowUnitInfo(hero);
        for (int i = 0; i < 3; i++) yield return null;
        Assert.IsNotNull(root.Q<VisualElement>("inspect-body"), "corpo da inspeção com ícones");
        Assert.IsTrue(hero.WorldDetailVisible, "selecionada: a placa mostra Mana e Stamina");
        Assert.IsFalse(f1.WorldDetailVisible, "fera sem hover: só a vida");
        CollectionAssert.Contains(f1.WorldStateIcons(), "burn");
        yield return Shot("hud-inspect-compact.png");
        var toggle = root.Q<Button>("inspect-toggle");
        using (var e = NavigationSubmitEvent.GetPooled()) { e.target = toggle; toggle.SendEvent(e); }
        for (int i = 0; i < 3; i++) yield return null;
        StringAssert.Contains("menos", toggle.text, "expandiu a ficha");
        yield return Shot("hud-inspect-expanded.png");
        using (var e = NavigationSubmitEvent.GetPooled()) { e.target = toggle; toggle.SendEvent(e); }

        planner.End();
        Object.Destroy(hud.gameObject);
    }
}
