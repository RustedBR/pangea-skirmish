using System.Collections;
using System.IO;
using NUnit.Framework;
using PangeaSkirmish;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Fumaça do mundo 3D: monta grid + unidades sem rede, confere que blocos e modelos
/// existem e salva uma captura em Temp/world3d.png para inspeção visual.
/// </summary>
public class World3DSmokeTest
{
    [UnityTest]
    public IEnumerator GridAndUnits_RenderAs3D()
    {
        // O overlay de arma em sprite loga erro de AnimatorController (problema antigo, fora do 3D).
        LogAssert.ignoreFailingMessages = true;
        var tuning = Resources.Load<GameTuning>("GameTuning");
        RuntimeTuning.Active = tuning;
        AttributeStats.Formulas = tuning.statFormulas;

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.10f, 0.11f, 0.15f);
        var ctrl = camGo.AddComponent<CameraController>();

        var grid = new GameObject("GridManager").AddComponent<GridManager>();
        grid.width = 12; grid.height = 12;
        grid.Build();
        // Platô e degraus para exercitar a altura.
        for (int x = 7; x < 10; x++)
        for (int y = 2; y < 5; y++)
            grid.SetCell(x, y, "stone_2", 2, "");
        grid.SetCell(6, 3, "stone_2", 1, "");
        grid.SetCell(2, 8, "water_full", 0, "");
        grid.SetCell(3, 8, "water_full", 0, "");

        ctrl.Configure(cam, grid.CellToWorld(new Vector2Int(5, 5)), 5.5f);

        var a = Spawn("Guerreiro", Team.Player, new Vector2Int(2, 3), new Color(0.2f, 0.45f, 0.9f), "Sprites/TinyTactics/Warrior", grid, "IronSword");
        Spawn("Arqueira", Team.Player, new Vector2Int(1, 6), new Color(0.2f, 0.45f, 0.9f), "Sprites/TinyTactics/Warrior", grid, "ShortBow");
        Spawn("Xama", Team.Player, new Vector2Int(3, 0), new Color(0.2f, 0.45f, 0.9f), "Sprites/TinyTactics/Warrior", grid, "WoodenStaff");
        var b = Spawn("Fera", Team.Enemy, new Vector2Int(6, 7), new Color(0.85f, 0.2f, 0.2f), "Sprites/TinyTactics/Warrior", grid, "");
        b.definitionId = "monster";
        grid.HighlightAnchors(new[] { new Vector2Int(4, 4), new Vector2Int(5, 4), new Vector2Int(4, 5) });

        for (int i = 0; i < 20; i++) yield return null;

        Assert.IsNotNull(GameObject.Find("Block"), "nenhum bloco 3D de terreno foi criado");
        Assert.IsNotNull(a.GetComponent<UnitModel3D>(), "unidade sem modelo 3D");

        var rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;
        string path = Path.Combine(Application.dataPath, "..", "Temp", "world3d.png");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Debug.Log("[World3DSmokeTest] captura: " + path);
    }

    private static Unit Spawn(string name, Team team, Vector2Int anchor, Color color, string path, GridManager grid, string weaponId)
    {
        var u = new GameObject(name).AddComponent<Unit>();
        u.unitName = name;
        u.team = team;
        u.weaponId = weaponId;
        if (name == "Fera") u.definitionId = "monster";
        u.stats = new UnitStatBlock().ToAttributeStats();
        u.Init(grid, anchor, color, path);
        return u;
    }
}
