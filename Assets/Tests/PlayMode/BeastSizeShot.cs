using System.Collections;
using System.IO;
using NUnit.Framework;
using PangeaSkirmish;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>Feras 1×1, 2×2 e 4×4 ao lado de um herói 3×3: o corpo acompanha o tamanho. Salva docs/beast-sizes.png.</summary>
public class BeastSizeShot
{
    [UnityTest]
    public IEnumerator BeastBodies_FollowTheirFootprint()
    {
        LogAssert.ignoreFailingMessages = true;
        foreach (var o in Object.FindObjectsByType<Unit>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        foreach (var o in Object.FindObjectsByType<GridManager>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        foreach (var o in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) Object.Destroy(o.gameObject);
        yield return null;
        var t = Resources.Load<GameTuning>("GameTuning");
        RuntimeTuning.Active = t; AttributeStats.Formulas = t.statFormulas;
        var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.10f, 0.11f, 0.15f);
        var ctrl = cam.gameObject.AddComponent<CameraController>();
        var grid = new GameObject("GridManager").AddComponent<GridManager>();
        grid.width = 22; grid.height = 12; grid.Build();
        ctrl.Configure(cam, grid.CellToWorld(new Vector2Int(10, 6)), 6.5f);

        Unit Make(string n, Vector2Int a, CharacterPreset p, Team team, string def)
        {
            var u = new GameObject(n).AddComponent<Unit>();
            u.unitName = n; u.team = team; u.definitionId = def; u.weaponId = p.weaponId;
            u.stats = p.stats.ToAttributeStats();
            u.Init(grid, a, team == Team.Player ? t.playerTeamColor : t.enemyTeamColor, "Sprites/TinyTactics/Warrior");
            return u;
        }
        var hero = Make("Herói", new Vector2Int(2, 4), new CharacterPreset { weaponId = "IronSword", stats = new UnitStatBlock { STR = 5, VIT = 5, Footprint = 3 } }, Team.Player, "warrior");
        var s = Make("Fera Pequena", new Vector2Int(7, 5), BeastAI.BuildPreset(0, 1, 30, BeastAI.BeastSize.Small), Team.Enemy, "monster");
        var m = Make("Fera Média", new Vector2Int(10, 5), BeastAI.BuildPreset(1, 1, 30, BeastAI.BeastSize.Medium), Team.Enemy, "monster");
        var l = Make("Fera Grande", new Vector2Int(14, 4), BeastAI.BuildPreset(2, 1, 30, BeastAI.BeastSize.Large), Team.Enemy, "monster");
        for (int i = 0; i < 6; i++) yield return null;
        Assert.Less(s.Model3D.ModelScale, m.Model3D.ModelScale, "pequena menor que a média");
        Assert.Less(m.Model3D.ModelScale, l.Model3D.ModelScale, "média menor que a grande");

        var rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
        RenderTexture.active = null; cam.targetTexture = null;
        File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "docs", "beast-sizes.png"), tex.EncodeToPNG());
        foreach (var u in new[] { hero, s, m, l }) Object.Destroy(u.gameObject);
    }
}
