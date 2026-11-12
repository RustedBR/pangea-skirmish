using System;
using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>Física gasta Estâmina; Elemental gasta Mana. Nunca os dois (GDD-018).</summary>
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
        PrepTrueAim,     // próximo ataque não erra e +crit
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
        [Tooltip("Custo em Mana (Elemental) ou Estâmina (Física).")]
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
            // ---- Física (GDD-019 §2): Estâmina, 1 tick de preparo ----
            P("Fury",      "Fúria",         Attr.STR, 3, 1, SpellEffect.PrepFury,      mult: 2f,
              desc: "O próximo ataque desta fila causa dano ×2. Não acumula com Golpe poderoso: vale o maior."),
            P("Leap",      "Salto",         Attr.AGI, 2, 1, SpellEffect.Leap,          mult: 3f, range: -1,
              desc: "Move até 3× o alcance da andada em linha reta, por cima de obstáculos e ameaças. Sem ataque de oportunidade."),
            P("StoneSkin", "Pele de pedra", Attr.VIT, 3, 1, SpellEffect.PrepStoneSkin, mult: 3f,
              desc: "Até o fim do próximo ponto de decisão, a redução de dano físico é ×3 (teto 90%)."),
            P("TrueAim",   "Olho certeiro", Attr.DEX, 2, 1, SpellEffect.PrepTrueAim,   crit: 0.30f,
              desc: "O próximo ataque não pode errar e tem +30% de crítico."),

            // ---- Elemental puras (GDD-019 §3.1): Mana ----
            E("Ember",      "Brasa",          Fire,  2, 2, 4, 0, SpellEffect.Damage, 4, 1.0f, dur: 3, tickDmg: 2, status: "Burning",
              desc: "Dano de Fogo. Aplica Queimando: 2 de dano por tick por 3 ticks."),
            E("WaterJet",   "Jato d'água",    Water, 2, 2, 4, 0, SpellEffect.Damage, 3, 0.8f, dur: 4, status: "Soaked",
              desc: "Dano de Água. Aplica Encharcado: −1 tile de andada por 4 ticks. Apaga Queimando."),
            E("StoneShard", "Lasca de pedra", Earth, 2, 2, 3, 0, SpellEffect.CancelMove, 3, 0.8f,
              desc: "Dano de Terra. Se o alvo estava andando neste tick, cancela o movimento restante."),
            E("Gust",       "Rajada",         Air,   2, 1, 3, 0, SpellEffect.Push, 2, 0.5f, push: 1,
              desc: "Dano de Ar. Empurra o alvo 1 tile para longe (2 se POT ≥ 8)."),
            E("ArcaneSight","Visão arcana",   Magic, 1, 1, -1, 0, SpellEffect.Foresight, dur: 6,
              desc: "Por 6 ticks, vê as filas inimigas até +2 ticks à frente e enxerga unidades ocultas."),

            // ---- Elemental pares (GDD-019 §3.2): Mana ----
            E("Explosion",  "Explosão",            new[]{ SpellElement.Fire,  SpellElement.Air },   5, 3, 5, 1, SpellEffect.Damage, 6, 1.2f, push: 1,
              desc: "Dano de Fogo em área. Empurra 1 tile para fora do centro."),
            E("Balm",       "Bálsamo",             new[]{ SpellElement.Water, SpellElement.Earth }, 4, 2, 3, 1, SpellEffect.Heal, 5, 1.0f,
              desc: "Cura aliados na área. Remove Queimando e Encharcado. Única área que toca aliado."),
            E("Steam",      "Vapor",               new[]{ SpellElement.Fire,  SpellElement.Water }, 3, 2, 4, 1, SpellEffect.ZoneSteam, dur: 3,
              desc: "Nuvem por 3 ticks: quem termina o tick dentro tem −20% de acerto e não pode ser alvo de Olho certeiro."),
            E("Magma",      "Magma",               new[]{ SpellElement.Fire,  SpellElement.Earth }, 4, 3, 3, 1, SpellEffect.ZoneLava, dur: 4, tickDmg: 3,
              desc: "Tiles viram lava por 4 ticks: 3 de dano por tick a quem está em cima. Não afeta o conjurador."),
            E("Mud",        "Lama",                new[]{ SpellElement.Water, SpellElement.Air },   3, 2, 4, 1, SpellEffect.ZoneMud, dur: 4,
              desc: "Tiles de lama por 4 ticks: andada = 1 dentro da área."),
            E("Sandstorm",  "Tempestade de areia", new[]{ SpellElement.Earth, SpellElement.Air },   3, 2, 4, 2, SpellEffect.ZoneSandstorm, dur: 3,
              desc: "Por 3 ticks, ataques à distância que cruzam a área erram 50%."),
            E("ArcaneFlame","Chama arcana",        new[]{ SpellElement.Fire,  SpellElement.Magic }, 4, 2, 5, 0, SpellEffect.Damage, 5, 1.3f, pierce: 0.5f,
              desc: "Dano de Fogo que ignora metade da armadura. Sem Queimando."),
            E("MindTide",   "Maré mental",         new[]{ SpellElement.Water, SpellElement.Magic }, 3, 2, 4, 0, SpellEffect.DrainMana, 2, 0.5f,
              desc: "Drena Mana do alvo e devolve metade ao conjurador."),
            E("Rampart",    "Muralha",             new[]{ SpellElement.Earth, SpellElement.Magic }, 4, 2, 3, 3, SpellEffect.Barrier, 8, 2.0f, dur: 4,
              desc: "Barreira de 3 tiles por 4 ticks: bloqueia movimento e projéteis. HP = 8 + POT×2."),
            E("WindStep",   "Passo do vento",      new[]{ SpellElement.Air,   SpellElement.Magic }, 3, 1, 5, 0, SpellEffect.Teleport,
              desc: "Teleporta o conjurador até 5 tiles para um tile livre. Sem ataque de oportunidade. (Mover um aliado fica para depois.)"),
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
}
