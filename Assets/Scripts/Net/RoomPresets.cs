using System;
using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Presets nomeados da sala (2026-11-14): modo, regras e todo o tuning avançado, salvos no navegador
    /// do anfitrião (PlayerPrefs) para reusar em outras salas.
    /// </summary>
    public static class RoomPresets
    {
        private const string Key = "pangea.roompresets.v1";
        public const int MaxPresets = 20;

        [Serializable]
        public class Preset
        {
            public string name;
            public int gameMode, maxPlayers;
            public float planningTime;
            public string tuningJson;
        }

        [Serializable] private class Store { public List<Preset> presets = new List<Preset>(); }

        private static Store Load()
        {
            try { var s = JsonUtility.FromJson<Store>(PlayerPrefs.GetString(Key, "")); if (s?.presets != null) return s; }
            catch (Exception e) { Debug.LogWarning("[Presets] ilegível, começando do zero: " + e.Message); }
            return new Store();
        }

        private static void Write(Store s) { PlayerPrefs.SetString(Key, JsonUtility.ToJson(s)); PlayerPrefs.Save(); }

        public static List<Preset> All() => Load().presets;

        /// <summary>Salva (ou substitui, se o nome já existe). Devolve false se o nome está vazio ou a lista cheia.</summary>
        public static bool Save(string name, int gameMode, int maxPlayers, float planningTime, GameTuning tuning)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || tuning == null) return false;
            var s = Load();
            var p = s.presets.Find(x => string.Equals(x.name, name, StringComparison.OrdinalIgnoreCase));
            if (p == null)
            {
                if (s.presets.Count >= MaxPresets) return false;
                p = new Preset { name = name }; s.presets.Add(p);
            }
            p.gameMode = gameMode; p.maxPlayers = maxPlayers; p.planningTime = planningTime;
            p.tuningJson = JsonUtility.ToJson(tuning);
            Write(s);
            return true;
        }

        public static void Delete(string name)
        {
            var s = Load();
            s.presets.RemoveAll(x => string.Equals(x.name, name, StringComparison.OrdinalIgnoreCase));
            Write(s);
        }

        /// <summary>Tuning do preset sobre uma cópia do padrão (campos novos, que o preset não tinha, ficam no padrão).</summary>
        public static GameTuning TuningOf(Preset p)
        {
            var baseT = Resources.Load<GameTuning>("GameTuning");
            var t = baseT != null ? UnityEngine.Object.Instantiate(baseT) : ScriptableObject.CreateInstance<GameTuning>();
            if (p != null && !string.IsNullOrEmpty(p.tuningJson)) JsonUtility.FromJsonOverwrite(p.tuningJson, t);
            return t;
        }
    }
}
