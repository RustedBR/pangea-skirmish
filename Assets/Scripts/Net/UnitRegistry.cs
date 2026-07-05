// Net/UnitRegistry.cs
// Registro estático de unidades por uint unitId (atribuído sequencialmente pelo host).
// Idêntico em todos os clientes após PlacementSync.SpawnUnitClientRpc.
// Limpo no início de cada batalha (chamado pelo GameBootstrap em modo MP).

using System.Collections.Generic;

namespace PangeaSkirmish
{
    public static class UnitRegistry
    {
        private static readonly Dictionary<uint, Unit> _byId    = new Dictionary<uint, Unit>();
        private static readonly Dictionary<Unit, uint> _reverse = new Dictionary<Unit, uint>();

        public static void Clear()
        {
            _byId.Clear();
            _reverse.Clear();
        }

        public static void Register(uint id, Unit unit)
        {
            _byId[id]     = unit;
            _reverse[unit] = id;
        }

        public static Unit Get(uint id)
        {
            _byId.TryGetValue(id, out var u);
            return u;
        }

        public static uint GetId(Unit unit)
        {
            _reverse.TryGetValue(unit, out var id);
            return id;
        }

        public static IEnumerable<Unit> AllUnits => _byId.Values;
    }
}
