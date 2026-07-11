using System;
using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    public struct PendingPush
    {
        public Unit Target;
        public Vector2Int Direction;
        public int Tiles;
    }

    public static class SpellResolver
    {
        /// <summary>
        /// Gasta a mana reservada (ManaRange + ManaPower). Retorna o total gasto.
        /// </summary>
        public static int SpendableMana(Unit caster, PlannedSpell s)
        {
            int mana = Mathf.Min(s.ManaRange + s.ManaPower, caster.currentMana);
            if (mana <= 0) return 0;
            caster.currentMana -= mana;
            return mana;
        }

        public static bool WillResolve(Unit caster, PlannedSpell s, List<Unit> all, GridManager grid)
        {
            if (s.ManaRange < 0 || s.ManaPower < 1) return false;
            int range = SpellBook.SpellRange(caster, s.ManaRange);
            switch (s.Target)
            {
                case SpellTargetKind.Self:
                    return caster.currentMana >= (s.ManaRange + s.ManaPower);
                case SpellTargetKind.Unit:
                    return s.TargetUnit != null && !s.TargetUnit.IsDead
                        && GridManager.FootprintGap(caster.anchor, caster.stats.Footprint,
                            s.TargetUnit.anchor, s.TargetUnit.stats.Footprint) <= range;
                case SpellTargetKind.Tile:
                    // Teleporte (Physical) não tem potência — checa só alcance + tile livre.
                    if (!grid.IsAnchorInBounds(s.TargetTile, 1) || grid.IsVoid(s.TargetTile.x, s.TargetTile.y))
                        return false;
                    return GridManager.FootprintGap(caster.anchor, caster.stats.Footprint,
                        s.TargetTile, 1) <= range;
            }
            return false;
        }

        public static (string log, PendingPush? push) Resolve(Unit caster, PlannedSpell s, List<Unit> all, GridManager grid, TileEffectManager tileFx)
        {
            int spent = SpendableMana(caster, s);
            if (spent <= 0)
                return ($"<color=#888888>x</color> {caster.unitName} magia falhou sem mana", null);

            int P = SpellBook.DamagePotency(caster, s.Element, s.ManaPower);
            PendingPush? push = null;

            switch (s.Target)
            {
                case SpellTargetKind.Self:
                    return ResolveSelf(caster, s.Element, s.ManaRange, SpellBook.BuffPotency(caster, s.Element, s.ManaPower), s.Direction);
                case SpellTargetKind.Unit:
                    return ResolveUnit(caster, s, s.TargetUnit, P, all, ref push);
                case SpellTargetKind.Tile:
                    return ResolveTile(caster, s, P, grid, tileFx, ref push);
            }

            return ($"{caster.unitName} magia sem efeito", null);
        }

        /// <summary>
        /// Buff Self rework: cada elemento buffa os 2 atributos do seu par.
        /// durationRounds = ManaRange (alcance da magia Self = duração).
        /// attrAmount      = ManaPower (potência da magia Self = pontos de atributo).
        /// buffFactor = 1.0 (decidido com o Marcus): bônus = attrAmount exato.
        /// Não é consumido no hit — dura os rounds inteiros.
        /// </summary>
        private static (string, PendingPush?) ResolveSelf(Unit caster, SpellElement element, int durationRounds, int attrAmount, Vector2Int direction)
        {
            var T = Tuning.Get();
            int dur = Mathf.Max(1, durationRounds);
            int amt = Mathf.Max(1, attrAmount);

            var fx = new StatusEffect
            {
                Kind = StatusEffectKind.AttrBuff,
                Element = element,
                RoundsLeft = dur
            };

            string attrDesc = "";
            switch (element)
            {
                case SpellElement.Physical: fx.DexBonus = amt; fx.StrBonus = amt; attrDesc = $"+{amt}DEX/+{amt}STR"; break;
                case SpellElement.Magic:    fx.IntBonus = amt; fx.WisBonus = amt; attrDesc = $"+{amt}INT/+{amt}WIS"; break;
                case SpellElement.Fire:     fx.IntBonus = amt; fx.AgiBonus = amt; attrDesc = $"+{amt}INT/+{amt}AGI"; break;
                case SpellElement.Water:    fx.WisBonus = amt; fx.VitBonus = amt; attrDesc = $"+{amt}WIS/+{amt}VIT"; break;
                case SpellElement.Air:      fx.AgiBonus = amt; fx.IntBonus = amt; attrDesc = $"+{amt}AGI/+{amt}INT"; break;
                case SpellElement.Earth:    fx.VitBonus = amt; fx.StrBonus = amt; attrDesc = $"+{amt}VIT/+{amt}STR"; break;
                default: return ($"{caster.unitName} magia desconhecida", null);
            }

            StatusEffectSystem.AddOrReplace(caster.statusEffects, fx);
            return ($"{caster.unitName} ganhou {attrDesc} ({SpellBook.ElementName(element)}) por {dur} rounds", null);
        }

        private static (string, PendingPush?) ResolveUnit(Unit caster, PlannedSpell s, Unit target, int P, List<Unit> all, ref PendingPush? push)
        {
            if (target == null || target.IsDead)
                return ($"<color=#888888>x</color> {caster.unitName} alvo morto", null);

            if (s.Element == SpellElement.Air)
            {
                int dmg = target.ApplySpellDamage(P, s.Element);
                int pushTiles = Tuning.Get().airProjectilePushTiles;
                push = new PendingPush { Target = target, Direction = s.Direction, Tiles = pushTiles };
                string hp = target.IsDead ? "<color=#ff6666>MORTO</color>" : $"{target.currentHP}/{target.stats.MaxHP} HP";
                return ($"> {caster.unitName} -> {target.unitName}: {dmg} ({SpellBook.ElementName(s.Element)}) +empurrao  {hp}", push);
            }

            int d = target.ApplySpellDamage(P, s.Element);
            string deathTag = "<color=#ff6666>MORTO</color>";
            string hp2 = target.IsDead ? deathTag : $"{target.currentHP}/{target.stats.MaxHP} HP";
            return ($"> {caster.unitName} -> {target.unitName}: {d} ({SpellBook.ElementName(s.Element)})  {hp2}", null);
        }

        private static (string, PendingPush?) ResolveTile(Unit caster, PlannedSpell s, int P, GridManager grid, TileEffectManager tileFx, ref PendingPush? push)
        {
            var cell = s.TargetTile;
            var dir = s.Direction;

            // Validação comum: tile alvo deve estar dentro do mapa e não ser void.
            if (!grid.IsAnchorInBounds(cell, 1) || grid.IsVoid(cell.x, cell.y))
                return ($"{caster.unitName} magia em tile inválido ({cell})", null);

            switch (s.Element)
            {
                case SpellElement.Physical:
                {
                    // TELETE: caster se move para o tile alvo (só alcance, sem potência).
                    // Tile destino deve estar livre (sem unidade/parede).
                    if (GridManager.FootprintsOverlapAny(cell, 1, caster))
                        return ($"{caster.unitName} teleporte falhou (tile ocupado)", null);
                    caster.TeleportTo(cell);
                    return ($"{caster.unitName} teleportou para {cell}", null);
                }
                case SpellElement.Magic:
                {
                    tileFx.InjectMana(cell, P);
                    return ($"{caster.unitName} injetou mana em {cell} ({P})", null);
                }
                case SpellElement.Fire:
                {
                    tileFx.Apply(TileEffectKind.Fire, cell, P, dir, caster.stats.Footprint);
                    return ($"{caster.unitName} botou fogo em {cell} ({P})", null);
                }
                case SpellElement.Water:
                {
                    tileFx.Apply(TileEffectKind.Water, cell, P, dir, caster.stats.Footprint);
                    return ($"{caster.unitName} agua em {cell}", null);
                }
                case SpellElement.Air:
                {
                    tileFx.Apply(TileEffectKind.Wind, cell, P, dir, caster.stats.Footprint);
                    return ($"{caster.unitName} vento em {cell} direcao {dir}", null);
                }
                case SpellElement.Earth:
                {
                    int existingTile = grid.GetTileIndex(cell.x, cell.y);
                    int stoneIdx = Tuning.Get().earthStoneTileIndex;
                    if (existingTile == stoneIdx)
                    {
                        int newH = grid.HeightAt(cell.x, cell.y) + Tuning.Get().earthRaiseAmount;
                        grid.SetCell(cell.x, cell.y, stoneIdx, newH);
                        foreach (var u in FindUnitsAtCell(caster, cell, grid))
                        {
                            u.SnapToAnchor();
                        }
                        return ($"{caster.unitName} elevou terreno em {cell}", null);
                    }
                    return ($"{caster.unitName} so eleva pedra ({stoneIdx})", null);
                }
            }

            return ("", null);
        }

        private static List<Unit> FindUnitsAtCell(Unit caster, Vector2Int cell, GridManager grid)
        {
            var units = new List<Unit>();
            foreach (var u in UnityEngine.Object.FindObjectsOfType<Unit>())
            {
                if (!u.IsDead && GridManager.FootprintsOverlap(u.anchor, u.stats.Footprint, cell, 1))
                    units.Add(u);
            }
            return units;
        }
    }
}
