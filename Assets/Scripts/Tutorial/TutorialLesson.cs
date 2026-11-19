using System;

namespace PangeaSkirmish
{
    /// <summary>Resultado de uma lição do tutorial (vira linha do relatório de teste, GDD-027 §6).</summary>
    public enum TutorialResult { Pending, OK, Failed, Skipped }

    /// <summary>
    /// Uma lição do tutorial (GDD-027). Ensina UM conceito e só avança quando o jogador executa
    /// a ação (gate). A asserção verifica se a mecânica funcionou → banco de testes.
    /// Os delegados (gate/assertion/setup) são montados pelo TutorialScenario, não serializam.
    /// </summary>
    [Serializable]
    public class TutorialLesson
    {
        public string id;
        public int chapter;
        public string title;
        /// <summary>Instrução curta e direta (uma coisa por vez).</summary>
        public string instruction;
        /// <summary>Texto mostrado depois que o jogador executa a ação (resultado concreto).</summary>
        public string feedback;
        /// <summary>Nome do elemento de UI a realçar, ou "tile"/"unit" para o mundo. Vazio = sem realce.</summary>
        public string highlightTarget;
        /// <summary>Nome da mecânica para o relatório de teste (ex.: "Movimento", "Reação Esquivar").</summary>
        public string mechanic;

        /// <summary>Prepara o estado da lição (dar Mana, posicionar fera…). Roda ao entrar/repetir.</summary>
        [NonSerialized] public Action setup;
        /// <summary>True quando o jogador executou a ação esperada. O director faz poll isto.</summary>
        [NonSerialized] public Func<bool> gate;
        /// <summary>Verifica a mecânica: null = OK; string = detalhe da falha. Roda quando o gate fecha.</summary>
        [NonSerialized] public Func<string> assertion;

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