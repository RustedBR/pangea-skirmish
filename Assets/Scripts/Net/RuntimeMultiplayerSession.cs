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
        /// <summary>0 = TDM, 1 = FFA</summary>
        public int gameMode = 0;
        public int attributeBudget = 30;
        public float planningTime = 15f;
        public int maxPlayers = 4;
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

        // ---- Reset completo (volta para single-player) -----------------------
        public static void Reset()
        {
            IsMultiplayer = false;
            IsHost = false;
            LocalClientId = 0;
            JoinCode = string.Empty;
            PlayerName = "Jogador";
            CurrentConfig = new RoomConfigData();
        }
    }
}
