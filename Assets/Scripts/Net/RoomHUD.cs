// Net/RoomHUD.cs
// HUD de multiplayer (lobby + sala) em UI Toolkit.
// Layout: Resources/UI/Screens/Room.uxml (edite no UI Builder) + Room.uss + PangeaTheme.uss.
// Vive como UIDocument (PanelSettings compartilhado) criado pelo MainMenuManager — sem cena nova.
//
// Migrado de UGUI → UI Toolkit MANTENDO toda a lógica de rede (RoomManager/NetBootstrap/LobbyService).
// Só a camada de apresentação mudou; a API pública (Init/ShowLobbyPanel/HideAll/EnterRoom/OnBackToMenu)
// permanece igual para o MainMenuManager.

using System;
using System.Collections;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;
using PangeaSkirmish.UI;

namespace PangeaSkirmish
{
    public class RoomHUD : PangeaScreen
    {
        protected override string UxmlResource => "UI/Screens/Room";

        public event Action OnBackToMenu;

        // ---- Views -----------------------------------------------------------
        private VisualElement _lobbyView, _roomView;

        // ---- Widgets do lobby ------------------------------------------------
        private TextField _nameInput, _roomNameInput, _joinCodeInput;
        private Toggle _loopbackToggle;
        private Label _lobbyStatus;

        // ---- Widgets da sala -------------------------------------------------
        private Label _joinCodeDisplay, _phaseLabel;
        private ScrollView _playerList, _chatLog;
        private TextField _chatInput;
        private Button _tdmBtn, _ffaBtn, _budgetMinus, _budgetPlus, _planMinus, _planPlus, _advanceBtn;
        private Label _budgetLabel, _planLabel;
        private VisualElement _hostControls, _waitingOverlay;
        private Label _waitingLabel;

        // ---- Estado ----------------------------------------------------------
        private int _gameMode = 0;       // 0=TDM, 1=FFA
        private int _budgetValue = 30;
        private float _planningValue = 15f;
        private bool _inRoom = false;
        private bool _boundRoom = false;
        private string _joinCode = "---";

        // =====================================================================
        // Inicialização / visibilidade (API pública mantida)
        // =====================================================================

        /// <summary>Mantido por compatibilidade com o MainMenuManager. UI Toolkit usa
        /// PanelSettings + tema, então canvas/font não são mais necessários.</summary>
        public void Init(Transform canvasTransform, Font font) { }

