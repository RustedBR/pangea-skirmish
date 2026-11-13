using System;
using System.Collections.Generic;
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
        [Tooltip("Legado (antes do dano por tile). O dano agora é dano por tile × (1 + atributo × dmgPctPerAttr).")]
        public float dmgPerDEXRanged = 3f;
        [Tooltip("GDD-020: cada ponto de STR (corpo a corpo) ou DEX (distância) aumenta o dano por tile nesta fração.")]
        public float dmgPctPerAttr = 0.06f;

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
        /// <summary>Arma à distância: o dano escala com a DEX (antes não escalava com nada: o Arco Longo batia 1).</summary>
        public bool dexScalesDamage = false;

        public int MaxHP => Mathf.RoundToInt(F.hpBase + VIT * F.hpPerVIT);
        public int MaxMana => Mathf.RoundToInt(F.manaBase + WIS * F.manaPerWIS);
        /// <summary>
        /// GDD-020: dano de CADA tile que o golpe pega no alvo. Dano da arma × (1 + atributo × %):
        /// STR para corpo a corpo, DEX para distância. A porcentagem não favorece armas largas nem estreitas.
        /// </summary>
        public float DamagePerTile
        {
            get
            {
                float attr = strScalesDamage ? STR : dexScalesDamage ? DEX : 0f;
                return (WeaponDamage >= 0 ? WeaponDamage : F.weaponBase) * (1f + Mathf.Max(0f, attr) * F.dmgPctPerAttr);
            }
        }
        /// <summary>Tiles que a forma da arma pega num alvo 3×3 quando centralizada nele (clique na unidade).</summary>
        public int ShapeTiles = 1;
        /// <summary>Dano de um golpe centralizado num alvo 3×3 (antes de defesa): dano por tile × tiles da forma.</summary>
        public int PhysicalDamage => Mathf.RoundToInt(DamagePerTile * Mathf.Max(1, ShapeTiles));

        /// <summary>Peso da arma: o atributo conta × (ticks ÷ 2). Arma de 1 tick bate metade por golpe, a de 3 bate 1,5×.</summary>
        public float WeightFactor => Mathf.Max(1, AttackTicks) / 2f;
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
        /// Ticks entre cada ponto de Stamina regenerado. A AGI acima de agiRegenFrom acelera:
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

        /// <summary>Stamina máxima: reserva do Corpo (GDD-016 §3). STR, VIT e DEX contam; a AGI acelera a regeneração.</summary>
        public int MaxStamina
        {
            get
            {
                var T = Tuning.Get();
                return Mathf.Max(1, Mathf.RoundToInt(T.staminaBase + (STR + VIT + DEX) * T.staminaPerBodyPoint));
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

        /// <summary>
        /// Rola o dano de um golpe destas stats (atacante) contra <paramref name="defender"/>.
        /// A variância é do atacante; armadura % (VIT), defesa flat e defesa mágica são do DEFENSOR.
        /// Antes de 2026-11-11 a redução usava as stats do próprio atacante (VIT alta batia mais
        /// fraco e não protegia). Sem defensor, usa estas stats (compatibilidade de testes antigos).
        /// </summary>
        public int RollDamage(int baseDamage, bool isPhysical, AttributeStats defender = null)
        {
            var d = defender ?? this;
            float halfRange = baseDamage * F.damageVariance;
            int varInt = Mathf.RoundToInt(halfRange);
            // [−varInt, +varInt] determinístico
            int variance = varInt > 0 ? BattleRng.Next(-varInt, varInt + 1) : 0;
            int dmg = baseDamage + variance;
            if (isPhysical) dmg = Mathf.RoundToInt(dmg * (1f - d.PhysicalReduction));
            dmg -= isPhysical ? d.PhysicalDefense : d.MagicDefense;
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
        [Tooltip("GDD-020: dano POR TILE que a forma pega no alvo.")]
        public int    damage = 3;
        public int    range  = 1;  // alcance (tiles)
        [Tooltip("GDD-020: forma do golpe (quais tiles ele pega). Gira na direção do golpe.")]
        public AttackShapeKind shape = AttackShapeKind.Single;
        [Tooltip("Largura da forma (perpendicular ao golpe). Linha/1 tile: 1.")]
        public int shapeWidth = 1;
        [Tooltip("Comprimento da forma (na direção do golpe).")]
        public int shapeLength = 1;
        [Tooltip("Peso da arma em ticks base do ataque: 1 leve, 2 média, 3 pesada. A DEX encurta.")]
        public int    attackTicks = 2;
        [Tooltip("Dureza: quanto dano FIXO o Bloquear absorve com esta arma/escudo (PF2e: Shield Block).")]
        public int    blockHardness = 2;

        [Header("Magia (conduíte)")]
        public SpellElement elementAffinity = SpellElement.None;
        [Tooltip("Multiplica o dano/cura das magias de quem empunha (cajados são conduítes melhores).")]
        public float spellPowerMult = 1f;
        [Tooltip("Células somadas ao alcance das magias.")]
        public int spellRangeBonus = 0;

        /// <summary>
        /// Aplica a arma aos stats: dano base, peso em ticks, alcance (base + arma) e qual
        /// atributo escala (STR corpo a corpo, DEX à distância). Fonte única para a batalha
        /// (Unit.EquipWeapon) e para a ficha na criação de personagem. null = desarmado.
        /// </summary>
        public static void ApplyTo(AttributeStats stats, WeaponDef w, GameTuning tuning)
        {
            if (stats == null) return;
            int baseRange = tuning != null ? tuning.baseAttackRange : 1;
            if (w != null)
            {
                stats.WeaponDamage = w.damage;
                stats.ShapeTiles = AttackShape.CenteredTiles(w);
                stats.AttackTicks = Mathf.Max(1, w.attackTicks);
                stats.AttackRange = baseRange + w.range;
                stats.strScalesDamage = w.range <= (tuning != null ? tuning.strDamageMaxRange : 4);
                stats.dexScalesDamage = !stats.strScalesDamage;
            }
            else
            {
                stats.WeaponDamage = tuning != null ? tuning.unarmedDamage : 1;
                stats.ShapeTiles = 1;
                stats.AttackTicks = tuning != null ? Mathf.Max(1, tuning.ticksUnarmed) : 1;
                stats.AttackRange = baseRange + (tuning != null ? tuning.unarmedRange : 0);
                stats.strScalesDamage = true;
                stats.dexScalesDamage = false;
            }
        }
    }

    // ------------------------------------------------------------------ SEQUÊNCIA DE AÇÕES

    /// <summary>Novos tipos entram no FIM para manter os inteiros do PlanWire estáveis (Spell=4, Breathe=5).</summary>
    public enum ActionType { Move, Attack, Concentrate, Wait, Spell, Breathe }

    /// <summary>Magia planejada (GDD-019): id do catálogo + alvo (unidade ou tile; alcance −1 = sem alvo).</summary>
    public struct PlannedSpell
    {
        public string     SpellId;
        public Unit       TargetUnit;
        public Vector2Int TargetTile;
        public SpellDef   Def => SpellCatalog.Get(SpellId);
    }

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
        public PlannedSpell  Spell;      // Spell
        public bool          IsBonus;
        public bool          IsAimed;
        public int           Duration;   // ticks totais
        public int           Elapsed;    // ticks já decorridos
        public int           DamageTaken; // Spell Elemental: dano recebido durante a conjuração
        public bool          Interrupted; // Spell Elemental: passou do limite (GDD-019 §1)
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
                case ActionType.Breathe:     return T.ticksConcentrate;   // Respirar: espelho do Concentrar
                case ActionType.Spell:       return T.ticksConcentrate; // sem SpellDef: usa Duration(u, type, bonus, spell)
                default:                     return T.ticksWait;
            }
        }

        /// <summary>
        /// ticks = max(1, round(base × k / (k + DEX))). A DEX é a rapidez das ações (GDD-016);
        /// Andar e Esperar não escalam (o alcance da andada é da AGI). A base do ataque é o
        /// peso da arma da unidade.
        /// </summary>
        public static int Duration(Unit u, ActionType type, bool isBonus) => Duration(u, type, isBonus, null);

        /// <summary>Magias usam spell.ticksBase como base; a DEX encurta como qualquer ação (GDD-018).</summary>
        public static int Duration(Unit u, ActionType type, bool isBonus, SpellDef spell)
        {
            int b = BaseTicks(type, isBonus);
            if (type == ActionType.Spell && spell != null) b = Mathf.Max(1, spell.ticksBase);
            if (u == null) return Mathf.Max(1, b);
            if (type == ActionType.Attack)
                b = Mathf.Max(1, u.stats.AttackTicks) + (isBonus ? Tuning.Get().ticksPowerStrikeExtra : 0);
            if (type == ActionType.Wait || type == ActionType.Move) return Mathf.Max(1, b);
            float k = Mathf.Max(1f, Tuning.Get().tickDexFactor);
            return Mathf.Max(1, Mathf.RoundToInt(b * k / (k + Mathf.Max(0f, u.stats.DEX))));
        }

        // ---- Custos em Stamina (GDD-016 §3) ----
        public static int StaminaCost(ScheduledAction a)
        {
            var T = Tuning.Get();
            int c = 0;
            if (a.Type == ActionType.Attack && a.IsBonus) c += T.staminaPowerStrike;
            if (a.Type == ActionType.Attack && a.IsAimed) c += T.staminaAim;
            if (a.Type == ActionType.Move   && a.IsBonus) c += T.staminaQuickStep;
            return c;
        }

        public static int Duration(Unit u, ScheduledAction a) =>
            Duration(u, a.Type, a.IsBonus, a.Type == ActionType.Spell ? SpellOf(u, a) : null);

        /// <summary>SpellDef de uma ação Spell do plano (ou null).</summary>
        public static SpellDef SpellOf(Unit u, ScheduledAction a)
        {
            if (u == null || a.Type != ActionType.Spell || a.Index < 0 || a.Index >= u.plannedSpells.Count) return null;
            return u.plannedSpells[a.Index].Def;
        }

        /// <summary>Stamina do plano incluindo magia Física (GDD-019 §2).</summary>
        public static int StaminaCost(Unit u, ScheduledAction a)
        {
            int c = StaminaCost(a);
            var s = SpellOf(u, a);
            if (s != null) c += s.StaminaPart;
            return c;
        }

        /// <summary>Mana do plano. Elemental: o custo todo; Física: o resto da metade (GDD-020 §6).</summary>
        public static int ManaCost(Unit u, ScheduledAction a)
        {
            var s = SpellOf(u, a);
            return s != null ? s.ManaPart : 0;
        }
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
        /// <summary>Mode == Tile (mira livre, GDD-020 §3): a CÉLULA mirada (não um anchor 3×3).</summary>
        public Vector2Int    TargetTile;
    }

    /// <summary>Posturas (GDD-020 §8.1). Valores fixos: vão no PlanWire como int.</summary>
    public enum Posture { Neutral, Aggressive, Defensive, Agile, Focused, Concentrated, Serene }

    public static class PostureRules
    {
        public static readonly Posture[] All =
            { Posture.Neutral, Posture.Aggressive, Posture.Defensive, Posture.Agile, Posture.Focused, Posture.Concentrated, Posture.Serene };

        /// <summary>Atributo que a postura aumenta e o que ela diminui ("" = nenhum).</summary>
        public static (string up, string down) Attrs(Posture p)
        {
            switch (p)
            {
                case Posture.Aggressive:   return ("STR", "VIT");
                case Posture.Defensive:    return ("VIT", "AGI");
                case Posture.Agile:        return ("AGI", "STR");
                case Posture.Focused:      return ("DEX", "AGI");
                case Posture.Concentrated: return ("INT", "VIT");
                case Posture.Serene:       return ("WIS", "STR");
                default:                   return ("", "");
            }
        }

        public static string Name(Posture p)
        {
            switch (p)
            {
                case Posture.Aggressive:   return "Agressiva";
                case Posture.Defensive:    return "Defensiva";
                case Posture.Agile:        return "Ágil";
                case Posture.Focused:      return "Focada";
                case Posture.Concentrated: return "Concentrada";
                case Posture.Serene:       return "Serena";
                default:                   return "Neutra";
            }
        }

        public static string Glyph(Posture p)
        {
            switch (p)
            {
                case Posture.Aggressive:   return "▲";
                case Posture.Defensive:    return "■";
                case Posture.Agile:        return "»";
                case Posture.Focused:      return "◎";
                case Posture.Concentrated: return "✶";
                case Posture.Serene:       return "≈";
                default:                   return "○";
            }
        }

        /// <summary>Texto do efeito com os números do tuning ("+2 STR, −1 VIT").</summary>
        public static string Effect(Posture p)
        {
            var (up, down) = Attrs(p);
            if (up == "") return "sem bônus";
            var T = Tuning.Get();
            return $"+{T.postureBonus} {up}, −{T.posturePenalty} {down}";
        }

        /// <summary>Soma (sign=+1) ou remove (sign=−1) o efeito da postura nos atributos.</summary>
        public static void Apply(AttributeStats s, Posture p, int sign)
        {
            var (up, down) = Attrs(p);
            if (up == "") return;
            var T = Tuning.Get();
            Add(s, up, sign * T.postureBonus);
            Add(s, down, -sign * T.posturePenalty);
        }

        private static void Add(AttributeStats s, string attr, float v)
        {
            switch (attr)
            {
                case "STR": s.STR += v; break;
                case "VIT": s.VIT += v; break;
                case "DEX": s.DEX += v; break;
                case "AGI": s.AGI += v; break;
                case "INT": s.INT += v; break;
                case "WIS": s.WIS += v; break;
            }
        }
    }

    /// <summary>Formas de golpe (GDD-020 §1).</summary>
    public enum AttackShapeKind { Single, Line, Arc, Square }

    /// <summary>
    /// Geometria das formas de ataque. A forma é posta sobre a célula mirada e gira na direção
    /// do golpe (do centro de quem ataca para a mira, em 8 direções). Determinístico: só inteiros.
    /// </summary>
    public static class AttackShape
    {
        /// <summary>Direção do golpe em 8 direções (diagonal quando os dois eixos são parecidos).</summary>
        public static Vector2Int Direction(Vector2Int fromCell, Vector2Int toCell)
        {
            int dx = toCell.x - fromCell.x, dy = toCell.y - fromCell.y;
            if (dx == 0 && dy == 0) return Vector2Int.up;
            int ax = System.Math.Abs(dx), ay = System.Math.Abs(dy);
            // diagonal se o eixo menor tiver pelo menos metade do maior
            int sx = System.Math.Sign(dx), sy = System.Math.Sign(dy);
            if (ax * 2 < ay) sx = 0;
            else if (ay * 2 < ax) sy = 0;
            return new Vector2Int(sx, sy);
        }

        /// <summary>Células que a forma pega, mirada em <paramref name="aim"/>, golpe vindo de <paramref name="fromCell"/>.</summary>
        public static List<Vector2Int> Cells(WeaponDef w, Vector2Int fromCell, Vector2Int aim)
        {
            var kind = w != null ? w.shape : AttackShapeKind.Single;
            int width = w != null ? Mathf.Max(1, w.shapeWidth) : 1;
            int length = w != null ? Mathf.Max(1, w.shapeLength) : 1;
            var d = Direction(fromCell, aim);
            var p = new Vector2Int(-d.y, d.x);                   // perpendicular
            var cells = new List<Vector2Int>();
            switch (kind)
            {
                case AttackShapeKind.Single:
                    cells.Add(aim);
                    break;
                case AttackShapeKind.Line:
                    // centrada na mira ao longo do golpe: linha 3 = atrás, mira, à frente
                    for (int i = 0; i < length; i++) cells.Add(aim + d * (i - (length - 1) / 2));
                    break;
                case AttackShapeKind.Arc:
                    // fileiras perpendiculares ao golpe; a primeira na mira, as outras para trás (lado de quem ataca)
                    for (int row = 0; row < length; row++)
                        for (int k = 0; k < width; k++)
                            cells.Add(aim - d * row + p * (k - (width - 1) / 2));
                    break;
                case AttackShapeKind.Square:
                    for (int x = 0; x < width; x++)
                        for (int y = 0; y < length; y++)
                            cells.Add(aim + new Vector2Int(x - (width - 1) / 2, y - (length - 1) / 2));
                    break;
            }
            // sem repetidas (diagonais podem coincidir)
            var unique = new List<Vector2Int>();
            foreach (var c in cells) if (!unique.Contains(c)) unique.Add(c);
            return unique;
        }

        /// <summary>Tiles de um footprint dentro das células.</summary>
        public static int TilesOn(List<Vector2Int> cells, Vector2Int anchor, int fp)
        {
            int n = 0;
            foreach (var c in cells)
                if (c.x >= anchor.x && c.x < anchor.x + fp && c.y >= anchor.y && c.y < anchor.y + fp) n++;
            return n;
        }

        /// <summary>Tiles que a forma pega num alvo 3×3 quando centralizada nele (golpe vindo de baixo).</summary>
        public static int CenteredTiles(WeaponDef w)
        {
            int fp = AttributeStats.DefaultFootprint;
            var anchor = new Vector2Int(10, 10);
            var center = anchor + new Vector2Int(fp / 2, fp / 2);
            return Mathf.Max(1, TilesOn(Cells(w, center + new Vector2Int(0, -6), center), anchor, fp));
        }

        /// <summary>Centro (célula) de um footprint.</summary>
        public static Vector2Int Center(Vector2Int anchor, int fp) => anchor + new Vector2Int(fp / 2, fp / 2);

        /// <summary>Texto curto da forma para a UI ("arco 3×1", "linha 3", "1 tile").</summary>
        public static string Describe(WeaponDef w)
        {
            if (w == null) return "1 tile";
            switch (w.shape)
            {
                case AttackShapeKind.Line:   return $"linha {Mathf.Max(1, w.shapeLength)}";
                case AttackShapeKind.Arc:    return w.shapeLength > 1 ? $"varredura {w.shapeWidth}×{w.shapeLength}" : $"arco {w.shapeWidth}×1";
                case AttackShapeKind.Square: return $"quadrado {w.shapeWidth}×{w.shapeLength}";
                default:                     return "1 tile";
            }
        }
    }
}
