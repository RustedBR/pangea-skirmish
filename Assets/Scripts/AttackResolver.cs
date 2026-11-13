using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Resolve ataques de arma pela FORMA da arma e pelos tiles pegos (GDD-020):
    ///   Unit — clique na unidade: a forma centraliza no alvo; dano comum.
    ///   Tile — mira livre: a forma fica onde o jogador mirou; +25% de dano e +15% de acerto.
    ///   Auto — IA / reserva: o inimigo mais próximo ao alcance, centralizado.
    /// Dano de cada unidade = dano por tile × tiles dela dentro da forma (+ mira), menos a defesa
    /// fixa uma vez. A esquiva tira tiles (AGI); Bloquear soma à defesa; aliados não são atingidos.
    /// Tudo determinístico (BattleRng, ordem por unitId).
    /// </summary>
    public static class AttackResolver
    {
        /// <summary>Resultado do último golpe resolvido (lido pelo RoundManager para reações em cadeia).</summary>
        public struct LastStrike
        {
            public bool AnyHit;          // alguma unidade tomou dano
            public bool AbsorbedAll;     // o bloqueio de algum alvo absorveu o golpe inteiro
            public List<Unit> Hit;       // quem tomou dano
        }
        public static LastStrike Last;

        public static Unit FindTargetInRange(Unit attacker, IEnumerable<Unit> all)
        {
            Unit best    = null;
            int  bestGap = int.MaxValue;
            foreach (var u in all)
            {
                if (u == attacker || u.IsDead || !u.IsHostileTo(attacker)) continue;
                int gap = GridManager.FootprintGap(attacker.anchor, attacker.stats.Footprint,
                                                   u.anchor, u.stats.Footprint);
                if (gap <= attacker.EffectiveAttackRange && gap < bestGap)
                {
                    best    = u;
                    bestGap = gap;
                }
            }
            return best;
        }

        /// <summary>Contra-atacar (golpe de oportunidade, GDD-020 §8): golpe comum centralizado no provocador.</summary>
        public static string ResolveOpportunityStrike(Unit attacker, Unit target, IEnumerable<Unit> all)
        {
            Last = new LastStrike { Hit = new List<Unit>() };
            if (target == null || target.IsDead || !attacker.IsHostileTo(target)) return null;
            // O gatilho já garante o alcance (o provocador estava nele ao andar, reagir ou conjurar).
            string log = Strike(attacker, AttackShape.Center(target.anchor, target.stats.Footprint), false, target, all,
                                Tuning.Get().aooDamageMult, null, null);
            return log != null ? "<color=#ff8800>⚔ Oportunidade</color> " + log : null;
        }

        /// <summary>Resolve um ataque planejado e devolve linha(s) de log; null = sem alvo.</summary>
        public static string ResolveAttack(Unit attacker, PlannedAttack attack, IEnumerable<Unit> all,
                                           Unit interceptor = null, Unit protectedUnit = null)
        {
            Last = new LastStrike { Hit = new List<Unit>() };
            switch (attack.Mode)
            {
                case AttackMode.Tile:
                {
                    var aim = attack.TargetTile;
                    if (GridManager.FootprintGap(attacker.anchor, attacker.stats.Footprint, aim, 1) > attacker.EffectiveAttackRange)
                        return $"<color=#888888>x</color> {attacker.unitName}: mira fora de alcance";
                    return Strike(attacker, aim, true, null, all, 1f, interceptor, protectedUnit);
                }
                case AttackMode.Unit:
                {
                    var t = attack.TargetUnit;
                    if (t == null || t.IsDead || GridManager.FootprintGap(attacker.anchor, attacker.stats.Footprint,
                            t.anchor, t.stats.Footprint) > attacker.EffectiveAttackRange)
                        return $"<color=#888888>x</color> {attacker.unitName} nao alcancou {(t != null ? t.unitName : "alvo")}";
                    return Strike(attacker, AttackShape.Center(t.anchor, t.stats.Footprint), false, t, all, 1f, interceptor, protectedUnit);
                }
                default:
                {
                    var t = FindTargetInRange(attacker, all);
                    if (t == null) return null;
                    return Strike(attacker, AttackShape.Center(t.anchor, t.stats.Footprint), false, t, all, 1f, interceptor, protectedUnit);
                }
            }
        }

        /// <summary>Células que o golpe planejado pega, da posição atual de quem ataca.</summary>
        public static List<Vector2Int> CellsFor(Unit attacker, Vector2Int aim) =>
            AttackShape.Cells(WeaponCatalog.Get(attacker.weaponId), AttackShape.Center(attacker.anchor, attacker.stats.Footprint), aim);

        /// <summary>Prévia sem sorte (para a HUD): tiles e dano esperado por unidade inimiga atingida.</summary>
        public static List<(Unit unit, int tiles, int damage)> Preview(Unit attacker, Vector2Int fromAnchor, Vector2Int aim,
                                                                       bool freeAim, IEnumerable<Unit> all)
        {
            var result = new List<(Unit, int, int)>();
            var w = WeaponCatalog.Get(attacker.weaponId);
            var cells = AttackShape.Cells(w, AttackShape.Center(fromAnchor, attacker.stats.Footprint), aim);
            float perTile = attacker.stats.DamagePerTile * (freeAim ? Tuning.Get().freeAimDamageMult : 1f);
            foreach (var u in Sorted(all))
            {
                if (u == attacker || u.IsDead || !u.IsHostileTo(attacker)) continue;
                int tiles = AttackShape.TilesOn(cells, u.anchor, u.stats.Footprint);
                if (tiles <= 0) continue;
                int dmg = Mathf.Max(Tuning.Get().minDamageFloor, Mathf.RoundToInt(perTile * tiles) - u.stats.PhysicalDefense);
                result.Add((u, tiles, dmg));
            }
            return result;
        }

        /// <summary>Tiles que a esquiva tira de um golpe (AGI, mínimo 1).</summary>
        public static int DodgeTiles(Unit u) =>
            Mathf.Max(1, Mathf.FloorToInt(u.stats.AGI) / Mathf.Max(1, Tuning.Get().dodgeAgiPerTile));

        private static List<Unit> Sorted(IEnumerable<Unit> all)
        {
            var list = new List<Unit>(all);
            list.Sort((a, b) => UnitRegistry.GetId(a).CompareTo(UnitRegistry.GetId(b)));
            return list;
        }

        /// <summary>
        /// O golpe em si. <paramref name="primary"/> é o alvo do clique (para a Muralha/Tempestade no
        /// caminho e o Olho certeiro); null na mira livre. Interpor: os tiles do protegido se dividem.
        /// </summary>
        private static string Strike(Unit attacker, Vector2Int aim, bool freeAim, Unit primary, IEnumerable<Unit> all,
                                     float dmgMult, Unit interceptor, Unit protectedUnit)
        {
            var T = Tuning.Get();
            var F = AttributeStats.Formulas;
            Last = new LastStrike { Hit = new List<Unit>() };
            var cells = CellsFor(attacker, aim);

            // Quem está dentro da forma (só inimigos, GDD-020 §2) e quantos tiles cada um tem nela.
            var tilesOf = new Dictionary<Unit, int>();
            var order = new List<Unit>();
            foreach (var u in Sorted(all))
            {
                if (u == attacker || u.IsDead || !u.IsHostileTo(attacker)) continue;
                int n = AttackShape.TilesOn(cells, u.anchor, u.stats.Footprint);
                if (n <= 0) continue;
                tilesOf[u] = n; order.Add(u);
            }
            // Interpor: o protetor entra na frente e fica com metade (arredondada para cima) dos tiles.
            if (interceptor != null && protectedUnit != null && tilesOf.TryGetValue(protectedUnit, out int pt) && !interceptor.IsDead)
            {
                int toGuard = (pt + 1) / 2;
                tilesOf[protectedUnit] = pt - toGuard;
                tilesOf[interceptor] = (tilesOf.TryGetValue(interceptor, out int g) ? g : 0) + toGuard;
                if (!order.Contains(interceptor)) order.Insert(order.IndexOf(protectedUnit) + 1, interceptor);
                BattleFeedback.Status(interceptor, $"Interpôs: {toGuard} tile{(toGuard == 1 ? "" : "s")}", new Color(1f, 0.86f, 0.40f));
            }

            bool power  = ConsumeBonus(attacker);
            int  aimDmg = ConsumeAim(attacker) ? Mathf.RoundToInt(attacker.stats.DEX * T.aimDexMultiplier) : 0;
            float fury  = attacker.furyReady;
            attacker.furyReady = 0f;

            if (order.Count == 0)
            {
                ConsumeStrBuffOnHit(attacker);
                return $"<color=#888888>x</color> {attacker.unitName}: o golpe não pegou ninguém";
            }

            // Muralha / Tempestade no caminho de golpes à distância (GDD-019 §3.2), contra o alvo do clique.
            var field = SpellField.Current;
            var lineTarget = primary ?? order[0];
            if (field != null && GridManager.FootprintGap(attacker.anchor, attacker.stats.Footprint, lineTarget.anchor, lineTarget.stats.Footprint) > 1)
            {
                var wall = field.BarrierOnLine(attacker.anchor, lineTarget.anchor);
                if (wall != null)
                {
                    int dmg = Mathf.RoundToInt(attacker.stats.DamagePerTile * tilesOf[lineTarget]);
                    wall.Hp -= dmg;
                    BattleFeedback.Absorbed(lineTarget, $"{wall.Name} absorveu {dmg}");
                    if (wall.Hp <= 0) field.Remove(wall);
                    return $"<color=#888888>x</color> {attacker.unitName} acerta a {wall.Name} ({Mathf.Max(0, wall.Hp)} HP)";
                }
                if (field.SandstormOnLine(attacker.anchor, lineTarget.anchor) && BattleRng.Roll10000() < 5000)
                {
                    BattleFeedback.Miss(lineTarget);
                    return $"<color=#888888>x</color> {attacker.unitName} errou na tempestade de areia";
                }
            }

            float perTile = attacker.stats.DamagePerTile * dmgMult
                          * (power ? T.powerStrikeMult : 1f)
                          * (freeAim ? T.freeAimDamageMult : 1f);
            float hitChance = attacker.EffectiveHitChance + (freeAim ? T.freeAimHitBonus : 0f);
            var lines = new List<string>();
            bool firstTarget = true;

            foreach (var t in order)
            {
                int tiles = tilesOf[t];
                if (tiles <= 0) continue;

                // Acerto e crítico (o Olho certeiro vale para o alvo do clique, ou o primeiro).
                HitResult res;
                bool trueAim = (t == primary || (primary == null && firstTarget)) && SpellResolver.TryTrueAim(attacker, t, out res);
                if (!trueAim)
                {
                    if (BattleRng.Roll10000() < Mathf.RoundToInt(attacker.stats.CritChance * 10000f)) res = HitResult.Critical;
                    else res = BattleRng.Roll10000() < Mathf.RoundToInt(Mathf.Clamp01(hitChance) * 10000f) ? HitResult.Hit : HitResult.Miss;
                }
                else res = HitResult.Hit;
                firstTarget = false;
                if (trueAim) BattleFeedback.Status(attacker, "Olho certeiro!", new Color(0.33f, 0.80f, 1f));

                if (res == HitResult.Miss)
                {
                    BattleFeedback.Miss(t);
                    lines.Add($"<color=#888888>x</color> {attacker.unitName} errou {t.unitName}");
                    ClearDefense(t);
                    continue;
                }

                // Esquiva tira tiles (GDD-020 §4). A reação Esquivar garante; senão rola pela AGI (crítico não se esquiva).
                int removed = 0;
                if (t.dodgeReactTiles > 0) removed = t.dodgeReactTiles;
                else if (res != HitResult.Critical && !trueAim &&
                         BattleRng.Roll10000() < Mathf.RoundToInt(t.stats.DodgeChance * 10000f))
                    removed = DodgeTiles(t);
                removed = Mathf.Min(removed, tiles);
                int left = tiles - removed;
                if (removed > 0)
                {
                    if (t.Model3D != null) t.Model3D.PlayDodge();
                    BattleFeedback.Status(t, left == 0 ? "Esquivou!" : $"Esquivou {removed} tile{(removed == 1 ? "" : "s")}", new Color(0.55f, 0.85f, 1f));
                }
                if (left <= 0)
                {
                    BattleFeedback.Dodge(t);
                    lines.Add($"<color=#55ccff>↯</color> {t.unitName} esquivou o golpe de {attacker.unitName} ({tiles}/{tiles} tiles)");
                    ClearDefense(t);
                    continue;
                }

                int raw = Mathf.RoundToInt(perTile * left) + aimDmg;
                if (fury > 0f) raw = Mathf.Max(raw, Mathf.RoundToInt(attacker.stats.DamagePerTile * left * fury) + aimDmg);
                int rolled = attacker.stats.RollDamage(raw, isPhysical: true, defender: t.stats);
                if (res == HitResult.Critical) rolled = Mathf.RoundToInt(rolled * F.critDamageMul);

                // Bloquear: soma à defesa fixa; absorver tudo desequilibra o atacante (GDD-020 §8).
                if (t.blockFlat > 0)
                {
                    int before = rolled;
                    rolled = Mathf.Max(0, rolled - t.blockFlat);
                    BattleFeedback.Block(t, before - rolled);
                    if (rolled == 0) Last.AbsorbedAll = true;
                }
                rolled = rolled > 0 ? SpellResolver.StoneSkin(t, rolled, physical: true) : 0;
                if (rolled > 0)
                {
                    t.TakeDamage(rolled, res == HitResult.Critical);
                    Last.AnyHit = true; Last.Hit.Add(t);
                }
                ClearDefense(t);

                string hp   = t.IsDead ? "<color=#ff6666>MORTO</color>" : $"{t.currentHP}/{t.stats.MaxHP} HP";
                string icon = res == HitResult.Critical ? "<color=#ffd700>★</color>" : (power ? "<color=#ffd700>*</color>" : ">");
                string tags = (freeAim ? " <color=#9fd0ff>MIRA LIVRE</color>" : "")
                            + (power ? " <color=#ffd700>PODEROSO</color>" : "")
                            + (aimDmg > 0 ? $" <color=#55ccff>+MIRA({aimDmg})</color>" : "")
                            + (fury > 0f ? " <color=#ffb060>FÚRIA</color>" : "")
                            + (removed > 0 ? $" <color=#55ccff>−{removed} esquiva</color>" : "");
                lines.Add($"{icon} {attacker.unitName} -> {t.unitName}: {rolled} ({left} tile{(left == 1 ? "" : "s")}){tags}  ({hp})");
            }
            if (fury > 0f) BattleFeedback.Status(attacker, "Fúria!", new Color(1f, 0.69f, 0.38f));
            ConsumeStrBuffOnHit(attacker);
            return lines.Count > 0 ? string.Join("\n", lines) : null;
        }

        private static void ClearDefense(Unit t) { t.dodgeReactBonus = 0f; t.dodgeReactTiles = 0; t.blockFlat = 0; }

        // ---- Helpers ----
        /// <summary>Dano de um golpe centralizado (base × golpe poderoso) + dano da mira. Usado pela IA e pela HUD.</summary>
        public static int PowerDamage(Unit u, bool power, int aimDmg) =>
            Mathf.RoundToInt(u.stats.PhysicalDamage * (power ? Tuning.Get().powerStrikeMult : 1f)) + aimDmg;

        private static bool ConsumeBonus(Unit u)
        {
            if (!u.bonusDamageThisAttack) return false;
            u.bonusDamageThisAttack = false;
            return true;
        }

        private static bool ConsumeAim(Unit u)
        {
            if (!u.aimBonusThisAttack) return false;
            u.aimBonusThisAttack = false;
            return true;
        }

        public static void ConsumeStrBuffOnHit(Unit attacker)
        {
            StatusEffectSystem.ConsumeStrBuffOnHit(attacker.statusEffects);
        }
    }
}
