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
        public int gameMode = 0;
        public int attributeBudget = 30;
        public float planningTime = 8f; // GDD-014: tempo de um ponto de decisão
        public int maxPlayers = 8;
        /// <summary>Snapshot do GameTuning (JSON) enviado ao servidor quando a sala entra em CharCreation.
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

        /// <summary>
        /// true quando estamos numa "sala" loopback SOLO criada pelo menu principal
        /// para edição de conteúdo (criar mapa / criar personagem) OFFLINE.
        /// Nesse modo o CharCreationHUD e o Sandbox salvam LOCALMENTE
        /// (CharacterStorage / MapStorage) em vez de enviar ao RoomManager, e o
        /// "voltar ao menu" faz shutdown do host loopback + Reset().
        /// </summary>
        public static bool IsLocalContentSession { get; set; } = false;
        public static ulong LocalClientId { get; set; } = 0;
        public static string JoinCode { get; set; } = string.Empty;
        public static string PlayerName { get; set; } = "Jogador";

        // ---- Configurações da sala -------------------------------------------
        public static RoomConfigData CurrentConfig { get; set; } = new RoomConfigData();

        // ---- Dados da batalha -----------------------------------------------
        /// <summary>Seed do round 1, distribuído pelo host via StartBattleClientRpc.
        /// Guardado aqui para a Fase 6 (LockstepBattleSync) consumir.</summary>
        public static int BattleSeed { get; set; } = 0;

        // ---- Mapa colaborativo final ----------------------------------------
        /// <summary>Mapa colaborativo finalizado (snapshot do host). Usado no
        /// GameBootstrap MP para montar o terreno sem unidades de placement.</summary>
        public static MapData CollabMap { get; set; } = null;

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
            IsLocalContentSession = false;
            LocalClientId = 0;
            JoinCode = string.Empty;
            PlayerName = "Jogador";
            CurrentConfig = new RoomConfigData();
            BattleSeed = 0;
            CollabMap = null;
            LocalCharacterPreset = null;
        }
    }
}
