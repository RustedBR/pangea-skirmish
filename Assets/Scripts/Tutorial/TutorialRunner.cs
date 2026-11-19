using System;
using System.Collections.Generic;

namespace PangeaSkirmish
{
    /// <summary>
    /// Motor do tutorial (GDD-027 §3): máquina de estados pura que dirige a sequência de lições.
    /// O TutorialDirector (MonoBehaviour) chama Tick() a cada frame e liga os callbacks à UI.
    /// Separado da Unity para ser testável em EditMode.
    ///
    /// Fluxo por lição: Entra (setup + instrução) → EsperaGate (jogador faz a ação) →
    /// Feedback (asserção + resultado) → Avança (próxima lição ou fim).
    /// </summary>
    public class TutorialRunner
    {
        public enum State { Idle, WaitingGate, Feedback, Done }

        private readonly List<TutorialLesson> _lessons;
        public int Index { get; private set; } = -1;
        public State CurrentState { get; private set; } = State.Idle;

        /// <summary>Entrou numa lição: mostrar instrução + realce (o setup já rodou).</summary>
        public Action<TutorialLesson> OnEnterLesson;
        /// <summary>Gate fechou: mostrar feedback + resultado (a asserção já rodou e foi registrada).</summary>
        public Action<TutorialLesson> OnFeedback;
        /// <summary>Todas as lições terminaram: mostrar relatório.</summary>
        public Action OnCompleted;

        public TutorialRunner(List<TutorialLesson> lessons)
        {
            _lessons = lessons ?? new List<TutorialLesson>();
        }

        public TutorialLesson Current =>
            (Index >= 0 && Index < _lessons.Count) ? _lessons[Index] : null;

        public int Count => _lessons.Count;
        public bool IsDone => CurrentState == State.Done;

        /// <summary>Texto de progresso: "Capítulo 3 · Lição 2/7".</summary>
        public string ProgressText()
        {
            var c = Current;
            if (c == null) return "";
            return $"Capítulo {c.chapter} · Lição {Index + 1}/{_lessons.Count}";
        }

        /// <summary>Começa o tutorial na primeira lição.</summary>
        public void Begin()
        {
            TutorialTestLog.Reset();
            if (_lessons.Count == 0) { CurrentState = State.Done; OnCompleted?.Invoke(); return; }
            Index = 0;
            EnterCurrent();
        }

        private void EnterCurrent()
        {
            var l = Current;
            if (l == null) { Finish(); return; }
            l.result = TutorialResult.Pending;
            l.detail = null;
            l.setup?.Invoke();
            CurrentState = State.WaitingGate;
            OnEnterLesson?.Invoke(l);
        }

        /// <summary>
        /// Chamado a cada frame pelo director. Se o gate da lição atual fechar, avalia a asserção,
        /// registra no log de teste e entra em Feedback. Lição sem gate (informativa) completa no 1º Tick.
        /// </summary>
        public void Tick()
        {
            if (CurrentState != State.WaitingGate) return;
            var l = Current;
            if (l == null) return;

            bool done = l.gate == null || l.gate();
            if (!done) return;

            string fail = l.assertion?.Invoke();
            if (fail == null) { l.result = TutorialResult.OK; l.detail = ""; }
            else { l.result = TutorialResult.Failed; l.detail = fail; }

            TutorialTestLog.Record(l);
            CurrentState = State.Feedback;
            OnFeedback?.Invoke(l);
        }

        /// <summary>Avança para a próxima lição (chamado pelo director após o feedback).</summary>
        public void Advance()
        {
            if (CurrentState == State.Done || CurrentState == State.Idle) return;
            Index++;
            if (Index >= _lessons.Count) { Finish(); return; }
            EnterCurrent();
        }

        /// <summary>Pula a lição atual (registra PULADA) e avança.</summary>
        public void Skip()
        {
            var l = Current;
            if (l != null && CurrentState != State.Done)
            {
                l.result = TutorialResult.Skipped;
                l.detail = "pulada pelo jogador";
                TutorialTestLog.Record(l);
            }
            Advance();
        }

        /// <summary>Repete a lição atual (roda o setup de novo).</summary>
        public void Repeat()
        {
            if (CurrentState == State.Done || Current == null) return;
            EnterCurrent();
        }

        private void Finish()
        {
            CurrentState = State.Done;
            OnCompleted?.Invoke();
        }
    }
}