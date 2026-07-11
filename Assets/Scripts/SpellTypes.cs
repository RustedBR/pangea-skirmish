using System;
using UnityEngine;

namespace PangeaSkirmish
{
    public enum SpellElement { None, Physical, Magic, Fire, Water, Air, Earth }

    public enum SpellTargetKind { Self, Unit, Tile }

    /// <summary>Atributos usados por magias. Espelha os campos de AttributeStats.</summary>
    public enum Attr { STR, VIT, DEX, AGI, INT, WIS }

    [Serializable]
    public struct PlannedSpell
    {
        public SpellElement Element;
        public SpellTargetKind Target;
        public Unit TargetUnit;
        public Vector2Int TargetTile;
        // Duas pools de mana SEPARADAS:
        //  ManaRange = alcance (magia de dano/tile) OU duração em rounds (buff Self). Só custa mana.
        //  ManaPower = potência (multiplica dano/atributo). Cada ponto custa +1 PA + mana.
        public int ManaRange;
        public int ManaPower;
        public Vector2Int Direction;
    }

    public static class SpellBook
    {
        public static (float a, float b) AttributePair(Unit u, SpellElement e)
        {
            switch (e)
            {
                case SpellElement.Physical: return (u.EffectiveStat(Attr.DEX), u.EffectiveStat(Attr.STR));
                case SpellElement.Magic:    return (u.EffectiveStat(Attr.INT), u.EffectiveStat(Attr.WIS));
                case SpellElement.Fire:     return (u.EffectiveStat(Attr.INT), u.EffectiveStat(Attr.VIT));
                case SpellElement.Water:    return (u.EffectiveStat(Attr.VIT), u.EffectiveStat(Attr.INT));
                case SpellElement.Air:      return (u.EffectiveStat(Attr.AGI), u.EffectiveStat(Attr.INT));
                case SpellElement.Earth:    return (u.EffectiveStat(Attr.VIT), u.EffectiveStat(Attr.STR));
                default:                    return (0f, 0f);
            }
        }

        public static WeaponDef Conduit(Unit u) => WeaponCatalog.Get(u.weaponId);

        /// <summary>
        /// Cálculo de potência (decisão Marcus 2026-07-10):
        ///   Pot = manaPower × base × (A + B do elemento)
        ///   - PA NÃO entra dobrado (só 1 PA por ponto de potência, cobrado em outro lugar).
        ///   - Sem teto (nem dano, nem buff).
        ///   - base separado: dano = spellPotencyBaseDamage, buff = spellPotencyBaseBuff.
        ///   - manaPower já vem clampado ao PA disponível (PlanningController), então nunca excede.
        /// </summary>
        public static int DamagePotency(Unit caster, SpellElement e, int manaPower)
        {
            var T = Tuning.Get();
            var pair = AttributePair(caster, e);
            float attrSum = (pair.a + pair.b) + caster.plannedSpellBonusINT;
            float condMult = 1f;
            var cond = Conduit(caster);
            if (cond != null) condMult = cond.spellPowerMult;
            float affinity = 1f;
            if (cond != null && cond.elementAffinity == e) affinity = 1f + T.conduitAffinityBonus;
            float raw = Mathf.Max(1, manaPower) * T.spellPotencyBaseDamage * attrSum * condMult * affinity;
            return Mathf.Max(T.spellMinDamage, Mathf.RoundToInt(raw));
        }

        /// <summary>Mesma fórmula de DamagePotency, mas usa a base de BUFF (mais alta).</summary>
        public static int BuffPotency(Unit caster, SpellElement e, int manaPower)
        {
            var T = Tuning.Get();
            var pair = AttributePair(caster, e);
            float attrSum = (pair.a + pair.b) + caster.plannedSpellBonusINT;
            float condMult = 1f;
            var cond = Conduit(caster);
            if (cond != null) condMult = cond.spellPowerMult;
            float affinity = 1f;
            if (cond != null && cond.elementAffinity == e) affinity = 1f + T.conduitAffinityBonus;
            float raw = Mathf.Max(1, manaPower) * T.spellPotencyBaseBuff * attrSum * condMult * affinity;
            return Mathf.Max(1, Mathf.RoundToInt(raw));
        }

        public static int SpellRange(Unit caster, int manaRange)
        {
            var T = Tuning.Get();
            int range = manaRange * T.spellRangePerMana;
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