using System;
using System.Collections;
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
        public static int SpendableMana(Unit caster, PlannedSpell s)
        {
            int mana = Mathf.Min(s.Mana, caster.currentMana);
            if (mana <= 0) return 0;
            caster.currentMana -= mana;
            return mana;
        }

        public static bool WillResolve(Unit caster, PlannedSpell s, List<Unit> all)
        {
            if (s.Mana <= 0) return false;
            int range = SpellBook.SpellRange(caster, s.Mana);
            switch (s.Target)
            {
                case SpellTargetKind.Self:
                    return caster.currentMana >= s.Mana;
                case SpellTargetKind.Unit:
                    return s.TargetUnit != null && !s.TargetUnit.IsDead
                        && GridManager.FootprintGap(caster.anchor, caster.stats.Footprint,
                            s.TargetUnit.anchor, s.TargetUnit.stats.Footprint) <= range;
                case SpellTargetKind.Tile:
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

            int P = SpellBook.Potency(caster, s.Element);
            PendingPush? push = null;

            switch (s.Target)
            {
                case SpellTargetKind.Self:
                    return ResolveSelf(caster, s.Element, P, s.Direction);
                case SpellTargetKind.Unit:
                    return ResolveUnit(caster, s, s.TargetUnit, P, all, ref push);
                case SpellTargetKind.Tile:
                    return ResolveTile(caster, s, P, grid, tileFx, ref push);
            }

            return ($"{caster.unitName} magia sem efeito", null);
        }

        private static (string, PendingPush?) ResolveSelf(Unit caster, SpellElement element, int P, Vector2Int direction)
        {
            var T = Tuning.Get();
            switch (element)
            {
                case SpellElement.Physical:
                {
                    int strB = Mathf.RoundToInt(P * T.physicalBuffStrFactor);
                    int vitB = Mathf.RoundToInt(P * T.physicalBuffVitFactor);
                    var fx = new StatusEffect
                    {
                        Kind = StatusEffectKind.PhysicalMight,
                        Element = element,
                        StrBonus = strB,
                        VitBonus = vitB,
                        RoundsLeft = T.physicalBuffDurationRounds
                    };
                    StatusEffectSystem.AddOrReplace(caster.statusEffects, fx);
                    return ($"{caster.unitName} ganhou +{strB}STR/+{vitB}VIT por {fx.RoundsLeft} rounds", null);
                }
                case SpellElement.Magic:
                {
                    int shield = Mathf.RoundToInt(P * T.shieldCapacityFactor);
                    var fx = new StatusEffect
                    {
                        Kind = StatusEffectKind.MagicShield,
                        Element = element,
                        ShieldRemaining = shield,
                        RoundsLeft = T.spellBuffDurationRounds
                    };
                    StatusEffectSystem.AddOrReplace(caster.statusEffects, fx);
                    return ($"{caster.unitName} escudo mágico ({shield}) por {fx.RoundsLeft} rounds", null);
                }
                default:
                {
                    int resist = Mathf.RoundToInt(P * T.spellResistFactor);
                    var fx = new StatusEffect
                    {
                        Kind = StatusEffectKind.ElementResist,
                        Element = element,
                        ResistAmount = resist,
                        RoundsLeft = T.spellBuffDurationRounds
                    };
                    StatusEffectSystem.AddOrReplace(caster.statusEffects, fx);
                    return ($"{caster.unitName} resistência a {SpellBook.ElementName(element)} +{resist} por {fx.RoundsLeft} rounds", null);
                }
            }
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

            switch (s.Element)
            {
                case SpellElement.Physical:
                {
                    bool column = dir.x != 0;
                    int index = column ? cell.x + Math.Sign(dir.x) : cell.y + Math.Sign(dir.y);
                    int copyFrom = column ? cell.x : cell.y;
                    int maxSize = Tuning.Get().spellFoldMaxGridSize;
                    if ((column && grid.width + 1 > maxSize) || (!column && grid.height + 1 > maxSize))
                        return ($"{caster.unitName} grid no tamanho maximo", null);

                    var newMap = grid.InsertLine(column, index, copyFrom);
                    if (newMap == null)
                        return ($"{caster.unitName} dobra falhou", null);

                    GridRemap.InsertLineAndRemap(grid, column, index, copyFrom, newMap, null, tileFx);
                    var label = column ? "col" : "linha";
                    return ($"{caster.unitName} expandiu o grid ({label} {index})", null);
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