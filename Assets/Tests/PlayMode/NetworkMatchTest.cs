using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PangeaSkirmish;
using PangeaSkirmish.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Partida Coop de ponta a ponta com host local (2026-11-14): perfil → sala → posicionar → batalha →
/// recompensa, baú do grupo (pegar, deixar, vender) → descanso com ponto de nível → descer.
/// </summary>
public class NetworkMatchTest
{
    private const string ProfileKey = "pangea.profile.v1";

    private static T Field<T>(object o, string name) =>
        (T)o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o);

    private static IEnumerator WaitFor(System.Func<bool> cond, float seconds, string what)
    {
        float t = seconds;
        while (!cond() && t > 0f) { t -= Time.unscaledDeltaTime; yield return null; }
        Assert.IsTrue(cond(), "esperava: " + what);
    }

    [UnityTest]
    public IEnumerator CoopMatch_PlaceLootRestDescend()
    {
        LogAssert.ignoreFailingMessages = true;
        string backup = PlayerPrefs.GetString(ProfileKey, "");
        var tuning = Resources.Load<GameTuning>("GameTuning");
        RuntimeTuning.Active = tuning; AttributeStats.Formulas = tuning.statFormulas;
        PlayerProfile.ResetForTests(true);
        PlayerProfile.SetName("Anfitrião");
        var preset = new CharacterPreset
        {
            presetName = "Escudeiro", weaponId = "IronSword", modelId = "Tank", level = 4,
            stats = new UnitStatBlock { STR = 5, VIT = 5, DEX = 5, AGI = 5, INT = 5, WIS = 5, Footprint = 3, AttackRange = 4 },
        };
        Assert.GreaterOrEqual(PlayerProfile.Put(-1, preset), 0, "personagem válido no perfil");
        RuntimeMultiplayerSession.PlayerName = PlayerProfile.Name;

        var boot = NetBootstrap.EnsureExists();
        try
        {
            boot.HostLoopback();
            yield return WaitFor(() => RoomManager.Instance != null && PlacementSync.Instance != null, 5f, "rede de pé");
            var room = RoomManager.Instance;
            room.SetConfigServerRpc(new RoomConfigNet { GameMode = GameModes.Coop, AttributeBudget = PlayerProfile.AttributeBudget, PlanningTime = 15f, MaxPlayers = 4 });
            yield return null;
            room.SubmitCharacterServerRpc(JsonUtility.ToJson(preset));
            yield return WaitFor(() => room.Slots.Count > 0 && room.Slots[0].ReadyChar, 3f, "personagem aceito na sala");
            room.AdvancePhaseServerRpc();
            yield return WaitFor(() => SceneManager.GetActiveScene().name == "Battle" && Object.FindFirstObjectByType<GridManager>() != null, 15f, "cena de batalha");
            var ps = PlacementSync.Instance;
            yield return WaitFor(() => Field<System.Collections.Generic.List<Vector2Int>>(ps, "_myZoneHighlights").Count > 0, 5f, "zona de posicionamento");
            ps.PlaceUnitServerRpc(4, 12);
            yield return WaitFor(() => UnitRegistry.AllUnits.Count(u => u != null) >= 2, 8f, "jogador e fera");
            var mine = UnitRegistry.AllUnits.First(u => u != null && u.isPlayerCharacter);
            uint myId = UnitRegistry.GetId(mine);
            Assert.AreEqual(DayNight.Day, DayNight.Current);

            // Baú do grupo: pegar (troca com o equipado), deixar e vender.
            ps.HostGenerateLoot(1);
            yield return WaitFor(() => ps.Stash.Count >= 2, 3f, "baú com 1 por personagem + 1");
            int pick = ps.Stash.FindIndex(id => ItemCatalog.Get(id) != null && Loot.CanEquip(mine, ItemCatalog.Get(id), out _));
            Assume.That(pick >= 0);
            var it = ItemCatalog.Get(ps.Stash[pick]);
            ps.LootTakeServerRpc(myId, pick, it.id);
            yield return WaitFor(() => Loot.Current(mine, it.slot) == it.id, 3f, "item equipado");
            ps.LootLeaveServerRpc(myId, (int)it.slot);
            yield return WaitFor(() => Loot.Current(mine, it.slot) == "", 3f, "item deixado no baú");
            int gold = PlayerProfile.Gold, n = ps.Stash.Count, idx = ps.Stash.IndexOf(it.id);
            ps.LootSellServerRpc(myId, idx, it.id);
            yield return WaitFor(() => ps.Stash.Count == n - 1, 3f, "vendido");
            Assert.AreEqual(gold + Loot.SellPrice(it), PlayerProfile.Gold, "moedas para quem vendeu");

            // Descanso: cura tudo, passa o tempo e aceita ponto de nível na unidade.
            mine.currentHP = 1;
            ps.HostRest(1);
            yield return WaitFor(() => DayNight.Current == DayNight.Dusk, 5f, "descanso vira entardecer");
            Assert.AreEqual(mine.stats.MaxHP, mine.currentHP);
            float str = mine.stats.STR;
            ps.RestAttrServerRpc(myId, (int)Attr.STR);
            yield return WaitFor(() => mine.stats.STR > str, 3f, "+1 STR no descanso");

            // Descer sem descansar: cura só parte e mantém a hora.
            mine.currentHP = 1;
            ps.HostDescend(2);
            for (int i = 0; i < 10; i++) yield return null;
            Assert.AreEqual(DayNight.Dusk, DayNight.Current);
            Assert.Greater(mine.currentHP, 1); Assert.Less(mine.currentHP, mine.stats.MaxHP);
        }
        finally
        {
            boot.Shutdown();
            if (string.IsNullOrEmpty(backup)) PlayerPrefs.DeleteKey(ProfileKey); else PlayerPrefs.SetString(ProfileKey, backup);
            PlayerPrefs.Save();
            PlayerProfile.ResetForTests(false);
            DayNight.Reset();
        }
        yield return null;
        var empty = SceneManager.CreateScene("NetTestEmpty");
        SceneManager.SetActiveScene(empty);
        if (SceneManager.GetSceneByName("Battle").isLoaded) yield return SceneManager.UnloadSceneAsync("Battle");
        foreach (var o in Object.FindObjectsByType<Unit>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        if (NetBootstrap.Instance != null) Object.Destroy(NetBootstrap.Instance.gameObject);
        yield return null;
    }
}
