using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>Categorias de tamanho (Pathfinder 2e, Table 9-1: Size and Reach).</summary>
    public enum SizeCategory { Tiny, Small, Medium, Large, Huge, Gargantuan }

    /// <summary>
    /// Tamanho com peso na gameplay (GDD-028 extensão). Base PF2e adaptada à escala ×3:
    /// Mapeamento definido pelo Marcus: Tiny 1×1, Small 2×2, Medium 3×3, Large 4×4, Huge 5×5, Gargantuan 6×6.
   /// FP2e Table 9-1 serve de referência para reach (Large +1, Huge +2, Gargantuan +3).
    ///
    /// Efeitos (maior = mais resistente e mais alcance, porém mais lento e mais fácil de acertar):
    ///   HP por VIT   → multiplicador
    ///   Alcance      → bônus em tiles (PF2e: Large +1, Huge +2, Gargantuan +3; Tiny −1)
    ///   Esquiva      → multiplicador (inverso ao tamanho)
    /// </summary>
    public static class UnitSize
    {
        public struct Row
        {
            public SizeCategory category;
            public int footprint;
            public float hpPerVit;
            public int reach;
            public float dodge;
        }

        // Ordenado por footprint. Of() usa a última linha cujo footprint <= valor (interpolação conservadora).
        // Footprint: Tiny 1×1, Small 2×2, Medium 3×3, Large 4×4, Huge 5×5, Gargantuan 6×6.
        // HP por VIT = 1 por lado do quadrado (Tiny 1, Small 2, Medium 3, Large 4, Huge 5, Gargantuan 6).
        public static readonly Row[] Table =
        {
            new Row { category = SizeCategory.Tiny,       footprint = 1, hpPerVit = 1f, reach = 0, dodge = 3.00f },
            new Row { category = SizeCategory.Small,      footprint = 2, hpPerVit = 2f, reach = 1, dodge = 2.00f },
            new Row { category = SizeCategory.Medium,     footprint = 3, hpPerVit = 3f, reach = 1, dodge = 1.00f },
            new Row { category = SizeCategory.Large,      footprint = 4, hpPerVit = 4f, reach = 2, dodge = 0.50f },
            new Row { category = SizeCategory.Huge,       footprint = 5, hpPerVit = 5f, reach = 3, dodge = 0.40f },
            new Row { category = SizeCategory.Gargantuan, footprint = 6, hpPerVit = 6f, reach = 4, dodge = 0.33f },
        };

        public static Row Of(int footprint)
        {
            int fp = Mathf.Max(1, footprint);
            Row best = Table[0];
            for (int i = 0; i < Table.Length; i++)
                if (fp >= Table[i].footprint) best = Table[i];
                else break;
            return best;
        }

        public static SizeCategory Category(int footprint) => Of(footprint).category;
        public static string Name(int footprint) => Of(footprint).category.ToString();
    }
}
