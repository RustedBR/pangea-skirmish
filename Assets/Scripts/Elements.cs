using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Os seis elementos de Pangea (ver design/gdd/GDD-013-magic-lore.md).
    /// Físico é conjurado pelo Corpo (Instinto); os demais pela Mente (Mana).
    /// O sistema de conjuração foi removido para ser refeito a partir da lore.
    /// </summary>
    public enum SpellElement { None, Physical, Magic, Fire, Water, Air, Earth }

    /// <summary>Atributos base. Espelha os campos de AttributeStats.</summary>
    public enum Attr { STR, VIT, DEX, AGI, INT, WIS }

    public static class ElementInfo
    {
        public static string Name(SpellElement e)
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

        public static Color Color(SpellElement e)
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
                default:                    return UnityEngine.Color.white;
            }
        }
    }
}
