using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    public static class SimpleEnemyAI
    {
        public static void Plan(Unit enemy, List<Unit> allUnits, GridManager grid)
        {
            var tuning = Tuning.Get();
            float aggression       = tuning.aiAggression;
            float attackPref       = tuning.aiAttackPreference;
            float intelligence     = tuning.aiIntelligence;
            float survivalInstinct = tuning.aiSurvivalInstinct;
            int   flankRange       = tuning.aiFlankRange;

            int efp       = enemy.stats.Footprint;
            int dmgPerHit = Mathf.Max(1, enemy.stats.PhysicalDamage);

            float survThreshold = Mathf.Lerp(0f, tuning.aiSurvivalHpThreshold, survivalInstinct);
            bool isSurvival = survivalInstinct > 0f &&
                enemy.currentHP < enemy.stats.MaxHP * survThreshold;

            // ── MAGIA: IA conjura antes de atacar ──
            if (!isSurvival && tuning.aiUseSpells && enemy.currentMana > 0 &&
                enemy.stats.INT + enemy.stats.WIS >= tuning.aiSpellMinPair)
            {
                TryCastSpell(enemy, allUnits, grid);
            }

            if (isSurvival)
            {
                PlanSurvival(enemy, allUnits, grid, efp);
                return;
            }

            // ── MODO NORMAL (agressivo / cauteloso) ──

            // Sorteia uma vez por planejamento: agressivo (gruda) ou cauteloso (folga)
            bool isAggressive = Random.value <= aggression;
            int desiredGap = Mathf.Max(1,
                isAggressive ? Mathf.Min(enemy.stats.AttackRange, tuning.aiAggressiveMaxGap)
                             : enemy.stats.AttackRange + tuning.aiCautiousGapOffset);

            // HP virtual: rastreia quanto dano ainda falta aplicar em cada alvo
            var virtualHP = new Dictionary<Unit, int>();
            foreach (var u in allUnits)
                if (!u.IsDead && u.team != enemy.team)
                    virtualHP[u] = u.currentHP;

            // Alvo travado (intelligence < 1.0 = tendência a focar um alvo até morrer)
            Unit lockedTarget = null;
            int? bapAttackIndex = null;

            while (enemy.remainingAP > 0)
            {
                // Reavalia alvo conforme inteligência
                bool reEvaluate = intelligence >= 1f ||
                    (intelligence > 0f && Random.value <= intelligence);

                Unit target;
                if (!reEvaluate && lockedTarget != null &&
                    virtualHP.TryGetValue(lockedTarget, out int lhp) && lhp > 0)
                {
                    target = lockedTarget;
                }
                else
                {
                    target = PickTarget(enemy, allUnits, virtualHP, efp,
                        attackPref, intelligence, dmgPerHit, enemy.remainingAP);
                    lockedTarget = target;
                }

                if (target == null) break;

                int tfp = target.stats.Footprint;
                int gap = GridManager.FootprintGap(enemy.FinalAnchor, efp, target.anchor, tfp);

                if (gap > enemy.stats.AttackRange)
                {
                    // Fora de alcance: tenta se aproximar
                    var blockers  = BlockerUnits(enemy, allUnits);
                    var reachable = grid.GetReachableAnchors(enemy.FinalAnchor, enemy.stats.MoveBudget, efp, blockers);
                    reachable.Add(enemy.FinalAnchor);

                    Vector2Int best      = enemy.FinalAnchor;
                    int        bestScore = int.MaxValue;
                    foreach (var a in reachable)
                    {
                        int score = Mathf.Abs(GridManager.FootprintGap(a, efp, target.anchor, tfp) - desiredGap);

                        // Bônus de flanco: prefere aproximação diagonal
                        if (flankRange > 0 && a.x != target.anchor.x && a.y != target.anchor.y)
                        {
                            int dist = Mathf.Max(Mathf.Abs(a.x - target.anchor.x),
                                                  Mathf.Abs(a.y - target.anchor.y));
                            if (dist <= flankRange) score -= tuning.aiFlankScoreBonus;
                        }

                        if (score < bestScore) { bestScore = score; best = a; }
                    }

                    if (best == enemy.FinalAnchor) break; // sem caminho disponível

                    enemy.plannedAnchor = best;
                    enemy.plannedPath.Add(best);
                    enemy.actionSequence.Add(new ScheduledAction { Type = ActionType.Move, Index = enemy.plannedMoveCount, IsBonus = false, BonusStep = Vector2Int.zero });
                    enemy.plannedMoveCount++;
                    enemy.remainingAP--;
                }
                else
                {
                    // Em alcance: ataca e desconta HP virtual
                    bool useBAP = enemy.remainingBAP > 0 && bapAttackIndex == null;
                    int atkIdx = enemy.plannedAttacks.Count;
                    enemy.plannedAttacks.Add(new PlannedAttack { Mode = AttackMode.Unit, TargetUnit = target });
                    target.SetAttackMarked(true);
                    if (useBAP) { bapAttackIndex = atkIdx; enemy.remainingBAP--; }
                    enemy.remainingAP--;

                    virtualHP[target] = Mathf.Max(0, virtualHP[target] - dmgPerHit);
                }
            }

            // ── CONCENTRAÇÃO: IA recupera mana quando threshold baixo ──
            if (enemy.plannedConcentrations == 0 && enemy.currentMana <= tuning.aiConcentrationThreshold
                && enemy.remainingBAP > 0)
            {
                enemy.plannedConcentrations++;
                enemy.remainingBAP--;
            }

            // Gasta BAP restante num passo extra ANTES do rebuild (senão a actionSequence fica sem a entrada)
            TryBonusMove(enemy, allUnits, grid, efp);

            enemy.RebuildSequenceFromLists();

            // Re-aplica IsBonus=true no ataque que gastou BAP (RebuildSequenceFromLists zera tudo)
            if (bapAttackIndex.HasValue)
            {
                int atkActIdx = enemy.plannedMoveCount + bapAttackIndex.Value;
                if (atkActIdx < enemy.actionSequence.Count)
                {
                    var a = enemy.actionSequence[atkActIdx];
                    a.IsBonus = true;
                    enemy.actionSequence[atkActIdx] = a;
                }
            }
        }

        /// <summary>Modo sobrevivência: foge do inimigo mais próximo.</summary>
        private static void PlanSurvival(Unit enemy, List<Unit> all,
            GridManager grid, int efp)
        {
            // Encontra o inimigo mais próximo (por gap)
            Unit nearestThreat = null;
            int closestGap = int.MaxValue;
            foreach (var u in all)
            {
                if (u == enemy || u.IsDead || u.team == enemy.team) continue;
                int g = GridManager.FootprintGap(enemy.anchor, efp, u.anchor, u.stats.Footprint);
                if (g < closestGap) { closestGap = g; nearestThreat = u; }
            }

            if (nearestThreat == null)
            {
                enemy.RebuildSequenceFromLists();
                return;
            }

            int tgtFP = nearestThreat.stats.Footprint;

            while (enemy.remainingAP > 0)
            {
                var blockers  = BlockerUnits(enemy, all);
                var reachable = grid.GetReachableAnchors(enemy.FinalAnchor, enemy.stats.MoveBudget, efp, blockers);
                reachable.Add(enemy.FinalAnchor);

                Vector2Int best      = enemy.FinalAnchor;
                int        bestScore = int.MinValue;
                foreach (var a in reachable)
                {
                    // Score = distância do inimigo mais próximo (quanto mais longe, melhor)
                    int distFromThreat = GridManager.FootprintGap(a, efp, nearestThreat.anchor, tgtFP);
                    if (distFromThreat > bestScore) { bestScore = distFromThreat; best = a; }
                }

                if (best == enemy.FinalAnchor) break; // já está no ponto mais seguro alcançável

                enemy.plannedAnchor = best;
                enemy.plannedPath.Add(best);
                enemy.actionSequence.Add(new ScheduledAction { Type = ActionType.Move, Index = enemy.plannedMoveCount, IsBonus = false, BonusStep = Vector2Int.zero });
                enemy.plannedMoveCount++;
                enemy.remainingAP--;
            }

            // Gasta BAP restante num passo extra de fuga
            TryBonusMove(enemy, all, grid, efp, nearestThreat.anchor, tgtFP);

            enemy.RebuildSequenceFromLists();
        }

        /// <summary>Escolhe alvo ponderando distância, fragilidade e matabilidade.</summary>
        private static Unit PickTarget(Unit self, List<Unit> all,
            Dictionary<Unit, int> virtualHP, int selfFP, float attackPref,
            float intelligence, int dmgPerHit, int remainingAP)
        {
            var tuning = Tuning.Get();
            Unit  best      = null;
            float bestScore = float.MaxValue;
            float maxGap    = 0f;

            // Primeira passada: descobre a distância máxima para normalização
            foreach (var u in all)
            {
                if (u == self || u.IsDead || u.team == self.team) continue;
                if (!virtualHP.TryGetValue(u, out int hp) || hp <= 0) continue;
                int g = GridManager.FootprintGap(self.anchor, selfFP, u.anchor, u.stats.Footprint);
                if (g > maxGap) maxGap = g;
            }
            if (maxGap < 1f) maxGap = 1f;

            foreach (var u in all)
            {
                if (u == self || u.IsDead || u.team == self.team) continue;
                if (!virtualHP.TryGetValue(u, out int hp) || hp <= 0) continue;

                int  gap      = GridManager.FootprintGap(self.anchor, selfFP, u.anchor, u.stats.Footprint);
                float hpMax   = Mathf.Max(1f, u.stats.MaxHP);
                float gapNorm = gap / maxGap;
                float frag    = 1f - (hp / hpMax); // 0 = HP cheio, 1 = quase morto

                // Score base: distância vs fragilidade
                float score = gapNorm * (1f - attackPref) + frag * attackPref;

                // Bônus de matabilidade: inteligência alta prefere alvos matáveis com os AP restantes
                if (intelligence > 0f && dmgPerHit > 0)
                {
                    int hitsToKill = Mathf.Max(1, Mathf.CeilToInt((float)hp / dmgPerHit));
                    float killWeight;
                    if (hitsToKill <= remainingAP)
                        killWeight = 1f; // Matável agora = prioridade máxima
                    else
                        killWeight = 1f - (hitsToKill - remainingAP) / Mathf.Max(1f, hitsToKill);

                    score -= killWeight * intelligence * tuning.aiKillabilityWeight;
                }

                if (score < bestScore) { bestScore = score; best = u; }
            }
            return best;
        }

        private static List<Unit> BlockerUnits(Unit self, List<Unit> all)
        {
            var list = new List<Unit>();
            foreach (var u in all)
                if (u != self && !u.IsDead) list.Add(u);
            return list;
        }

        /// <summary>Tenta gastar BAP restante num passo extra. Se threatAnchor for fornecido,
        /// foge dele (threatFP = footprint da ameaça); senão anda pra qualquer tile vazio adjacente.</summary>
        private static void TryBonusMove(Unit enemy, List<Unit> all,
            GridManager grid, int efp, Vector2Int? threatAnchor = null, int threatFP = 1)
        {
            if (enemy.remainingBAP <= 0) return;

            var blockers  = BlockerUnits(enemy, all);
            var reachable = grid.GetReachableAnchors(enemy.FinalAnchor, 1, efp, blockers);
            reachable.RemoveAll(a => a == enemy.FinalAnchor);
            if (reachable.Count == 0) return;

            Vector2Int best = enemy.FinalAnchor;
            int bestScore = int.MinValue;
            foreach (var a in reachable)
            {
                int score = threatAnchor.HasValue
                    ? GridManager.FootprintGap(a, efp, threatAnchor.Value, threatFP)
                    : Random.Range(1, 100); // sem ameaça: tile aleatório
                if (score > bestScore) { bestScore = score; best = a; }
            }
            if (best == enemy.FinalAnchor) return;

            enemy.hasPlannedBonus = true;
            enemy.plannedBonusAnchor = best;
            enemy.remainingBAP = 0;
        }

        /// <summary>IA tenta conjurar uma magia: gasta 1 AP e reserva mana. Self buff se HP baixo, senão ataca.</summary>
        private static void TryCastSpell(Unit enemy, List<Unit> all, GridManager grid)
        {
            if (enemy.remainingAP <= 0) return;

            var elements = (SpellElement[])System.Enum.GetValues(typeof(SpellElement));
            int pairStat = Mathf.RoundToInt(enemy.stats.INT + enemy.stats.WIS);
            int manaGasto = Mathf.Min(enemy.currentMana, 10 + pairStat);
            float manaFrac = manaGasto / (float)enemy.stats.MaxMana;

            // Prefere self-buff se HP < 50% e Physical/Magic element
            bool lowHP = enemy.currentHP < enemy.stats.MaxHP * 0.5f;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                var elem = elements[Random.Range(0, elements.Length)];

                // Self-buff (mana = custo fixo, já que mana define alcance e self-buff não usa alcance)
                if (lowHP && (elem == SpellElement.Physical || elem == SpellElement.Magic))
                {
                    int selfMana = Tuning.Get().spellSelfManaCost;
                    if (enemy.currentMana < selfMana) continue;
                    enemy.plannedSpells.Add(new PlannedSpell
                    {
                        Element = elem,
                        Mana = selfMana,
                        Target = SpellTargetKind.Self,
                        TargetUnit = null,
                        TargetTile = Vector2Int.zero,
                        Direction = Vector2Int.zero,
                    });
                    enemy.currentMana -= selfMana;
                    enemy.remainingAP--;
                    return;
                }

                // Unit-target spell (projectile)
                Unit target = null;
                int bestGap = int.MaxValue;
                foreach (var u in all)
                {
                    if (u == enemy || u.IsDead || u.team == enemy.team) continue;
                    int g = GridManager.FootprintGap(enemy.anchor, enemy.stats.Footprint,
                        u.anchor, u.stats.Footprint);
                    if (g < bestGap) { bestGap = g; target = u; }
                }

                int range = SpellBook.SpellRange(enemy, manaGasto);
                if (target != null && bestGap <= range)
                {
                    var dir = new Vector2Int(
                        System.Math.Sign(target.anchor.x - enemy.anchor.x),
                        System.Math.Sign(target.anchor.y - enemy.anchor.y));
                    enemy.plannedSpells.Add(new PlannedSpell
                    {
                        Element = elem,
                        Mana = manaGasto,
                        Target = SpellTargetKind.Unit,
                        TargetUnit = target,
                        TargetTile = Vector2Int.zero,
                        Direction = dir.x != 0 || dir.y != 0 ? dir : Vector2Int.right,
                    });
                    enemy.currentMana -= manaGasto;
                    enemy.remainingAP--;
                    return;
                }

                // Tile-target spell (fogo/vento/terra)
                if (bestGap <= range + 2)
                {
                    var dir = target != null
                        ? new Vector2Int(System.Math.Sign(target.anchor.x - enemy.anchor.x),
                                         System.Math.Sign(target.anchor.y - enemy.anchor.y))
                        : Vector2Int.right;
                    enemy.plannedSpells.Add(new PlannedSpell
                    {
                        Element = elem,
                        Mana = manaGasto,
                        Target = SpellTargetKind.Tile,
                        TargetUnit = null,
                        TargetTile = target != null ? target.anchor : enemy.anchor,
                        Direction = dir.x != 0 || dir.y != 0 ? dir : Vector2Int.right,
                    });
                    enemy.currentMana -= manaGasto;
                    enemy.remainingAP--;
                    return;
                }
            }
        }
    }
}
