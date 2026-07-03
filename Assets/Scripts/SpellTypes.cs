using System;
using UnityEngine;

namespace PangeaSkirmish
{
    public enum SpellElement { None, Physical, Magic, Fire, Water, Air, Earth }

    public enum SpellTargetKind { Self, Unit, Tile }

    [Serializable]
    public struct PlannedSpell
    {
        public SpellElement Element;
        public SpellTargetKind Target;
        public Unit TargetUnit;
        public Vector2Int TargetTile;
        public int Mana;
        public Vector2Int Direction;
    }

    public static class SpellBook
    {
        public static (float a, float b) AttributePair(AttributeStats stats, SpellElement e)
        {
            switch (e)
            {
                case SpellElement.Physical: return (stats.DEX, stats.STR);
                case SpellElement.Magic:    return (stats.INT, stats.WIS);
                case SpellElement.Fire:     return (stats.INT, stats.VIT);
                case SpellElement.Water:    return (stats.VIT, stats.INT);
                case SpellElement.Air:      return (stats.AGI, stats.INT);
                case SpellElement.Earth:    return (stats.VIT, stats.STR);
                default:                    return (0f, 0f);
            }
        }

        public static WeaponDef Conduit(Unit u) => WeaponCatalog.Get(u.weaponId);

        public static int Potency(Unit caster, SpellElement e)
        {
            var T = Tuning.Get();
            var pair = AttributePair(caster.stats, e);
            float basePotency = T.BasePotencyForElement(e);
            var cond = Conduit(caster);
            float condMult = 1f;
            if (cond != null) condMult = cond.spellPowerMult;
            float affinity = 1f;
            if (cond != null && cond.elementAffinity == e) affinity = 1f + T.conduitAffinityBonus;
            float raw = (pair.a + pair.b) * basePotency * condMult * affinity;
            return Mathf.Max(T.spellMinDamage, Mathf.RoundToInt(raw));
        }

        public static int SpellRange(Unit caster, int mana)
        {
            var T = Tuning.Get();
            int range = mana * T.spellRangePerMana;
            var cond = Conduit(caster);
            if (cond != null) range += cond.spellRangeBonus;
            return range;
        }

        public static string ElementName(SpellElement e)
        {
            switch (e)
            {
                case SpellElement.Physical: return "Físico";
                case SpellElement.Magic:    return "Mágico";
                case SpellElement.Fire:     return "Fogo";
                case SpellElement.Water:    return "Água";
                case SpellElement.Air:      return "Ar";
                case SpellElement.Earth:    return "Terra";
                default:                    return "Nenhum";
            }
        }

        public static Color ElementColor(SpellElement e)
        {
            var T = Tuning.Get();
            switch (e)
            {
                case SpellElement.Physical: return T.elemPhysicalColor;
                case SpellElement.Magic:    return T.elemMagicColor;
                case SpellElement.Fire:     return T.elemFireColor;
                case SpellElement.Water:    return T.elemWaterColor;
                case SpellElement.Air:      return T.elemAirColor;
                case SpellElement.Earth:    return T.elemEarthColor;
                default:                    return Color.white;
            }
        }
    }
}