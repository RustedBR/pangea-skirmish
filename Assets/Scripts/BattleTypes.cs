using System;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>Time a que uma unidade pertence.</summary>
    public enum Team { Player, Enemy }

    /// <summary>Fases do round no combate "semi-action".</summary>
    public enum RoundPhase { Initiative, Planning, ActionMovement, ActionAttack, ActionSpell, Resolving, GameOver }

    /// <summary>Resultado de uma jogada de ataque.</summary>
    public enum HitResult { Miss, Hit, Critical }

    [Serializable]
    public class StatFormulas
    {
        [Header("HP / Mana")]
        public float hpBase    = 10f;
        public float hpPerVIT  = 2f;
        public float manaBase  = 5f;
        public float manaPerWIS = 1f;

        [Header("Dano")]
        public float weaponBase = 2f;
        public float dmgPerSTR  = 1f;
        public float dmgPerINT  = 1f;

        [Header("Iniciativa")]
        public float iniPerAGI = 1f;
        public float iniPerDEX = 1f;

        [Header("Movimento / Ações")]
        public float movePerAGI = 1f;
        public float apPerAGI   = 1f;
        public float bapPerDEX  = 1f;
        public int   bonusMoveBudget = 1;

        [Header("Incremento (ação bônus)")]
        public int incrementAttackDamage = 3;
        public int incrementMoveTiles    = 1;

        [Header("Precisão / Esquiva")]
        public float baseHitChance = 0.70f;
        public float hitPerDEX    = 0.03f;
        public float dodgePerAGI   = 0.02f;
        public float critPerDEX    = 0.01f;
        public float critDamageMul = 1.5f;
        public float damageVariance = 0.15f;

        [Header("Defesa / Mitigação")]
        public int armorBase     = 0;
        public int armorPerVIT   = 1;
        public int magicResistBase  = 0;
        public int magicResistPerWIS = 1;

        [Header("Magia")]
        public float manaRegenBase = 2f;
        public float manaRegenPerWIS = 0.5f;
    }

    /// <summary>
    /// Bloco de status inicial de uma entidade — editável no GameTuning.
    /// Atributos em float (balanceamento fino); footprint/alcance em tiles (int).
    /// </summary>
    [Serializable]
    public class UnitStatBlock
    {
        public float STR = 1f, VIT = 1f, DEX = 1f, AGI = 1f, INT = 1f, WIS = 1f;
        public int   Footprint   = 3;
        public int   AttackRange = 1;

        public AttributeStats ToAttributeStats() => new AttributeStats
        {
            STR = STR, VIT = VIT, DEX = DEX, AGI = AGI, INT = INT, WIS = WIS,
            Footprint = Footprint, AttackRange = AttackRange,
        };
    }

    [Serializable]
    public class AttributeStats
    {
        [Header("Atributos primários")]
        public float STR = 1f;
        public float VIT = 1f;
        public float DEX = 1f;
        public float AGI = 1f;
        public float INT = 1f;
        public float WIS = 1f;

        public const int DefaultFootprint = 3;
        public int Footprint = DefaultFootprint;

        public static StatFormulas Formulas = new StatFormulas();
        private static StatFormulas F => Formulas ?? (Formulas = new StatFormulas());

        public int WeaponDamage = -1;
        public int AttackRange = 1;
        public bool strScalesDamage = true; // false para armas ranged com range > strDamageMaxRange

        public int MaxHP => Mathf.RoundToInt(F.hpBase + VIT * F.hpPerVIT);
        public int MaxMana => Mathf.RoundToInt(F.manaBase + WIS * F.manaPerWIS);
        public int PhysicalDamage => Mathf.RoundToInt((WeaponDamage >= 0 ? WeaponDamage : F.weaponBase) + (strScalesDamage ? STR * F.dmgPerSTR : 0));
        public int MagicDamage => Mathf.RoundToInt(INT * F.dmgPerINT);
        public int Initiative => Mathf.RoundToInt(AGI * F.iniPerAGI + DEX * F.iniPerDEX);
        public int MoveBudget => Footprint + Mathf.RoundToInt(AGI * F.movePerAGI);
        public int ActionPoints => Mathf.Max(Tuning.Get().minActionPoints, Mathf.RoundToInt(AGI * F.apPerAGI));
        public int BonusActionPoints => Mathf.RoundToInt(DEX * F.bapPerDEX);
        public int BonusMoveBudget => Mathf.Max(1, F.bonusMoveBudget);

        public int ManaRegen => Mathf.RoundToInt(F.manaRegenBase + WIS * F.manaRegenPerWIS);

        public float HitChance => Mathf.Clamp01(F.baseHitChance + DEX * F.hitPerDEX);
        public float DodgeChance => Mathf.Clamp01(AGI * F.dodgePerAGI);
        public float CritChance => Mathf.Clamp01(DEX * F.critPerDEX);
        public int PhysicalDefense => F.armorBase + Mathf.RoundToInt(VIT * F.armorPerVIT);
        public int MagicDefense => F.magicResistBase + Mathf.RoundToInt(WIS * F.magicResistPerWIS);

        public HitResult RollHit(float attackerHitChance, float targetDodgeChance)
        {
            // Fixed-point: evitar float-ordering divergente em MP
            if (BattleRng.Roll10000() < Mathf.RoundToInt(CritChance * 10000f))
                return HitResult.Critical;
            float effectiveChance = attackerHitChance - targetDodgeChance;
            if (BattleRng.Roll10000() < Mathf.RoundToInt(effectiveChance * 10000f))
                return HitResult.Hit;
            return HitResult.Miss;
        }

        /// <summary>Critico independente de dodge (usado em ataques de tile, que nao rolam esquiva).</summary>
        private bool _rollCritLast;
        public bool RollCritLast => _rollCritLast;
        public bool RollCrit()
        {
            _rollCritLast = BattleRng.Roll10000() < Mathf.RoundToInt(CritChance * 10000f);
            return _rollCritLast;
        }

        public int RollDamage(int baseDamage, bool isPhysical)
        {
            float halfRange = baseDamage * F.damageVariance;
            int varInt = Mathf.RoundToInt(halfRange);
            // [−varInt, +varInt] determinístico
            int variance = varInt > 0 ? BattleRng.Next(-varInt, varInt + 1) : 0;
            int dmg = baseDamage + variance;
            dmg -= isPhysical ? PhysicalDefense : MagicDefense;
            return Mathf.Max(Tuning.Get().minDamageFloor, dmg);
        }
    }

    // ------------------------------------------------------------------ ARMAS

    /// <summary>Definição de uma arma (dano base e alcance).</summary>
    [Serializable]
    public class WeaponDef
    {
        public string id;          // = prefixo do sprite, ex "Hatchet" → HatchetattackNE_0
        public string displayName; // ex "Machado"
        public int    damage = 3;  // dano base da arma
        public int    range  = 1;  // alcance (tiles)

        [Header("Magia (conduíte)")]
        public float spellPowerMult = 1f;
        public int   spellRangeBonus = 0;
        public SpellElement elementAffinity = SpellElement.None;
    }

    // ------------------------------------------------------------------ SEQUÊNCIA DE AÇÕES

    public enum ActionType { Move, Attack, Spell, Concentrate }

    /// <summary>
    /// Uma entrada na sequência de ações planejadas de uma unidade.
    /// Index aponta para plannedPath (Move) ou plannedAttacks (Attack).
    /// IsBonus = potencializada com 1 PAB (Golpe Poderoso / Passo Rápido).
    /// BonusStep = destino do passo extra (válido se Type==Move && IsBonus).
    /// </summary>
    public struct ScheduledAction
    {
        public ActionType Type;
        public int        Index;
        public bool       IsBonus;     // potencializada com 1 PAB (Golpe Poderoso/Incremento)
        public bool       IsAimed;     // mirada com 1 PAB (Mirar): +DEX no próximo ataque
        public Vector2Int BonusStep;   // destino do passo extra (Type==Move && IsBonus)
    }

    // ------------------------------------------------------------------ ATAQUE

    public enum AttackMode { Auto, Unit, Tile }

    /// <summary>Ataque planejado pelo jogador (ou IA) com alvo específico ou posição.</summary>
    public struct PlannedAttack
    {
        public AttackMode    Mode;
        public Unit          TargetUnit; // Mode == Unit
        public Vector2Int    TargetTile; // Mode == Tile
    }
}
