using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PangeaSkirmish;
using PangeaSkirmish.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Fluxo real de uma sala (anfitrião em loopback, Coop): perfil → sala → iniciar → posicionar →
/// batalha → reiniciar → posicionar de novo. Cobre o que quebrou no teste de coop: o reinício
/// voltava ao posicionamento sem aceitar clique, e as fases de criar personagem/mapa saíram.
/// </summary>
public class MatchFlowTest
{
    private const string Key = "pangea.profile.v1";

    private static T Field<T>(object o, string name) =>
        (T)o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o);

    private static GridManager Grid() => Object.FindFirstObjectByType<GridManager>();

    private static CharacterPreset Preset(string name, float str = 5) => new CharacterPreset
    {
        presetName = name, weaponId = "IronSword", modelId = "Tank",
        level = 4,   // 30 pontos = nível 4 (GDD-024)
        stats = new UnitStatBlock { STR = str, VIT = 5, DEX = 5, AGI = 5, INT = 5, WIS = 5, Footprint = 3, AttackRange = 4 }
    };

    private static IEnumerator WaitFor(System.Func<bool> cond, float seconds, string what)
    {
        float t = seconds;
        while (!cond() && t > 0f) { t -= Time.unscaledDeltaTime; yield return null; }
        Assert.IsTrue(cond(), "esperava: " + what);
    }

    [UnityTest]
    public IEnumerator Room_StartsFromProfile_AndRestartAcceptsPlacementAgain()
    {
        LogAssert.ignoreFailingMessages = true;
        string backup = PlayerPrefs.GetString(Key, "");
        var tuning = Resources.Load<GameTuning>("GameTuning");
        RuntimeTuning.Active = tuning; AttributeStats.Formulas = tuning.statFormulas;
        PlayerProfile.ResetForTests(true);
        PlayerProfile.SetName("Anfitrião");
        PlayerProfile.Put(-1, Preset("Escudeiro"));
        Assert.IsTrue(PlayerProfile.IsReady);
        RuntimeMultiplayerSession.PlayerName = PlayerProfile.Name;

        var boot = NetBootstrap.EnsureExists();
        boot.HostLoopback();
        yield return WaitFor(() => RoomManager.Instance != null && PlacementSync.Instance != null, 5f, "RoomManager e PlacementSync spawnados");
        var room = RoomManager.Instance;
        room.SetConfigServerRpc(new RoomConfigNet { GameMode = GameModes.Coop, AttributeBudget = PlayerProfile.AttributeBudget, PlanningTime = 15f, MaxPlayers = 4 });
        yield return null;

        // Sem personagem enviado, a partida não inicia.
        room.AdvancePhaseServerRpc();
        yield return null;
        Assert.AreEqual(RoomPhase.Lobby, room.CurrentPhase, "sem personagem não inicia");
        StringAssert.Contains("Anfitrião", room.PlayersWithoutCharacter());

        // Personagem fora do orçamento fixo é recusado pelo servidor.
        string rejected = null;
        room.OnCharacterRejected += r => rejected = r;
        room.SubmitCharacterServerRpc(JsonUtility.ToJson(Preset("Trapaceiro", str: 9)));
        yield return WaitFor(() => rejected != null, 3f, "recusa do personagem com 34 pontos");
        Assert.IsFalse(room.Slots[0].ReadyChar);

        // A tela da sala envia sozinha o personagem em uso no perfil.
        var hud = PangeaScreen.Spawn<RoomHUD>("RoomHUD");
        yield return null;
        hud.EnterRoom("LOOPBACK");
        yield return WaitFor(() => room.Slots.Count > 0 && room.Slots[0].ReadyChar, 3f, "personagem do perfil aceito");
        Assert.AreEqual("Escudeiro", room.Slots[0].CharName.ToString());
        Assert.AreEqual("", room.PlayersWithoutCharacter());
        var root = hud.GetComponent<UIDocument>().rootVisualElement;
        Assert.AreEqual("Escudeiro", root.Q<Label>("char-label").text, "sala mostra o personagem em uso");
        Assert.IsNull(root.Q<Button>("btn-budget-plus"), "sem controle de orçamento na sala");
        Assert.IsNull(root.Q<TextField>("name-input"), "o nome vem do perfil, não é digitado na sala");
        for (int i = 0; i < 4; i++) yield return null;
        yield return new WaitForEndOfFrame();
        var shot = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "docs", "menu-room.png"), shot.EncodeToPNG());

        // Iniciar: do lobby direto ao posicionamento, na cena de batalha, com o mapa padrão maior.
        room.AdvancePhaseServerRpc();
        yield return WaitFor(() => SceneManager.GetActiveScene().name == "Battle" && Grid() != null, 15f, "cena Battle carregada");
        Assert.AreEqual(RoomPhase.Placement, room.CurrentPhase, "sem fases de criar personagem ou mapa");
        Assert.AreEqual(30, Grid().width, "mapa padrão 50% maior (30)");
        Assert.AreEqual(30, Grid().height);
        var ps = PlacementSync.Instance;
        yield return WaitFor(() => Field<System.Collections.Generic.List<Vector2Int>>(ps, "_myZoneHighlights").Count > 0, 5f, "zona de posicionamento");
        Assert.IsFalse(Field<bool>(ps, "_placementDone"));

        ps.PlaceUnitServerRpc(4, 12);
        yield return WaitFor(() => UnitRegistry.AllUnits.Count(u => u != null) >= 2, 8f, "jogador e fera na batalha");
        Assert.IsTrue(Field<bool>(ps, "_placementDone"));
        var mine = UnitRegistry.AllUnits.First(u => u != null && u.isPlayerCharacter);
        Assert.AreEqual("Escudeiro", mine.unitName);
        Assert.AreEqual("Tank", mine.modelId, "aparência do perfil chega à batalha");
        for (int i = 0; i < 20; i++) yield return null;

        // Reinício pelo anfitrião: volta ao posicionamento E aceita posicionar de novo.
        var oldGrid = Grid();
        room.RestartBattleServerRpc();
        yield return WaitFor(() => Grid() != null && Grid() != oldGrid, 15f, "cena Battle recarregada");
        yield return WaitFor(() => Field<System.Collections.Generic.List<Vector2Int>>(ps, "_myZoneHighlights").Count > 0, 5f, "zona após o reinício");
        Assert.AreEqual(RoomPhase.Placement, room.CurrentPhase);
        Assert.IsFalse(Field<bool>(ps, "_placementDone"), "após o reinício o clique de posicionar volta a valer");
        Assert.AreEqual(0, Field<System.Collections.Generic.List<Unit>>(ps, "_allUnits").Count, "unidades da batalha anterior esquecidas");
        Assert.IsFalse(room.Slots[0].Placed);

        ps.PlaceUnitServerRpc(6, 15);
        yield return WaitFor(() => UnitRegistry.AllUnits.Count(u => u != null) >= 2, 8f, "segunda batalha começa");
        var again = UnitRegistry.AllUnits.First(u => u != null && u.isPlayerCharacter);
        Assert.AreEqual(new Vector2Int(6, 15), again.spawnAnchor, "posicionou no lugar novo");
        for (int i = 0; i < 20; i++) yield return null;

        // Limpeza: encerra a rede e sai da cena de batalha.
        boot.Shutdown();
        yield return null;
        var empty = SceneManager.CreateScene("FlowTestEmpty");
        SceneManager.SetActiveScene(empty);
        yield return SceneManager.UnloadSceneAsync("Battle");
        foreach (var o in Object.FindObjectsByType<Unit>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        if (NetBootstrap.Instance != null) Object.Destroy(NetBootstrap.Instance.gameObject);
        if (string.IsNullOrEmpty(backup)) PlayerPrefs.DeleteKey(Key); else PlayerPrefs.SetString(Key, backup);
        PlayerPrefs.Save();
        PlayerProfile.ResetForTests(false);
        yield return null;
    }

    /// <summary>GDD-024: descansar cura tudo e passa o tempo; descer sem descanso cura parte e mantém a hora.
    /// As feras acompanham os pontos do grupo.</summary>
    [UnityTest]
    public IEnumerator Coop_RestHealsAll_AndAdvancesTime()
    {
        LogAssert.ignoreFailingMessages = true;
        string backup = PlayerPrefs.GetString(Key, "");
        var tuning = Resources.Load<GameTuning>("GameTuning");
        RuntimeTuning.Active = tuning; AttributeStats.Formulas = tuning.statFormulas;
        PlayerProfile.ResetForTests(true);
        PlayerProfile.SetName("Anfitrião");
        PlayerProfile.Put(-1, Preset("Escudeiro"));
        RuntimeMultiplayerSession.PlayerName = PlayerProfile.Name;

        var boot = NetBootstrap.EnsureExists();
        boot.HostLoopback();
        yield return WaitFor(() => RoomManager.Instance != null && PlacementSync.Instance != null, 5f, "rede de pé");
        var room = RoomManager.Instance;
        room.SetConfigServerRpc(new RoomConfigNet { GameMode = GameModes.Coop, AttributeBudget = PlayerProfile.AttributeBudget, PlanningTime = 15f, MaxPlayers = 4 });
        yield return null;
        room.SubmitCharacterServerRpc(JsonUtility.ToJson(Preset("Escudeiro")));
        yield return WaitFor(() => room.Slots.Count > 0 && room.Slots[0].ReadyChar, 3f, "personagem aceito");
        room.AdvancePhaseServerRpc();
        yield return WaitFor(() => SceneManager.GetActiveScene().name == "Battle" && Grid() != null, 15f, "cena Battle");
        var ps = PlacementSync.Instance;
        yield return WaitFor(() => Field<System.Collections.Generic.List<Vector2Int>>(ps, "_myZoneHighlights").Count > 0, 5f, "zona");
        ps.PlaceUnitServerRpc(4, 12);
        yield return WaitFor(() => UnitRegistry.AllUnits.Count(u => u != null) >= 2, 8f, "batalha");
        var mine = UnitRegistry.AllUnits.First(u => u != null && u.isPlayerCharacter);
        Assert.AreEqual(DayNight.Day, DayNight.Current, "a partida começa de dia");

        // Feras pelo grupo: 30 pontos (nível 4) → orçamento dos 30.
        float BeastPoints() => UnitRegistry.AllUnits.Where(u => u != null && BeastAI.Controls(u))
            .Sum(u => u.stats.STR + u.stats.VIT + u.stats.DEX + u.stats.AGI + u.stats.INT + u.stats.WIS);
        float dayBeasts = BeastPoints();
        Assert.Greater(dayBeasts, 0f);

        // Baú do grupo (GDD-024 etapa B): gerar, pegar (troca), deixar e vender, tudo pelo host.
        uint myId = UnitRegistry.GetId(mine);
        ps.HostGenerateLoot(1);
        yield return WaitFor(() => ps.Stash.Count >= 2, 3f, "baú com 1 item por jogador + 1");
        int before = ps.Stash.Count;
        int pick = ps.Stash.FindIndex(id => ItemCatalog.Get(id) != null && Loot.CanEquip(mine, ItemCatalog.Get(id), out _));
        Assert.GreaterOrEqual(pick, 0);
        var taken = ItemCatalog.Get(ps.Stash[pick]);
        string had = Loot.Current(mine, taken.slot);
        ps.LootTakeServerRpc(myId, pick, taken.id);
        yield return WaitFor(() => Loot.Current(mine, taken.slot) == taken.id, 3f, "item equipado");
        Assert.AreEqual(before - 1 + (ItemCatalog.Get(had) != null ? 1 : 0), ps.Stash.Count, "o item antigo volta para o baú");
        ps.LootLeaveServerRpc(myId, (int)taken.slot);
        yield return WaitFor(() => Loot.Current(mine, taken.slot) == "", 3f, "deixou o item");
        Assert.Contains(taken.id, ps.Stash);
        int gold = PlayerProfile.Gold, idx = ps.Stash.IndexOf(taken.id), n = ps.Stash.Count;
        ps.LootSellServerRpc(myId, idx, taken.id);
        yield return WaitFor(() => ps.Stash.Count == n - 1, 3f, "vendeu");
        Assert.AreEqual(gold + Loot.SellPrice(taken), PlayerProfile.Gold, "moedas para quem vendeu");
        ps.LootSellServerRpc(myId, n + 5, taken.id);   // pedido velho, fora do baú: o host recusa
        for (int i = 0; i < 5; i++) yield return null;
        Assert.AreEqual(n - 1, ps.Stash.Count);

        mine.currentHP = 1;
        ps.HostDescend(2, rest: true);
        yield return WaitFor(() => DayNight.Current == DayNight.Dusk, 5f, "descanso vira entardecer");
        for (int i = 0; i < 10; i++) yield return null;
        Assert.AreEqual(mine.stats.MaxHP, mine.currentHP, "descanso cura tudo");

        mine.currentHP = 1;
        ps.HostDescend(3);
        for (int i = 0; i < 10; i++) yield return null;
        Assert.AreEqual(DayNight.Dusk, DayNight.Current, "descer sem descansar não passa o tempo");
        Assert.Less(mine.currentHP, mine.stats.MaxHP, "descer cura só parte");
        Assert.Greater(mine.currentHP, 1);

        // Descanso separado (2026-11-14): cura, passa o tempo e deixa somar pontos de nível na unidade.
        mine.currentHP = 1;
        ps.HostRest(3);
        yield return WaitFor(() => DayNight.Current == DayNight.Night, 5f, "descanso vira noite");
        Assert.AreEqual(mine.stats.MaxHP, mine.currentHP, "descanso cura tudo");
        float str = mine.stats.STR;
        ps.RestAttrServerRpc(UnitRegistry.GetId(mine), (int)Attr.STR);
        yield return WaitFor(() => mine.stats.STR > str, 3f, "+1 STR na unidade");
        Assert.AreEqual(str + 1f, mine.stats.STR, 0.001f);

        boot.Shutdown();
        yield return null;
        var empty = SceneManager.CreateScene("RestTestEmpty");
        SceneManager.SetActiveScene(empty);
        yield return SceneManager.UnloadSceneAsync("Battle");
        foreach (var o in Object.FindObjectsByType<Unit>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        if (NetBootstrap.Instance != null) Object.Destroy(NetBootstrap.Instance.gameObject);
        if (string.IsNullOrEmpty(backup)) PlayerPrefs.DeleteKey(Key); else PlayerPrefs.SetString(Key, backup);
        PlayerPrefs.Save();
        PlayerProfile.ResetForTests(false);
        DayNight.Reset();
        yield return null;
    }

    [UnityTest]
    public IEnumerator Profile_CreateCharacter_EnablesPlay()
    {
        LogAssert.ignoreFailingMessages = true;
        string backup = PlayerPrefs.GetString(Key, "");
        var tuning = Resources.Load<GameTuning>("GameTuning");
        RuntimeTuning.Active = tuning; AttributeStats.Formulas = tuning.statFormulas;
        PlayerProfile.ResetForTests(true);
        if (Camera.main == null) new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();

        // Menu principal: sem perfil, o cartão diz o que falta.
        var menu = PangeaScreen.Spawn<MainMenuScreen>("MainMenuScreen");
        for (int i = 0; i < 4; i++) yield return null;
        var mroot = menu.GetComponent<UIDocument>().rootVisualElement;
        StringAssert.Contains("nome", mroot.Q<Label>("profile-char").text);
        Assert.IsNull(mroot.Q<Button>("btn-create-map"), "criação de mapa saiu do menu");
        menu.SetVisible(false);

        var screen = PangeaScreen.Spawn<ProfileScreen>("ProfileScreen");
        for (int i = 0; i < 4; i++) yield return null;
        var root = screen.GetComponent<UIDocument>().rootVisualElement;
        var cont = root.Q<Button>("btn-continue");
        Assert.IsFalse(cont.enabledSelf, "sem nome e sem personagem não dá para jogar");

        root.Q<TextField>("name-input").value = "Marquinho";
        yield return null;
        Assert.AreEqual("Marquinho", PlayerProfile.Name);
        Assert.IsFalse(cont.enabledSelf, "ainda falta personagem");

        // Novo personagem: o editor abre, salva no perfil e volta.
        var editor = screen.OpenEditor(-1);
        for (int i = 0; i < 4; i++) yield return null;
        var eroot = editor.GetComponent<UIDocument>().rootVisualElement;
        using (var e = ClickEvent.GetPooled()) { var b = eroot.Q<Button>("sprite-next"); e.target = b; b.SendEvent(e); }   // Automático → Guerreiro
        using (var e = ClickEvent.GetPooled()) { var b = eroot.Q<Button>("sprite-next"); e.target = b; b.SendEvent(e); }   // → Tanque
        yield return null;
        using (var e = ClickEvent.GetPooled()) { var b = eroot.Q<Button>("confirm-btn"); e.target = b; b.SendEvent(e); }
        for (int i = 0; i < 4; i++) yield return null;
        Assert.AreEqual(1, PlayerProfile.Count, "personagem salvo no perfil");
        Assert.AreEqual("Tank", PlayerProfile.Selected.modelId, "aparência Tanque disponível e salva");
        Assert.IsTrue(cont.enabledSelf, "perfil pronto libera o Jogar");
        Assert.IsNotNull(root.Q<VisualElement>("char-row-0"), "personagem aparece na lista");
        var preview = Object.FindFirstObjectByType<CharacterPreview3D>();
        Assert.IsNotNull(preview, "prévia 3D do personagem em uso");
        Assert.AreEqual("Tank", preview.BodyModel);

        // Segundo personagem e troca de qual está em uso.
        PlayerProfile.Put(-1, Preset("Segundo"));
        screen.Show("");
        yield return null;
        var row0 = root.Q<VisualElement>("char-row-0");
        using (var e = ClickEvent.GetPooled()) { e.target = row0; row0.SendEvent(e); }
        yield return null;
        Assert.AreEqual(0, PlayerProfile.Data.selected, "clicar na linha troca o personagem em uso");

        for (int i = 0; i < 4; i++) yield return null;
        yield return new WaitForEndOfFrame();
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "docs", "menu-profile.png"), tex.EncodeToPNG());

        screen.Hide();
        menu.SetVisible(true); menu.RefreshProfile();
        for (int i = 0; i < 4; i++) yield return null;
        StringAssert.Contains("Marquinho", mroot.Q<Label>("profile-name").text);
        yield return new WaitForEndOfFrame();
        tex = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "docs", "menu-main.png"), tex.EncodeToPNG());

        Object.Destroy(screen.gameObject); Object.Destroy(menu.gameObject);
        if (string.IsNullOrEmpty(backup)) PlayerPrefs.DeleteKey(Key); else PlayerPrefs.SetString(Key, backup);
        PlayerPrefs.Save();
        PlayerProfile.ResetForTests(false);
        yield return null;
    }
}
