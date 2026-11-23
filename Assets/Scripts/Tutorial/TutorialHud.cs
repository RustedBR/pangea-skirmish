using System.Collections.Generic;

namespace PangeaSkirmish
{
    /// <summary>
    /// Foco de HUD do tutorial (GDD-027): revela só o que é relevante para a lição atual.
    /// Estado estático lido pelo PlanningController.BuildActionBar (filtra ações) e pelo
    /// BattleHUD.SetTutorialVisibility (mostra/esconde painéis). O TutorialDirector define o
    /// foco ao entrar na lição e o mantém no Update. Aditivo — não altera o fluxo normal.
    /// </summary>
    public static class TutorialHud
    {
        public static bool Active;
        /// <summary>Ids de ação permitidos (move/attack/aim/spell/concentrate/breathe/wait/posture/reaction). null = todas.</summary>
        public static HashSet<string> Actions;
        /// <summary>Botões do painel de round permitidos (confirm/undo/clear). null = todos; vazio = nenhum.</summary>
        public static HashSet<string> RoundButtons;

        /// <summary>A ação está liberada nesta lição? (fora do tutorial, sempre.)</summary>
        public static bool Allows(string actionId) => !Active || Actions == null || Actions.Contains(actionId);
        /// <summary>O botão de round está liberado nesta lição?</summary>
        public static bool AllowsRound(string id) => !Active || RoundButtons == null || RoundButtons.Contains(id);
        /// <summary>True enquanto o card de feedback está aberto: o plano ignora teclas (Espaço = Próxima).</summary>
        public static bool BlockPlanInput;
        /// <summary>O editor de personagem (Cap. 8) está aberto por cima da batalha: o card da lição vai para a direita.</summary>
        public static bool EditorOpen;
        /// <summary>A fera só Espera (capítulos antes das reações).</summary>
        public static bool BeastIdle;
        public static bool ActionBar = true, Round = true, TickBar = true, UnitInfo = true, Topbar = true;

        /// <summary>Só o mapa + overlay (lição de câmera).</summary>
        public static void None()
        {
            Active = true; Actions = new HashSet<string>(); RoundButtons = new HashSet<string>();
            ActionBar = false; Round = false; TickBar = false; UnitInfo = false; Topbar = false;
        }

        /// <summary>Só a barra de ticks (lição de leitura da barra).</summary>
        public static void Ticks()
        {
            None(); TickBar = true;   // relógio, bússola e faixa de dicas ficam ocultos
        }

        /// <summary>Barra de ações filtrada + painéis de round/ticks/ficha.</summary>
        public static void Show(string[] actions, string[] roundButtons = null, bool tickBar = true, bool unitInfo = false)
        {
            Active = true;
            Actions = actions == null ? null : new HashSet<string>(actions);
            RoundButtons = new HashSet<string>(roundButtons ?? new string[0]);
            bool round = RoundButtons.Count > 0 || Actions != null && (Actions.Contains("posture") || Actions.Contains("reaction"));
            ActionBar = true; Round = round; TickBar = tickBar; UnitInfo = unitInfo; Topbar = false;   // só a UI da lição
        }

        /// <summary>Restaura a HUD normal (fim do tutorial).</summary>
        public static void Clear()
        {
            Active = false; Actions = null; RoundButtons = null; BlockPlanInput = false; BeastIdle = false; EditorOpen = false;
            ActionBar = true; Round = true; TickBar = true; UnitInfo = true; Topbar = true;
        }
    }
}