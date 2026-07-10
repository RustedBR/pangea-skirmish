using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Registry estático de UnitDefinitions carregadas de Resources/Units.
    /// Usado pelo SaveSystem para recriar unidades a partir do unitId salvo.
    /// Indexa por unitId E por displayName (lowercase) como fallback.
    /// </summary>
    public static class UnitDefinitionRegistry
    {
        private static readonly Dictionary<string, UnitDefinition> _byId = new Dictionary<string, UnitDefinition>();
        private static bool _loaded;

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            var defs = Resources.LoadAll<UnitDefinition>("Units");
            foreach (var d in defs)
            {
                if (!string.IsNullOrEmpty(d.unitId))
                    _byId[d.unitId] = d;
                if (!string.IsNullOrEmpty(d.displayName))
                    _byId[d.displayName.ToLowerInvariant()] = d;
            }
        }

        public static UnitDefinition Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            EnsureLoaded();
            if (_byId.TryGetValue(key, out var def)) return def;
            // fallback case-insensitive
            _byId.TryGetValue(key.ToLowerInvariant(), out def);
            return def;
        }
    }
}
