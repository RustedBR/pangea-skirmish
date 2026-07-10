// Net/NetBootstrap.cs
// Singleton DontDestroyOnLoad que monta o NetworkManager + UnityTransport por código
// e gerencia o ciclo de vida NGO + UGS (Relay, Auth).
// Criado on-demand via NetBootstrap.EnsureExists().

using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace PangeaSkirmish
{
    // -------------------------------------------------------------------------
    // Contrato de estratégia de transporte (extensível: DirectUtp no futuro)
    // -------------------------------------------------------------------------
    public interface ITransportStrategy
    {
        Task ConfigureHostAsync(UnityTransport transport);
        Task ConfigureClientAsync(UnityTransport transport, string connectParam);
    }

    /// <summary>Log de diagnóstico multiplayer — evita abort silencioso no WebGL
    /// (exceptionSupport=None remove blocos catch, mas Debug.Log NÃO é removido).
    /// Classe estática acessível de RelayWssStrategy e NetBootstrap.</summary>
    internal static class MpDiag
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void MpDiagLogJS(string msg);
#endif
        public static void Log(string tag, string msg)
        {
            var line = $"[MP-DIAG][{tag}] {msg}";
            Debug.Log(line);
#if UNITY_WEBGL && !UNITY_EDITOR
            // Espelha no <pre id="mpdiag"> do index.html (legível via screenshot headless).
            try { MpDiagLogJS(line); } catch (System.Exception) {}
#endif
        }
    }

    /// <summary>Relay via WebSocket — padrão para WebGL.</summary>
    public class RelayWssStrategy : ITransportStrategy
    {
        // Antes fixo em 3 (host+3=4) independente da config da sala — se a sala fosse
        // configurada pra 2 jogadores, o Relay ainda alocava capacidade pra 4. Agora deriva
        // do maxPlayers atual da sala (default 4 se ainda não configurado).
        private static int MaxConnections =>
            Mathf.Max(1, (RuntimeMultiplayerSession.CurrentConfig?.maxPlayers ?? 8) - 1);

        public async Task ConfigureHostAsync(UnityTransport transport)
        {
            MpDiag.Log("Host", $"CreateAllocationAsync(Max={MaxConnections})...");
            Allocation allocation = null;
            try
            {
                allocation = await RelayService.Instance.CreateAllocationAsync(MaxConnections);
            }
            catch (Exception ex)
            {
                MpDiag.Log("Host", $"CreateAllocationAsync FAILED: {ex.GetType().FullName}: {ex.Message}");
                throw;
            }

            MpDiag.Log("Host", $"Allocation OK id={allocation.AllocationId} #endpoints={allocation.ServerEndpoints.Count}");
            foreach (var e in allocation.ServerEndpoints)
                MpDiag.Log("Host", $"  endpoint: type='{e.ConnectionType}' host={e.Host} port={e.Port} secure={e.Secure}");

            string joinCode = null;
            try
            {
                joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            }
            catch (Exception ex)
            {
                MpDiag.Log("Host", $"GetJoinCodeAsync FAILED: {ex.GetType().FullName}: {ex.Message}");
                throw;
            }

            RuntimeMultiplayerSession.JoinCode = joinCode;
            MpDiag.Log("Host", $"JoinCode={joinCode}");

            var relayData = BuildRelayData(allocation.ServerEndpoints,
                allocation.AllocationIdBytes, allocation.ConnectionData,
                allocation.ConnectionData, allocation.Key);
            transport.UseWebSockets = true;
            MpDiag.Log("Host", $"UseWebSockets={transport.UseWebSockets}");
            transport.SetRelayServerData(relayData);
            MpDiag.Log("Host", "SetRelayServerData OK");
        }

        public async Task ConfigureClientAsync(UnityTransport transport, string joinCode)
        {
            MpDiag.Log("Client", $"JoinAllocationAsync(code={joinCode})...");

            JoinAllocation allocation = null;
            try
            {
                allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            }
            catch (Exception ex)
            {
                MpDiag.Log("Client", $"JoinAllocationAsync FAILED: {ex.GetType().FullName}: {ex.Message}");
                throw;
            }

            MpDiag.Log("Client", $"Join OK #endpoints={allocation.ServerEndpoints.Count}");
            foreach (var e in allocation.ServerEndpoints)
                MpDiag.Log("Client", $"  endpoint: type='{e.ConnectionType}' host={e.Host} port={e.Port} secure={e.Secure}");

            var relayData = BuildRelayData(allocation.ServerEndpoints,
                allocation.AllocationIdBytes, allocation.ConnectionData,
                allocation.HostConnectionData, allocation.Key);
            transport.UseWebSockets = true;
            MpDiag.Log("Client", $"UseWebSockets={transport.UseWebSockets}");
            transport.SetRelayServerData(relayData);
            MpDiag.Log("Client", "SetRelayServerData OK");
        }

        /// <summary>Monta RelayServerData escolhendo o endpoint wss (fallback ws → primeiro).</summary>
        private static RelayServerData BuildRelayData(
            System.Collections.Generic.List<RelayServerEndpoint> endpoints,
            byte[] allocationIdBytes, byte[] connectionData, byte[] hostConnectionData, byte[] key)
        {
            RelayServerEndpoint chosen = null;
            foreach (var e in endpoints) if (e.ConnectionType == "wss") { chosen = e; break; }
            if (chosen == null)
                foreach (var e in endpoints) if (e.ConnectionType == "ws") { chosen = e; break; }
            if (chosen == null) chosen = endpoints[0];
            MpDiag.Log("BuildRelayData", $"chosen: type='{chosen.ConnectionType}' host={chosen.Host} port={chosen.Port} secure={chosen.Secure}");

            // WebGL/Relay SEMPRE usa WebSocket (wss). O navegador nao tem UDP, entao
            // IsWebSocket PRECISA ser 1 no RelayServerData — caso contrario o UnityTransport
            // dispara "Relay server data isn't WebSocket" e o wasm da abort() silencioso no
            // browser. Forcamos true independente do ConnectionType retornado pelo Relay
            // (que as vezes vem "dtls"/"udp" mas o Relay aceita wss sob demanda).
            bool isWebSocket = true;
            var data = new Unity.Networking.Transport.Relay.RelayServerData(chosen.Host, (ushort)chosen.Port, allocationIdBytes,
                connectionData, hostConnectionData, key, chosen.Secure, isWebSocket);
            MpDiag.Log("BuildRelayData", $"RelayServerData criado: isWebSocket={isWebSocket} secure={chosen.Secure}");
            return data;
        }
    }

    /// <summary>Stub de conexão direta IPv6 — futuro desktop. Não implementado.</summary>
    // public class DirectStrategy : ITransportStrategy { ... }

    // -------------------------------------------------------------------------
    // Bootstrap principal
    // -------------------------------------------------------------------------
    public class NetBootstrap : MonoBehaviour
    {
        // Singleton
        private static NetBootstrap _instance;
        public static NetBootstrap Instance => _instance;

        // ---- Componentes NGO ------------------------------------------------
        private NetworkManager _networkManager;
        private UnityTransport _transport;

        // ---- Modo loopback local (sem UGS, para testes rápidos) -------------
        public bool useLoopback = false;
        private const string LoopbackAddress = "127.0.0.1";
        private const ushort LoopbackPort = 7777;

        // ---- Estratégia de transporte ----------------------------------------
        private ITransportStrategy _strategy = new RelayWssStrategy();

        // ---- Eventos de conexão/desconexão -----------------------------------
        public Action<ulong> OnClientConnected;
        public Action<ulong> OnClientDisconnected;

        // ---- Criação on-demand -----------------------------------------------
        public static NetBootstrap EnsureExists()
        {
            if (_instance != null) return _instance;

            var go = new GameObject("NetBootstrap");
            DontDestroyOnLoad(go);

            var nm = go.AddComponent<NetworkManager>();

            var transport = go.AddComponent<UnityTransport>();
            transport.UseWebSockets = true;

            // NGO 2.x: um NetworkManager criado por AddComponent em runtime NÃO instancia o
            // NetworkConfig (é campo serializado, só preenchido pelo Inspector) — criar na mão.
            // A inicialização de UGS/Relay fica em InitUgsAsync (só o caminho relay a usa;
            // loopback dispensa UGS), mantendo este método síncrono e sem risco de deadlock.
            nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport      = transport,
                EnableSceneManagement = true,
                ConnectionApproval    = false,
                TickRate              = 60,
                ForceSamePrefabs      = false,
            };

            // Registra os prefabs de rede dos managers (spawnados em runtime pelo host).
            // SEM isto, o host spawna mas os CLIENTES não conseguem instanciar o objeto
            // replicado (hash não registrado) → exceção em SynchronizeSceneNetworkObjects e
            // "[Deferred OnSpawn] NetworkObject not received" no cliente. Registrar em ambos
            // (host e cliente) ANTES de StartHost/StartClient.
            RegisterNetPrefab(nm, "Net/RoomManagerNet");
            RegisterNetPrefab(nm, "Net/CollabMapSyncNet");
            RegisterNetPrefab(nm, "Net/PlacementSyncNet");
            RegisterNetPrefab(nm, "Net/LockstepBattleSyncNet");

            var bootstrap = go.AddComponent<NetBootstrap>();
            bootstrap._networkManager = nm;
            bootstrap._transport = transport;

            _instance = bootstrap;
            // IMPORTANTE: Awake/OnEnable rodam DURANTE o AddComponent acima — antes de
            // _networkManager ser atribuído. Assinar os callbacks explicitamente agora,
            // senão HandleClientConnected/Disconnected nunca disparam (LocalClientId do
            // cliente ficava 0 e a detecção de queda do host não funcionava).
            bootstrap.SubscribeCallbacks();
            return bootstrap;
        }

        // ---- Log de diagnostico multiplayer (abort silencioso no WebGL) ----------
        // (agora em MpDiag.Log, classe estatica — veja acima)

        private bool _callbacksSubscribed;
        private void SubscribeCallbacks()
        {
            if (_callbacksSubscribed || _networkManager == null) return;
            _networkManager.OnClientConnectedCallback += HandleClientConnected;
            _networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            _callbacksSubscribed = true;
        }

        private static void RegisterNetPrefab(NetworkManager nm, string resPath)
        {
            var prefab = Resources.Load<GameObject>(resPath);
            if (prefab != null) nm.AddNetworkPrefab(prefab);
            else Debug.LogError($"[NetBootstrap] Prefab de rede nao encontrado em Resources: {resPath}");
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }

        private void OnEnable() => SubscribeCallbacks();

        private void OnDisable()
        {
            if (!_callbacksSubscribed || _networkManager == null) return;
            _networkManager.OnClientConnectedCallback -= HandleClientConnected;
            _networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            _callbacksSubscribed = false;
        }

        private void HandleClientConnected(ulong clientId)
        {
            // Atualiza o id local com o valor REAL do NGO (no cliente, StartClient captura 0
            // cedo demais; o id verdadeiro só existe após conectar).
            RuntimeMultiplayerSession.LocalClientId = _networkManager.LocalClientId;
            OnClientConnected?.Invoke(clientId);
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            OnClientDisconnected?.Invoke(clientId);

            // Cliente detecta que o servidor (host) caiu: clientId == ServerClientId
            if (!_networkManager.IsServer && clientId == Unity.Netcode.NetworkManager.ServerClientId)
            {
                Debug.LogWarning("[NetBootstrap] Host desconectou — voltando ao menu principal.");
                RuntimeMultiplayerSession.Reset();
                _networkManager.Shutdown();
                UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
            }
        }

        // -------------------------------------------------------------------------
        // UGS: inicializa serviços + autenticação anônima
        // -------------------------------------------------------------------------
        public async Task InitUgsAsync(string playerName)
        {
            RuntimeMultiplayerSession.PlayerName = playerName;
            MpDiag.Log("InitUgs", $"UnityServices.State={UnityServices.State}");

            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                MpDiag.Log("InitUgs", "UnityServices.InitializeAsync()...");
                await UnityServices.InitializeAsync();
                MpDiag.Log("InitUgs", "UnityServices.InitializeAsync() OK");
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                MpDiag.Log("InitUgs", "AuthenticationService.SignInAnonymouslyAsync()...");
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                MpDiag.Log("InitUgs", $"SignIn OK playerId={AuthenticationService.Instance.PlayerId}");
            }
        }

        // -------------------------------------------------------------------------
        // Host: configura Relay + StartHost
        // -------------------------------------------------------------------------
        public async Task<string> HostRelayAsync()
        {
            MpDiag.Log("HostRelay", "ConfigureHostAsync...");
            await _strategy.ConfigureHostAsync(_transport);
            MpDiag.Log("HostRelay", "ConfigureHostAsync OK. StartHost()...");
            _networkManager.StartHost();
            MpDiag.Log("HostRelay", "StartHost() retornou (host iniciado, conexao Relay assincrona em andamento).");
            RuntimeMultiplayerSession.IsMultiplayer = true;
            RuntimeMultiplayerSession.IsHost = true;
            RuntimeMultiplayerSession.LocalClientId = _networkManager.LocalClientId;
            MpPhaseDirector.EnsureExists();
            SpawnRoomManager();
            return RuntimeMultiplayerSession.JoinCode;
        }

        // -------------------------------------------------------------------------
        // Client: configura Relay + StartClient
        // -------------------------------------------------------------------------
        public async Task JoinRelayAsync(string joinCode)
        {
            MpDiag.Log("JoinRelay", "ConfigureClientAsync...");
            await _strategy.ConfigureClientAsync(_transport, joinCode);
            MpDiag.Log("JoinRelay", "ConfigureClientAsync OK. StartClient()...");
            _networkManager.StartClient();
            MpDiag.Log("JoinRelay", "StartClient() retornou (cliente iniciado, conexao Relay assincrona em andamento).");
            RuntimeMultiplayerSession.IsMultiplayer = true;
            RuntimeMultiplayerSession.IsHost = false;
            RuntimeMultiplayerSession.JoinCode = joinCode;
            MpPhaseDirector.EnsureExists();
            // LocalClientId será atualizado após OnClientConnected
        }

        // -------------------------------------------------------------------------
        // Loopback local (sem UGS) — para testes no Editor
        // -------------------------------------------------------------------------
        public void HostLoopback()
        {
            _transport.UseWebSockets = false;
            _transport.SetConnectionData(LoopbackAddress, LoopbackPort);
            _networkManager.StartHost();
            RuntimeMultiplayerSession.IsMultiplayer = true;
            RuntimeMultiplayerSession.IsHost = true;
            RuntimeMultiplayerSession.JoinCode = "LOOPBACK";
            RuntimeMultiplayerSession.LocalClientId = _networkManager.LocalClientId;
            MpPhaseDirector.EnsureExists();
            SpawnRoomManager();
        }

        public void JoinLoopback()
        {
            _transport.UseWebSockets = false;
            _transport.SetConnectionData(LoopbackAddress, LoopbackPort);
            _networkManager.StartClient();
            RuntimeMultiplayerSession.IsMultiplayer = true;
            RuntimeMultiplayerSession.IsHost = false;
            RuntimeMultiplayerSession.JoinCode = "LOOPBACK";
            MpPhaseDirector.EnsureExists();
        }

        // -------------------------------------------------------------------------
        // Spawn do RoomManager (chamado pelo host após StartHost)
        // -------------------------------------------------------------------------
        public void SpawnRoomManager()
        {
            if (!_networkManager.IsHost) return;

            // Instancia os PREFABS registrados (ver RegisterNetPrefab). Assim o cliente
            // consegue instanciar o objeto replicado a partir do hash do prefab.
            SpawnNetManager("Net/RoomManagerNet");
            SpawnNetManager("Net/CollabMapSyncNet");
            SpawnNetManager("Net/PlacementSyncNet");
        }

        private static void SpawnNetManager(string resPath)
        {
            var prefab = Resources.Load<GameObject>(resPath);
            if (prefab == null) { Debug.LogError($"[NetBootstrap] prefab nulo no spawn: {resPath}"); return; }
            var go = UnityEngine.Object.Instantiate(prefab);
            DontDestroyOnLoad(go);
            go.GetComponent<NetworkObject>().Spawn();
        }

        // -------------------------------------------------------------------------
        // Shutdown limpo
        // -------------------------------------------------------------------------
        public void Shutdown()
        {
            if (_networkManager != null && _networkManager.IsListening)
                _networkManager.Shutdown();
            RuntimeMultiplayerSession.Reset();
        }
    }
}
