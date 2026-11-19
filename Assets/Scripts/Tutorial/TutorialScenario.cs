using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PangeaSkirmish
{
    /// <summary>
    /// Lições do tutorial (GDD-027 §4), capítulos 0–9. DUPLO PROPÓSITO: ensinar o jogador E
    /// testar CADA mecânica de verdade. Toda lição tem um gate que lê o estado REAL do jogo e uma
    /// asserção que verifica o resultado — completá-la exercita e valida a mecânica (banco de testes).
    /// Lições de ação do jogador (câmera/mover/atacar/conjurar) ele executa; lições de sistema
    /// (status/zonas/cura/reação/progressão/coop) o tutorial resolve a mecânica real e verifica o
    /// efeito, enquanto explica. Baselines capturados no setup de cada lição.
    /// </summary>
    public static class TutorialScenario
    {
        private static List<Unit> Units(TutorialDirector d) => new List<Unit> { d.PlayerUnit, d.Fera };

        /// <summary>Resolve uma magia de verdade (SpellResolver) para demonstrar + testar a mecânica.</summary>
        private static void Cast(TutorialDirector d, string id, Unit target, Vector2Int tile, int boosts = 0)
        {
            var ps = new PlannedSpell { SpellId = id, TargetUnit = target, TargetTile = tile, Boosts = boosts };
            SpellResolver.Resolve(d.PlayerUnit, ps, Units(d), d.Grid, SpellField.Current);
        }

        private static void KeepFeraAlive(TutorialDirector d)
        {
            if (d.Fera != null && d.Fera.currentHP < 80) d.Fera.currentHP = 300;
        }

        public static List<TutorialLesson> Build(TutorialDirector d)
        {
            var L = new List<TutorialLesson>();

            // ── Capítulo 0: Orientação ──────────────────────────────────────────
            float camT0 = 0f;
            var cam = new TutorialLesson("cam", 0, "Mova a câmera", "Câmera",
                "Arraste o mouse (ou o dedo) para mover a câmera e veja o mapa. Role a roda do mouse para dar zoom.",
                "Câmera respondendo ao input.");
            cam.setup = () => camT0 = Time.unscaledTime;
            cam.gate = () => CameraController.LastManualMoveTime > camT0;
            L.Add(cam);

            var bar = new TutorialLesson("bar", 0, "A barra de ticks", "Barra de ticks (UI)",
                "Em cima fica a barra de ticks: o tempo do combate. Cada ação ocupa um trecho dela. Você planeja e confirma o round; as ações resolvem juntas, tick a tick.",
                "Barra de ticks renderizada.");
            bar.gate = () =>
            {
                if (d.Hud == null || d.Hud.Document == null) return false;
                var tb = UQueryExtensions.Q<VisualElement>(d.Hud.Document.rootVisualElement, "ticks-bar");
                return tb != null && tb.resolvedStyle.display != DisplayStyle.None;
            };
            L.Add(bar);

            // ── Capítulo 1: Planejamento semi-turno ─────────────────────────────
            var plan = new TutorialLesson("plan", 1, "Enfileire uma andada", "Planejar movimento",
                "Clique no botão Andar (ou tecla 1) e depois clique numa célula do chão para enfileirar um movimento. Veja o bloco aparecer na barra de ticks.",
                "Movimento enfileirado na barra.");
            plan.gate = () => d.PlayerUnit != null && d.PlayerUnit.plannedMoveCount > 0;
            L.Add(plan);

            var sched = new TutorialLesson("sched", 1, "Ação virou tick", "Agendamento de ação",
                "Sua andada foi agendada na sequência de ações do round. Cada ação ocupa ticks e resolve na ordem.",
                "Ação de movimento agendada na sequência.");
            sched.gate = () => d.PlayerUnit != null && d.PlayerUnit.actionSequence.Exists(a => a.Type == ActionType.Move);
            L.Add(sched);

            bool sawPlan = false, confirmed = false;
            var confirm = new TutorialLesson("confirm", 1, "Confirme o round", "Confirmar round",
                "Pressione Enter (ou o botão Confirmar) para fechar o round. Suas ações vão resolver tick a tick.",
                "Round confirmado e resolvido.");
            confirm.setup = () => { sawPlan = false; confirmed = false; };
            confirm.gate = () =>
            {
                if (d.Planner != null && d.PlayerUnit != null && d.Planner.IsEngaged && d.PlayerUnit.plannedMoveCount > 0)
                    sawPlan = true;
                if (sawPlan && d.Planner != null && !d.Planner.IsEngaged) confirmed = true;
                return confirmed;
            };
            L.Add(confirm);

            // ── Capítulo 2: Movimento ───────────────────────────────────────────
            var moved = new TutorialLesson("moved", 2, "Sua unidade andou", "Movimento",
                "Observe: sua unidade saiu do lugar e andou até a célula que você escolheu. O alcance da andada depende da AGI (MoveStep).",
                "Unidade moveu no grid.");
            moved.gate = () => d.PlayerUnit != null && d.PlayerUnit.anchor != d.PlayerUnit.spawnAnchor;
            L.Add(moved);

            // ── Capítulo 3: Ataque ──────────────────────────────────────────────
            int feraHp0 = 0;
            var attack = new TutorialLesson("attack", 3, "Ataque a fera", "Ataque corpo a corpo",
                "Mova até a Fera de Treino, use Atacar (tecla 2) clicando nela e confirme o round (Enter). O dano corpo a corpo escala com STR.",
                "Ataque causou dano na fera.");
            attack.setup = () =>
            {
                if (d.Fera != null) { d.Fera.currentHP = 300; feraHp0 = d.Fera.currentHP; }
                if (d.PlayerUnit != null) { d.PlayerUnit.currentHP = 45; d.PlayerUnit.GainStamina(10); }
            };
            attack.gate = () => d.Fera != null && d.Fera.currentHP < feraHp0;
            attack.assertion = () => d.Fera != null && d.Fera.currentHP < feraHp0 ? null : "fera não sofreu dano";
            L.Add(attack);

            // ── Capítulo 4: Reações ─────────────────────────────────────────────
            int staminaBeforeReact = 0; bool couldReact = false;
            var reaction = new TutorialLesson("reaction", 4, "Reação de defesa", "Reação (consumo)",
                "Reações (Bloquear/Esquivar/Contra-atacar/Interpor) respondem a ataques. Custam Stamina e você só pode usar UMA por round. Veja uma reação de Bloqueio ser consumida.",
                "Reação consumida: 1 por round, gasta Stamina.");
            reaction.setup = () =>
            {
                if (d.PlayerUnit == null) return;
                d.PlayerUnit.GainStamina(10);
                d.PlayerUnit.remainingReactions = 1;
                couldReact = d.PlayerUnit.CanReact(ReactionKind.Block);
                staminaBeforeReact = d.PlayerUnit.currentStamina;
                d.PlayerUnit.ConsumeReaction(ReactionKind.Block);
            };
            reaction.gate = () => d.PlayerUnit != null && d.PlayerUnit.remainingReactions == 0;
            reaction.assertion = () =>
            {
                if (d.PlayerUnit == null) return "sem jogador";
                if (!couldReact) return "não podia reagir (Stamina insuficiente)";
                if (d.PlayerUnit.remainingReactions != 0) return "reação não consumida";
                if (d.PlayerUnit.currentStamina >= staminaBeforeReact) return "Stamina não foi gasta";
                return null;
            };
            L.Add(reaction);

            // ── Capítulo 5: Magia — básico ──────────────────────────────────────
            int mana0 = 0;
            var cast = new TutorialLesson("cast", 5, "Conjure Brasa (Fogo)", "Conjurar magia elemental",
                "Abra a aba Magia, escolha Brasa (Fogo), mire na fera e confirme o round. Magia Elemental gasta Mana. Veja a Mana cair.",
                "Magia conjurada — Mana gasta.");
            cast.setup = () =>
            {
                if (d.PlayerUnit != null) { d.PlayerUnit.currentMana = 20; mana0 = d.PlayerUnit.currentMana; }
                KeepFeraAlive(d);
            };
            cast.gate = () => d.PlayerUnit != null && d.PlayerUnit.currentMana < mana0;
            L.Add(cast);

            // ── Capítulo 6: Magia — avançado (cada efeito resolvido + verificado) ──
            var status = new TutorialLesson("status", 6, "Status: Queimando", "Status Queimando",
                "Brasa aplica Queimando: dano por tick na fera. Veja a magia resolver e a fera queimar.",
                "Queimando aplicado (dano por tick).");
            status.setup = () => { KeepFeraAlive(d); if (d.Fera != null) { d.Fera.burningTicks = 0; Cast(d, "Ember", d.Fera, d.Fera.anchor); } };
            status.gate = () => d.Fera != null && d.Fera.burningTicks > 0;
            status.assertion = () => d.Fera != null && d.Fera.burningTicks > 0 ? null : "Queimando não aplicado";
            L.Add(status);

            var soaked = new TutorialLesson("soaked", 6, "Status: Encharcado", "Status Encharcado",
                "Jato d'água aplica Encharcado: −1 tile de andada, e apaga Queimando. Veja a fera ficar encharcada.",
                "Encharcado aplicado (−1 andada).");
            soaked.setup = () => { KeepFeraAlive(d); if (d.Fera != null) { d.Fera.soakedTicks = 0; Cast(d, "WaterJet", d.Fera, d.Fera.anchor); } };
            soaked.gate = () => d.Fera != null && d.Fera.soakedTicks > 0;
            soaked.assertion = () => d.Fera != null && d.Fera.soakedTicks > 0 ? null : "Encharcado não aplicado";
            L.Add(soaked);

            var zone = new TutorialLesson("zone", 6, "Zona: Magma (lava)", "Zona de lava",
                "Magma cria uma zona de lava: dano por tick em quem pisar. Veja a zona aparecer no grid.",
                "Zona de lava criada no SpellField.");
            zone.setup = () => { KeepFeraAlive(d); if (d.Fera != null) Cast(d, "Magma", d.Fera, d.Fera.anchor); };
            zone.gate = () => SpellField.Current != null && SpellField.Current.Zones.Exists(z => z.Kind == SpellEffect.ZoneLava);
            zone.assertion = () => zone.gate() ? null : "zona de lava não criada";
            L.Add(zone);

            var barrier = new TutorialLesson("barrier", 6, "Barreira: Muralha", "Barreira",
                "Muralha cria uma barreira que bloqueia movimento e projéteis, com HP. Veja a muralha aparecer.",
                "Barreira criada no SpellField.");
            barrier.setup = () => { if (d.PlayerUnit != null) Cast(d, "Rampart", null, d.PlayerUnit.anchor + new Vector2Int(2, 0)); };
            barrier.gate = () => SpellField.Current != null && SpellField.Current.Zones.Exists(z => z.Kind == SpellEffect.Barrier);
            barrier.assertion = () => barrier.gate() ? null : "barreira não criada";
            L.Add(barrier);

            int areaHp0 = 0;
            var area = new TutorialLesson("area", 6, "Área: Explosão", "Dano em área",
                "Explosão causa dano em área (raio 1) e empurra. Veja o dano atingir a fera na área.",
                "Dano em área aplicado.");
            area.setup = () => { KeepFeraAlive(d); if (d.Fera != null) { areaHp0 = d.Fera.currentHP; Cast(d, "Explosion", d.Fera, d.Fera.anchor); } };
            area.gate = () => d.Fera != null && d.Fera.currentHP < areaHp0;
            area.assertion = () => d.Fera != null && d.Fera.currentHP < areaHp0 ? null : "dano em área não aplicado";
            L.Add(area);

            int healHp0 = 0;
            var heal = new TutorialLesson("heal", 6, "Cura: Bálsamo", "Cura em área",
                "Bálsamo cura aliados na área (único caso em que área toca aliado) e remove Queimando/Encharcado. Veja sua vida subir.",
                "Cura aplicada ao aliado.");
            heal.setup = () => { if (d.PlayerUnit != null) { d.PlayerUnit.currentHP = 20; healHp0 = 20; Cast(d, "Balm", d.PlayerUnit, d.PlayerUnit.anchor); } };
            heal.gate = () => d.PlayerUnit != null && d.PlayerUnit.currentHP > healHp0;
            heal.assertion = () => d.PlayerUnit != null && d.PlayerUnit.currentHP > healHp0 ? null : "cura não aplicada";
            L.Add(heal);

            float incBase = 0, incBoosted = 0;
            var increment = new TutorialLesson("increment", 6, "Incremento: Potência", "Incremento de magia",
                "Incrementos melhoram a magia por mais Mana. Potência aumenta o dano ×1.5 (e +1 tick). Veja o valor subir.",
                "Incremento Potência aumenta o valor.");
            increment.setup = () =>
            {
                var b = SpellCatalog.Get("Ember");
                incBase = b != null ? b.baseAmount : 0;
                var boosted = SpellBoosts.Apply(b, (int)SpellBoost.Power);
                incBoosted = boosted != null ? boosted.baseAmount : 0;
            };
            increment.gate = () => incBoosted > incBase;
            increment.assertion = () => incBoosted > incBase ? null : "incremento não aumentou o valor";
            L.Add(increment);

            // ── Capítulo 7: Magia Física (preparos resolvidos + verificados) ─────
            var fury = new TutorialLesson("fury", 7, "Física: Fúria", "Magia física Fúria",
                "Fúria prepara seu próximo ataque para causar dano ×2.5. Gasta metade Stamina. Veja o preparo ativar.",
                "Fúria pronta (próximo ataque ×2.5).");
            fury.setup = () => { if (d.PlayerUnit != null) { d.PlayerUnit.furyReady = 0; Cast(d, "Fury", null, d.PlayerUnit.anchor); } };
            fury.gate = () => d.PlayerUnit != null && d.PlayerUnit.furyReady > 1f;
            fury.assertion = () => d.PlayerUnit != null && d.PlayerUnit.furyReady > 1f ? null : "Fúria não ativou";
            L.Add(fury);

            var stoneskin = new TutorialLesson("stoneskin", 7, "Física: Pele de pedra", "Magia física Pele de pedra",
                "Pele de pedra reduz o dano físico recebido (×5) por 1 round. Veja a proteção ativar.",
                "Pele de pedra ativa (redução de dano).");
            stoneskin.setup = () => { if (d.PlayerUnit != null) { d.PlayerUnit.stoneSkinTicks = 0; Cast(d, "StoneSkin", null, d.PlayerUnit.anchor); } };
            stoneskin.gate = () => d.PlayerUnit != null && d.PlayerUnit.stoneSkinTicks > 0;
            stoneskin.assertion = () => d.PlayerUnit != null && d.PlayerUnit.stoneSkinTicks > 0 ? null : "Pele de pedra não ativou";
            L.Add(stoneskin);

            var curve = new TutorialLesson("curve", 7, "Física: Curva", "Magia física Curva",
                "Curva faz seu próximo ataque à distância seguir o alvo e contornar obstáculos. Veja o preparo ativar.",
                "Curva pronta.");
            curve.setup = () => { if (d.PlayerUnit != null) { d.PlayerUnit.curveReady = false; Cast(d, "Curve", null, d.PlayerUnit.anchor); } };
            curve.gate = () => d.PlayerUnit != null && d.PlayerUnit.curveReady;
            curve.assertion = () => d.PlayerUnit != null && d.PlayerUnit.curveReady ? null : "Curva não ativou";
            L.Add(curve);

            Vector2Int leapStart = Vector2Int.zero;
            var leap = new TutorialLesson("leap", 7, "Física: Salto", "Magia física Salto",
                "Salto move por cima de obstáculos e ameaças, sem provocar ataque de oportunidade. Veja a unidade saltar.",
                "Salto moveu a unidade.");
            leap.setup = () => { if (d.PlayerUnit != null) { leapStart = d.PlayerUnit.anchor; Cast(d, "Leap", null, d.PlayerUnit.anchor + new Vector2Int(0, 3)); } };
            leap.gate = () => d.PlayerUnit != null && d.PlayerUnit.anchor != leapStart;
            leap.assertion = () => d.PlayerUnit != null && d.PlayerUnit.anchor != leapStart ? null : "Salto não moveu";
            L.Add(leap);

            // ── Capítulo 8: Progressão (XP → nível → pontos, via API real) ───────
            CharacterPreset xpPreset = null; int xpLevel0 = 1, xpBudget0 = 15;
            var xp = new TutorialLesson("xp", 8, "Progressão: XP e nível", "Progressão XP/nível/pontos",
                "Vitórias dão XP. Ao juntar XP suficiente, o personagem sobe de nível e ganha mais pontos para distribuir (orçamento 15 + 5×(nível−1)). Veja um nível subir.",
                "XP subiu o nível e aumentou o orçamento.");
            xp.setup = () =>
            {
                xpPreset = new CharacterPreset { level = 1, xp = 0 };
                xpLevel0 = PlayerProfile.Level(xpPreset);
                xpBudget0 = PlayerProfile.BudgetFor(xpPreset);
                PlayerProfile.AddXp(xpPreset, 1000);
            };
            xp.gate = () => xpPreset != null && PlayerProfile.Level(xpPreset) > xpLevel0;
            xp.assertion = () =>
            {
                if (xpPreset == null) return "sem preset";
                if (PlayerProfile.Level(xpPreset) <= xpLevel0) return "nível não subiu";
                if (PlayerProfile.BudgetFor(xpPreset) <= xpBudget0) return "orçamento não aumentou";
                return null;
            };
            L.Add(xp);

            // ── Capítulo 9: Coop (descida de camada, via API real) ───────────────
            var layer = new TutorialLesson("layer", 9, "Coop: descer camada", "Coop descida de camada",
                "No Coop, o grupo desce camadas rumo ao Inferno. Cada camada é mais difícil; recuar é livre. Veja a descida para a camada 2.",
                "Camada avançada (descida).");
            layer.setup = () => { if (d.Round != null) d.Round.BeginNextLayer(2); };
            layer.gate = () => d.Round != null && d.Round.Layer >= 2;
            layer.assertion = () => d.Round != null && d.Round.Layer >= 2 ? null : "camada não avançou";
            L.Add(layer);

            return L;
        }
    }
}