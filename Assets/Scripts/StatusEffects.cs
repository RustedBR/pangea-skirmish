using System;
using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    public enum StatusEffectKind { PhysicalMight, MagicShield, ElementResist, AttrBuff }

    [Serializable]
    public class StatusEffect
    {
        public StatusEffectKind Kind;
        public SpellElement Element;

        // Bônus de atributo (para AttrBuff — buffs Self rework).
        // Cada elemento buffa os 2 atributos do seu par (ver SpellBook.AttributePair).
        public int StrBonus;
        public int VitBonus;
        public int DexBonus;
        public int AgiBonus;
        public int IntBonus;
        public int WisBonus;

        // Legados (ainda usados por MagicShield / ElementResist durante transição).
        public int ShieldRemaining;
        public int ResistAmount;
        public int RoundsLeft;

        public bool IsExpired => RoundsLeft <= 0;
        public bool IsPhysicalMight => Kind == StatusEffectKind.PhysicalMight;
        public bool IsMagicShield => Kind == StatusEffectKind.MagicShield;
        public bool IsElementResist => Kind == StatusEffectKind.ElementResist;
        public bool IsAttrBuff => Kind == StatusEffectKind.AttrBuff;
    }

    public static class StatusEffectSystem
    {
        public static void AddOrReplace(List<StatusEffect> list, StatusEffect incoming)
        {
            var existing = list.Find(s => s.Kind == incoming.Kind && s.Element == incoming.Element);
            if (existing != null)
            {
                existing.StrBonus = Mathf.Max(existing.StrBonus, incoming.StrBonus);
                existing.VitBonus = Mathf.Max(existing.VitBonus, incoming.VitBonus);
                existing.DexBonus = Mathf.Max(existing.DexBonus, incoming.DexBonus);
                existing.AgiBonus = Mathf.Max(existing.AgiBonus, incoming.AgiBonus);
                existing.IntBonus = Mathf.Max(existing.IntBonus, incoming.IntBonus);
                existing.WisBonus = Mathf.Max(existing.WisBonus, incoming.WisBonus);
                existing.ShieldRemaining = Mathf.Max(existing.ShieldRemaining, incoming.ShieldRemaining);
                existing.ResistAmount = Mathf.Max(existing.ResistAmount, incoming.ResistAmount);
                existing.RoundsLeft = Mathf.Max(existing.RoundsLeft, incoming.RoundsLeft);
            }
            else
            {
                list.Add(incoming);
            }
        }

        /// <summary>Soma de todos os bônus de um atributo entre os buffs ativos.</summary>
        public static int AttrBonus(List<StatusEffect> effects, Attr attr)
        {
            int sum = 0;
            foreach (var fx in effects)
            {
                if (!fx.IsAttrBuff || fx.IsExpired) continue;
                switch (attr)
                {
                    case Attr.STR: sum += fx.StrBonus; break;
                    case Attr.VIT: sum += fx.VitBonus; break;
                    case Attr.DEX: sum += fx.DexBonus; break;
                    case Attr.AGI: sum += fx.AgiBonus; break;
                    case Attr.INT: sum += fx.IntBonus; break;
                    case Attr.WIS: sum += fx.WisBonus; break;
                }
            }
            return sum;
        }

        public static int ReduceIncomingDamage(List<StatusEffect> effects, int amount)
        {
            int reduced = amount;
            foreach (var fx in effects)
            {
                if (fx.IsPhysicalMight && fx.VitBonus > 0)
                {
                    int absorb = Mathf.Min(fx.VitBonus, reduced);
                    fx.VitBonus -= absorb;
                    reduced -= absorb;
                    if (fx.VitBonus <= 0 && fx.StrBonus <= 0) fx.RoundsLeft = 0;
                    if (reduced <= 0) break;
                }
            }
            return Mathf.Max(0, reduced);
        }

        // Mantido por compatibilidade (PhysicalMight antigo consumia STR no hit).
        // No rework os buffs de atributo NÃO são consumidos por hit — duram os rounds inteiros.
        public static int ConsumeStrBuffOnHit(List<StatusEffect> effects)
        {
            int bonus = 0;
            foreach (var fx in effects)
            {
                if (fx.IsPhysicalMight && fx.StrBonus > 0)
                {
                    bonus += fx.StrBonus;
                    fx.StrBonus = 0;
                    if (fx.VitBonus <= 0) fx.RoundsLeft = 0;
                }
            }
            return bonus;
        }

        public static int ElementResist(List<StatusEffect> effects, SpellElement e)
        {
            var fx = effects.Find(s => s.IsElementResist && s.Element == e);
            return fx != null ? fx.ResistAmount : 0;
        }

        public static int AbsorbWithShield(List<StatusEffect> effects, int amount)
        {
            var fx = effects.Find(s => s.IsMagicShield && s.ShieldRemaining > 0);
            if (fx == null) return amount;
            int absorb = Mathf.Min(fx.ShieldRemaining, amount);
            fx.ShieldRemaining -= absorb;
            if (fx.ShieldRemaining <= 0) fx.RoundsLeft = 0;
            return amount - absorb;
        }

        public static void TickEndOfRound(List<StatusEffect> effects)
        {
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                effects[i].RoundsLeft--;
                if (effects[i].IsExpired) effects.RemoveAt(i);
            }
        }

        public static string Summary(List<StatusEffect> effects)
        {
            if (effects == null || effects.Count == 0) return "";
            var parts = new List<string>();
            foreach (var fx in effects)
            {
                if (fx.IsPhysicalMight) parts.Add($"Poder Físico (+{fx.StrBonus}STR/+{fx.VitBonus}VIT, {fx.RoundsLeft}r)");
                else if (fx.IsMagicShield) parts.Add($"Escudo Mágico ({fx.ShieldRemaining}, {fx.RoundsLeft}r)");
                else if (fx.IsElementResist) parts.Add($"Resist {SpellBook.ElementName(fx.Element)} ({fx.ResistAmount}, {fx.RoundsLeft}r)");
                else if (fx.IsAttrBuff) parts.Add($"Buff {SpellBook.ElementName(fx.Element)} (r{fx.RoundsLeft})");
            }
            return string.Join(" | ", parts);
        }
    }
}