// Net/PlanWire.cs
// DTOs serializáveis para transmissão de planos de round via rede (LockstepBattleSync).
// TargetUnit é substituído por uint targetUnitId para serialização JSON plana.
// Conversores: FromUnit / ApplyToUnit via UnitRegistry.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    [Serializable]
    public class AttackWire
    {
        /// <summary>AttackMode cast para int: 0=Auto, 1=Unit, 2=Tile</summary>
        public int mode;
        /// <summary>UnitRegistry id do alvo (mode==Unit); 0 = sem alvo.</summary>
        public uint targetUnitId;
        public int  targetTileX;
        public int  targetTileY;
    }

    [Serializable]
    public class SpellWire
    {
        /// <summary>SpellElement cast para int.</summary>
        public int  element;
        /// <summary>SpellTargetKind cast para int.</summary>
        public int  targetKind;
        /// <summary>UnitRegistry id (targetKind==Unit); 0 = sem alvo.</summary>
        public uint targetUnitId;
        public int  targetTileX;
        public int  targetTileY;
        public int  manaRange;  // alcance/duração
        public int  manaPower;   // potência/atributo
        public int  directionX;
        public int  directionY;
    }

    [Serializable]
    public class ScheduledWire
    {
        /// <summary>ActionType cast para int.</summary>
        public int  type;
        public int  index;
        public bool isBonus;
        public bool isAimed;
        public int  bonusStepX;
        public int  bonusStepY;
    }

    [Serializable]
    public class UnitPlanWire
    {
        public uint   unitId;
        public int    plannedMoveCount;
        public int    plannedAnchorX;
        public int    plannedAnchorY;
        public int    plannedBonusAnchorX;
        public int    plannedBonusAnchorY;
        public bool   hasPlannedBonus;
        public int    concentrations;
        public int    reservedMana;

        /// <summary>Caminho planejado (posições X serializado em paralelo a pathY).</summary>
        public List<int> pathX = new List<int>();
        public List<int> pathY = new List<int>();

        public List<AttackWire>    attacks  = new List<AttackWire>();
        public List<SpellWire>     spells   = new List<SpellWire>();
        public List<ScheduledWire> sequence = new List<ScheduledWire>();

        // ---- Conversores -------------------------------------------------------

        /// <summary>Serializa o plano atual de uma unidade para transmissão.</summary>
        public static UnitPlanWire FromUnit(Unit u)
        {
            var w = new UnitPlanWire
            {
                unitId               = UnitRegistry.GetId(u),
                plannedMoveCount     = u.plannedMoveCount,
                plannedAnchorX       = u.plannedAnchor.x,
                plannedAnchorY       = u.plannedAnchor.y,
                plannedBonusAnchorX  = u.plannedBonusAnchor.x,
                plannedBonusAnchorY  = u.plannedBonusAnchor.y,
                hasPlannedBonus      = u.hasPlannedBonus,
                concentrations       = u.plannedConcentrations,
                reservedMana         = u.reservedMana,
            };

            foreach (var p in u.plannedPath)
            {
                w.pathX.Add(p.x);
                w.pathY.Add(p.y);
            }

            foreach (var atk in u.plannedAttacks)
            {
                w.attacks.Add(new AttackWire
                {
                    mode         = (int)atk.Mode,
                    targetUnitId = atk.TargetUnit != null ? UnitRegistry.GetId(atk.TargetUnit) : 0u,
                    targetTileX  = atk.TargetTile.x,
                    targetTileY  = atk.TargetTile.y,
                });
            }

            foreach (var sp in u.plannedSpells)
            {
                w.spells.Add(new SpellWire
                {
                    element      = (int)sp.Element,
                    targetKind   = (int)sp.Target,
                    targetUnitId = sp.TargetUnit != null ? UnitRegistry.GetId(sp.TargetUnit) : 0u,
                    targetTileX  = sp.TargetTile.x,
                    targetTileY  = sp.TargetTile.y,
                    manaRange   = sp.ManaRange,
                    manaPower   = sp.ManaPower,
                    directionX   = sp.Direction.x,
                    directionY   = sp.Direction.y,
                });
            }

            foreach (var act in u.actionSequence)
            {
                w.sequence.Add(new ScheduledWire
                {
                    type       = (int)act.Type,
                    index      = act.Index,
                    isBonus    = act.IsBonus,
                    isAimed    = act.IsAimed,
                    bonusStepX = act.BonusStep.x,
                    bonusStepY = act.BonusStep.y,
                });
            }

            return w;
        }

        /// <summary>Aplica o plano serializado a uma unidade local (via UnitRegistry).</summary>
        public static void ApplyToUnit(UnitPlanWire w, Unit u)
        {
            if (u == null) return;

            u.plannedMoveCount     = w.plannedMoveCount;
            u.plannedAnchor        = new Vector2Int(w.plannedAnchorX, w.plannedAnchorY);
            u.plannedBonusAnchor   = new Vector2Int(w.plannedBonusAnchorX, w.plannedBonusAnchorY);
            u.hasPlannedBonus      = w.hasPlannedBonus;
            u.plannedConcentrations = w.concentrations;
            u.reservedMana         = w.reservedMana;

            u.plannedPath.Clear();
            int n = Mathf.Min(w.pathX.Count, w.pathY.Count);
            for (int i = 0; i < n; i++)
                u.plannedPath.Add(new Vector2Int(w.pathX[i], w.pathY[i]));

            u.plannedAttacks.Clear();
            foreach (var aw in w.attacks)
            {
                Unit target = aw.targetUnitId != 0 ? UnitRegistry.Get(aw.targetUnitId) : null;
                u.plannedAttacks.Add(new PlannedAttack
                {
                    Mode       = (AttackMode)aw.mode,
                    TargetUnit = target,
                    TargetTile = new Vector2Int(aw.targetTileX, aw.targetTileY),
                });
            }

            u.plannedSpells.Clear();
            foreach (var sw in w.spells)
            {
                Unit target = sw.targetUnitId != 0 ? UnitRegistry.Get(sw.targetUnitId) : null;
                u.plannedSpells.Add(new PlannedSpell
                {
                    Element    = (SpellElement)sw.element,
                    Target     = (SpellTargetKind)sw.targetKind,
                    TargetUnit = target,
                    TargetTile = new Vector2Int(sw.targetTileX, sw.targetTileY),
                    ManaRange = sw.manaRange,
                    ManaPower  = sw.manaPower,
                    Direction  = new Vector2Int(sw.directionX, sw.directionY),
                });
            }

            u.actionSequence.Clear();
            foreach (var seq in w.sequence)
            {
                u.actionSequence.Add(new ScheduledAction
                {
                    Type      = (ActionType)seq.type,
                    Index     = seq.index,
                    IsBonus   = seq.isBonus,
                    IsAimed   = seq.isAimed,
                    BonusStep = new Vector2Int(seq.bonusStepX, seq.bonusStepY),
                });
            }
        }
    }

    [Serializable]
    public class RoundPlansWire
    {
        public int roundSeed;
        /// <summary>Número do round (definido pelo host). O cliente sincroniza seu contador
        /// local com este valor — sem isso, o cliente nunca incrementava _currentRound
        /// (só o host faz isso em BroadcastRound), então o cliente sempre reportava
        /// "round 0" e o hash dele nunca caía no mesmo "balde" do round real do host.</summary>
        public int roundNum;
        public List<UnitPlanWire> plans = new List<UnitPlanWire>();
    }
}
