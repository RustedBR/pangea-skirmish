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

        /// <summary>Preenche o plano (actionSequence + listas) de uma fera livre.</summary>
        public static void Plan(Unit self, List<Unit> units, GridManager grid)
        {
            if (self == null || self.IsDead || grid == null) return;
            self.ResetPlan();

            var target = NearestHostile(self, units, self.anchor);
            if (target == null) return; // sem alvo: fila vira "Esperar"

            int fp = self.stats.Footprint;
            var blockers = new List<Unit>();
            foreach (var o in units) if (o != self && !o.IsDead) blockers.Add(o);

            Vector2Int pos = self.anchor;
            int budget = Tuning.Get().maxQueueTicks;
            int moveCost = TickRules.Duration(self, ActionType.Move, false);
            int atkCost  = TickRules.Duration(self, ActionType.Attack, false);

            // Aproxima: 1 deslocamento por decisão (fila curta = a fera reage ao que o jogador faz).
            for (int step = 0; step < 1 && budget >= moveCost; step++)
            {
                if (Gap(pos, fp, target) <= self.stats.AttackRange) break;
                var reach = grid.GetReachableAnchors(pos, self.stats.MoveStep, fp, blockers);
                Vector2Int best = pos;
                int bestGap = Gap(pos, fp, target);
                foreach (var a in reach)
                {
                    int g = Gap(a, fp, target);
                    // Desempate fixo (x, depois y): a ordem da lista não pode decidir.
                    if (g < bestGap || (g == bestGap && best != pos && (a.x < best.x || (a.x == best.x && a.y < best.y))))
                    { bestGap = g; best = a; }
                }
                if (best == pos) break; // encurralada
                self.plannedPath.Add(best);
                self.actionSequence.Add(new ScheduledAction { Type = ActionType.Move, Index = self.plannedMoveCount });
                self.plannedMoveCount++;
                self.plannedAnchor = best;
                pos = best;
                budget -= moveCost;
            }

            // Ataca se o alvo estiver no alcance (1 golpe por decisão).
            for (int n = 0; n < 1 && budget >= atkCost; n++)
            {
                if (Gap(pos, fp, target) > self.stats.AttackRange) break;
                self.plannedAttacks.Add(new PlannedAttack { Mode = AttackMode.Unit, TargetUnit = target, TargetTile = target.anchor });
                self.actionSequence.Add(new ScheduledAction { Type = ActionType.Attack, Index = self.plannedAttacks.Count - 1 });
                budget -= atkCost;
            }
            self.RecomputeTickBudget();
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

        /// <summary>Ficha de uma fera para a camada/grupo dados (GDD-015 §3.3).</summary>
        public static CharacterPreset BuildPreset(int index, int layer, int attributeBudget)
        {
            // Orçamento um pouco abaixo do dos jogadores na camada 1, crescendo por camada.
            float pts = Mathf.Max(6f, attributeBudget * (0.55f + 0.10f * (layer - 1)));
            return new CharacterPreset
            {
                presetName = "Fera " + (index + 1),
                spritePath = "",
                weaponId = "Claws", // fora do catálogo de propósito: usa os valores de desarmado
                stats = new UnitStatBlock
                {
                    STR = Mathf.Round(pts * 0.28f), VIT = Mathf.Round(pts * 0.30f),
                    DEX = Mathf.Round(pts * 0.14f), AGI = Mathf.Round(pts * 0.18f),
                    INT = 1f, WIS = Mathf.Max(1f, Mathf.Round(pts * 0.10f)),
                    Footprint = AttributeStats.DefaultFootprint, AttackRange = 1,
                },
            };
        }
    }
}
