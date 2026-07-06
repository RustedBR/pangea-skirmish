// Net/RoomManager.cs
// NetworkBehaviour autoridade da sala.
// Spawn: host cria GameObject em runtime e chama NetworkObject.Spawn() logo após StartHost.
// NetBootstrap cria este objeto. Veja NetBootstrap.SpawnRoomManager().
//
// Escolha de spawn: objeto "spawned" (não in-scene), registrado como prefab em runtime via
// NetworkManager.Prefabs.Add() antes do Spawn(). Mais simples que prefab asset para um objeto
// criado 100% por código.

using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PangeaSkirmish
{
    // -------------------------------------------------------------------------
    // Enum de fases da sala
    // -------------------------------------------------------------------------
    public enum RoomPhase
    {
        Lobby = 0,
        CharCreation,
        MapEditing,
        Placement,
        Battle,
        PostGame
    }

    // -------------------------------------------------------------------------
    // Configuração serializável de rede (espelha RoomConfigData)
    // -------------------------------------------------------------------------
    public struct RoomConfigNet : INetworkSerializable, IEquatable<RoomConfigNet>
    {
        public int GameMode;        // 0 = TDM, 1 = FFA
        public int AttributeBudget;
        public float PlanningTime;
        public int MaxPlayers;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref GameMode);
            serializer.SerializeValue(ref AttributeBudget);
            serializer.SerializeValue(ref PlanningTime);
            serializer.SerializeValue(ref MaxPlayers);
        }

        public bool Equals(RoomConfigNet other) =>
            GameMode == other.GameMode &&
            AttributeBudget == other.AttributeBudget &&
            PlanningTime.Equals(other.PlanningTime) &&
            MaxPlayers == other.MaxPlayers;

        public static RoomConfigNet FromData(RoomConfigData d) => new RoomConfigNet
        {
            GameMode = d.gameMode,
            AttributeBudget = d.attributeBudget,
            PlanningTime = d.planningTime,
            MaxPlayers = d.maxPlayers
        };

        public RoomConfigData ToData() => new RoomConfigData
        {
            gameMode = GameMode,
            attributeBudget = AttributeBudget,
            planningTime = PlanningTime,
            maxPlayers = MaxPlayers
        };
    }

    // -------------------------------------------------------------------------
    // Slot de jogador
    // -------------------------------------------------------------------------
    public struct PlayerSlot : INetworkSerializable, IEquatable<PlayerSlot>
    {
        public ulong ClientId;
        public FixedString64Bytes PlayerName;
        public int Team;
        public bool ReadyMap;
        public bool ReadyChar;
        public bool Placed;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref PlayerName);
            serializer.SerializeValue(ref Team);
            serializer.SerializeValue(ref ReadyMap);
            serializer.SerializeValue(ref ReadyChar);
            serializer.SerializeValue(ref Placed);
        }

        public bool Equals(PlayerSlot other) =>
            ClientId == other.ClientId &&
            PlayerName.Equals(other.PlayerName) &&
            Team == other.Team &&
            ReadyMap == other.ReadyMap &&
            ReadyChar == other.ReadyChar &&
            Placed == other.Placed;
    }

    // -------------------------------------------------------------------------
    // RoomManager — NetworkBehaviour
    // -------------------------------------------------------------------------
    public class RoomManager : NetworkBehaviour
    {
        // ---- Singleton de conveniência (existe 1 por sessão) ----------------
        public static RoomManager Instance { get; private set; }

        // ---- Dados de rede --------------------------------------------------
        // NetworkList DEVE ser inicializado no campo ou no Awake (nunca OnNetworkSpawn)
        private NetworkList<PlayerSlot> _slots;
        private NetworkVariable<RoomPhase> _phase;
        private NetworkVariable<RoomConfigNet> _config;

        // ---- Eventos locais (para o HUD) ------------------------------------
        public event Action OnSlotsChanged;
        public event Action<RoomPhase> OnPhaseChanged;
        public event Action OnConfigChanged; // config da sala (modo/budget/timer) mudou
        public event Action<string, string> OnChatMessage; // (senderName, msg)

        // ---- Awake: cria NetworkList/Variable antes do Spawn ----------------
        private void Awake()
        {
            _slots = new NetworkList<PlayerSlot>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

            _phase = new NetworkVariable<RoomPhase>(
                RoomPhase.Lobby,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

            _config = new NetworkVariable<RoomConfigNet>(
                new RoomConfigNet { GameMode = 0, AttributeBudget = 30, PlanningTime = 15f, MaxPlayers = 4 },
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;

            _slots.OnListChanged += _ => OnSlotsChanged?.Invoke();
            _phase.OnValueChanged += (_, newVal) => OnPhaseChanged?.Invoke(newVal);
            _config.OnValueChanged += (_, newVal) =>
            {
                RuntimeMultiplayerSession.CurrentConfig = newVal.ToData();
                OnConfigChanged?.Invoke();
            };

            if (IsServer)
            {
                // Registrar callbacks de conexão
                NetworkManager.Singleton.OnClientConnectedCallback += ServerOnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += ServerOnClientDisconnected;

                // Adicionar slot do host
                AddSlot(NetworkManager.Singleton.LocalClientId, RuntimeMultiplayerSession.PlayerName);
            }

            // Sincronizar config atual no cliente
            RuntimeMultiplayerSession.CurrentConfig = _config.Value.ToData();
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= ServerOnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= ServerOnClientDisconnected;
            }
            if (Instance == this) Instance = null;
        }

        // ---- Accessors públicos (leitura) ------------------------------------
        public NetworkList<PlayerSlot> Slots => _slots;
        public RoomPhase CurrentPhase => _phase.Value;
        public RoomConfigNet CurrentConfig => _config.Value;

        // ---- Callbacks do servidor ------------------------------------------
        private void ServerOnClientConnected(ulong clientId)
        {
            // Nome será enviado via SetPlayerNameRpc em seguida
            AddSlot(clientId, "...");
            Debug.Log($"[MP] Player conectou: clientId={clientId} | jogadores na sala={_slots.Count}");
        }

        private void ServerOnClientDisconnected(ulong clientId)
        {
            // Obter nome antes de remover o slot
            string playerName = $"Jogador({clientId})";
            for (int i = 0; i < _slots.Count; i++)
                if (_slots[i].ClientId == clientId) { playerName = _slots[i].PlayerName.ToString(); break; }

            RemoveSlot(clientId);

            // Toast/chat para todos
            PlayerDisconnectedClientRpc(playerName);

            // Durante batalha: eliminar unidades do jogador desconectado
            if (_phase.Value == RoomPhase.Battle && LockstepBattleSync.Instance != null)
                LockstepBattleSync.Instance.EliminateDisconnectedPlayer(clientId);
        }

        [ClientRpc]
        private void PlayerDisconnectedClientRpc(string playerName)
        {
            OnChatMessage?.Invoke("Sistema", $"{playerName} saiu da partida.");
        }

        // ---- Helpers de slot (servidor) -------------------------------------
        private void AddSlot(ulong clientId, string playerName)
        {
            // Evita duplicata
            for (int i = 0; i < _slots.Count; i++)
                if (_slots[i].ClientId == clientId) return;

            int team = DetermineDefaultTeam(clientId);
            _slots.Add(new PlayerSlot
            {
                ClientId = clientId,
                PlayerName = playerName,
                Team = team,
                ReadyMap = false,
                ReadyChar = false,
                Placed = false
            });
        }

        private void RemoveSlot(ulong clientId)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].ClientId == clientId)
                {
                    _slots.RemoveAt(i);
                    return;
                }
            }
        }

        private int DetermineDefaultTeam(ulong clientId)
        {
            // TDM: distribui alternando 0/1; FFA: índice do slot
            int idx = _slots.Count;
            if (_config.Value.GameMode == 1) return idx; // FFA
            return idx % 2; // TDM
        }

        private int FindSlot(ulong clientId)
        {
            for (int i = 0; i < _slots.Count; i++)
                if (_slots[i].ClientId == clientId) return i;
            return -1;
        }

        // =========================================================================
        // RPCs
        // =========================================================================

        // --- Cliente envia seu nome ao conectar --------------------------------
        [ServerRpc(RequireOwnership = false)]
        public void SetPlayerNameServerRpc(string name, ServerRpcParams rpc = default)
        {
            int idx = FindSlot(rpc.Receive.SenderClientId);
            if (idx < 0) return;
            var slot = _slots[idx];
            slot.PlayerName = name;
            _slots[idx] = slot;
        }

        // --- Chat --------------------------------------------------------------
        [ServerRpc(RequireOwnership = false)]
        public void SendChatServerRpc(string msg, ServerRpcParams rpc = default)
        {
            int idx = FindSlot(rpc.Receive.SenderClientId);
            string senderName = idx >= 0 ? _slots[idx].PlayerName.ToString() : "?";
            Debug.Log($"[MP] Chat (host recebeu) {senderName}: {msg}");
            ChatMessageClientRpc(senderName, msg);
        }

        [ClientRpc]
        private void ChatMessageClientRpc(string senderName, string msg)
        {
            Debug.Log($"[MP] Chat (broadcast recebido) {senderName}: {msg}");
            OnChatMessage?.Invoke(senderName, msg);
        }

        // --- Mudar time de um jogador (host envia) ----------------------------
        [ServerRpc(RequireOwnership = false)]
        public void SetTeamServerRpc(ulong targetClientId, int team, ServerRpcParams rpc = default)
        {
            // Só o host chama; verificação por clientId
            if (rpc.Receive.SenderClientId != NetworkManager.Singleton.LocalClientId) return;
            int idx = FindSlot(targetClientId);
            if (idx < 0) return;
            var slot = _slots[idx];
            slot.Team = team;
            _slots[idx] = slot;
            Debug.Log($"[MP] Troca de time: clientId={targetClientId} -> time={team}");
        }

        // --- Configurar sala (host envia) -------------------------------------
        [ServerRpc(RequireOwnership = false)]
        public void SetConfigServerRpc(RoomConfigNet cfg, ServerRpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != NetworkManager.Singleton.LocalClientId) return;
            Debug.Log($"[MP] Config alterada: modo={(cfg.GameMode == 0 ? "TDM" : "FFA")} budget={cfg.AttributeBudget} planejamento={cfg.PlanningTime}s");
            _config.Value = cfg;
        }

        // --- Avançar fase (host envia) ----------------------------------------
        [ServerRpc(RequireOwnership = false)]
        public void AdvancePhaseServerRpc(ServerRpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != NetworkManager.Singleton.LocalClientId) return;
            var next = (RoomPhase)((int)_phase.Value + 1);
            Debug.Log($"[MP] AdvancePhase: {_phase.Value} -> {next}");
            if ((int)next < System.Enum.GetValues(typeof(RoomPhase)).Length)
            {
                _phase.Value = next;
                ApplyPhaseDefaults(next);
            }
        }

        // -------------------------------------------------------------------------
        // Fase 2 — defaults ao avançar fase
        // -------------------------------------------------------------------------
        private void ApplyPhaseDefaults(RoomPhase phase)
        {
            // Garante config populada em RuntimeMultiplayerSession
            RuntimeMultiplayerSession.CurrentConfig = _config.Value.ToData();

            if (phase == RoomPhase.MapEditing)
            {
                // TDM: redistribui times 0/1 balanceados; FFA: team = índice do slot
                for (int i = 0; i < _slots.Count; i++)
                {
                    var slot = _slots[i];
                    slot.Team = _config.Value.GameMode == 1 ? i : (i % 2);
                    _slots[i] = slot;
                }

                // Transição de cena sincronizada pelo NGO Scene Management
                if (NetworkManager.Singleton.SceneManager != null)
                    NetworkManager.Singleton.SceneManager.LoadScene("Sandbox", LoadSceneMode.Single);
            }
            else if (phase == RoomPhase.CharCreation)
            {
                // Permanece na cena Sandbox (CharCreationHUD será exibido por overlay)
                PhaseChangedClientRpc(phase);
            }
            else if (phase == RoomPhase.Placement)
            {
                if (NetworkManager.Singleton.SceneManager != null)
                    NetworkManager.Singleton.SceneManager.LoadScene("Battle", LoadSceneMode.Single);
            }
        }

        // Notifica clientes de mudança de fase (para fases sem LoadScene)
        [ClientRpc]
        private void PhaseChangedClientRpc(RoomPhase phase)
        {
            OnPhaseChanged?.Invoke(phase);
        }

        // -------------------------------------------------------------------------
        // Fase 3 — ReadyMap + AdvancePhase para CharCreation
        // -------------------------------------------------------------------------
        [ServerRpc(RequireOwnership = false)]
        public void SetReadyMapServerRpc(bool ready, ServerRpcParams rpc = default)
        {
            int idx = FindSlot(rpc.Receive.SenderClientId);
            if (idx < 0) return;
            var slot = _slots[idx];
            slot.ReadyMap = ready;
            _slots[idx] = slot;

            // Verificar se todos estão prontos
            if (ready) CheckAllReadyMap();
        }

        private void CheckAllReadyMap()
        {
            if (_slots.Count == 0) return;
            for (int i = 0; i < _slots.Count; i++)
                if (!_slots[i].ReadyMap) return;

            // Todos prontos: congela o mapa (snapshot final a todos) e vai para o posicionamento
            CollabMapSync.Instance?.SendFinalSnapshotAndAdvance();
            _phase.Value = RoomPhase.Placement;
            ApplyPhaseDefaults(RoomPhase.Placement);
        }

        // -------------------------------------------------------------------------
        // Fase 4 — SubmitCharacter + ReadyChar
        // -------------------------------------------------------------------------

        // Dicionário servidor: clientId → CharacterPreset validado
        private readonly System.Collections.Generic.Dictionary<ulong, CharacterPreset> _submittedChars
            = new System.Collections.Generic.Dictionary<ulong, CharacterPreset>();

        public System.Collections.Generic.Dictionary<ulong, CharacterPreset> SubmittedCharacters
            => _submittedChars;

        [ServerRpc(RequireOwnership = false)]
        public void SubmitCharacterServerRpc(string presetJson, ServerRpcParams rpc = default)
        {
            ulong senderId = rpc.Receive.SenderClientId;
            CharacterPreset preset;
            try { preset = JsonUtility.FromJson<CharacterPreset>(presetJson); }
            catch { CharacterRejectedClientRpc("JSON inválido", BuildTargetParams(senderId)); return; }

            if (preset == null) { CharacterRejectedClientRpc("Preset nulo", BuildTargetParams(senderId)); return; }

            // Revalidar budget (autoridade do servidor)
            int budget = _config.Value.AttributeBudget;
            var s = preset.stats;
            int total = (int)(s.STR + s.VIT + s.DEX + s.AGI + s.INT + s.WIS);
            if (total > budget)
            {
                CharacterRejectedClientRpc($"Budget excedido ({total}/{budget})", BuildTargetParams(senderId));
                return;
            }

            float min = CharacterConfig.AttrMin, max = CharacterConfig.AttrMax;
            if (s.STR < min || s.STR > max || s.VIT < min || s.VIT > max ||
                s.DEX < min || s.DEX > max || s.AGI < min || s.AGI > max ||
                s.INT < min || s.INT > max || s.WIS < min || s.WIS > max)
            {
                CharacterRejectedClientRpc("Atributo fora dos limites", BuildTargetParams(senderId));
                return;
            }

            _submittedChars[senderId] = preset;
            Debug.Log($"[MP] Personagem aceito (host): clientId={senderId} nome={preset.presetName} pts={total}/{budget}");

            // Marcar readyChar no slot
            int idx = FindSlot(senderId);
            if (idx >= 0)
            {
                var slot = _slots[idx];
                slot.ReadyChar = true;
                _slots[idx] = slot;
            }

            CheckAllReadyChar();
        }

        [ClientRpc]
        private void CharacterRejectedClientRpc(string reason, ClientRpcParams target = default)
        {
            Debug.LogWarning($"[CharCreation] Personagem rejeitado: {reason}");
            OnCharacterRejected?.Invoke(reason);
        }

        public event Action<string> OnCharacterRejected;

        private void CheckAllReadyChar()
        {
            if (_slots.Count == 0) return;
            for (int i = 0; i < _slots.Count; i++)
                if (!_slots[i].ReadyChar) return;

            // Personagens prontos → criar o mapa colaborativo
            _phase.Value = RoomPhase.MapEditing;
            ApplyPhaseDefaults(RoomPhase.MapEditing);
        }

        // -------------------------------------------------------------------------
        // Fase 5 — Placed + StartBattle
        // -------------------------------------------------------------------------
        [ServerRpc(RequireOwnership = false)]
        public void SetPlacedServerRpc(ServerRpcParams rpc = default) => MarkPlaced(rpc.Receive.SenderClientId);

        /// <summary>Marca um jogador como posicionado. Chamado DIRETO pelo servidor
        /// (PlacementSync já roda no host) — sem passar por RPC, pois o NGO sobrescreveria
        /// o SenderClientId pelo do host e marcaria sempre o slot errado.</summary>
        public void MarkPlaced(ulong clientId)
        {
            if (!IsServer) return;
            int idx = FindSlot(clientId);
            if (idx < 0) { Debug.LogWarning($"[RoomManager] MarkPlaced: slot nao encontrado p/ clientId={clientId}"); return; }
            var slot = _slots[idx];
            slot.Placed = true;
            _slots[idx] = slot;
            CheckAllPlaced();
        }

        private void CheckAllPlaced()
        {
            if (_slots.Count == 0) return;
            int placedCount = 0;
            for (int i = 0; i < _slots.Count; i++) if (_slots[i].Placed) placedCount++;
            Debug.Log($"[RoomManager] posicionados {placedCount}/{_slots.Count}");
            for (int i = 0; i < _slots.Count; i++)
                if (!_slots[i].Placed) return;
            Debug.Log("[RoomManager] todos posicionados — StartBattle");

            // Spawna o LockstepBattleSync (prefab registrado) ANTES do StartBattle — assim o
            // objeto replica nos clientes junto/antes do início. Criar em runtime sem prefab
            // NÃO replica no cliente (mesmo bug do RoomManager: NRE no Netcode + timeout).
            if (LockstepBattleSync.Instance == null)
            {
                var prefab = Resources.Load<GameObject>("Net/LockstepBattleSyncNet");
                if (prefab != null)
                {
                    var go = Instantiate(prefab);
                    DontDestroyOnLoad(go);
                    go.GetComponent<Unity.Netcode.NetworkObject>().Spawn();
                    Debug.Log("[MP] LockstepBattleSync spawnado (prefab) antes do StartBattle");
                }
                else Debug.LogError("[MP] prefab Net/LockstepBattleSyncNet nao encontrado!");
            }

            // Gerar seed determinístico e iniciar batalha
            int seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            RuntimeMultiplayerSession.BattleSeed = seed;
            StartBattleClientRpc(seed);
        }

        [ClientRpc]
        private void StartBattleClientRpc(int initiativeSeed)
        {
            RuntimeMultiplayerSession.BattleSeed = initiativeSeed;
            OnBattleStart?.Invoke(initiativeSeed);
        }

        public event Action<int> OnBattleStart;

        // -------------------------------------------------------------------------
        // Utilitário: ClientRpcParams para um único cliente
        // -------------------------------------------------------------------------
        private static ClientRpcParams BuildTargetParams(ulong clientId)
        {
            return new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { clientId }
                }
            };
        }
    }
}
