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

        /// <summary>clientId do cliente na sessão ANTES da queda (NGO troca o id na reconexão).
        /// O host usa para remapear ownerId antigo → novo (ReconnectController).</summary>
        public static ulong PreviousClientId { get; set; } = 0;

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

        // =====================================================================
        // Reconexão — dados persistentes (sobrevivem a queda de conexão E recarga
        // da página no celular). Preenchidos pelo NetBootstrap ao criar/entrar numa
        // sala; usados por ReconnectController ao voltar à partida.
        // =====================================================================
        public static string PendingRelayCode { get; set; } = string.Empty;   // join code do Relay
        public static string PendingLobbyId   { get; set; } = string.Empty;   // GUID do lobby (cliente)
        public static bool   PendingWasHost   { get; set; } = false;          // o jogador era o host?

        private const string RkRelay = "pangea_reconnect_relay";
        private const string RkLobby = "pangea_reconnect_lobby";
        private const string RkHost  = "pangea_reconnect_host";

        public static bool HasPendingSession =>
            !string.IsNullOrEmpty(PendingRelayCode);

        /// <summary>Grava o estado da partida atual em PlayerPrefs (chamado ao entrar na sala).</summary>
        public static void SavePendingSession(string relayCode, string lobbyId, bool wasHost)
        {
            PendingRelayCode = relayCode ?? string.Empty;
            PendingLobbyId   = lobbyId ?? string.Empty;
            PendingWasHost   = wasHost;
            UnityEngine.PlayerPrefs.SetString(RkRelay, PendingRelayCode);
            UnityEngine.PlayerPrefs.SetString(RkLobby, PendingLobbyId);
            UnityEngine.PlayerPrefs.SetInt(RkHost, PendingWasHost ? 1 : 0);
            UnityEngine.PlayerPrefs.Save();
            UnityEngine.Debug.Log($"[MP] Reconexao: sessao salva (relay=\"{PendingRelayCode}\" host={PendingWasHost}).");
        }

        /// <summary>Recarrega do PlayerPrefs (ao boot do MainMenu / antes de tentar reconectar).</summary>
        public static void LoadPendingSessionFromDisk()
        {
            PendingRelayCode = UnityEngine.PlayerPrefs.GetString(RkRelay, "");
            PendingLobbyId   = UnityEngine.PlayerPrefs.GetString(RkLobby, "");
            PendingWasHost   = UnityEngine.PlayerPrefs.GetInt(RkHost, 0) == 1;
            if (!string.IsNullOrEmpty(PendingRelayCode))
                UnityEngine.Debug.Log($"[MP] Reconexao: sessao carregada do disco (relay=\"{PendingRelayCode}\" host={PendingWasHost}).");
        }

        /// <summary>Limpa a sessão pendente (sair da partida com intenção ou reset).</summary>
        public static void ClearPendingSession()
        {
            PendingRelayCode = string.Empty;
            PendingLobbyId   = string.Empty;
            PendingWasHost   = false;
            UnityEngine.PlayerPrefs.DeleteKey(RkRelay);
            UnityEngine.PlayerPrefs.DeleteKey(RkLobby);
            UnityEngine.PlayerPrefs.DeleteKey(RkHost);
            UnityEngine.PlayerPrefs.Save();
        }

        // ---- Reset completo (volta para single-player) -----------------------
        public static void Reset()
        {
            // Tripwire de debug: se o MP "virar SP" no meio de uma partida, este log diz QUEM resetou.
            if (IsMultiplayer)
                UnityEngine.Debug.Log($"[MP] RuntimeMultiplayerSession.Reset() chamado!\n{Environment.StackTrace}");
            IsMultiplayer = false;
            IsHost = false;
            LocalClientId = 0;
            PreviousClientId = 0;
            JoinCode = string.Empty;
            PlayerName = "Jogador";
            CurrentConfig = new RoomConfigData();
            BattleSeed = 0;
            LocalCharacterPreset = null;
        }
    }
}
