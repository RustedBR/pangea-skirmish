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
                if (u == attacker || u.IsDead || u.team == attacker.team) continue;
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
            target.TakeDamage(dmg, bonus);
            ConsumeStrBuffOnHit(attacker);

            string deathTag = "<color=#ff6666>MORTO</color>";
            string hp       = target.IsDead ? deathTag : $"{target.currentHP}/{target.stats.MaxHP} HP";
            string icon     = bonus ? "<color=#ffd700>*</color>" : ">";
            string tag      = bonus ? " <color=#ffd700>+INCR</color>" : "";
            string aimTag   = aimDmg > 0 ? $" <color=#55ccff>+MIRA({aimDmg})</color>" : "";
            return $"{icon} {attacker.unitName} -> {target.unitName}: {dmg}{tag}{aimTag}  ({hp})";
        }

        // ---- Tile (posicional, bônus de dano) ----
        private static string ResolveTileAttack(Unit attacker, Vector2Int tile, IEnumerable<Unit> all)
        {
            int afp = attacker.stats.Footprint;
            if (GridManager.FootprintGap(attacker.anchor, afp, tile, afp) > attacker.stats.AttackRange)
                return $"<color=#888888>x</color> {attacker.unitName} fora de alcance";

            Unit target = null;
            foreach (var u in all)
            {
                if (u == attacker || u.IsDead || u.team == attacker.team) continue;
                if (GridManager.FootprintsOverlap(u.anchor, u.stats.Footprint, tile, afp))
                { target = u; break; }
            }

            if (target == null)
                return $"<color=#888888>x</color> {attacker.unitName} posicao vazia";

            bool bonus = ConsumeBonus(attacker);
            int  aimDmg = ConsumeAim(attacker) ? Mathf.RoundToInt(attacker.stats.DEX * Tuning.Get().aimDexMultiplier) : 0;
            int  strDmg = attacker.stats.strScalesDamage ? Mathf.RoundToInt(attacker.stats.STR * Tuning.Get().tileAttackStrMultiplier) : 0;
            int  dmg   = BaseDamage(attacker)
                       + strDmg
                       + (bonus ? AttributeStats.Formulas.incrementAttackDamage : 0)
                       + aimDmg;
            target.TakeDamage(dmg, bonus);
            ConsumeStrBuffOnHit(attacker);

            string deathTag = "<color=#ff6666>MORTO</color>";
            string hp       = target.IsDead ? deathTag : $"{target.currentHP}/{target.stats.MaxHP} HP";
            string icon     = bonus ? "<color=#ffd700>*</color>" : ">";
            string tag      = bonus ? " <color=#ffd700>+INCR</color>" : "";
            string aimTag   = aimDmg > 0 ? $" <color=#55ccff>+MIRA({aimDmg})</color>" : "";
            return $"{icon} {attacker.unitName} -> {target.unitName}: {dmg}{tag}{aimTag}  ({hp})";
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
