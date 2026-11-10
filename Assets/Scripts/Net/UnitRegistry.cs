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

        // Fix (2026-07-29): Register só é chamado no caminho de rede
        // (PlacementSync.SpawnUnitClientRpc). Unidades criadas pelo Sandbox, por
        // UnitDefinition.SpawnUnit ou pelo SaveSystem nunca entravam no registro —
        // e como a rotação de vista iterava AllUnits, apertar Q/E no editor de
        // mapa movia os tiles e deixava TODAS as unidades paradas no lugar antigo.
        // Esta lista rastreia toda Unit viva, com ou sem id de rede; é a fonte
        // usada pela rotação. _byId continua exclusivo da rede (o hash de lockstep
        // depende dele), então o comportamento em MP não muda.
        private static readonly List<Unit> _tracked = new List<Unit>();

        public static void Clear()
        {
            _byId.Clear();
            _reverse.Clear();
            _tracked.RemoveAll(u => u == null);
        }

        /// <summary>Passa a rastrear a unidade para efeitos de vista (chamado em Unit.Init).</summary>
        public static void Track(Unit unit)
        {
            if (unit != null && !_tracked.Contains(unit)) _tracked.Add(unit);
        }

        /// <summary>Para de rastrear (chamado em Unit.OnDestroy).</summary>
        public static void Untrack(Unit unit) => _tracked.Remove(unit);

        /// <summary>
        /// Toda unidade viva na cena, tenha ou não id de rede. Use isto para
        /// efeitos puramente visuais (rotação de vista); para qualquer coisa que
        /// dependa de identidade de rede, use AllUnits.
        /// </summary>
        public static IEnumerable<Unit> Tracked
        {
            get
            {
                for (int i = _tracked.Count - 1; i >= 0; i--)
                    if (_tracked[i] == null) _tracked.RemoveAt(i);
                return _tracked;
            }
        }

        public static void Register(uint id, Unit unit)
        {
            _byId[id]     = unit;
            _reverse[unit] = id;
        }

        /// <summary>Remove uma unidade do registro de ids (ex.: fera morta ao trocar de camada).</summary>
        public static void Unregister(Unit unit)
        {
            uint id = GetId(unit);
            if (id != 0) _byId.Remove(id);
            _reverse.Remove(unit);
            _tracked.Remove(unit);
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

        /// <summary>
        /// Caminho 1 (2026-07-14): a vista da câmera virou 90° (degrees = 0/90/180/270).
        /// Re-deriva o facing de TODAS as unidades para encarar o novo "norte" da tela,
        /// usando as 4 direções que o motor já suporta (NE/SE + flipX). Chamado pelo
        /// CameraController.CycleView/SetViewOrientation.
        /// </summary>
        public static void ApplyViewOrientation(int degrees)
        {
            // Tracked (não _byId): inclui unidades sem id de rede, como as do Sandbox.
            foreach (var u in Tracked)
                u.SetViewOrientation(degrees);
        }
    }
}
