using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace PangeaSkirmish.EditorTools
{
    /// <summary>
    /// Símbolos Unicode viram sprites em todo texto da interface (2026-11-13).
    /// Monta um atlas com os ícones de Resources/UI/Icons, cria um SpriteAsset do TextCore em que
    /// cada símbolo (⚔ ◈ ✦ → − …) aponta para um ícone, e liga como sprite padrão/fallback do
    /// PanelTextSettings da PangeaPanelSettings. A fonte do WebGL não tem esses símbolos.
    /// Rode de novo (menu Pangea) quando mudar ícones ou o mapa abaixo.
    /// </summary>
    public static class TextIconAssetBuilder
    {
        public static (string sym, string icon)[] Map => TextIcons.Map;

        const string Dir = "Assets/Resources/UI/TextIcons";
        const int Cell = 64, Cols = 8;

        [MenuItem("Pangea/Gerar sprites de texto (símbolos)")]
        public static string Build()
        {
            Directory.CreateDirectory(Dir);
            var icons = new List<string>();
            foreach (var (_, ic) in Map) if (!icons.Contains(ic)) icons.Add(ic);
            int rows = (icons.Count + Cols - 1) / Cols;
            var atlas = new Texture2D(Cols * Cell, rows * Cell, TextureFormat.RGBA32, false);
            atlas.SetPixels32(new Color32[atlas.width * atlas.height]);
            var rect = new Dictionary<string, GlyphRect>();
            for (int i = 0; i < icons.Count; i++)
            {
                var png = new Texture2D(2, 2);
                png.LoadImage(File.ReadAllBytes($"Assets/Resources/UI/Icons/{icons[i]}.png"));
                int x = (i % Cols) * Cell, y = atlas.height - (i / Cols + 1) * Cell;
                atlas.SetPixels(x, y, Cell, Cell, png.GetPixels());
                rect[icons[i]] = new GlyphRect(x, y, Cell, Cell);
                Object.DestroyImmediate(png);
            }
            atlas.Apply();
            string atlasPath = Dir + "/TextIconsAtlas.png";
            File.WriteAllBytes(atlasPath, atlas.EncodeToPNG());
            Object.DestroyImmediate(atlas);
            AssetDatabase.ImportAsset(atlasPath);
            var ti = (TextureImporter)AssetImporter.GetAtPath(atlasPath);
            ti.textureType = TextureImporterType.Default; ti.filterMode = FilterMode.Point; ti.mipmapEnabled = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed; ti.alphaIsTransparency = true; ti.npotScale = TextureImporterNPOTScale.None;
            ti.SaveAndReimport();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);

            string saPath = Dir + "/TextIcons.asset";
            var sa = AssetDatabase.LoadAssetAtPath<SpriteAsset>(saPath);
            if (sa == null) { sa = ScriptableObject.CreateInstance<SpriteAsset>(); AssetDatabase.CreateAsset(sa, saPath); }
            // spriteSheet é só leitura: grava o campo serializado que guarda a textura
            foreach (var f in typeof(SpriteAsset).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                if (typeof(Texture).IsAssignableFrom(f.FieldType)) f.SetValue(sa, tex);
            var mat = sa.material;
            if (mat == null) { mat = new Material(Shader.Find("Hidden/TextCore/Sprite")) { name = "TextIcons Material" }; AssetDatabase.AddObjectToAsset(mat, sa); sa.material = mat; }
            mat.mainTexture = tex;
            sa.spriteGlyphTable.Clear(); sa.spriteCharacterTable.Clear();
            var glyphIdx = new Dictionary<string, uint>();
            foreach (var ic in icons)
            {
                uint idx = (uint)sa.spriteGlyphTable.Count;
                // métrica: quadrado do tamanho de uma linha; desce um pouco para alinhar ao texto
                var g = new SpriteGlyph(idx, new GlyphMetrics(Cell, Cell, 0, Cell * 0.80f, Cell + 6), rect[ic], 1.6f, 0);
                sa.spriteGlyphTable.Add(g);
                glyphIdx[ic] = idx;
            }
            foreach (var (sym, ic) in Map)
            {
                uint cp = (uint)char.ConvertToUtf32(sym, 0);
                var sc = new SpriteCharacter(cp, sa, sa.spriteGlyphTable[(int)glyphIdx[ic]]) { name = ic };
                sa.spriteCharacterTable.Add(sc);
            }
            // também por nome: <sprite name="dmg">
            sa.UpdateLookupTables();
            EditorUtility.SetDirty(sa);

            // PanelTextSettings da HUD e menus
            var ps = Resources.Load<PanelSettings>("UI/PangeaPanelSettings");
            string tsPath = "Assets/Resources/UI/PangeaTextSettings.asset";
            var ts = ps.textSettings != null ? ps.textSettings : AssetDatabase.LoadAssetAtPath<PanelTextSettings>(tsPath);
            if (ts == null) { ts = ScriptableObject.CreateInstance<PanelTextSettings>(); AssetDatabase.CreateAsset(ts, tsPath); }
            ts.defaultSpriteAsset = sa;
            if (ts.emojiFallbackTextAssets == null) ts.emojiFallbackTextAssets = new List<UnityEngine.TextCore.Text.TextAsset>();
            if (!ts.emojiFallbackTextAssets.Contains(sa)) ts.emojiFallbackTextAssets.Add(sa);
            EditorUtility.SetDirty(ts);
            ps.textSettings = ts; EditorUtility.SetDirty(ps);
            AssetDatabase.SaveAssets();
            return $"{icons.Count} ícones, {Map.Length} símbolos → {saPath}; textSettings={AssetDatabase.GetAssetPath(ts)}";
        }
    }
}