        protected override void Bind()
        {
            var r = Root;

            _lobbyView = r.Q<VisualElement>("lobby-view");
            _roomView  = r.Q<VisualElement>("room-view");

            // --- lobby ---
            _nameInput      = r.Q<TextField>("name-input");
            _roomNameInput  = r.Q<TextField>("roomname-input");
            _joinCodeInput  = r.Q<TextField>("joincode-input");
            _loopbackToggle = r.Q<Toggle>("loopback-toggle");
            _lobbyStatus    = r.Q<Label>("lobby-status");
            r.Q<Button>("btn-create").clicked += OnClickCreateRoom;
            r.Q<Button>("btn-join").clicked   += OnClickJoinRoom;
            r.Q<Button>("btn-back").clicked   += () => { HideAll(); OnBackToMenu?.Invoke(); };

            // --- sala ---
            _joinCodeDisplay = r.Q<Label>("joincode-display");
            _phaseLabel      = r.Q<Label>("phase-label");
            _playerList      = r.Q<ScrollView>("player-list");
            _chatLog         = r.Q<ScrollView>("chat-log");
            _chatInput       = r.Q<TextField>("chat-input");
            _hostControls    = r.Q<VisualElement>("host-controls");
            _waitingOverlay  = r.Q<VisualElement>("waiting-overlay");
            _waitingLabel    = r.Q<Label>("waiting-label");
            _budgetLabel     = r.Q<Label>("budget-label");
            _planLabel       = r.Q<Label>("plan-label");
            _tdmBtn          = r.Q<Button>("btn-tdm");
            _ffaBtn          = r.Q<Button>("btn-ffa");
            _budgetMinus     = r.Q<Button>("btn-budget-minus");
            _budgetPlus      = r.Q<Button>("btn-budget-plus");
            _planMinus       = r.Q<Button>("btn-plan-minus");
            _planPlus        = r.Q<Button>("btn-plan-plus");
            _advanceBtn      = r.Q<Button>("btn-advance");

            r.Q<Button>("btn-copy-code").clicked += OnClickCopyCode;
            r.Q<Button>("btn-send").clicked      += () => OnChatSubmit(_chatInput.value);
            r.Q<Button>("btn-leave").clicked     += OnClickLeaveRoom;
            _advanceBtn.clicked += OnClickAdvancePhase;

            _tdmBtn.clicked += () => { _gameMode = 0; UpdateModeHighlight(); SyncConfig(); };
            _ffaBtn.clicked += () => { _gameMode = 1; UpdateModeHighlight(); SyncConfig(); };
            _budgetMinus.clicked += () => { _budgetValue = Mathf.Max(10, _budgetValue - 5); _budgetLabel.text = _budgetValue.ToString(); SyncConfig(); };
            _budgetPlus.clicked  += () => { _budgetValue = Mathf.Min(100, _budgetValue + 5); _budgetLabel.text = _budgetValue.ToString(); SyncConfig(); };
            _planMinus.clicked   += () => { _planningValue = Mathf.Max(5f, _planningValue - 5f); _planLabel.text = _planningValue + "s"; SyncConfig(); };
            _planPlus.clicked    += () => { _planningValue = Mathf.Min(120f, _planningValue + 5f); _planLabel.text = _planningValue + "s"; SyncConfig(); };

            // Enviar chat com Enter
            _chatInput.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    OnChatSubmit(_chatInput.value);
                    evt.StopPropagation();
                }
            });

            UpdateModeHighlight();
            HideAll(); // começa oculto (como o Init antigo, que deixava os dois painéis inativos)
        }

        public void ShowLobbyPanel()
        {
            Root.style.display = DisplayStyle.Flex;
            _lobbyView.style.display = DisplayStyle.Flex;
            _roomView.style.display = DisplayStyle.None;
        }

        public void HideAll()
        {
            if (Root != null) Root.style.display = DisplayStyle.None;
        }

        private void ShowRoomView()
        {
            Root.style.display = DisplayStyle.Flex;
            _lobbyView.style.display = DisplayStyle.None;
            _roomView.style.display = DisplayStyle.Flex;
            _waitingOverlay.style.display = DisplayStyle.None;
        }

        // =====================================================================
        // Entrada na sala: vincular ao RoomManager
        // =====================================================================

        public void EnterRoom(string joinCode)
        {
            _inRoom = true;
            _joinCode = joinCode;
            _joinCodeDisplay.text = joinCode;
            ShowRoomView();

            // Controles do host visíveis para todos; só o host interage (demais em read-only).
            SetHostControlsInteractable(RuntimeMultiplayerSession.IsHost);

            // No CLIENTE o RoomManager é replicado alguns frames após conectar — tentamos já e,
            // se ainda não existir, uma corrotina fica tentando até ele aparecer.
            _boundRoom = false;
            BindRoomManager();
            if (!_boundRoom) StartCoroutine(BindWhenReady());
        }

        private void BindRoomManager()
        {
            if (_boundRoom || RoomManager.Instance == null) return;
            RoomManager.Instance.Slots.OnListChanged += OnSlotsChanged;
            RoomManager.Instance.OnPhaseChanged += OnPhaseChanged;
            RoomManager.Instance.OnConfigChanged += UpdateConfigDisplay;
            RoomManager.Instance.OnChatMessage += AppendChatMessage;
            _boundRoom = true;
            RebuildPlayerList();
            UpdateConfigDisplay();
            OnPhaseChanged(RoomManager.Instance.CurrentPhase);
        }

        private IEnumerator BindWhenReady()
        {
            float t = 8f;
            while (!_boundRoom && t > 0f)
            {
                BindRoomManager();
                t -= Time.deltaTime;
                yield return null;
            }
            if (!_boundRoom)
                Debug.LogWarning("[RoomHUD] RoomManager nao apareceu no cliente (timeout) — sala nao vinculou.");
        }

        private void OnDestroy()
        {
            if (_boundRoom && RoomManager.Instance != null)
            {
                RoomManager.Instance.Slots.OnListChanged -= OnSlotsChanged;
                RoomManager.Instance.OnPhaseChanged -= OnPhaseChanged;
                RoomManager.Instance.OnConfigChanged -= UpdateConfigDisplay;
                RoomManager.Instance.OnChatMessage -= AppendChatMessage;
            }
            _boundRoom = false;
        }

        // =====================================================================
        // Callbacks de rede
        // =====================================================================

        private void OnSlotsChanged(NetworkListEvent<PlayerSlot> evt) => RebuildPlayerList();

        private void OnPhaseChanged(RoomPhase phase)
        {
            _phaseLabel.text = "Fase: " + phase.ToString();

            bool isHost = RuntimeMultiplayerSession.IsHost;
            bool isLobby = phase == RoomPhase.Lobby;
            if (_advanceBtn != null) _advanceBtn.SetEnabled(isHost && isLobby);

            if (!isHost && phase != RoomPhase.Lobby)
            {
                _waitingOverlay.style.display = DisplayStyle.Flex;
                _waitingLabel.text = $"Aguardando o host...\nFase: {phase}";
            }
            else
            {
                _waitingOverlay.style.display = DisplayStyle.None;
            }
        }

        private void AppendChatMessage(string senderName, string msg)
        {
            if (_chatLog == null) return;
            var line = new Label($"[{senderName}] {msg}");
            line.AddToClassList("room__chat-line");
            if (senderName == "Sistema") line.AddToClassList("room__chat-line--system");
            _chatLog.Add(line);
            // Auto-scroll para o fim após o layout.
            _chatLog.schedule.Execute(() => _chatLog.ScrollTo(line));
        }

        private void RebuildPlayerList()
        {
            if (_playerList == null) return;
            _playerList.Clear();
            if (RoomManager.Instance == null) return;

            bool isHost = RuntimeMultiplayerSession.IsHost;
            bool isFfa = RoomManager.Instance.CurrentConfig.GameMode == 1;
            ulong hostId = NetworkManager.ServerClientId;
            var slots = RoomManager.Instance.Slots;

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                var row = new VisualElement();
                row.AddToClassList("room__player-row");

                var nameLbl = new Label(slot.PlayerName.ToString() + (slot.ClientId == hostId ? "  (host)" : ""));
                nameLbl.AddToClassList("room__player-name");
                row.Add(nameLbl);

                var teamLbl = new Label(isFfa ? "FFA" : (slot.Team == 0 ? "Time A" : "Time B"));
                teamLbl.AddToClassList("pg-badge");
                if (!isFfa) teamLbl.AddToClassList(slot.Team == 0 ? "pg-badge--team-a" : "pg-badge--team-b");
                row.Add(teamLbl);

                // Botão trocar time — só host, só TDM
                if (isHost && !isFfa)
                {
                    var slotCapture = slot;
                    var swap = new Button(() =>
                    {
                        int newTeam = (slotCapture.Team == 0) ? 1 : 0;
                        RoomManager.Instance?.SetTeamServerRpc(slotCapture.ClientId, newTeam);
                    }) { text = "Troca" };
                    swap.AddToClassList("pg-button");
                    swap.AddToClassList("room__swap-btn");
                    row.Add(swap);
                }

                _playerList.Add(row);
            }
        }

        /// <summary>Reflete o config replicado da sala no display (todos veem; só host edita).</summary>
        private void UpdateConfigDisplay()
        {
            if (RoomManager.Instance == null) return;
            var cfg = RoomManager.Instance.CurrentConfig;
            _gameMode = cfg.GameMode;
            _budgetValue = cfg.AttributeBudget;
            _planningValue = cfg.PlanningTime;
            UpdateModeHighlight();
            if (_budgetLabel != null) _budgetLabel.text = _budgetValue.ToString();
            if (_planLabel != null) _planLabel.text = _planningValue + "s";
            RebuildPlayerList(); // rótulos de time dependem do modo
        }

        private void SyncConfig()
        {
            if (!RuntimeMultiplayerSession.IsHost || RoomManager.Instance == null) return;
            var cfg = new RoomConfigNet
            {
                GameMode = _gameMode,
                AttributeBudget = _budgetValue,
                PlanningTime = _planningValue,
                MaxPlayers = 4
            };
            RoomManager.Instance.SetConfigServerRpc(cfg);
        }

        private void UpdateModeHighlight()
        {
            if (_tdmBtn != null) _tdmBtn.EnableInClassList("room__mode-btn--active", _gameMode == 0);
            if (_ffaBtn != null) _ffaBtn.EnableInClassList("room__mode-btn--active", _gameMode == 1);
        }

        private void SetHostControlsInteractable(bool on)
        {
            _tdmBtn?.SetEnabled(on);
            _ffaBtn?.SetEnabled(on);
            _budgetMinus?.SetEnabled(on);
            _budgetPlus?.SetEnabled(on);
            _planMinus?.SetEnabled(on);
            _planPlus?.SetEnabled(on);
            _advanceBtn?.SetEnabled(on);
        }

        // =====================================================================
        // Handlers de botão
        // =====================================================================

        private async void OnClickCreateRoom()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            { SetLobbyStatus("Já conectado — saia da sala antes."); return; }

            SetLobbyStatus("Criando sala...");
            try
            {
                string playerName = _nameInput.value.Trim();
                if (string.IsNullOrEmpty(playerName)) playerName = "Jogador";
                string roomName = _roomNameInput.value.Trim();
                if (string.IsNullOrEmpty(roomName)) roomName = "Sala";

                // ANTES de StartHost: o slot do host lê RuntimeMultiplayerSession.PlayerName no spawn.
                RuntimeMultiplayerSession.PlayerName = playerName;

                var bootstrap = NetBootstrap.EnsureExists();
                bootstrap.useLoopback = _loopbackToggle != null && _loopbackToggle.value;

                string joinCode;
                if (bootstrap.useLoopback)
                {
                    bootstrap.HostLoopback();
                    joinCode = "LOOPBACK";
                }
                else
                {
                    await bootstrap.InitUgsAsync(playerName);
                    joinCode = await bootstrap.HostRelayAsync();

                    var (lobbyCode, err) = await LobbyService.CreateLobbyAsync(roomName, 4, joinCode);
                    if (err != null) Debug.LogWarning($"[RoomHUD] Lobby criado com aviso: {err}");
                }

                RuntimeMultiplayerSession.PlayerName = playerName;
                Debug.Log($"[MP] Sala criada (host): jogador={playerName} loopback={bootstrap.useLoopback} codigo={joinCode}");
                SetLobbyStatus("Sala criada!");
                EnterRoom(joinCode);
            }
            catch (Exception ex)
            {
                SetLobbyStatus($"Erro: {ex.Message}");
                Debug.LogError($"[RoomHUD] OnClickCreateRoom: {ex}");
            }
        }

        private async void OnClickJoinRoom()
        {
            string code = _joinCodeInput.value.Trim().ToUpper();
            if (string.IsNullOrEmpty(code)) { SetLobbyStatus("Digite o código."); return; }
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            { SetLobbyStatus("Já conectado — saia da sala antes."); return; }

            SetLobbyStatus("Conectando...");
            try
            {
                string playerName = _nameInput.value.Trim();
                if (string.IsNullOrEmpty(playerName)) playerName = "Jogador";
                RuntimeMultiplayerSession.PlayerName = playerName;

                var bootstrap = NetBootstrap.EnsureExists();
                bootstrap.useLoopback = _loopbackToggle != null && _loopbackToggle.value;

                if (bootstrap.useLoopback)
                {
                    bootstrap.JoinLoopback();
                }
                else
                {
                    await bootstrap.InitUgsAsync(playerName);
                    await bootstrap.JoinRelayAsync(code);
                }

                Debug.Log($"[MP] Entrou na sala (cliente): jogador={playerName} loopback={bootstrap.useLoopback} codigo={code}");
                SetLobbyStatus("Conectado!");
                EnterRoom(code);

                StartCoroutine(SendNameAfterConnect(playerName));
            }
            catch (Exception ex)
            {
                SetLobbyStatus($"Erro: {ex.Message}");
                Debug.LogError($"[RoomHUD] OnClickJoinRoom: {ex}");
            }
        }

        private IEnumerator SendNameAfterConnect(string playerName)
        {
            float timeout = 5f;
            while (RoomManager.Instance == null && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
            if (RoomManager.Instance != null)
                RoomManager.Instance.SetPlayerNameServerRpc(playerName);
        }

        private void OnClickLeaveRoom()
        {
            _inRoom = false;
            if (_boundRoom && RoomManager.Instance != null)
            {
                RoomManager.Instance.Slots.OnListChanged -= OnSlotsChanged;
                RoomManager.Instance.OnPhaseChanged -= OnPhaseChanged;
                RoomManager.Instance.OnConfigChanged -= UpdateConfigDisplay;
                RoomManager.Instance.OnChatMessage -= AppendChatMessage;
            }
            _boundRoom = false;
            NetBootstrap.Instance?.Shutdown();
            _ = LobbyService.LeaveAsync();
            ShowLobbyPanel();
            SetLobbyStatus("Saiu da sala.");
        }

        private void OnClickAdvancePhase() => RoomManager.Instance?.AdvancePhaseServerRpc();

        private void OnClickCopyCode()
        {
            if (!string.IsNullOrEmpty(_joinCode) && _joinCode != "---")
                GUIUtility.systemCopyBuffer = _joinCode;
        }

        private void OnChatSubmit(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            RoomManager.Instance?.SendChatServerRpc(text);
            _chatInput.value = "";
            _chatInput.Focus();
        }

        private void SetLobbyStatus(string msg)
        {
            if (_lobbyStatus != null) _lobbyStatus.text = msg;
        }
    }
}
