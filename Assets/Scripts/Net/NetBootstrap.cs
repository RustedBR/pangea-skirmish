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

    /// <summary>Relay via WebSocket — padrão para WebGL.</summary>
    public class RelayWssStrategy : ITransportStrategy
    {
        private const int MaxConnections = 3; // host + 3 clientes = 4 jogadores

        public async Task ConfigureHostAsync(UnityTransport transport)
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MaxConnections);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            RuntimeMultiplayerSession.JoinCode = joinCode;

            // Host: hostConnectionData == connectionData (docs do RelayServerData).
            RelayServerData relayData = BuildRelayData(allocation.ServerEndpoints,
                allocation.AllocationIdBytes, allocation.ConnectionData,
                allocation.ConnectionData, allocation.Key);
            transport.UseWebSockets = true;
            transport.SetRelayServerData(relayData);
        }

        public async Task ConfigureClientAsync(UnityTransport transport, string joinCode)
        {
            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            RelayServerData relayData = BuildRelayData(allocation.ServerEndpoints,
                allocation.AllocationIdBytes, allocation.ConnectionData,
                allocation.HostConnectionData, allocation.Key);
            transport.UseWebSockets = true;
            transport.SetRelayServerData(relayData);
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

            bool isWebSocket = chosen.ConnectionType == "ws" || chosen.ConnectionType == "wss";
            return new RelayServerData(chosen.Host, (ushort)chosen.Port, allocationIdBytes,
                connectionData, hostConnectionData, key, chosen.Secure, isWebSocket);
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

            // NetworkManager
            var nm = go.AddComponent<NetworkManager>();

            // UnityTransport
            var transport = go.AddComponent<UnityTransport>();
            transport.UseWebSockets = true;

            // Configurar NetworkConfig (NetworkManager já possui uma instância interna)
            nm.NetworkConfig.EnableSceneManagement = true;
            nm.NetworkConfig.ConnectionApproval = false;
            nm.NetworkConfig.TickRate = 60;
            nm.NetworkConfig.NetworkTransport = transport;
            // Permite spawn de objetos criados em runtime sem exigir prefab idêntico nos clientes
            nm.NetworkConfig.ForceSamePrefabs = false;

            var bootstrap = go.AddComponent<NetBootstrap>();
            bootstrap._networkManager = nm;
            bootstrap._transport = transport;

            _instance = bootstrap;
            return bootstrap;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }

        private void OnEnable()
        {
            if (_networkManager == null) return;
            _networkManager.OnClientConnectedCallback += HandleClientConnected;
            _networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        }

        private void OnDisable()
        {
            if (_networkManager == null) return;
            _networkManager.OnClientConnectedCallback -= HandleClientConnected;
            _networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        private void HandleClientConnected(ulong clientId) => OnClientConnected?.Invoke(clientId);

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

            if (UnityServices.State == ServicesInitializationState.Uninitialized)
                await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        // -------------------------------------------------------------------------
        // Host: configura Relay + StartHost
        // -------------------------------------------------------------------------
        public async Task<string> HostRelayAsync()
        {
            await _strategy.ConfigureHostAsync(_transport);
            _networkManager.StartHost();
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
            await _strategy.ConfigureClientAsync(_transport, joinCode);
            _networkManager.StartClient();
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

            // ---- RoomManager ----
            var go = new GameObject("RoomManager");
            DontDestroyOnLoad(go);
            var netObj = go.AddComponent<NetworkObject>();
            go.AddComponent<RoomManager>();

            // ForceSamePrefabs=false (configurado em EnsureExists) permite spawn de objetos
            // criados em runtime sem exigir prefab registrado nos clientes.
            // O cliente receberá o spawn message e instanciará localmente via NGO.
            netObj.Spawn();

            // ---- CollabMapSync ----
            var syncGo = new GameObject("CollabMapSync");
            DontDestroyOnLoad(syncGo);
            var syncNet = syncGo.AddComponent<NetworkObject>();
            syncGo.AddComponent<CollabMapSync>();
            syncNet.Spawn();

            // ---- PlacementSync ----
            var placementGo = new GameObject("PlacementSync");
            DontDestroyOnLoad(placementGo);
            var placementNet = placementGo.AddComponent<NetworkObject>();
            placementGo.AddComponent<PlacementSync>();
            placementNet.Spawn();
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
