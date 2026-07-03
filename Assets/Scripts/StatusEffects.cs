using System;
using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    public enum StatusEffectKind { PhysicalMight, MagicShield, ElementResist }

    [Serializable]
    public class StatusEffect
    {
        public StatusEffectKind Kind;
        public SpellElement Element;

        public int StrBonus;
        public int VitBonus;
        public int ShieldRemaining;
        public int ResistAmount;
        public int RoundsLeft;

        public bool IsExpired => RoundsLeft <= 0;
        public bool IsPhysicalMight => Kind == StatusEffectKind.PhysicalMight;
        public bool IsMagicShield => Kind == StatusEffectKind.MagicShield;
        public bool IsElementResist => Kind == StatusEffectKind.ElementResist;
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
                existing.ShieldRemaining = Mathf.Max(existing.ShieldRemaining, incoming.ShieldRemaining);
                existing.ResistAmount = Mathf.Max(existing.ResistAmount, incoming.ResistAmount);
                existing.RoundsLeft = Mathf.Max(existing.RoundsLeft, incoming.RoundsLeft);
            }
            else
            {
                list.Add(incoming);
            }
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

        public static int ConsumeStrBuffOnHit(List<StatusEffect> effects)
        {
            var fx = effects.Find(s => s.IsPhysicalMight && s.StrBonus > 0);
            if (fx == null) return 0;
            int bonus = fx.StrBonus;
            fx.StrBonus = 0;
            if (fx.VitBonus <= 0) fx.RoundsLeft = 0;
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
            }
            return string.Join(" | ", parts);
        }
    }
}