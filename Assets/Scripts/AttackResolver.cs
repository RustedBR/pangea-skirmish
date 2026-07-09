using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Resolve ataques conforme o modo planejado:
    ///   Auto — encontra o inimigo mais próximo em alcance (comportamento original / IA)
    ///   Unit — ataca a unidade especificada se ainda estiver em alcance; fallback = Auto
    ///   Tile — ataca qualquer unidade no tile especificado com bônus de dano; erra se vazio
    /// </summary>
    public static class AttackResolver
    {
        public static Unit FindTargetInRange(Unit attacker, IEnumerable<Unit> all)
        {
            Unit best    = null;
            int  bestGap = int.MaxValue;
            foreach (var u in all)
            {
                if (u == attacker || u.IsDead || !u.IsHostileTo(attacker)) continue;
                int gap = GridManager.FootprintGap(attacker.anchor, attacker.stats.Footprint,
                                                   u.anchor, u.stats.Footprint);
                if (gap <= attacker.stats.AttackRange && gap < bestGap)
                {
                    best    = u;
                    bestGap = gap;
                }
            }
            return best;
        }

        /// <summary>Resolve um ataque planejado e devolve linha de log; null = sem alvo.</summary>
        public static string ResolveAttack(Unit attacker, PlannedAttack attack, IEnumerable<Unit> all)
        {
            switch (attack.Mode)
            {
                case AttackMode.Tile: return ResolveTileAttack(attacker, attack.TargetTile, all);
                // Unit: sem fallback — se não chegou perto, erra e perde o PA
                case AttackMode.Unit: return ResolveUnitAttack(attacker, attack.TargetUnit, all, fallback: false);
                // Auto (IA): mantém fallback para o mais próximo
                default:             return ResolveAutoAttack(attacker, all);
            }
        }

        // ---- Auto (IA / fallback) ----
        private static string ResolveAutoAttack(Unit attacker, IEnumerable<Unit> all)
            => ResolveUnitAttack(attacker, null, all, fallback: true);

        // ---- Alvo direto ----
        private static string ResolveUnitAttack(Unit attacker, Unit target, IEnumerable<Unit> all, bool fallback)
        {
            bool outOfRange = target == null || target.IsDead ||
                GridManager.FootprintGap(attacker.anchor, attacker.stats.Footprint,
                                         target.anchor,   target.stats.Footprint)
                    > attacker.stats.AttackRange;

            if (outOfRange)
            {
                if (!fallback)
                {
                    string nome = target != null ? target.unitName : "alvo";
                    return $"<color=#888888>x</color> {attacker.unitName} nao alcancou {nome}";
                }
                target = FindTargetInRange(attacker, all);
            }
            if (target == null) return null;

            bool bonus  = ConsumeBonus(attacker);
            int  aimDmg = ConsumeAim(attacker) ? Mathf.RoundToInt(attacker.stats.DEX * Tuning.Get().aimDexMultiplier) : 0;
            int  dmg    = BaseDamage(attacker) + (bonus ? AttributeStats.Formulas.incrementAttackDamage : 0) + aimDmg;

            // ---- Rolagem de acerto / esquiva / crítico (lockstep-safe via BattleRng) ----
            var res = attacker.stats.RollHit(attacker.stats.HitChance, target.stats.DodgeChance);
            if (res == HitResult.Miss)
            {
                ConsumeStrBuffOnHit(attacker);
                string missTag = bonus ? " <color=#ffd700>+INCR</color>" : "";
                return $"<color=#888888>x</color> {attacker.unitName} errou {target.unitName}{missTag}";
            }
            int rolled = attacker.stats.RollDamage(dmg, isPhysical: true);
            if (res == HitResult.Critical)
                rolled = Mathf.RoundToInt(rolled * AttributeStats.Formulas.critDamageMul);
            target.TakeDamage(rolled, res == HitResult.Critical);
            ConsumeStrBuffOnHit(attacker);

            string deathTag = "<color=#ff6666>MORTO</color>";
            string hp       = target.IsDead ? deathTag : $"{target.currentHP}/{target.stats.MaxHP} HP";
            string icon     = res == HitResult.Critical ? "<color=#ffd700>★</color>" : (bonus ? "<color=#ffd700>*</color>" : ">");
            string tag      = (bonus || res == HitResult.Critical) ? " <color=#ffd700>+INCR</color>" : "";
            string aimTag   = aimDmg > 0 ? $" <color=#55ccff>+MIRA({aimDmg})</color>" : "";
            return $"{icon} {attacker.unitName} -> {target.unitName}: {rolled}{tag}{aimTag}  ({hp})";
        }

        // ---- Tile (posicional, SEM rolagem de acerto) ----
        // Melee: linha reta do atacante ate o tile clicado, atinge max(1, range-2) tiles.
        // Ranged: apenas o tile clicado (1 tile). Alcance longo balanceia a falta de AoE.
        // Se QUALQUER tile atingido contiver inimigo hostil -> acerta (sem dodge).
        private static string ResolveTileAttack(Unit attacker, Vector2Int tile, IEnumerable<Unit> all)
        {
            int afp = attacker.stats.Footprint;
            int range = attacker.stats.AttackRange;
            if (GridManager.FootprintGap(attacker.anchor, afp, tile, afp) > range)
                return $"<color=#888888>x</color> {attacker.unitName} fora de alcance";

            // Coleta os tiles atingidos conforme tipo de arma
            var hitTiles = new List<Vector2Int>();
            bool isMelee = range <= Tuning.Get().strDamageMaxRange; // melee = alcance curto
            if (isMelee)
            {
                int extra = Mathf.Max(1, range - 2); // tiles extras alem do alvo
                hitTiles = LineTiles(attacker.anchor, tile, extra);
            }
            else
            {
                hitTiles.Add(tile); // ranged: so o tile clicado
            }

            // Procura inimigo hostil em qualquer tile atingido
            Unit target = null;
            foreach (var t in hitTiles)
            {
                foreach (var u in all)
                {
                    if (u == attacker || u.IsDead || !u.IsHostileTo(attacker)) continue;
                    if (GridManager.FootprintsOverlap(u.anchor, u.stats.Footprint, t, afp))
                    { target = u; break; }
                }
                if (target != null) break;
            }

            if (target == null)
                return $"<color=#888888>x</color> {attacker.unitName} posicao vazia";

            bool bonus = ConsumeBonus(attacker);
            int  aimDmg = ConsumeAim(attacker) ? Mathf.RoundToInt(attacker.stats.DEX * Tuning.Get().aimDexMultiplier) : 0;
            // Dano SEM +STR extra (diferente do comportamento anterior)
            int  dmg   = BaseDamage(attacker)
                       + (bonus ? AttributeStats.Formulas.incrementAttackDamage : 0)
                       + aimDmg;

            // Sem rolagem de dodge — acerto garantido se o tile tem inimigo
            int rolled = attacker.stats.RollDamage(dmg, isPhysical: true);
            if (attacker.stats.RollCrit()) // critico ainda rola (independente de dodge)
                rolled = Mathf.RoundToInt(rolled * AttributeStats.Formulas.critDamageMul);
            bool crit = attacker.stats.RollCritLast;
            target.TakeDamage(rolled, crit);
            ConsumeStrBuffOnHit(attacker);

            string deathTag = "<color=#ff6666>MORTO</color>";
            string hp       = target.IsDead ? deathTag : $"{target.currentHP}/{target.stats.MaxHP} HP";
            string icon     = crit ? "<color=#ffd700>★</color>" : (bonus ? "<color=#ffd700>*</color>" : ">");
            string tag      = (bonus || crit) ? " <color=#ffd700>+INCR</color>" : "";
            string aimTag   = aimDmg > 0 ? $" <color=#55ccff>+MIRA({aimDmg})</color>" : "";
            return $"{icon} {attacker.unitName} -> {target.unitName}: {rolled}{tag}{aimTag}  ({hp})";
        }

        /// <summary>Line trace do atacante ate o tile alvo, incluindo 'extra' tiles alem do alvo.</summary>
        private static List<Vector2Int> LineTiles(Vector2Int from, Vector2Int to, int extra)
        {
            var tiles = new List<Vector2Int>();
            int dx = to.x - from.x;
            int dy = to.y - from.y;
            int steps = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
            if (steps == 0) { tiles.Add(from); return tiles; }

            // Direcao normalizada por passo
            float stepX = (float)dx / steps;
            float stepY = (float)dy / steps;
            Vector2Int last = from;
            tiles.Add(from);
            for (int i = 1; i <= steps + extra; i++)
            {
                int x = Mathf.RoundToInt(from.x + stepX * i);
                int y = Mathf.RoundToInt(from.y + stepY * i);
                var cur = new Vector2Int(x, y);
                if (cur != last) { tiles.Add(cur); last = cur; }
            }
            return tiles;
        }

        // ---- Helpers ----
        private static int BaseDamage(Unit u) => u.stats.PhysicalDamage;

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
