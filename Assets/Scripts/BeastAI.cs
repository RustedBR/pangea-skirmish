using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// IA das feras do Coop (GDD-015 §4): simples, previsível e DETERMINÍSTICA.
    ///
    /// Não trafega plano pela rede: cada máquina chama Plan() no mesmo ponto da simulação,
    /// com o mesmo estado, e obtém a mesma jogada. Por isso só usa dados da batalha
    /// (âncoras, stats, ids) e nenhum tempo real, input ou UnityEngine.Random.
    ///
    /// Regra base: avançar até o inimigo mais próximo; atacar quando estiver no alcance.
    /// </summary>
    public static class BeastAI
    {
        /// <summary>Dono fictício das unidades controladas pela IA (nenhum cliente real usa este id).</summary>
        public const ulong OwnerId = ulong.MaxValue;
        /// <summary>Time das feras no Coop (os jogadores são o time 0).</summary>
        public const int TeamId = 1;

        public static bool Controls(Unit u) => u != null && u.ownerId == OwnerId;

        /// <summary>
        /// Preenche o plano de uma fera livre: Investir contra o hostil mais próximo, com fila
        /// curta (1 andada + 1 ataque) para reagir ao jogador a cada ponto de decisão.
        /// Usa o mesmo montador de intenções do jogador (IntentBuilder).
        /// </summary>
        public static void Plan(Unit self, List<Unit> units, GridManager grid)
        {
            if (self == null || self.IsDead || grid == null) return;
            var target = NearestHostile(self, units, self.anchor);
            if (target == null) { self.ResetPlan(); return; } // sem alvo: fila vira "Esperar"
            IntentBuilder.Charge(self, target, units, grid, maxMoves: 1, maxAttacks: 1);
        }

        private static int Gap(Vector2Int from, int fp, Unit target) =>
            GridManager.FootprintGap(from, fp, target.anchor, target.stats.Footprint);

        /// <summary>Hostil vivo mais próximo; empate pelo menor id de unidade.</summary>
        private static Unit NearestHostile(Unit self, List<Unit> units, Vector2Int from)
        {
            Unit best = null;
            int bestGap = int.MaxValue;
            uint bestId = uint.MaxValue;
            foreach (var u in units)
            {
                if (u == self || u.IsDead || !u.IsHostileTo(self)) continue;
                int g = Gap(from, self.stats.Footprint, u);
                uint id = UnitRegistry.GetId(u);
                if (g < bestGap || (g == bestGap && id < bestId)) { best = u; bestGap = g; bestId = id; }
            }
            return best;
        }

        // ------------------------------------------------------- TAMANHOS (teste 2026-11-13)
        // Pequena 1x1 (fraca, stats de hoje), Media 2x2 (~2x HP/dano), Grande 4x4 (~4x + alcance 2).
        // Rapidez (DEX/AGI) e IGUAL entre tamanhos: fera grande e forte, nao rapida.

        public enum BeastSize { Small, Medium, Large }

        /// <summary>Tamanho da fera n. <paramref name="index"/> na camada <paramref name="layer"/>:
        /// alterna com a camada (cada camada comeca num tamanho diferente, garantindo mistura).</summary>
        public static BeastSize SizeFor(int index, int layer) => (BeastSize)((index + layer) % 3);

        public static int FootprintOf(BeastSize s) => s == BeastSize.Small ? 1 : s == BeastSize.Medium ? 2 : 4;

        private static string SizeName(BeastSize s) =>
            s == BeastSize.Small ? "Fera Pequena" : s == BeastSize.Medium ? "Fera Média" : "Fera Grande";

        /// <summary>Ficha de uma fera para a camada/grupo dados (GDD-015 §3.3 + tamanhos).</summary>
        public static CharacterPreset BuildPreset(int index, int layer, int attributeBudget,
                                                  BeastSize? size = null, int footprintOverride = 0)
        {
            var sz = size ?? BeastSize.Medium;
            int fp = footprintOverride > 0 ? footprintOverride : FootprintOf(sz);
            // Orçamento um pouco abaixo do dos jogadores na camada 1, crescendo por camada.
            float pts = Mathf.Max(6f, attributeBudget * (0.55f + 0.10f * (layer - 1)));
            // Multiplicador de FORÇA por tamanho: pequeno = stats de hoje, media ~ 2x, grande ~ 4x.
            int mul = sz == BeastSize.Small ? 1 : sz == BeastSize.Medium ? 2 : 4;
            return new CharacterPreset
            {
                presetName = SizeName(sz) + " " + (index + 1),
                spritePath = "",
                weaponId = "Claws", // fora do catálogo de propósito: usa os valores de desarmado
                stats = new UnitStatBlock
                {
                    STR = Mathf.Round(pts * 0.28f * mul), VIT = Mathf.Round(pts * 0.30f * mul),
                    DEX = Mathf.Round(pts * 0.14f), AGI = Mathf.Round(pts * 0.18f),
                    INT = 1f, WIS = Mathf.Max(1f, Mathf.Round(pts * 0.10f)),
                    Footprint = fp, AttackRange = sz == BeastSize.Large ? 2 : 1,
                },
            };
        }
    }
}
