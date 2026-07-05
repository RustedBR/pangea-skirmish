// Net/LobbyService.cs
// Wrapper do Unity Lobby UGS.
// Responsabilidade: criar/entrar em lobby como porta de entrada (troca de join code Relay).
// Após conexão NGO estabelecida, o Lobby é secundário — estado vivo é o NGO.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace PangeaSkirmish
{
    public static class LobbyService
    {
        private const string RelayCodeKey = "relayCode";
        private const float HeartbeatInterval = 15f;

        private static Lobby _currentLobby;
        private static string _currentLobbyId;
        private static bool _isHost;

        // Heartbeat cancelation
        private static bool _heartbeatActive;

        // -------------------------------------------------------------------------
        // Criar lobby (host)
        // -------------------------------------------------------------------------
        public static async Task<(string lobbyCode, string error)> CreateLobbyAsync(
            string roomName, int maxPlayers, string relayJoinCode)
        {
            try
            {
                var options = new CreateLobbyOptions
                {
                    IsPrivate = false,
                    Data = new Dictionary<string, DataObject>
                    {
                        [RelayCodeKey] = new DataObject(DataObject.VisibilityOptions.Public, relayJoinCode)
                    }
                };

                _currentLobby = await Unity.Services.Lobbies.LobbyService.Instance
                    .CreateLobbyAsync(roomName, maxPlayers, options);
                _currentLobbyId = _currentLobby.LobbyCode;
                _isHost = true;

                StartHeartbeat();
                return (_currentLobby.LobbyCode, null);
            }
            catch (LobbyServiceException ex)
            {
                Debug.LogWarning($"[LobbyService] Erro ao criar lobby: {ex.Message}");
                return (null, ex.Message);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LobbyService] Erro inesperado ao criar lobby: {ex.Message}");
                return (null, ex.Message);
            }
        }

        // -------------------------------------------------------------------------
        // Entrar em lobby por código (cliente)
        // -------------------------------------------------------------------------
        public static async Task<(string relayCode, string error)> JoinLobbyByCodeAsync(string lobbyCode)
        {
            try
            {
                _currentLobby = await Unity.Services.Lobbies.LobbyService.Instance
                    .JoinLobbyByCodeAsync(lobbyCode);
                _currentLobbyId = _currentLobby.Id;
                _isHost = false;

                string relayCode = null;
                if (_currentLobby.Data != null && _currentLobby.Data.TryGetValue(RelayCodeKey, out var data))
                    relayCode = data.Value;

                if (string.IsNullOrEmpty(relayCode))
                    return (null, "Lobby encontrado mas sem relay code.");

                return (relayCode, null);
            }
            catch (LobbyServiceException ex)
            {
                Debug.LogWarning($"[LobbyService] Erro ao entrar no lobby: {ex.Message}");
                return (null, ex.Message);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LobbyService] Erro inesperado ao entrar: {ex.Message}");
                return (null, ex.Message);
            }
        }

        // -------------------------------------------------------------------------
        // Sair do lobby
        // -------------------------------------------------------------------------
        public static async Task LeaveAsync()
        {
            StopHeartbeat();

            if (string.IsNullOrEmpty(_currentLobbyId)) return;

            try
            {
                if (_isHost)
                    await Unity.Services.Lobbies.LobbyService.Instance.DeleteLobbyAsync(_currentLobbyId);
                else
                    await Unity.Services.Lobbies.LobbyService.Instance.RemovePlayerAsync(
                        _currentLobbyId, Unity.Services.Authentication.AuthenticationService.Instance.PlayerId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LobbyService] Erro ao sair do lobby: {ex.Message}");
            }
            finally
            {
                _currentLobby = null;
                _currentLobbyId = null;
            }
        }

        // -------------------------------------------------------------------------
        // Heartbeat — mantém o lobby ativo (host envia a cada 15s)
        // -------------------------------------------------------------------------
        private static void StartHeartbeat()
        {
            _heartbeatActive = true;
            HeartbeatLoop();
        }

        private static void StopHeartbeat()
        {
            _heartbeatActive = false;
        }

        private static async void HeartbeatLoop()
        {
            while (_heartbeatActive && !string.IsNullOrEmpty(_currentLobbyId))
            {
                await Task.Delay(Mathf.RoundToInt(HeartbeatInterval * 1000));
                if (!_heartbeatActive || string.IsNullOrEmpty(_currentLobbyId)) break;

                try
                {
                    await Unity.Services.Lobbies.LobbyService.Instance
                        .SendHeartbeatPingAsync(_currentLobbyId);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[LobbyService] Heartbeat falhou: {ex.Message}");
                }
            }
        }
    }
}
