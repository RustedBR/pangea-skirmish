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

            return L;
        }
    }
}