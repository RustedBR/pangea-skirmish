using System;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>Time a que uma unidade pertence.</summary>
    public enum Team { Player, Enemy }

    /// <summary>Fases do round no combate "semi-action".</summary>
    public enum RoundPhase { Initiative, Planning, ActionMovement, ActionAttack, Resolving, GameOver }

    /// <summary>Resultado de uma jogada de ataque.</summary>
    public enum HitResult { Miss, Hit, Critical }

    [Serializable]
    public class StatFormulas
    {
        [Header("HP / Mana")]
        public float hpBase    = 10f;
        public float hpPerVIT  = 2f;
        public float manaBase  = 6f;
        public float manaPerWIS = 1.5f;

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
        [Tooltip("Redução PERCENTUAL de dano físico por ponto de VIT (substitui a armadura fixa: dano alto passa a ser reduzido na mesma proporção).")]
        public float armorPctPerVIT = 0f;
        public float armorPctMax    = 0.6f;
        public int magicResistBase  = 0;
        public int magicResistPerWIS = 1;

        [Header("Magia")]
        [Tooltip("Legado: regen por round. A Mana agora volta por tick (GameTuning.manaRegenTicks) e Concentrar dá GameTuning.concentrateMana.")]
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
            STR = STR, VIT = VIT, DEX = DEX, AGI = Mathf.Min(AGI, CharacterConfig.AgiMax), INT = INT, WIS = WIS,
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
        /// <summary>
        /// Tiles que UMA ação de andar cobre (GDD-016 §2): base + AGI, até maxTilesPerMove.
        /// </summary>
        public int MoveStep
        {
            get
            {
                var T = Tuning.Get();
                int tiles = Mathf.RoundToInt(T.moveTilesBase + AGI * T.moveTilesPerAGI);
                return Mathf.Clamp(tiles, 1, Mathf.Max(1, T.maxTilesPerMove));
            }
        }

        /// <summary>
        /// Ticks entre cada ponto de Estâmina regenerado. A AGI acima de agiRegenFrom acelera:
        /// a andada já está no teto, então o excedente vira fôlego (GDD-016 §13).
        /// </summary>
        public int StaminaRegenTicks
        {
            get
            {
                var T = Tuning.Get();
                int bonus = Mathf.Max(0, Mathf.FloorToInt(AGI) - T.agiRegenFrom) / Mathf.Max(1, T.agiRegenStep);
                return Mathf.Max(1, T.staminaRegenTicks - bonus);
            }
        }

        /// <summary>Ticks base do ataque, pelo peso da arma equipada (GDD-016 §2.1).</summary>
        public int AttackTicks = 2;

        /// <summary>Estâmina máxima: reserva do Corpo (GDD-016 §3).</summary>
        public int MaxStamina
        {
            get
            {
                var T = Tuning.Get();
                return Mathf.Max(1, Mathf.RoundToInt(T.staminaBase + (STR + VIT) * T.staminaPerBodyPoint));
            }
        }
        public int ActionPoints => Mathf.Max(Tuning.Get().minActionPoints, Mathf.RoundToInt(AGI * F.apPerAGI));
        public int BonusActionPoints => Mathf.RoundToInt(DEX * F.bapPerDEX);
        public int BonusMoveBudget => Mathf.Max(1, F.bonusMoveBudget);

        /// <summary>
        /// Ticks entre cada ponto de Mana regenerado (GDD-019 §4). A WIS acelera:
        /// a cada wisRegenStep pontos, a Mana volta 1 tick mais cedo.
        /// </summary>
        public int ManaRegenTicks
        {
            get
            {
                var T = Tuning.Get();
                int bonus = Mathf.FloorToInt(WIS) / Mathf.Max(1, T.wisRegenStep);
                return Mathf.Max(1, T.manaRegenTicks - bonus);
            }
        }

        /// <summary>Mana imediata que a ação Concentrar dá, além da regeneração normal (GDD-019 §4).</summary>
        public int ConcentrateMana => Mathf.Max(0, Tuning.Get().concentrateMana);

        /// <summary>Legado (regen por round). Mantido só para leitura; o jogo usa ManaRegenTicks + ConcentrateMana.</summary>
        public int ManaRegen => Mathf.RoundToInt(F.manaRegenBase + WIS * F.manaRegenPerWIS);

        public float HitChance => Mathf.Clamp01(F.baseHitChance + DEX * F.hitPerDEX);
        public float DodgeChance => Mathf.Clamp01(AGI * F.dodgePerAGI);
        public float CritChance => Mathf.Clamp01(DEX * F.critPerDEX);
        public int PhysicalDefense => F.armorBase + Mathf.RoundToInt(VIT * F.armorPerVIT);
        /// <summary>Redução percentual do dano físico pela VIT (0..armorPctMax).</summary>
        public float PhysicalReduction => Mathf.Clamp(VIT * F.armorPctPerVIT, 0f, F.armorPctMax);
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
            if (isPhysical) dmg = Mathf.RoundToInt(dmg * (1f - PhysicalReduction));
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
        [Tooltip("Peso da arma em ticks base do ataque: 1 leve, 2 média, 3 pesada. A DEX encurta.")]
        public int    attackTicks = 2;
        [Tooltip("Dureza: quanto dano FIXO o Bloquear absorve com esta arma/escudo (PF2e: Shield Block).")]
        public int    blockHardness = 2;

        [Header("Magia (conduíte)")]
        public SpellElement elementAffinity = SpellElement.None;
    }

    // ------------------------------------------------------------------ SEQUÊNCIA DE AÇÕES

    public enum ActionType { Move, Attack, Concentrate, Wait }

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

    /// <summary>
    /// Ação já comprometida na fila de ticks da unidade (GDD-014). Autocontida: guarda
    /// destino/ataque por valor, então sobrevive entre pontos de decisão (carry-over).
    /// </summary>
    [Serializable]
    public class QueuedAction
    {
        public ActionType    Type;
        public Vector2Int    Dest;       // Move
        public PlannedAttack Attack;     // Attack
        public bool          IsBonus;
        public bool          IsAimed;
        public int           Duration;   // ticks totais
        public int           Elapsed;    // ticks já decorridos
        public int Remaining => Mathf.Max(0, Duration - Elapsed);
    }

    /// <summary>Duração das ações em ticks (GDD-014 §2). Determinístico (só stats + tuning).</summary>
    public static class TickRules
    {
        public static int BaseTicks(ActionType type, bool isBonus)
        {
            var T = Tuning.Get();
            switch (type)
            {
                case ActionType.Move:        return T.ticksMove;
                case ActionType.Attack:      return T.ticksUnarmed + (isBonus ? T.ticksPowerStrikeExtra : 0);
                case ActionType.Concentrate: return T.ticksConcentrate;
                default:                     return T.ticksWait;
            }
        }

        /// <summary>
        /// ticks = max(1, round(base × k / (k + DEX))). A DEX é a rapidez das ações (GDD-016);
        /// Andar e Esperar não escalam (o alcance da andada é da AGI). A base do ataque é o
        /// peso da arma da unidade.
        /// </summary>
        public static int Duration(Unit u, ActionType type, bool isBonus)
        {
            int b = BaseTicks(type, isBonus);
            if (u == null) return Mathf.Max(1, b);
            if (type == ActionType.Attack)
                b = Mathf.Max(1, u.stats.AttackTicks) + (isBonus ? Tuning.Get().ticksPowerStrikeExtra : 0);
            if (type == ActionType.Wait || type == ActionType.Move) return Mathf.Max(1, b);
            float k = Mathf.Max(1f, Tuning.Get().tickDexFactor);
            return Mathf.Max(1, Mathf.RoundToInt(b * k / (k + Mathf.Max(0f, u.stats.DEX))));
        }

        // ---- Custos em Estâmina (GDD-016 §3) ----
        public static int StaminaCost(ScheduledAction a)
        {
            var T = Tuning.Get();
            int c = 0;
            if (a.Type == ActionType.Attack && a.IsBonus) c += T.staminaPowerStrike;
            if (a.Type == ActionType.Attack && a.IsAimed) c += T.staminaAim;
            if (a.Type == ActionType.Move   && a.IsBonus) c += T.staminaQuickStep;
            return c;
        }

        public static int Duration(Unit u, ScheduledAction a) => Duration(u, a.Type, a.IsBonus);
    }

    // ------------------------------------------------------------------ ATAQUE

    public enum AttackMode { Auto, Unit, Tile }

    /// <summary>Reações do rework de Ações Bônus. Disparadas por trigger na fase de ação.</summary>
    public enum ReactionKind { None, CounterAttack, Dodge, Block, Step, Intercept }

    /// <summary>Ataque planejado pelo jogador (ou IA) com alvo específico ou posição.</summary>
    public struct PlannedAttack
    {
        public AttackMode    Mode;
        public Unit          TargetUnit; // Mode == Unit
        public Vector2Int    TargetTile; // Mode == Tile
    }
}
