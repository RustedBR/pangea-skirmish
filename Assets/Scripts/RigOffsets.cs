using System;
using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Ajustes de rig feitos à mão no editor (GDD-025, 2026-11-14): para cada modelo e cada peça
    /// (cabeça, braços, pernas, arma, escudo...), um deslocamento de posição, rotação e escala aplicado
    /// a um nó "Rig" que fica entre a articulação animada e a peça. A animação gira a articulação; o
    /// nó Rig nunca é tocado por ela, então o ajuste soma com qualquer animação.
    /// Fica em Resources/RigOffsets.asset e vale no jogo inteiro (build incluída).
    /// </summary>
    [CreateAssetMenu(menuName = "Pangea Skirmish/Rig Offsets", fileName = "RigOffsets")]
    public class RigOffsets : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string model;     // Warrior, Mage, Monster... (ModelName do UnitModel3D)
            public string part;      // Head, ArmL, ArmR, LegL, LegR, Tail, Weapon, Offhand, Shield...
            public Vector3 position;
            public Vector3 euler;
            public Vector3 scale = Vector3.one;
        }

        public List<Entry> entries = new List<Entry>();

        private static RigOffsets _cached;
        public static RigOffsets Get()
        {
            if (_cached == null) _cached = Resources.Load<RigOffsets>("RigOffsets");
            return _cached;
        }

        /// <summary>O editor criou o asset agora: passa a usar este.</summary>
        public static void SetCache(RigOffsets asset) { _cached = asset;
        }

        public Entry Find(string model, string part) =>
            entries.Find(e => e.model == model && e.part == part);

        /// <summary>Grava (ou substitui) o ajuste de uma peça. Ajuste nulo apaga a entrada.</summary>
        public void Set(string model, string part, Vector3 pos, Vector3 euler, Vector3 scale)
        {
            var e = Find(model, part);
            bool identity = pos.sqrMagnitude < 1e-8f && euler.sqrMagnitude < 1e-6f && (scale - Vector3.one).sqrMagnitude < 1e-8f;
            if (identity) { if (e != null) entries.Remove(e); return; }
            if (e == null) { e = new Entry { model = model, part = part }; entries.Add(e); }
            e.position = pos; e.euler = euler; e.scale = scale;
        }

        /// <summary>Aplica o ajuste salvo no nó Rig (ou zera, se não há ajuste).</summary>
        public static void Apply(Transform rig, string model, string part)
        {
            if (rig == null) return;
            var e = Get()?.Find(model, part);
            rig.localPosition = e != null ? e.position : Vector3.zero;
            rig.localRotation = e != null ? Quaternion.Euler(e.euler) : Quaternion.identity;
            rig.localScale = e != null ? e.scale : Vector3.one;
        }
    }
}
