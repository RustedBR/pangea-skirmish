using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Monta a fila de uma unidade a partir de uma INTENÇÃO (GDD-016 §5): o jogador (ou a IA
    /// das feras) diz o que quer e o montador gera as andadas e os ataques que cabem no
    /// orçamento de ticks. Determinístico: só usa o estado da batalha, com desempates fixos.
    ///
    /// Todo método substitui o plano atual da unidade (ResetPlan) e devolve true se gerou
    /// alguma ação.
    /// </summary>
    public static class IntentBuilder
    {
        // ------------------------------------------------------------------ intenções

        /// <summary>Investir: vai até o alvo e gasta o tempo restante atacando.</summary>
        public static bool Charge(Unit self, Unit target, List<Unit> units, GridManager grid,
                                  int maxMoves = int.MaxValue, int maxAttacks = int.MaxValue)
        {
            if (!Begin(self, grid) || target == null || target.IsDead) return false;
            var blockers = Blockers(self, units);
            Approach(self, target, grid, blockers, maxMoves, reserveTicks: 0);
            for (int n = 0; n < maxAttacks; n++)
                if (!TryAttack(self, target)) break;
            return Finish(self);
        }

        /// <summary>
        /// Golpear e recuar: vai até o alvo, ataca UMA vez e gasta o resto do tempo voltando
        /// para <paramref name="endAt"/> (ou para onde estava, se null).
        /// </summary>
        public static bool HitAndRun(Unit self, Unit target, List<Unit> units, GridManager grid, Vector2Int? endAt = null)
        {
            if (!Begin(self, grid) || target == null || target.IsDead) return false;
            var blockers = Blockers(self, units);
            Vector2Int home = endAt ?? self.anchor;

            // Guarda tempo para o golpe: aproximar sem conseguir bater não é "golpear e recuar".
            int atkCost = TickRules.Duration(self, ActionType.Attack, false);
            Approach(self, target, grid, blockers, int.MaxValue, reserveTicks: atkCost);
            if (!TryAttack(self, target)) return Finish(self);
            // Já estava no alcance (não precisou andar)? Então "recuar" é afastar-se dos inimigos.
            if (self.plannedAnchor == home && !endAt.HasValue) FleeSteps(self, units, grid, blockers);
            else WalkToward(self, home, grid, blockers, int.MaxValue);
            return Finish(self);
        }

        /// <summary>Andar até um ponto, em quantas andadas couberem.</summary>
        public static bool MoveTo(Unit self, Vector2Int dest, List<Unit> units, GridManager grid)
        {
            if (!Begin(self, grid)) return false;
            WalkToward(self, dest, grid, Blockers(self, units), int.MaxValue);
            return Finish(self);
        }

        /// <summary>
        /// Recuar: gasta todo o movimento se afastando dos inimigos — ou indo até
        /// <paramref name="toward"/>, se informado.
        /// </summary>
        public static bool Retreat(Unit self, List<Unit> units, GridManager grid, Vector2Int? toward = null)
        {
            if (!Begin(self, grid)) return false;
            var blockers = Blockers(self, units);
            if (toward.HasValue) { WalkToward(self, toward.Value, grid, blockers, int.MaxValue); return Finish(self); }

            FleeSteps(self, units, grid, blockers);
            return Finish(self);
        }

        /// <summary>Gasta o tempo restante em andadas que aumentam a distância ao hostil mais próximo.</summary>
        private static void FleeSteps(Unit self, List<Unit> units, GridManager grid, List<Unit> blockers)
        {
            int fp = self.stats.Footprint;
            int moveCost = TickRules.Duration(self, ActionType.Move, false);
            while (self.remainingAP >= moveCost)
            {
                Vector2Int pos = self.plannedAnchor;
                int here = NearestHostileGap(self, pos, units);
                if (here == int.MaxValue) break; // sem inimigos: nada de que fugir
                Vector2Int best = pos; int bestGap = here;
                foreach (var a in grid.GetReachableAnchors(pos, self.EffectiveMoveStep, fp, blockers))
                {
                    int g = NearestHostileGap(self, a, units);
                    if (g > bestGap || (g == bestGap && best != pos && Before(a, best))) { bestGap = g; best = a; }
                }
                if (best == pos) break; // encurralada
                AddMove(self, best);
            }
        }

        // ------------------------------------------------------------------ peças

        private static bool Begin(Unit self, GridManager grid)
        {
            if (self == null || self.IsDead || grid == null) return false;
            self.ResetPlan();
            self.RecomputeTickBudget();
            return true;
        }

        private static bool Finish(Unit self)
        {
            self.RecomputeTickBudget();
            return self.actionSequence.Count > 0;
        }

        private static List<Unit> Blockers(Unit self, List<Unit> units)
        {
            var list = new List<Unit>();
            foreach (var o in units) if (o != null && o != self && !o.IsDead) list.Add(o);
            return list;
        }

        private static int Gap(Vector2Int from, int fp, Unit target) =>
            GridManager.FootprintGap(from, fp, target.anchor, target.stats.Footprint);

        /// <summary>Desempate fixo entre âncoras (x, depois y) — a ordem da lista nunca decide.</summary>
        private static bool Before(Vector2Int a, Vector2Int b) => a.x < b.x || (a.x == b.x && a.y < b.y);

        private static int NearestHostileGap(Unit self, Vector2Int from, List<Unit> units)
        {
            int best = int.MaxValue;
            foreach (var u in units)
            {
                if (u == null || u == self || u.IsDead || !u.IsHostileTo(self)) continue;
                best = Mathf.Min(best, Gap(from, self.stats.Footprint, u));
            }
            return best;
        }

        private static void AddMove(Unit self, Vector2Int dest)
        {
            self.plannedPath.Add(dest);
            self.actionSequence.Add(new ScheduledAction { Type = ActionType.Move, Index = self.plannedMoveCount });
            self.plannedMoveCount++;
            self.plannedAnchor = dest;
            self.RecomputeTickBudget();
        }

        /// <summary>Acrescenta um ataque se o alvo está no alcance (da posição planejada) e cabe na fila.</summary>
        private static bool TryAttack(Unit self, Unit target)
        {
            if (!self.CanFit(ActionType.Attack)) return false;
            if (Gap(self.plannedAnchor, self.stats.Footprint, target) > self.EffectiveAttackRange) return false;
            self.plannedAttacks.Add(new PlannedAttack { Mode = AttackMode.Unit, TargetUnit = target, TargetTile = target.anchor });
            self.actionSequence.Add(new ScheduledAction { Type = ActionType.Attack, Index = self.plannedAttacks.Count - 1 });
            self.RecomputeTickBudget();
            return true;
        }

        /// <summary>Anda em direção ao alvo até ele entrar no alcance, guardando <paramref name="reserveTicks"/>.</summary>
        private static void Approach(Unit self, Unit target, GridManager grid, List<Unit> blockers, int maxMoves, int reserveTicks)
        {
            int fp = self.stats.Footprint;
            int moveCost = TickRules.Duration(self, ActionType.Move, false);
            for (int step = 0; step < maxMoves && self.remainingAP - reserveTicks >= moveCost; step++)
            {
                Vector2Int pos = self.plannedAnchor;
                int here = Gap(pos, fp, target);
                if (here <= self.EffectiveAttackRange) break;
                Vector2Int best = pos; int bestGap = here;
                foreach (var a in grid.GetReachableAnchors(pos, self.EffectiveMoveStep, fp, blockers))
                {
                    int g = Gap(a, fp, target);
                    if (g < bestGap || (g == bestGap && best != pos && Before(a, best))) { bestGap = g; best = a; }
                }
                if (best == pos) break; // sem caminho melhor
                AddMove(self, best);
            }
        }

        /// <summary>Anda em direção a um ponto (distância de xadrez entre âncoras) até chegar ou acabar o tempo.</summary>
        private static void WalkToward(Unit self, Vector2Int dest, GridManager grid, List<Unit> blockers, int maxMoves)
        {
            int fp = self.stats.Footprint;
            int moveCost = TickRules.Duration(self, ActionType.Move, false);
            for (int step = 0; step < maxMoves && self.remainingAP >= moveCost; step++)
            {
                Vector2Int pos = self.plannedAnchor;
                int here = Dist(pos, dest);
                if (here == 0) break;
                Vector2Int best = pos; int bestD = here;
                foreach (var a in grid.GetReachableAnchors(pos, self.EffectiveMoveStep, fp, blockers))
                {
                    int d = Dist(a, dest);
                    if (d < bestD || (d == bestD && best != pos && Before(a, best))) { bestD = d; best = a; }
                }
                if (best == pos) break;
                AddMove(self, best);
            }
        }

        private static int Dist(Vector2Int a, Vector2Int b) =>
            Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y)) * 100 + Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }
}
