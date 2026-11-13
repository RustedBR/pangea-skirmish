using System.Collections;
using System.IO;
using NUnit.Framework;
using PangeaSkirmish;
using PangeaSkirmish.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>Criação de personagem: rodapé sempre visível, prévia 3D com arma e aparência, ficha e explicações no hover.</summary>
public class CharCreationTest
{
    private static void Click(VisualElement el)
    {
        using (var e = ClickEvent.GetPooled()) { e.target = el; el.SendEvent(e); }
    }

    [UnityTest]
    public IEnumerator Layout_Preview_Sheet_AndHover()
    {
        LogAssert.ignoreFailingMessages = true;
        foreach (var o in Object.FindObjectsByType<CharCreationHUD>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        foreach (var o in Object.FindObjectsByType<CharacterPreview3D>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        yield return null;
        var tuning = Resources.Load<GameTuning>("GameTuning");
        RuntimeTuning.Active = tuning; AttributeStats.Formulas = tuning.statFormulas;
        PlayerProfile.ResetForTests(true);
        if (Camera.main == null) new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();

        var hud = PangeaScreen.Spawn<CharCreationHUD>("CharCreationHUD");
        for (int i = 0; i < 6; i++) yield return null;
        var root = hud.GetComponent<UIDocument>().rootVisualElement;

        // Rodapé: Confirmar inteiro dentro da tela, fora da área que rola.
        var confirm = root.Q<Button>("confirm-btn");
        var panel = root.Q<VisualElement>("panel");
        Assert.IsNotNull(confirm);
        Assert.LessOrEqual(confirm.worldBound.yMax, root.worldBound.yMax + 0.5f, "Confirmar cabe na tela");
        Assert.LessOrEqual(panel.worldBound.height, root.worldBound.height, "o painel não passa da altura da tela");
        Assert.IsNull(root.Q<ScrollView>("cc-scroll").Q<Button>("confirm-btn"), "Confirmar fica fora da área que rola");

        // Prévia 3D: no automático, o corpo acompanha a arma, e a arma aparece na mão.
        var preview = Object.FindFirstObjectByType<CharacterPreview3D>();
        Assert.IsNotNull(preview, "prévia 3D criada");
        Assert.AreEqual("Hatchet", preview.WeaponModel, "arma inicial na mão");
        Assert.AreEqual("Warrior", preview.BodyModel, "machado → Guerreiro no automático");
        var weaponNext = root.Q<Button>("weapon-next");
        string seenBow = null, seenStaff = null;
        for (int i = 0; i < 12; i++)
        {
            Click(weaponNext); yield return null;
            if (preview.WeaponModel == "LongBow") seenBow = preview.BodyModel;
            if (preview.WeaponModel == "ArcaneStaff") seenStaff = preview.BodyModel;
        }
        Assert.AreEqual("Archer", seenBow, "arco → Arqueiro");
        Assert.AreEqual("Mage", seenStaff, "cajado → Mago");

        // Aparência escolhida manda no corpo, com qualquer arma.
        var lookNext = root.Q<Button>("sprite-next");
        for (int i = 0; i < 6 && preview.BodyModel != "Tank"; i++) { Click(lookNext); yield return null; }
        Assert.AreEqual("Tank", preview.BodyModel, "aparência Tanque");
        Assert.IsTrue(preview.HasShield, "Tanque com escudo");
        StringAssert.Contains("Tanque", root.Q<Label>("sprite-label").text);

        // Ficha e explicações.
        StringAssert.Contains("Vida", root.Q<Label>("derived-label").text);
        StringAssert.Contains("Brasa", root.Q<Label>("derived-label").text);
        StringAssert.Contains("Força", hud.AttrHelp(0));
        StringAssert.Contains("Alcance das magias", hud.AttrHelp(4));
        StringAssert.Contains("Dano por tile", hud.WeaponHelp());
        StringAssert.Contains("Forma", hud.WeaponHelp());
        var row = root.Q<VisualElement>("attr-row-1");
        using (var e = PointerEnterEvent.GetPooled()) { e.target = row; row.SendEvent(e); }
        yield return null;
        StringAssert.Contains("Vitalidade", root.Q<Label>("info-label").text, "hover no atributo explica o que faz");

        // Ficha reage aos pontos: +STR aumenta o golpe.
        int before = hud.CurrentStats().PhysicalDamage;
        for (int i = 0; i < 12; i++) { Click(weaponNext); yield return null; if (preview.WeaponModel == "IronSword") break; }
        before = hud.CurrentStats().PhysicalDamage;
        Click(root.Q<Button>("attr-minus-5")); yield return null;      // o orçamento começa todo gasto: libera 1 ponto
        Click(root.Q<Button>("attr-plus-0")); yield return null;
        Assert.Greater(hud.CurrentStats().PhysicalDamage, before, "STR sobe o dano da espada");

        // Regressão (teste no site, 2026-11-12): o hover na arma mudava a altura do painel, o
        // botão de trocar saía de baixo do cursor e o menu "saltava". Painel e botão ficam parados.
        var weaponRow = root.Q<Button>("weapon-prev").parent;
        var next = root.Q<Button>("weapon-next");
        using (var e = PointerEnterEvent.GetPooled()) { e.target = row; row.SendEvent(e); }
        for (int i = 0; i < 3; i++) yield return null;
        float panelH = panel.layout.height; var nextPos = next.worldBound.position;
        for (int i = 0; i < 10; i++)
        {
            using (var e = PointerEnterEvent.GetPooled()) { e.target = weaponRow; weaponRow.SendEvent(e); }
            Click(next);
            for (int f = 0; f < 3; f++) yield return null;
            Assert.AreEqual(panelH, panel.layout.height, 0.5f, $"altura do painel fixa (arma {i})");
            Assert.AreEqual(nextPos.x, next.worldBound.position.x, 0.5f, "botão de trocar arma não se move");
            Assert.AreEqual(nextPos.y, next.worldBound.position.y, 0.5f, "botão de trocar arma não se move");
        }

        // Personagem novo já nasce válido (orçamento exato) e não há mais controle de orçamento.
        Assert.AreEqual(PlayerProfile.AttributeBudget, PlayerProfile.Points(hud.Editing), "orçamento fixo todo gasto");
        Assert.IsNull(root.Q<VisualElement>("budget-stepper"), "sem seletor de orçamento");
        Assert.IsTrue(confirm.enabledSelf, "Salvar habilitado com o orçamento exato");
        StringAssert.Contains("Stamina", hud.AttrHelp(2), "DEX explica que dá Stamina");

        // Confirmar devolve o personagem com a aparência e a arma escolhidas.
        CharacterPreset saved = null;
        hud.OnConfirmed = p => saved = p;
        Click(confirm); yield return null;
        Assert.IsNotNull(saved, "OnConfirmed chamado");
        Assert.AreEqual("Tank", saved.modelId, "aparência escolhida vai no personagem");
        Assert.AreEqual("IronSword", saved.weaponId);
        Assert.IsTrue(PlayerProfile.IsValid(saved));

        for (int i = 0; i < 4; i++) yield return null;
        yield return new WaitForEndOfFrame();
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "docs", "char-creation.png"), tex.EncodeToPNG());
        Object.Destroy(hud.gameObject);
    }

    [Test]
    public void WeaponTable_IsBalancedPerTick()
    {
        var tuning = Resources.Load<GameTuning>("GameTuning");
        AttributeStats.Formulas = tuning.statFormulas;
        float min = float.MaxValue, max = 0f; string lo = "", hi = "";
        foreach (var w in tuning.weapons)
        {
            var st = new UnitStatBlock { STR = 6, VIT = 4, DEX = 6, AGI = 4, INT = 4, WIS = 4 }.ToAttributeStats();
            WeaponDef.ApplyTo(st, w, tuning);
            float perTick = st.PhysicalDamage / (float)w.attackTicks;      // sem o encurtamento por DEX: compara as armas entre si
            if (perTick < min) { min = perTick; lo = w.id; }
            if (perTick > max) { max = perTick; hi = w.id; }
        }
        AttributeStats.Formulas = null;
        Assert.LessOrEqual(max / min, 1.5f, $"nenhuma arma bate mais de 1,5× outra por tick (menor {lo} {min:0.#}, maior {hi} {max:0.#})");
    }
}
