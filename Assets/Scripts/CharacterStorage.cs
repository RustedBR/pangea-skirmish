using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>Persistência de personagens salvos (presets) em JSON, um arquivo por preset.</summary>
    public static class CharacterStorage
    {
        private static string Dir => Path.Combine(Application.persistentDataPath, "Characters");

        public static void EnsureDir() { if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir); }
        public static string PathFor(string presetName) => Path.Combine(Dir, Sanitize(presetName) + ".json");

        public static void Save(CharacterPreset preset)
        {
            EnsureDir();
            File.WriteAllText(PathFor(preset.presetName), JsonUtility.ToJson(preset, true));
            Debug.Log("[CharacterStorage] Salvo: " + PathFor(preset.presetName));
        }

        public static List<CharacterPreset> LoadAll()
        {
            EnsureDir();
            var list = new List<CharacterPreset>();
            foreach (var f in Directory.GetFiles(Dir, "*.json"))
            {
                try
                {
                    var p = JsonUtility.FromJson<CharacterPreset>(File.ReadAllText(f));
                    if (p != null)
                    {
                        // Migração: preset antigo com classId mas sem spritePath → derivar sprite
                        if (string.IsNullOrEmpty(p.spritePath) && !string.IsNullOrEmpty(p.classId))
                            p.spritePath = CharacterSpriteCatalog.LegacySprite(p.classId);
                        // Fallback final: garante que spritePath nunca fica vazio
                        if (string.IsNullOrEmpty(p.spritePath))
                            p.spritePath = CharacterSpriteCatalog.Default;
                        list.Add(p);
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[CharacterStorage] Ignorando preset inválido '" + f + "': " + e.Message);
                }
            }
            return list;
        }

        public static void Delete(string presetName)
        {
            string p = PathFor(presetName);
            if (File.Exists(p)) File.Delete(p);
        }

        private static string Sanitize(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "personagem" : name;
        }
    }
}
