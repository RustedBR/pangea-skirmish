using System.Collections;
using NUnit.Framework;
using PangeaSkirmish;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>Diagnóstico no build: o GameLog captura erros e despeja texto; copiar funciona; criar unidade não gera erro de Animator.</summary>
public class DiagnosticsTest
{
    [UnityTest]
    public IEnumerator GameLog_CapturesErrors_AndDumpsText()
    {
        LogAssert.ignoreFailingMessages = true;
        yield return null;
        Assert.IsNotNull(GameObject.Find("GameLog"), "o GameLog se cria sozinho ao iniciar");
        GameLog.Clear();
        Debug.Log("linha comum de teste 123");
        Debug.LogError("ERRO de teste 456");
        yield return null;
        Assert.AreEqual(1, GameLog.ErrorCount, "conta erros");
        string dump = GameLog.Dump();
        StringAssert.Contains("linha comum de teste 123", dump);
        StringAssert.Contains("ERRO de teste 456", dump);
        StringAssert.Contains("log da partida", dump);

        WebBridge.CopyText("ABC123");
        Assert.AreEqual("ABC123", WebBridge.LastCopied);
        Assert.AreEqual("ABC123", GUIUtility.systemCopyBuffer, "fora do WebGL vai para a área de transferência do sistema");
    }

    [UnityTest]
    public IEnumerator SpawningUnits_DoesNotLogAnimatorErrors()
    {
        LogAssert.ignoreFailingMessages = true;
        foreach (var o in Object.FindObjectsByType<Unit>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        foreach (var o in Object.FindObjectsByType<GridManager>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        yield return null;
        var tuning = Resources.Load<GameTuning>("GameTuning");
        RuntimeTuning.Active = tuning; AttributeStats.Formulas = tuning.statFormulas;
        var grid = new GameObject("GridManager").AddComponent<GridManager>();
        grid.width = 16; grid.height = 16; grid.Build();
        GameLog.Clear();
        string[] weapons = { "IronSword", "WoodenStaff", "ArcaneStaff", "LongBow", "Hatchet" };
        for (int i = 0; i < weapons.Length; i++)
        {
            var u = new GameObject("U" + i).AddComponent<Unit>();
            u.unitName = "U" + i; u.team = Team.Player; u.weaponId = weapons[i];
            u.stats = new UnitStatBlock().ToAttributeStats();
            u.Init(grid, new Vector2Int(1 + i * 3, 2), Color.blue, "Sprites/TinyTactics/Warrior");
            Assert.IsNotNull(u.Model3D, "corpo 3D presente");
        }
        for (int i = 0; i < 5; i++) yield return null;
        string dump = GameLog.Dump();
        StringAssert.DoesNotContain("Could not set Runtime Animator Controller", dump);
        StringAssert.DoesNotContain("Sem controller para", dump);
        Assert.AreEqual(0, GameLog.ErrorCount, "criar unidades não gera erros");
    }
}
