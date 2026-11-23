using System;
using System.Collections.Generic;

namespace PangeaSkirmish
{
    /// <summary>
    /// Motor do tutorial (GDD-027 §3): máquina de estados pura que dirige a sequência de lições.
    /// O TutorialDirector (MonoBehaviour) chama Tick() a cada frame e liga os callbacks à UI.
    /// Separado da Unity para ser testável em EditMode.
    ///
    /// Fluxo por lição: Entra (setup + instrução) → espera a ação (Action), o "Continuar"
    /// (Info) ou o "Ver demonstração" (Demo) → Feedback (asserção + resultado) → Avança.
    /// Nada completa no mesmo frame em que entra: o director aplica um dwell mínimo.
    /// </summary>
    public class TutorialRunner
    {
        public enum State { Idle, WaitingGate, WaitingContinue, WaitingDemo, Feedback, Done }

        private readonly List<TutorialLesson> _lessons;
        public int Index { get; private set; } = -1;
        public State CurrentState { get; private set; } = State.Idle;

        /// <summary>Entrou numa lição: mostrar instrução + realce (o setup já rodou).</summary>
        public Action<TutorialLesson> OnEnterLesson;
        /// <summary>Gate fechou: mostrar feedback + resultado (a asserção já rodou e foi registrada).</summary>
        public Action<TutorialLesson> OnFeedback;
        /// <summary>Todas as lições terminaram: mostrar relatório.</summary>
        public Action OnCompleted;
        /// <summary>Antes de entrar/repetir uma lição: o director garante que o jogo esteja em Planejamento.</summary>
        public Action OnBeforeEnter;
        /// <summary>Terminou o último passo de um capítulo (índice do capítulo): salvar o checkpoint.</summary>
        public Action<int> OnChapterDone;

        public TutorialRunner(List<TutorialLesson> lessons)
        {
            _lessons = lessons ?? new List<TutorialLesson>();
        }

        public TutorialLesson Current =>
            (Index >= 0 && Index < _lessons.Count) ? _lessons[Index] : null;

        public int Count => _lessons.Count;
        public bool IsDone => CurrentState == State.Done;

        /// <summary>Texto de progresso: "Fase 2 · Ataque · 1/4".</summary>
        public string ProgressText()
        {
            var c = Current;
            if (c == null) return "";
            int total = 0, at = 0;
            for (int i = 0; i < _lessons.Count; i++)
            {
                if (_lessons[i].phaseIndex != c.phaseIndex) continue;
                total++;
                if (i <= Index) at++;
            }
            string phase = string.IsNullOrEmpty(c.phaseTitle)
                ? $"Fase {c.phaseIndex}"
                : $"Fase {c.phaseIndex} · {c.phaseTitle}";
            return $"{phase} · Lição {at}/{MathfMax(total, 1)}";
        }

        private static int MathfMax(int a, int b) => a > b ? a : b;

        /// <summary>Começa o tutorial na primeira lição.</summary>
        public void Begin()
        {
            TutorialTestLog.Reset();
            if (_lessons.Count == 0) { CurrentState = State.Done; OnCompleted?.Invoke(); return; }
            Index = 0;
            EnterCurrent();
        }

        /// <summary>Pula direto para uma lição (menu de lições). Mantém o relatório já registrado.</summary>
        public void BeginAt(int index, bool keepLog = false)
        {
            if (_lessons.Count == 0) return;
            if (Index < 0 && !keepLog) TutorialTestLog.Reset();
            else Current?.onExit?.Invoke();
            Index = index < 0 ? 0 : index >= _lessons.Count ? _lessons.Count - 1 : index;
            EnterCurrent();
        }

        private void EnterCurrent()
        {
            var l = Current;
            if (l == null) { Finish(); return; }
            OnBeforeEnter?.Invoke();
            l.result = TutorialResult.Pending;
            l.detail = null;
            l.onEnter?.Invoke();
            l.setup?.Invoke();

            if (l.kind == TutorialLessonKind.Info) CurrentState = State.WaitingContinue;
            else if (l.kind == TutorialLessonKind.Demo) CurrentState = State.WaitingDemo;
            else CurrentState = State.WaitingGate;

            OnEnterLesson?.Invoke(l);
        }

        /// <summary>
        /// Chamado a cada frame pelo director. Só a lição de AÇÃO avança por gate polled.
        /// Info espera Continue(); Demo espera RunDemo().
        /// </summary>
        public void Tick()
        {
            if (CurrentState != State.WaitingGate) return;
            var l = Current;
            if (l == null) return;

            bool done = l.gate == null || l.gate();
            if (!done) return;
            CompleteCurrent();
        }

        /// <summary>Fecha a lição atual: roda a asserção, registra e entra em Feedback.</summary>
        private void CompleteCurrent()
        {
            var l = Current;
            if (l == null) return;
            string fail = l.assertion?.Invoke();
            if (fail == null) { l.result = TutorialResult.OK; l.detail = ""; }
            else { l.result = TutorialResult.Failed; l.detail = fail; }

            TutorialTestLog.Record(l);
            CurrentState = State.Feedback;
            OnFeedback?.Invoke(l);
        }

        /// <summary>Lição informativa: o jogador leu e pediu para continuar.</summary>
        public void Continue()
        {
            if (CurrentState != State.WaitingContinue) return;
            CompleteCurrent();
        }

        /// <summary>Lição de demonstração: o jogador pediu para ver. Roda o demo e passa a polled o gate.</summary>
        public void RunDemo()
        {
            if (CurrentState != State.WaitingDemo) return;
            var l = Current;
            if (l == null) return;
            l.demo?.Invoke();
            if (l.gate == null) { CompleteCurrent(); return; }
            CurrentState = State.WaitingGate;
        }

        /// <summary>Avança para a próxima lição (chamado pelo director após o feedback).</summary>
        public void Advance()
        {
            if (CurrentState == State.Done || CurrentState == State.Idle) return;
            var leaving = Current;
            leaving?.onExit?.Invoke();
            Index++;
            if (leaving != null && (Index >= _lessons.Count || _lessons[Index].chapter != leaving.chapter))
                OnChapterDone?.Invoke(leaving.chapter);
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

        /// <summary>Repete a lição atual (roda o setup de novo; Demo volta a esperar "Ver").</summary>
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
