// Net/ReconnectController.cs
// Reconexão de CLIENTE (host-authoritative). Quando o cliente perde a conexão com o
// Relay (queda de rede / aba pausada no celular / recarga da página), a partida segue
// no host. Este controller tenta voltar à MESMA partida usando a sessão persistida
// (relayCode + lobby) e re-sincroniza o estado via snapshot do host.
//
// LIMITE (documentado): migração de host NÃO é suportada (NGO host-authoritative sem
// servidor dedicado). Se o HOST cair, a partida encerra para todos — comportamento
// atual preservado.
//
// Fluxo:
//   1. NetBootstrap dispara ReconnectController.NotifyConnectionLost() (cliente, host vivo).
//   2. Tentativas: rejoin lobby → rejoin Relay → aguarda NGO reconectar → re-enter no
//      snapshot (re-entrega de unidades + estado).
//   3. Se voltou: RaiseReconnected(); senão, após N tentativas, volta ao menu.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PangeaSkirmish
{
    public class ReconnectController : MonoBehaviour
    {
        public static ReconnectController Instance { get; private set; }

        public event Action OnReconnecting;      // começou tentativa
        public event Action OnReconnected;       // voltou à partida
        public event Action OnReconnectFailed;   // desistiu

        // ---- Configuração ---------------------------------------------------
        private const int MaxAttempts = 5;
        private const float DelayBetweenAttempts = 3f;
        private const float ElementRejoinTimeout = 12f;
        private static readonly string[] Scenes = { "MainMenu", "Lobby", "Room", "Battle" };

        private bool _active;
        private bool _rejoining;
        private int _attempts;

        // ---- Sessão que estamos tentando retomar --------------------------
        private string _relayCode;
        private string _lobbyId;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Cliente perdeu a conexão mas o host continua vivo. Grava o ponto de parada e
        /// começa as tentativas de voltar (com pequena espera inicial para a rede acalmar).
        /// </summary>
        public void NotifyConnectionLost()
        {
            if (_active) return;
            if (!RuntimeMultiplayerSession.HasPendingSession) { FailQuietly(); return; }
            _relayCode = RuntimeMultiplayerSession.PendingRelayCode;
            _lobbyId   = RuntimeMultiplayerSession.PendingLobbyId;
            _active    = true;
            _attempts  = 0;
            OnReconnecting?.Invoke();
            MpDiag.Log("Reconnect", $"Conexão perdida — iniciando reconexão (relay={_relayCode})");
            StartCoroutine(RetryLoop());
        }

        private void FailQuietly()
        {
            MpDiag.Log("Reconnect", "Sem sessão pendente para reconectar.");
            OnReconnectFailed?.Invoke();
        }

        private System.Collections.IEnumerator RetryLoop()
        {
            yield return new WaitForSecondsRealtime(1.5f);
            while (_active && _attempts < MaxAttempts)
            {
                _attempts++;
                MpDiag.Log("Reconnect", $"Tentativa {_attempts}/{MaxAttempts}...");
                _rejoining = true;
                yield return TryRejoin();
                _rejoining = false;

                if (!_active) yield break;          // conseguiu ou cancelado

                // Se NGO já relançou a cena mas ainda não achamos unidades, pode estar
                // aplicando o snapshot em paralelo — espera um pouco antes de desistir.
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient
                    && NetworkManager.Singleton.IsConnectedClient)
                {
                    MpDiag.Log("Reconnect", "NGO reconectado; aguardando elementos da cena...");
                    yield return new WaitForSecondsRealtime(ElementRejoinTimeout);
                    if (RuntimeMultiplayerSession.IsMultiplayer) { FinishReconnect(); yield break; }
                }

                yield return new WaitForSecondsRealtime(DelayBetweenAttempts);
            }

            MpDiag.Log("Reconnect", "Desistindo da reconexão (tentativas esgotadas).");
            OnReconnectFailed?.Invoke();
            ResetState();
        }

        private async Task TryRejoin()
        {
            var bootstrap = NetBootstrap.EnsureExists();
            try
            {
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                    NetworkManager.Singleton.Shutdown();

                string playerName = RuntimeMultiplayerSession.PlayerName;
                await bootstrap.InitUgsAsync(playerName);

                // Re-join no lobby (mantém o slot ativo no host) e pega relay code fresco.
                string code = _relayCode;
                if (!string.IsNullOrEmpty(_lobbyId))
                {
                    try
                    {
                        var (freshCode, err) = await LobbyService.JoinLobbyByIdAsync(_lobbyId);
                        if (err == null && !string.IsNullOrEmpty(freshCode)) code = freshCode;
                        else MpDiag.Log("Reconnect", $"Re-join lobby avisou: {err}");
                    }
                    catch (Exception ex) { MpDiag.Log("Reconnect", $"Re-join lobby falhou: {ex.Message}"); }
                }

                if (string.IsNullOrEmpty(code)) { MpDiag.Log("Reconnect", "Sem relay code — abortando tentativa."); return; }

                await bootstrap.JoinRelayAsync(code);
                MpDiag.Log("Reconnect", "JoinRelayAsync ok — aguardando NGO conexão.");
            }
            catch (Exception ex)
            {
                MpDiag.Log("Reconnect", $"Tentativa falhou: {ex.Message}");
            }
        }

        /// <summary>Volta a marcar a sessão como ativa e pede o snapshot de estado ao host.</summary>
        private void FinishReconnect()
        {
            MpDiag.Log("Reconnect", "Reconexão concluída.");
            _active = false;
            RuntimeMultiplayerSession.IsMultiplayer = true;
            RuntimeMultiplayerSession.IsHost = RuntimeMultiplayerSession.PendingWasHost;
            RuntimeMultiplayerSession.SavePendingSession(_relayCode, _lobbyId, RuntimeMultiplayerSession.PendingWasHost);

            // O host dispara a re-entrega de unidades + snapshot para este cliente recém-chegado.
            // Se a batalha ainda não começou, ele re-ocupa o slot e re-entra na fase atual.
            // Este código roda no CLIENTE — usa ServerRpc (OnClientRejoined é host-only).
            if (RoomManager.Instance != null)
                RoomManager.Instance.RequestRejoinServerRpc();

            RaiseReconnected();
        }

        public void RaiseReconnected() => OnReconnected?.Invoke();

        public void ResetState()
        {
            _active = false;
            _rejoining = false;
            _attempts = 0;
        }

        public static ReconnectController EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("ReconnectController");
            return go.AddComponent<ReconnectController>();
        }
    }
}
