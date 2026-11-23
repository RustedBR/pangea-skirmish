using System;

namespace PangeaSkirmish
{
    /// <summary>Resultado de uma lição do tutorial (vira linha do relatório de teste, GDD-027 §6).</summary>
    public enum TutorialResult { Pending, OK, Failed, Skipped }

    /// <summary>
    /// Como a lição avança (GDD-027 §2): ação do jogador, leitura com "Continuar", ou
    /// demonstração que só roda quando o jogador pede ("Ver demonstração").
    /// </summary>
    public enum TutorialLessonKind { Action, Info, Demo }

    /// <summary>
    /// Uma lição do tutorial (GDD-027). Ensina UM conceito e só avança quando o jogador executa
    /// a ação (gate). A asserção verifica se a mecânica funcionou → banco de testes.
    /// Os delegados (gate/assertion/setup/demo) são montados pelo TutorialScenario, não serializam.
    /// </summary>
    [Serializable]
    public class TutorialLesson
    {
        public string id;
        /// <summary>Compatibilidade com o relatório antigo (capítulo). Fase = phaseIndex.</summary>
        public int chapter;
        /// <summary>Fase do tutorial (0..8). Cada fase tem sua arena e seu elenco.</summary>
        public int phaseIndex;
        public string phaseId;
        public string phaseTitle;
        public string title;
        /// <summary>Instrução curta e direta (uma coisa por vez).</summary>
        public string instruction;
        /// <summary>Texto mostrado depois que o jogador executa a ação (resultado concreto).</summary>
        public string feedback;
        /// <summary>Nome do elemento de UI a realçar, ou "tile"/"unit" para o mundo. Vazio = sem realce.</summary>
        public string highlightTarget;
        /// <summary>Nome da mecânica para o relatório de teste (ex.: "Movimento", "Reação Esquivar").</summary>
        public string mechanic;
        /// <summary>Action = espera o gate; Info = espera "Continuar"; Demo = espera "Ver demonstração".</summary>
        public TutorialLessonKind kind = TutorialLessonKind.Action;

        /// <summary>Prepara o estado da lição (dar Mana, posicionar fera…). Roda ao entrar/repetir.</summary>
        [NonSerialized] public Action setup;
        /// <summary>Roda quando o jogador pede a demonstração (kind == Demo). Depois o gate é polled.</summary>
        [NonSerialized] public Action demo;
        /// <summary>True quando o jogador executou a ação esperada. O director faz poll isto.</summary>
        [NonSerialized] public Func<bool> gate;
        /// <summary>Verifica a mecânica: null = OK; string = detalhe da falha. Roda quando o gate fecha.</summary>
        [NonSerialized] public Func<string> assertion;
        /// <summary>Roda ao entrar na lição (ex.: trocar de arena). Antes do setup.</summary>
        [NonSerialized] public Action onEnter;
        /// <summary>Roda ao sair da lição (limpar realces/estado temporário).</summary>
        [NonSerialized] public Action onExit;
        /// <summary>Define o foco de HUD da lição (TutorialHud) — revela só o relevante. Roda ao entrar.</summary>
        [NonSerialized] public System.Action hudFocus;

        /// <summary>Texto do botão de ação do card (ex.: "Editar personagem"). Vazio = sem botão.</summary>
        public string actionLabel;
        /// <summary>O que o botão de ação do card faz.</summary>
        [NonSerialized] public Action action;
        /// <summary>Checklist de passos do jogador (anexo §2.8). null = sem checklist.</summary>
        public string[] steps;
        /// <summary>Quantos passos o jogador já fez (o director faz poll a cada frame). null = 0.</summary>
        [NonSerialized] public Func<int> stepsDone;
        /// <summary>Feedback com números lidos em runtime (anexo §2.5). Vence o texto fixo.</summary>
        [NonSerialized] public Func<string> feedbackFn;
        /// <summary>Dica inline quando o jogador erra sem fechar a lição. null = sem dica.</summary>
        [NonSerialized] public Func<string> onWrong;
        /// <summary>Resolvedor custom do alvo do anel (quando o nome do VisualElement não basta).</summary>
        [NonSerialized] public Func<UnityEngine.UIElements.VisualElement> highlightResolver;

        [NonSerialized] public TutorialResult result = TutorialResult.Pending;
        [NonSerialized] public string detail;

        public TutorialLesson() { }

        public TutorialLesson(string id, int chapter, string title, string mechanic,
                              string instruction, string feedback = "", string highlightTarget = "")
        {
            this.id = id;
            this.chapter = chapter;
            this.title = title;
            this.mechanic = mechanic;
            this.instruction = instruction;
            this.feedback = feedback;
            this.highlightTarget = highlightTarget;
        }
    }
}