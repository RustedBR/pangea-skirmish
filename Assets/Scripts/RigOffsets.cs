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
            public string model;     // corpo (Warrior, Mage...) ou par corpo+item nos acessórios ("Warrior+IronAxe")
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

        /// <summary>Chave do par corpo + item (arma, mão esquerda, escudo).</summary>
        public static string PairKey(string body, string item) => string.IsNullOrEmpty(item) ? body : body + "+" + item;

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

        private static Matrix4x4 Trs(Entry e) => e == null ? Matrix4x4.identity : Matrix4x4.TRS(e.position, Quaternion.Euler(e.euler), e.scale);

        /// <summary>
        /// Ajuste de um acessório (arma, mão esquerda, escudo) no corpo <paramref name="body"/> (2026-11-15).
        /// Com ajuste próprio do par corpo+item, usa ele. Sem, espelha o item de outro corpo — primeiro o corpo
        /// padrão da arma — mantendo a mesma posição em relação à mão: o braço ajustado de cada corpo leva a arma junto.
        /// Devolve de onde veio ("" = sem ajuste, a pose padrão).
        /// </summary>
        /// <summary>Ajuste do braço do corpo com esta arma na mão: o do par corpo+arma, se houver, senão o do corpo.</summary>
        public Entry ArmFor(string body, string mainWeapon, string armPart) =>
            (string.IsNullOrEmpty(mainWeapon) ? null : Find(PairKey(body, mainWeapon), armPart)) ?? Find(body, armPart);

        public string ResolveAccessory(string body, string item, string part, string armPart, string preferredSource,
                                       string mainWeapon, out Vector3 pos, out Quaternion rot, out Vector3 scale)
        {
            pos = Vector3.zero; rot = Quaternion.identity; scale = Vector3.one;
            var own = Find(PairKey(body, item), part);
            if (own != null) { pos = own.position; rot = Quaternion.Euler(own.euler); scale = own.scale; return PairKey(body, item); }
            Entry src = null; string srcBody = null;
            if (!string.IsNullOrEmpty(preferredSource) && preferredSource != body)
            { src = Find(PairKey(preferredSource, item), part); if (src != null) srcBody = preferredSource; }
            if (src == null)
                foreach (var e in entries)
                    if (e.part == part && e.model.EndsWith("+" + item) && !e.model.StartsWith(body + "+"))
                    { src = e; srcBody = e.model.Substring(0, e.model.Length - item.Length - 1); break; }
            if (src == null) return "";
            // Mesma pose em relação ao braço: alvo = braçoAlvo · braçoOrigem⁻¹ · armaOrigem (todos no espaço do ombro).
            // Braço de origem: o da arma com que a origem foi ajustada (a própria, se for a arma principal).
            var srcArm = ArmFor(srcBody, part == "Weapon" ? item : null, armPart);
            var m = Trs(ArmFor(body, mainWeapon, armPart)) * Trs(srcArm).inverse * Trs(src);
            pos = m.GetColumn(3);
            rot = m.rotation;
            scale = m.lossyScale;
            return PairKey(srcBody, item);
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
