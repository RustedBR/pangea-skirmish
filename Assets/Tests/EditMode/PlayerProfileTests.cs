using NUnit.Framework;
using PangeaSkirmish;
using UnityEngine;

/// <summary>Identidade do jogador: nome + personagens salvos, orçamento fixo, persistência.</summary>
public class PlayerProfileTests
{
    private string _backup;
    private const string Key = "pangea.profile.v1";

    [SetUp]
    public void SetUp()
    {
        _backup = PlayerPrefs.GetString(Key, "");            // não apagar o perfil real de quem roda os testes
        var tuning = Resources.Load<GameTuning>("GameTuning");
        RuntimeTuning.Active = tuning;
        PlayerProfile.ResetForTests(true);
    }

    [TearDown]
    public void TearDown()
    {
        if (string.IsNullOrEmpty(_backup)) PlayerPrefs.DeleteKey(Key); else PlayerPrefs.SetString(Key, _backup);
        PlayerPrefs.Save();
        PlayerProfile.ResetForTests(false);
    }

    private static CharacterPreset Make(string name, float str = 5, float wis = 5) => new CharacterPreset
    {
        presetName = name, weaponId = "IronSword", modelId = "Tank",
        level = 4,   // 30 pontos = nível 4 (GDD-024: 15 + 5 por nível)
        stats = new UnitStatBlock { STR = str, VIT = 5, DEX = 5, AGI = 5, INT = 5, WIS = wis, Footprint = 3, AttackRange = 4 }
    };

    [Test]
    public void EmptyProfile_IsNotReady_AndSaysWhatIsMissing()
    {
        Assert.IsFalse(PlayerProfile.IsReady);
        StringAssert.Contains("nome", PlayerProfile.MissingText);
        PlayerProfile.SetName("  Marquinho  ");
        Assert.AreEqual("Marquinho", PlayerProfile.Name, "nome sem espaços nas pontas");
        Assert.IsFalse(PlayerProfile.IsReady, "ainda falta personagem");
        StringAssert.Contains("personagem", PlayerProfile.MissingText);
        Assert.GreaterOrEqual(PlayerProfile.Put(-1, Make("Tanque")), 0);
        Assert.IsTrue(PlayerProfile.IsReady);
        Assert.AreEqual("", PlayerProfile.MissingText);
    }

    [Test]
    public void Budget_IsFixedAndExact()
    {
        Assert.IsTrue(PlayerProfile.IsValid(Make("ok")));
        Assert.IsFalse(PlayerProfile.IsValid(Make("sobra", str: 4)), "29 pontos: abaixo do orçamento");
        Assert.IsFalse(PlayerProfile.IsValid(Make("estoura", str: 6)), "31 pontos: acima do orçamento");
        Assert.AreEqual(-1, PlayerProfile.Put(-1, Make("estoura", str: 6)), "inválido não entra no perfil");
        Assert.IsFalse(PlayerProfile.IsValid(Make("limite", str: 0, wis: 10)), "atributo abaixo do mínimo");
        Assert.AreEqual(PlayerProfile.AttributeBudget, new RoomConfigData().attributeBudget, "a sala usa o mesmo orçamento");
    }

    /// <summary>Personagem novo de 15 pontos (nível 1): editável à vontade.</summary>
    private static CharacterPreset Small(string name) => new CharacterPreset
    {
        presetName = name, weaponId = "IronSword", modelId = "Tank", level = 1,
        stats = new UnitStatBlock { STR = 3, VIT = 3, DEX = 3, AGI = 2, INT = 2, WIS = 2, Footprint = 3, AttackRange = 4 }
    };

    [Test]
    public void SeveralCharacters_SelectEditRemove()
    {
        PlayerProfile.SetName("A");
        PlayerProfile.Put(-1, Small("Um"));
        PlayerProfile.Put(-1, Small("Dois"));
        PlayerProfile.Put(-1, Small("Três"));
        Assert.AreEqual(3, PlayerProfile.Count);
        Assert.AreEqual("Três", PlayerProfile.Selected.presetName, "o recém-criado fica em uso");
        PlayerProfile.Select(0);
        Assert.AreEqual("Um", PlayerProfile.Selected.presetName);
        Assert.AreEqual(1, PlayerProfile.Put(1, Small("Dois editado")), "editar substitui no lugar (nível 1: livre)");
        Assert.AreEqual(3, PlayerProfile.Count);
        Assert.AreEqual("Dois editado", PlayerProfile.Selected.presetName);
        PlayerProfile.Remove(1);
        Assert.AreEqual(2, PlayerProfile.Count);
        Assert.IsNotNull(PlayerProfile.Selected, "depois de apagar, outro fica em uso");
        PlayerProfile.Remove(0); PlayerProfile.Remove(0);
        Assert.IsNull(PlayerProfile.Selected);
        Assert.IsFalse(PlayerProfile.IsReady);
    }

    [Test]
    public void Profile_SurvivesReload()
    {
        PlayerProfile.SetName("Persistente");
        PlayerProfile.Put(-1, Make("Um"));
        PlayerProfile.Put(-1, Make("Dois"));
        PlayerProfile.Select(0);
        PlayerProfile.ResetForTests(false);                 // esquece a memória: força ler o salvo
        Assert.AreEqual("Persistente", PlayerProfile.Name);
        Assert.AreEqual(2, PlayerProfile.Count);
        Assert.AreEqual("Um", PlayerProfile.Selected.presetName);
        Assert.AreEqual("Tank", PlayerProfile.Selected.modelId, "a aparência é salva junto");
    }

    [Test]
    public void Dex_RaisesMaxStamina_LikeTheOtherBodyAttributes()
    {
        var T = RuntimeTuning.Active;
        var low  = new UnitStatBlock { STR = 4, VIT = 4, DEX = 2, AGI = 4 }.ToAttributeStats();
        var high = new UnitStatBlock { STR = 4, VIT = 4, DEX = 10, AGI = 4 }.ToAttributeStats();
        Assert.Greater(high.MaxStamina, low.MaxStamina, "DEX é atributo do Corpo: aumenta a Stamina");
        Assert.AreEqual(Mathf.Max(1, Mathf.RoundToInt(T.staminaBase + 18 * T.staminaPerBodyPoint)), high.MaxStamina);
    }
}
