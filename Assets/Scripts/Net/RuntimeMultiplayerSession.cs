// Net/RuntimeMultiplayerSession.cs
// Dados globais de sessão multiplayer. Sem dependência de NGO/UGS —
// compila mesmo antes dos pacotes serem resolvidos pelo UPM.
// Princípio sagrado: todo código single-player continua intacto quando IsMultiplayer == false.

using System;

namespace PangeaSkirmish
{
    [Serializable]
    public class RoomConfigData
    {
        /// <summary>0 = PvP Times, 1 = PvP Todos contra todos, 2 = Coop (ver GameModes).</summary>
        public int gameMode = GameModes.Coop;   // GDD-024: Coop é o padrão
        /// <summary>Orçamento de atributos: fixo (PlayerProfile.AttributeBudget), igual em toda sala.</summary>
        public int attributeBudget = PlayerProfile.AttributeBudget;
        public float planningTime = 60f; // GDD-014: tempo de um ponto de decisão
        public int maxPlayers = 8;
        /// <summary>Snapshot do GameTuning (JSON) enviado ao servidor quando o anfitrião inicia a partida.
        /// Clientes aplicam em RuntimeTuning.Active. Vazio = usa GameTuning.asset default.</summary>
        public string tuningJson = "";
    }

    /// <summary>Modos de jogo (GDD-015). O valor é o gameMode da sala.</summary>
    public static class GameModes
    {
        public const int PvpTeams = 0, PvpFreeForAll = 1, Coop = 2;

        public static int Current =>
            RuntimeMultiplayerSession.CurrentConfig != null ? RuntimeMultiplayerSession.CurrentConfig.gameMode : PvpTeams;
        public static bool IsCoop => RuntimeMultiplayerSession.IsMultiplayer && Current == Coop;
        /// <summary>Hostilidade por time (PvP Times e Coop) em vez de por dono.</summary>
        public static bool IsTeamBased => Current != PvpFreeForAll;

        public static string Name(int mode) =>
            mode == Coop ? "Coop" : mode == PvpFreeForAll ? "PvP — Todos contra todos" : "PvP — Times";
    }

    public static class RuntimeMultiplayerSession
    {
        // ---- Estado de sessão ------------------------------------------------
        public static bool IsMultiplayer { get; set; } = false;
        public static bool IsHost { get; set; } = false;

        public static ulong LocalClientId { get; set; } = 0;
        public static string JoinCode { get; set; } = string.Empty;
        public static string PlayerName { get; set; } = "Jogador";

        // ---- Configurações da sala -------------------------------------------
        public static RoomConfigData CurrentConfig { get; set; } = new RoomConfigData();

        // ---- Dados da batalha -----------------------------------------------
        /// <summary>Seed do round 1, distribuído pelo host via StartBattleClientRpc.
        /// Guardado aqui para a Fase 6 (LockstepBattleSync) consumir.</summary>
        public static int BattleSeed { get; set; } = 0;

        // ---- Personagem local confirmado ------------------------------------
        /// <summary>CharacterPreset do jogador local confirmado pelo servidor.
        /// Preenchido pelo CharCreationHUD após SubmitCharacterServerRpc aceito.</summary>
        public static CharacterPreset LocalCharacterPreset { get; set; } = null;

        // ---- Reset completo (volta para single-player) -----------------------
        public static void Reset()
        {
            // Tripwire de debug: se o MP "virar SP" no meio de uma partida, este log diz QUEM resetou.
            if (IsMultiplayer)
                UnityEngine.Debug.Log($"[MP] RuntimeMultiplayerSession.Reset() chamado!\n{Environment.StackTrace}");
            IsMultiplayer = false;
            IsHost = false;
            LocalClientId = 0;
            JoinCode = string.Empty;
            PlayerName = "Jogador";
            CurrentConfig = new RoomConfigData();
            BattleSeed = 0;
            LocalCharacterPreset = null;
        }
    }
}
