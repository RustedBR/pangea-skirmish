using System;
using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>Física gasta Stamina; Elemental gasta Mana. Nunca os dois (GDD-018).</summary>
    public enum SpellKind { Physical, Elemental }

    /// <summary>
    /// O que a magia faz ao resolver. Cada valor é um "verbo" que o RoundManager implementa;
    /// os números (dano, duração, raio) ficam no SpellDef.
    /// </summary>
    public enum SpellEffect
    {
        // ---- Elemental: dano / cura ----
        Damage,          // dano único no alvo (ou em área)
        Heal,            // cura em aliados na área (único caso em que área toca aliado)
        // ---- Elemental: controle ----
        Push,            // empurra o alvo pushTiles para longe do conjurador/centro
        CancelMove,      // cancela o movimento restante do alvo neste tick
        DrainMana,       // drena Mana do alvo; devolve metade ao conjurador
        Teleport,        // move um aliado (ou a si) para um tile livre visível
        // ---- Elemental: zona (tiles por N ticks) ----
        ZoneLava,        // dano por tick a quem está em cima
        ZoneMud,         // MoveStep = 1 dentro da área
        ZoneSteam,       // −acerto a quem termina o tick dentro; imune a Olho certeiro
        ZoneSandstorm,   // projéteis que cruzam a área erram 50%
        Barrier,         // linha de tiles bloqueando movimento e projéteis, com HP
        // ---- Elemental: percepção ----
        Foresight,       // vê filas inimigas +N ticks e unidades ocultas
        // ---- Física (preparo de 1 tick; o efeito se consome depois) ----
        PrepFury,        // próximo ataque: dano ×2 (não acumula com Golpe poderoso)
        Leap,            // move até leapMult × MoveStep em linha reta, ignorando obstáculos e ameaças
        PrepStoneSkin,   // até o fim do próximo ponto de decisão: redução ×3 (teto 90%)
        PrepTrueAim,     // LEGADO: virou reação (ReactionKind.TrueAim, 2026-11-13)
        PrepCurve,       // Curva: o próximo ataque à distância segue o alvo e contorna obstáculos
    }

    /// <summary>
    /// Definição de uma magia (GDD-019). A "montagem" (elements) é explícita para que a UI
    /// mostre de que a magia foi feita e para abrir a combinação livre no futuro.
    /// </summary>
    [Serializable]
    public class SpellDef
    {
        public string id;
        public string displayName;
        public SpellKind kind = SpellKind.Elemental;
        [Tooltip("Montagem: 1 elemento (pura) ou 2 (par). Física usa sempre [Physical].")]
        public SpellElement[] elements = Array.Empty<SpellElement>();
        [Tooltip("Física: atributo que escala o efeito (STR/AGI/VIT/DEX). Elemental: sempre INT (POT).")]
        public Attr scalesWith = Attr.INT;
        [Tooltip("Custo em Mana (Elemental) ou Stamina (Física).")]
        public int cost = 2;
        [Tooltip("Ticks base; a DEX encurta como qualquer ação.")]
        public int ticksBase = 2;
        [Tooltip("Alcance em tiles a partir do conjurador. 0 = só em si. −1 = sem alvo (ex.: Visão arcana).")]
        public int range = 4;
        [Tooltip("Raio da área em tiles. 0 = tile único. Para Barrier, é o comprimento da linha.")]
        public int area = 0;
        public SpellEffect effect = SpellEffect.Damage;

        [Header("Números do efeito")]
        [Tooltip("Dano/cura/drenagem = baseAmount + POT × potFactor (POT = INT).")]
        public float baseAmount = 0f;
        public float potFactor = 0f;
        [Tooltip("Ticks que a zona/status/buff dura.")]
        public int durationTicks = 0;
        [Tooltip("Dano por tick da zona ou do status aplicado (Queimando, lava).")]
        public int tickDamage = 0;
        [Tooltip("Tiles empurrados (Push).")]
        public int pushTiles = 0;
        [Tooltip("Status aplicado ao acertar (nome do StatusEffect), vazio = nenhum.")]
        public string appliesStatus = "";
        [Tooltip("Multiplicador do efeito Físico (Fúria ×2, Pele ×3, Salto ×MoveStep).")]
        public float multiplier = 1f;
        [Tooltip("Bônus de crítico (Olho certeiro).")]
        public float critBonus = 0f;
        [Tooltip("Ignora esta fração da armadura (Mágico puro = 0,5).")]
        public float armorPierce = 0f;
        [TextArea(2, 4)] public string description = "";

        public bool IsPhysical => kind == SpellKind.Physical;
        /// <summary>GDD-020 §6: magia Física gasta metade Stamina (arredondada para baixo) e o resto em Mana. Elemental: só Mana.</summary>
        public int StaminaPart => IsPhysical ? cost / 2 : 0;
        public int ManaPart => cost - StaminaPart + boostMana;
        /// <summary>Mana extra dos incrementos escolhidos ao planejar (GDD-023). Só nas cópias de SpellBoosts.Apply.</summary>
        [NonSerialized] public int boostMana;
        /// <summary>Incrementos aplicados nesta cópia (flags de SpellBoost).</summary>
        [NonSerialized] public int boosts;
        public SpellDef CloneDef() => (SpellDef)MemberwiseClone();
        /// <summary>Custo para a UI: "2 Mana + 1 Stamina" ou "4 Mana".</summary>
        public string CostText => StaminaPart > 0 ? $"{ManaPart} Mana + {StaminaPart} Stamina" : $"{ManaPart} Mana";
        public bool IsPure => elements != null && elements.Length == 1;
        public bool IsPair => elements != null && elements.Length == 2;
        public SpellElement PrimaryElement => (elements != null && elements.Length > 0) ? elements[0] : SpellElement.None;

        /// <summary>Quantidade principal (dano, cura ou drenagem) para uma potência POT.</summary>
        public int Amount(float pot) => Mathf.RoundToInt(baseAmount + pot * potFactor);

        /// <summary>Texto da montagem para a UI: "Fogo + Ar", "Água", "Físico (STR)".</summary>
        public string MontageText()
        {
            if (IsPhysical) return $"Físico ({scalesWith})";
            if (elements == null || elements.Length == 0) return "—";
            var parts = new List<string>();
            foreach (var e in elements) parts.Add(ElementInfo.Name(e));
            return string.Join(" + ", parts);
        }
    }

    /// <summary>
    /// Catálogo das magias (GDD-019): 4 Físicas (uma por atributo do Corpo) e 15 Elementais
    /// (5 puras + 10 pares), fixas e nomeadas. GameTuning.spells pode sobrescrever.
    /// </summary>
    public static class SpellCatalog
    {
        private static SpellDef E(string id, string name, SpellElement[] el, int cost, int ticks, int range, int area,
                                  SpellEffect fx, float baseAmt = 0, float pot = 0, int dur = 0, int tickDmg = 0,
                                  int push = 0, string status = "", float pierce = 0f, string desc = "") =>
            new SpellDef { id = id, displayName = name, kind = SpellKind.Elemental, elements = el, scalesWith = Attr.INT,
                           cost = cost, ticksBase = ticks, range = range, area = area, effect = fx,
                           baseAmount = baseAmt, potFactor = pot, durationTicks = dur, tickDamage = tickDmg,
                           pushTiles = push, appliesStatus = status, armorPierce = pierce, description = desc };

        private static SpellDef P(string id, string name, Attr attr, int cost, int ticks, SpellEffect fx,
                                  float mult = 1f, float crit = 0f, int range = 0, string desc = "") =>
            new SpellDef { id = id, displayName = name, kind = SpellKind.Physical, elements = new[] { SpellElement.Physical },
                           scalesWith = attr, cost = cost, ticksBase = ticks, range = range, area = 0, effect = fx,
                           multiplier = mult, critBonus = crit, description = desc };

        private static readonly SpellElement[] Fire = { SpellElement.Fire };
        private static readonly SpellElement[] Water = { SpellElement.Water };
        private static readonly SpellElement[] Earth = { SpellElement.Earth };
        private static readonly SpellElement[] Air = { SpellElement.Air };
        private static readonly SpellElement[] Magic = { SpellElement.Magic };

        private static readonly SpellDef[] _defaults =
        {
            // ---- Física (GDD-019 §2, GDD-020 §6): metade Mana, metade Stamina; 1 tick de preparo ----
            P("Fury",      "Fúria",         Attr.STR, 2, 1, SpellEffect.PrepFury,      mult: 2.5f,
              desc: "O próximo ataque causa dano ×{mult} em cada tile que pegar. Não soma com Golpe poderoso: vale o maior."),
            P("Leap",      "Salto",         Attr.AGI, 3, 1, SpellEffect.Leap,          mult: 2f, range: -1,
              desc: "Salta até {reach} células por cima de obstáculos e ameaças. O salto não provoca oportunidade (começar a conjurar no alcance de alguém provoca)."),
            P("StoneSkin", "Pele de pedra", Attr.VIT, 2, 1, SpellEffect.PrepStoneSkin, mult: 5f,
              desc: "Por {skin} ticks, a defesa da VIT conta ×{mult} contra dano físico (VIT 5 → −{skinEx} por golpe)."),
            P("Curve",     "Curva",         Attr.AGI, 2, 1, SpellEffect.PrepCurve,
              desc: "O seu próximo ataque à distância (arma ou magia, inclusive de área) faz curva: segue o alvo se ele se mover ou esquivar, e contorna Muralha, Tempestade de areia e quem estiver no caminho."),

            // ---- Elemental puras (GDD-019 §3.1): Mana ----
            E("Ember",      "Brasa",          Fire,  2, 2, 4, 0, SpellEffect.Damage, 4, 1.0f, dur: 3, tickDmg: 2, status: "Burning",
              desc: "~{amount} de dano de Fogo e Queimando: {tick} de dano por tick por {dur} ticks."),
            E("WaterJet",   "Jato d'água",    Water, 2, 2, 4, 0, SpellEffect.Damage, 3, 0.8f, dur: 4, status: "Soaked",
              desc: "~{amount} de dano de Água e Encharcado: −1 tile de andada por {dur} ticks. Apaga Queimando."),
            E("StoneShard", "Lasca de pedra", Earth, 2, 2, 3, 0, SpellEffect.CancelMove, 3, 0.8f,
              desc: "~{amount} de dano de Terra. Se o alvo estiver andando, cancela o passo seguinte."),
            E("Gust",       "Rajada",         Air,   2, 1, 3, 0, SpellEffect.Push, 2, 0.5f, push: 1,
              desc: "~{amount} de dano de Ar e empurra {push} tile para longe (+1 com INT 8 ou mais)."),
            E("ArcaneSight","Visão arcana",   Magic, 1, 1, -1, 0, SpellEffect.Foresight, dur: 6,
              desc: "Por {dur} ticks, vê a fila inimiga 2 ticks além do normal."),

            // ---- Elemental pares (GDD-019 §3.2): Mana ----
            E("Explosion",  "Explosão",            new[]{ SpellElement.Fire,  SpellElement.Air },   5, 3, 5, 1, SpellEffect.Damage, 6, 1.2f, push: 1,
              desc: "Área {area}: ~{amount} de dano de Fogo em quem estiver inteiro dentro (menos em quem só a ponta pegar). Empurra {push} tile para fora do centro."),
            E("Balm",       "Bálsamo",             new[]{ SpellElement.Water, SpellElement.Earth }, 4, 2, 3, 1, SpellEffect.Heal, 5, 1.0f,
              desc: "Área {area}: cura ~{amount} de vida dos aliados e tira Queimando e Encharcado. É a única área que toca aliados."),
            E("Steam",      "Vapor",               new[]{ SpellElement.Fire,  SpellElement.Water }, 3, 2, 4, 1, SpellEffect.ZoneSteam, dur: 3,
              desc: "Área {area} de vapor por {dur} ticks: quem estiver dentro tem −20% de acerto e não pode ser alvo de Olho certeiro."),
            E("Magma",      "Magma",               new[]{ SpellElement.Fire,  SpellElement.Earth }, 4, 3, 3, 1, SpellEffect.ZoneLava, dur: 4, tickDmg: 3,
              desc: "Área {area} de lava por {dur} ticks: {tick} de dano por tick em quem pisar nela. Não fere o conjurador."),
            E("Mud",        "Lama",                new[]{ SpellElement.Water, SpellElement.Air },   3, 2, 4, 1, SpellEffect.ZoneMud, dur: 4,
              desc: "Área {area} de lama por {dur} ticks: dentro dela a andada cai para 1 tile."),
            E("Sandstorm",  "Tempestade de areia", new[]{ SpellElement.Earth, SpellElement.Air },   3, 2, 4, 2, SpellEffect.ZoneSandstorm, dur: 3,
              desc: "Área {area} por {dur} ticks: ataques à distância que cruzam a área erram 50% das vezes."),
            E("ArcaneFlame","Chama arcana",        new[]{ SpellElement.Fire,  SpellElement.Magic }, 4, 2, 5, 0, SpellEffect.Damage, 5, 1.3f, pierce: 0.5f,
              desc: "~{amount} de dano de Fogo que ignora metade da defesa mágica (WIS). Não queima."),
            E("MindTide",   "Maré mental",         new[]{ SpellElement.Water, SpellElement.Magic }, 3, 2, 4, 0, SpellEffect.DrainMana, 2, 0.5f,
              desc: "Drena até ~{drain} de Mana do alvo e devolve metade a você."),
            E("Rampart",    "Muralha",             new[]{ SpellElement.Earth, SpellElement.Magic }, 4, 2, 3, 3, SpellEffect.Barrier, 8, 2.0f, dur: 4,
              desc: "Muralha de {wall} células por {dur} ticks, com {amount} de vida: bloqueia passagem e projéteis de todos."),
            E("WindStep",   "Passo do vento",      new[]{ SpellElement.Air,   SpellElement.Magic }, 3, 1, 5, 0, SpellEffect.Teleport,
              desc: "Teleporta você para um tile livre a até {reach} células. Não provoca oportunidade ao chegar."),
        };

        public static SpellDef[] All()
        {
            var t = Tuning.Get();
            return (t != null && t.spells != null && t.spells.Length > 0) ? t.spells : _defaults;
        }

        public static SpellDef Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var s in All()) if (s.id == id) return s;
            return null;
        }

        public static IEnumerable<SpellDef> OfKind(SpellKind k)
        {
            foreach (var s in All()) if (s.kind == k) yield return s;
        }

        /// <summary>Mágico puro ignora metade da armadura (GDD-019 §1), além do que a magia já diz.</summary>
        public static float ArmorPierce(SpellDef s)
        {
            if (s == null) return 0f;
            if (s.IsPure && s.PrimaryElement == SpellElement.Magic) return Mathf.Max(s.armorPierce, 0.5f);
            return s.armorPierce;
        }
    }

    /// <summary>Incrementos de magia escolhidos ao planejar (GDD-023). Flags somáveis; pagos em Mana.</summary>
    [Flags]
    public enum SpellBoost { None = 0, Power = 1, Effect = 2, Range = 4, Area = 8, Quick = 16 }

    public static class SpellBoosts
    {
        public static readonly SpellBoost[] All = { SpellBoost.Power, SpellBoost.Effect, SpellBoost.Range, SpellBoost.Area, SpellBoost.Quick };

        /// <summary>Mana de cada incremento.</summary>
        public static int Cost(SpellBoost b) => b switch
        {
            SpellBoost.Power => 2, SpellBoost.Effect => 1, SpellBoost.Range => 1, SpellBoost.Area => 2, SpellBoost.Quick => 2, _ => 0
        };

        public static int TotalCost(int flags)
        {
            int c = 0;
            foreach (var b in All) if ((flags & (int)b) != 0) c += Cost(b);
            return c;
        }

        public const float PowerMult = 1.5f;
        public const int EffectTicks = 2;
        /// <summary>Células a mais de alcance de conjuração.</summary>
        public const int RangeCells = 2;

        /// <summary>O incremento faz sentido nesta magia?</summary>
        public static bool Allowed(SpellDef s, SpellBoost b)
        {
            if (s == null) return false;
            switch (b)
            {
                case SpellBoost.Power:  return s.baseAmount > 0f || s.potFactor > 0f || s.tickDamage > 0;
                case SpellBoost.Effect: return s.durationTicks > 0;
                case SpellBoost.Range:  return s.range > 0 && s.effect != SpellEffect.Leap;
                case SpellBoost.Area:   return s.area > 0;
                case SpellBoost.Quick:  return s.ticksBase > 1;
            }
            return false;
        }

        public static string Label(SpellBoost b) => b switch
        {
            SpellBoost.Power  => $"Potência: dano/cura ×{PowerMult:0.#} (+1 tick)",
            SpellBoost.Effect => $"Efeito: +{EffectTicks} ticks de duração",
            SpellBoost.Range  => $"Alcance: +{RangeCells} células",
            SpellBoost.Area   => "Área: +1 de raio",
            SpellBoost.Quick  => "Rápida: −1 tick de conjuração",
            _ => ""
        };

        /// <summary>Cópia da magia com os incrementos aplicados (flags 0 = a própria magia do catálogo).</summary>
        public static SpellDef Apply(SpellDef s, int flags)
        {
            if (s == null || flags == 0) return s;
            var d = s.CloneDef();
            d.boosts = flags;
            foreach (var b in All)
            {
                if ((flags & (int)b) == 0 || !Allowed(s, b)) continue;
                d.boostMana += Cost(b);
                switch (b)
                {
                    case SpellBoost.Power:
                        d.baseAmount *= PowerMult; d.potFactor *= PowerMult;
                        d.tickDamage = Mathf.RoundToInt(d.tickDamage * PowerMult);
                        d.ticksBase += 1;
                        break;
                    case SpellBoost.Effect: d.durationTicks += EffectTicks; break;
                    case SpellBoost.Range:
                        // Reach = range × spellRangeMult + ...: soma em unidades de range.
                        d.range += Mathf.Max(1, Mathf.RoundToInt(RangeCells / Mathf.Max(0.01f, Tuning.Get().spellRangeMult)));
                        break;
                    case SpellBoost.Area:   d.area += 1; break;
                    case SpellBoost.Quick:  d.ticksBase = Mathf.Max(1, d.ticksBase - 1); break;
                }
            }
            return d;
        }
    }
}
