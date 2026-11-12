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
        // CharCreation e MapEditing saíram do fluxo (2026-11-12): o personagem vem do perfil do
        // jogador e o mapa é o padrão. Os valores ficam para não renumerar as fases na rede.
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
        /// <summary>Nome do personagem que o jogador escolheu do perfil (vazio = ainda não enviou).</summary>
        public FixedString64Bytes CharName;
        public bool ReadyChar;
        public bool Placed;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref PlayerName);
            serializer.SerializeValue(ref Team);
            serializer.SerializeValue(ref CharName);
            serializer.SerializeValue(ref ReadyChar);
            serializer.SerializeValue(ref Placed);
        }

        public bool Equals(PlayerSlot other) =>
            ClientId == other.ClientId &&
            PlayerName.Equals(other.PlayerName) &&
            Team == other.Team &&
            CharName.Equals(other.CharName) &&
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
                new RoomConfigNet { GameMode = 0, AttributeBudget = PlayerProfile.AttributeBudget, PlanningTime = 15f, MaxPlayers = 8 },
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
            if (_config.Value.GameMode == GameModes.PvpFreeForAll) return idx;
            if (_config.Value.GameMode == GameModes.Coop) return 0; // todos juntos
            return idx % 2; // PvP Times
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
            Debug.Log($"[MP] Config alterada: modo={GameModes.Name(cfg.GameMode)} budget={cfg.AttributeBudget} planejamento={cfg.PlanningTime}s");
            _config.Value = cfg;
        }

        // --- Iniciar a partida (host envia) ------------------------------------
        /// <summary>
        /// Lobby → posicionamento. Não há mais fases de criação de personagem nem de mapa: cada
        /// jogador já entrou com um personagem do seu perfil. Só inicia quando todos têm um.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void AdvancePhaseServerRpc(ServerRpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != NetworkManager.Singleton.LocalClientId) return;
            if (_phase.Value != RoomPhase.Lobby) { Debug.Log($"[MP] Iniciar ignorado: fase atual {_phase.Value}"); return; }

            string missing = PlayersWithoutCharacter();
            if (missing.Length > 0)
            {
                Debug.Log($"[MP] Iniciar recusado: sem personagem -> {missing}");
                ChatMessageClientRpc("Sistema", $"Não dá para iniciar: {missing} ainda sem personagem.");
                return;
            }
            StartMatch();
        }

        /// <summary>Nomes dos jogadores que ainda não enviaram personagem (vazio = todos prontos).</summary>
        public string PlayersWithoutCharacter()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _slots.Count; i++)
                if (!_slots[i].ReadyChar) sb.Append(sb.Length > 0 ? ", " : "").Append(_slots[i].PlayerName.ToString());
            return sb.ToString();
        }

        private void StartMatch()
        {
            RuntimeMultiplayerSession.CurrentConfig = _config.Value.ToData();
            AssignTeamsForMode();

            // O tuning que o anfitrião editou na aba da sala vai junto com o início.
            var active = RuntimeTuning.Active
                ?? Resources.Load<GameTuning>("GameTuning")
                ?? ScriptableObject.CreateInstance<GameTuning>();
            string json = JsonUtility.ToJson(active);
            RuntimeMultiplayerSession.CurrentConfig.tuningJson = json;
            SyncTuningClientRpc(json);

            Debug.Log($"[MP] Partida iniciada: modo={GameModes.Name(_config.Value.GameMode)}, {_slots.Count} jogador(es), tuning {json.Length} bytes");
            _phase.Value = RoomPhase.Placement;
            if (NetworkManager.Singleton.SceneManager != null)
                NetworkManager.Singleton.SceneManager.LoadScene("Battle", LoadSceneMode.Single);
        }

        /// <summary>
        /// Coop: todos no time 0. Todos contra todos: um time por jogador. PvP Times: respeita o
        /// que o anfitrião montou na sala; só redistribui se algum lado ficou vazio ou inválido.
        /// </summary>
        private void AssignTeamsForMode()
        {
            int mode = _config.Value.GameMode;
            bool keep = mode == GameModes.PvpTeams;
            if (keep)
            {
                int a = 0, b = 0;
                for (int i = 0; i < _slots.Count; i++)
                {
                    if (_slots[i].Team == 0) a++; else if (_slots[i].Team == 1) b++; else keep = false;
                }
                if (_slots.Count > 1 && (a == 0 || b == 0)) keep = false;
            }
            if (keep) return;
            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                slot.Team = mode == GameModes.PvpFreeForAll ? i : mode == GameModes.Coop ? 0 : (i % 2);
                _slots[i] = slot;
            }
        }

        /// <summary>
        /// Reinício rápido (só o anfitrião): mesma sala, mesmos personagens e mapa, de volta ao
        /// posicionamento. Refaz exatamente o caminho da fase Placement (recarrega a cena Battle
        /// pelo NGO, que leva todos os clientes juntos) depois de zerar o que a batalha deixou.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void RestartBattleServerRpc(ServerRpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != NetworkManager.Singleton.LocalClientId) return;
            Debug.Log($"[MP] RestartBattle pedido pelo anfitrião (fase atual {_phase.Value}, {_slots.Count} jogador(es))");

            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                slot.Placed = false;
                _slots[i] = slot;
            }

            // O lockstep da batalha anterior guarda rodada, planos e hashes: sai junto com ela.
            // CheckAllPlaced cria um novo quando todos posicionarem de novo.
            if (LockstepBattleSync.Instance != null)
            {
                var nob = LockstepBattleSync.Instance.GetComponent<NetworkObject>();
                if (nob != null && nob.IsSpawned) nob.Despawn(true);
                else Destroy(LockstepBattleSync.Instance.gameObject);
            }

            RuntimeMultiplayerSession.CurrentConfig = _config.Value.ToData();
            _phase.Value = RoomPhase.Placement;
            if (NetworkManager.Singleton.SceneManager != null)
                NetworkManager.Singleton.SceneManager.LoadScene("Battle", LoadSceneMode.Single);
        }

        // -------------------------------------------------------------------------
        // Personagem do jogador (vem do perfil, enviado ao entrar na sala ou ao trocar)
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

            // Só no lobby: depois que a partida começa, o personagem fica travado.
            if (_phase.Value != RoomPhase.Lobby)
            { CharacterRejectedClientRpc("A partida já começou", BuildTargetParams(senderId)); return; }

            // Mesma regra do perfil (orçamento fixo exato, atributos nos limites) — autoridade do servidor.
            string why = PlayerProfile.WhyInvalid(preset);
            if (why.Length > 0) { CharacterRejectedClientRpc(why, BuildTargetParams(senderId)); return; }
            int total = PlayerProfile.Points(preset), budget = PlayerProfile.AttributeBudget;

            _submittedChars[senderId] = preset;
            Debug.Log($"[MP] Personagem aceito (host): clientId={senderId} nome={preset.presetName} pts={total}/{budget}");

            // Marcar readyChar no slot
            int idx = FindSlot(senderId);
            if (idx >= 0)
            {
                var slot = _slots[idx];
                slot.ReadyChar = true;
                slot.CharName = preset.presetName ?? "";
                _slots[idx] = slot;
            }
        }

        [ClientRpc]
        private void CharacterRejectedClientRpc(string reason, ClientRpcParams target = default)
        {
            Debug.LogWarning($"[CharCreation] Personagem rejeitado: {reason}");
            OnCharacterRejected?.Invoke(reason);
        }

        public event Action<string> OnCharacterRejected;

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

            // Coop: as feras entram antes do início (mesmo RPC de spawn dos jogadores).
            if (_config.Value.GameMode == GameModes.Coop && PlacementSync.Instance != null)
                PlacementSync.Instance.SpawnBeastsForCoop(_slots.Count, 1);

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

        // --- Sync do GameTuning: enviado quando o anfitrião inicia a partida ----
        [ClientRpc]
        private void SyncTuningClientRpc(string tuningJson, ClientRpcParams rpc = default)
        {
            if (string.IsNullOrEmpty(tuningJson)) return;
            if (RuntimeTuning.Active == null)
                RuntimeTuning.Active = ScriptableObject.CreateInstance<GameTuning>();
            JsonUtility.FromJsonOverwrite(tuningJson, RuntimeTuning.Active);
            Debug.Log("[MP] Tuning recebido e aplicado (RuntimeTuning.Active atualizado).");
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
