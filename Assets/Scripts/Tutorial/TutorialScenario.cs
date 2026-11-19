using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Lições do tutorial (GDD-027 §4). Primeira fatia: capítulos 0–2 (orientação, planejamento,
    /// movimento). Cada lição tem um gate que lê o estado REAL do jogo — completá-la exercita a
    /// mecânica (banco de testes). Os baselines são capturados no setup de cada lição.
    /// </summary>
    public static class TutorialScenario
    {
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

            var bar = new TutorialLesson("bar", 0, "A barra de ticks", "Barra de ticks (leitura)",
                "Em cima fica a barra de ticks: o tempo do combate. Cada ação ocupa um trecho dela. Você planeja e confirma o round; as ações resolvem juntas, tick a tick.",
                "Conceito apresentado.");
            // lição informativa: sem gate, completa no primeiro Tick.
            L.Add(bar);

            // ── Capítulo 1: Planejamento semi-turno ─────────────────────────────
            var plan = new TutorialLesson("plan", 1, "Enfileire uma andada", "Planejar movimento",
                "Clique no botão Andar (ou tecla 1) e depois clique numa célula do chão para enfileirar um movimento. Veja o bloco aparecer na barra de ticks.",
                "Movimento enfileirado na barra.");
            plan.gate = () => d.PlayerUnit != null && d.PlayerUnit.plannedMoveCount > 0;
            L.Add(plan);

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
                if (d.Fera != null) { d.Fera.currentHP = 300; feraHp0 = d.Fera.currentHP; } // fera tanque p/ sobreviver às lições
                if (d.PlayerUnit != null) { d.PlayerUnit.currentHP = 45; d.PlayerUnit.GainStamina(10); }
            };
            attack.gate = () => d.Fera != null && d.Fera.currentHP < feraHp0;
            L.Add(attack);

            // ── Capítulo 4: Reações ─────────────────────────────────────────────
            L.Add(new TutorialLesson("reactions", 4, "Reações de defesa", "Reações (leitura)",
                "Quando um inimigo ataca, você pode reagir: Defender (Bloquear por VIT ou Esquivar por AGI), Contra-atacar ou Interpor. Reações custam Stamina e você só pode usar UMA por round. Prepare uma reação padrão no plano para responder automaticamente.",
                "Conceito apresentado."));

            // ── Capítulo 5: Magia — básico ──────────────────────────────────────
            int mana0 = 0;
            var cast = new TutorialLesson("cast", 5, "Conjure Brasa (Fogo)", "Conjurar magia elemental",
                "Abra a aba Magia, escolha Brasa (Fogo), mire na fera e confirme o round. Magia Elemental gasta Mana. Veja a Mana cair.",
                "Magia conjurada — Mana gasta.");
            cast.setup = () =>
            {
                if (d.PlayerUnit != null) { d.PlayerUnit.currentMana = 20; mana0 = d.PlayerUnit.currentMana; }
                if (d.Fera != null && d.Fera.currentHP < 50) d.Fera.currentHP = 300;
            };
            cast.gate = () => d.PlayerUnit != null && d.PlayerUnit.currentMana < mana0;
            L.Add(cast);

            // ── Capítulo 6: Magia — avançado ────────────────────────────────────
            L.Add(new TutorialLesson("advanced", 6, "Área, status e incrementos", "Magia avançada (leitura)",
                "Magias de área (Explosão), status (Queimando/Encharcado), zonas (Magma/Lama), barreira (Muralha) e cura (Bálsamo, a única área que toca aliados). Os incrementos (Potência/Área/Alcance/Duração/Rápida) melhoram a magia por mais Mana. Conjuração elemental pode ser interrompida por dano.",
                "Conceito apresentado."));

            // ── Capítulo 7: Magia Física ────────────────────────────────────────
            L.Add(new TutorialLesson("physical", 7, "Magia Física do corpo", "Magia física (leitura)",
                "Magias Físicas amplificam o corpo e gastam metade Stamina: Fúria (próximo ataque ×2.5), Salto (move por cima de obstáculos), Pele de pedra (redução de dano) e Curva (ataque à distância segue o alvo). Não são interrompidas.",
                "Conceito apresentado."));

            // ── Capítulo 8: Atributos e progressão ──────────────────────────────
            L.Add(new TutorialLesson("progression", 8, "Atributos e progressão", "Progressão (leitura)",
                "6 atributos: Corpo (STR, VIT, AGI, DEX) e Mente (INT, WIS). Vitórias dão XP → nível → mais pontos para distribuir. Orçamento: 15 + 5×(nível−1). Edite seu personagem no Perfil. Personagens de nível>2 só podem redistribuir pontos (nome/arma travados).",
                "Conceito apresentado."));

            // ── Capítulo 9: Coop ────────────────────────────────────────────────
            L.Add(new TutorialLesson("coop", 9, "Coop: a descida", "Coop (leitura)",
                "No Coop, desça camadas rumo ao Inferno. Descanse para recuperar Vida/Mana/Stamina, abra baús (loot) e equipe. Cada camada é mais difícil e os conduítes mais fundos ressoam mais forte. Recuar (subir) é sempre livre.",
                "Conceito apresentado."));

            return L;
        }
    }
}