using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PangeaSkirmish
{
    public static class MapStorage
    {
        private static string Dir => Path.Combine(Application.persistentDataPath, "Maps");

        public static void EnsureDir() { if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir); }
        public static string PathFor(string mapName) => Path.Combine(Dir, Sanitize(mapName) + ".json");

        public static void Save(MapData map)
        {
            EnsureDir();
            File.WriteAllText(PathFor(map.mapName), JsonUtility.ToJson(map, true));
            Debug.Log("[MapStorage] Salvo: " + PathFor(map.mapName));
        }

        public static MapData Load(string mapName)
        {
            string p = PathFor(mapName);
            if (!File.Exists(p)) return null;
            try { return JsonUtility.FromJson<MapData>(File.ReadAllText(p)); }
            catch (System.Exception e)
            {
                Debug.LogWarning("[MapStorage] Mapa inválido '" + p + "': " + e.Message);
                return null;
            }
        }

        public static List<string> ListMapNames()
        {
            EnsureDir();
            var names = new List<string>();
            foreach (var f in Directory.GetFiles(Dir, "*.json"))
                names.Add(Path.GetFileNameWithoutExtension(f));
            return names;
        }

        public static void Delete(string mapName)
        { string p = PathFor(mapName); if (File.Exists(p)) File.Delete(p); }

        private static string Sanitize(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "mapa" : name;
        }
    }
}
