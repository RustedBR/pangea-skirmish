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
using System.Collections.Generic;
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
        private Label _profileSummary;
        private ScrollView _roomList;
        private Button _browseBtn;
        private Button _refreshBtn;
        private Toggle _loopbackToggle;
        private Label _lobbyStatus;

        // ---- Widgets da sala -------------------------------------------------
        private Label _joinCodeDisplay, _phaseLabel;
        private ScrollView _playerList, _chatLog;
        private TextField _chatInput;
        private Button _tdmBtn, _ffaBtn, _coopBtn, _planMinus, _planPlus, _advanceBtn;
        private Label _budgetLabel, _planLabel;
        private VisualElement _hostControls, _waitingOverlay;
        private Label _waitingLabel;
        private TextField _roomNameInput;
        private Label _maxLabel;
        private Button _maxMinus, _maxPlus;

        // ---- Abas da sala (Chat / Room / Game) ----
        private Button _tabChatBtn, _tabRoomBtn, _tabGameBtn, _resetTuningBtn;
        private VisualElement _tabChat, _tabRoom, _tabGame, _tuningList;
        private bool _tuningBuilt = false;
        // Tooltip estilo BattleHUD
        private VisualElement _tooltipGo;
        private Label _tooltipTxt;
        private VisualElement _tipOwner;
        private IVisualElementScheduledItem _pendingTip;

        // ---- Estado ----------------------------------------------------------
        private int _gameMode = 0;       // 0=TDM, 1=FFA
        private int _budgetValue = PlayerProfile.AttributeBudget;
        private int _maxPlayersValue = 4;
        private float _planningValue = 60f; // GDD-014: segundos por ponto de decisão
        private bool _inRoom = false;
        private Label _charLabel, _charInfo, _startHint;
        private Button _charPrev, _charNext;
        private bool _boundRoom = false;
        private string _joinCode = "---";

        // =====================================================================
        // Inicialização / visibilidade (API pública mantida)
        // =====================================================================

        /// <summary>Mantido por compatibilidade com o MainMenuManager. UI Toolkit usa
        /// PanelSettings + tema, então canvas/font não são mais necessários.</summary>
        public void Init(Transform canvasTransform = null, Font font = null) { }

        // ---- Ícones BDragon (Opção C): valida uso de sprites do atlas na UI ----
        private static Sprite[] _bdragonSprites;
        private static Sprite BdragonIcon(string spriteName)
        {
            _bdragonSprites ??= Resources.LoadAll<Sprite>("Sprites/BDragon1727/UI/UI_buttons_00");
            return System.Array.Find(_bdragonSprites, s => s.name == spriteName);
        }
        private static void SetBdragonIcon(VisualElement root, string elementName, string spriteName)
        {
            var img = root.Q<Image>(elementName);
            var sprite = BdragonIcon(spriteName);
            if (img != null && sprite != null)
                img.style.backgroundImage = new StyleBackground(sprite);
        }
        // ---- Scroller BDragon (Opção 1): sprite simple_scroll_blue no handle ----
        // O sprite é uma barra VERTICAL fina (8x18). Em scroll vertical encaixa direto;
        // em scroll horizontal precisamos girar 90° pra ficar na orientação certa.
        private static void StyleBdragonScroller(Scroller scroller, float rotateDeg = 0f)
        {
            if (scroller == null) return;
            var handle = scroller.Q("unity-dragger");
            var sprite = BdragonIcon("UI_buttons_simple_scroll_blue");
            if (handle != null && sprite != null)
            {
                handle.style.backgroundImage = new StyleBackground(sprite);
                if (rotateDeg != 0f)
                    handle.style.rotate = new StyleRotate(new Rotate(rotateDeg));
            }
        }
        private void ApplyBdragonScrollbars(VisualElement r)
        {
            // Aba Chat (listas — verticais)
            StyleBdragonScroller(r.Q<ScrollView>("player-list")?.verticalScroller);
            StyleBdragonScroller(r.Q<ScrollView>("chat-log")?.verticalScroller);
            // Aba Game (tuning list — vertical)
            StyleBdragonScroller(r.Q<ScrollView>("tuning-list")?.verticalScroller);
            // Aba Lobby (room-list — HORIZONTAL: gira o sprite 90°)
            StyleBdragonScroller(r.Q<ScrollView>("room-list")?.horizontalScroller, 90f);
        }

        private void ApplyBdragonIcons(VisualElement r)
        {
            // Aba Room (steppers de -/+)
            SetBdragonIcon(r, "room-max-icon-minus", "UI_buttons_left_arrow_round_blue");
            SetBdragonIcon(r, "room-max-icon-plus", "UI_buttons_right_arrow_round_blue");
            SetBdragonIcon(r, "room-plan-icon-minus", "UI_buttons_left_arrow_round_blue");
            SetBdragonIcon(r, "room-plan-icon-plus", "UI_buttons_right_arrow_round_blue");
            // Aba Game (tuning list)
        }

        protected override void Bind()
        {
            var r = Root;

            _lobbyView = r.Q<VisualElement>("lobby-view");
            _roomView  = r.Q<VisualElement>("room-view");

            // --- lobby ---
            _profileSummary = r.Q<Label>("profile-summary");
            _roomList       = r.Q<ScrollView>("room-list");
            _browseBtn      = r.Q<Button>("btn-browse");
            _refreshBtn     = r.Q<Button>("btn-refresh");
            _loopbackToggle = r.Q<Toggle>("loopback-toggle");
            _lobbyStatus    = r.Q<Label>("lobby-status");
            r.Q<Button>("btn-create").clicked += OnClickCreateRoom;
            _browseBtn.clicked                += OnClickBrowse;
            _refreshBtn.clicked               += () => _ = RefreshRoomListAsync();
            r.Q<Button>("btn-back").clicked   += () => { StopAutoRefresh(); HideAll(); OnBackToMenu?.Invoke(); };

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
            _roomNameInput   = r.Q<TextField>("roomname-input");
            _maxLabel        = r.Q<Label>("max-label");
            _maxMinus        = r.Q<Button>("btn-max-minus");
            _maxPlus         = r.Q<Button>("btn-max-plus");
            _tdmBtn          = r.Q<Button>("btn-tdm");
            _ffaBtn          = r.Q<Button>("btn-ffa");
            _coopBtn         = r.Q<Button>("btn-coop");
            _planMinus       = r.Q<Button>("btn-plan-minus");
            _planPlus        = r.Q<Button>("btn-plan-plus");
            _advanceBtn      = r.Q<Button>("btn-advance");

            // --- abas da sala (Chat / Room / Game) ---
            _tabChatBtn  = r.Q<Button>("btn-tab-chat");
            _tabRoomBtn  = r.Q<Button>("btn-tab-room");
            _tabGameBtn  = r.Q<Button>("btn-tab-game");
            _tabChat     = r.Q<VisualElement>("tab-chat");
            _tabRoom     = r.Q<VisualElement>("tab-room");
            _tabGame     = r.Q<VisualElement>("tab-game");
            _tuningList  = r.Q<VisualElement>("tuning-list");
            _resetTuningBtn = r.Q<Button>("btn-reset-tuning");


            // Scroller BDragon (Opção 1): handle com sprite
            ApplyBdragonScrollbars(r);
            _tabChatBtn.clicked  += () => { ShowTab("chat"); };
            _tabGameBtn.clicked  += () => { ShowTab("game"); if (!_tuningBuilt) BuildTuningTab(); };
            _resetTuningBtn.clicked += ResetTuningToDefault;

            // Tooltip estilo BattleHUD (criado em runtime, append no Root)
            _tooltipGo = new VisualElement();
            _tooltipGo.name = "room-tooltip";
            _tooltipGo.AddToClassList("room__tooltip");
            _tooltipGo.style.display = DisplayStyle.None;
            _tooltipGo.pickingMode = PickingMode.Ignore;
            _tooltipTxt = new Label("");
            _tooltipTxt.AddToClassList("room__tooltip-txt");
            _tooltipTxt.pickingMode = PickingMode.Ignore;
            _tooltipGo.Add(_tooltipTxt);
            Root.Add(_tooltipGo);

            r.Q<Button>("btn-copy-code").clicked += OnClickCopyCode;
            _copyFeedback = r.Q<Label>("copy-feedback");
            _codeInput = r.Q<TextField>("code-input");
            var joinCodeBtn = r.Q<Button>("btn-join-code");
            if (joinCodeBtn != null) joinCodeBtn.clicked += () => _ = JoinByCodeAsync(_codeInput != null ? _codeInput.value : "");
            if (_codeInput != null)
                _codeInput.RegisterCallback<KeyDownEvent>(e =>
                {
                    if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) _ = JoinByCodeAsync(_codeInput.value);
                });
            r.Q<Button>("btn-send").clicked      += () => OnChatSubmit(_chatInput.value);
            r.Q<Button>("btn-leave").clicked     += OnClickLeaveRoom;
            _advanceBtn.clicked += OnClickAdvancePhase;

            _tdmBtn.clicked += () => { _gameMode = 0; UpdateModeHighlight(); SyncConfig(); };
            _ffaBtn.clicked += () => { _gameMode = 1; UpdateModeHighlight(); SyncConfig(); };
            if (_coopBtn != null)
                _coopBtn.clicked += () => { _gameMode = GameModes.Coop; UpdateModeHighlight(); SyncConfig(); };
            // Orçamento de atributos é fixo (PlayerProfile.AttributeBudget): sem controle na sala.

            // Personagem do perfil usado nesta sala (troca só no lobby).
            _charLabel = r.Q<Label>("char-label");
            _charInfo  = r.Q<Label>("char-info");
            _charPrev  = r.Q<Button>("char-prev");
            _charNext  = r.Q<Button>("char-next");
            _startHint = r.Q<Label>("start-hint");
            if (_charPrev != null) _charPrev.clicked += () => StepCharacter(-1);
            if (_charNext != null) _charNext.clicked += () => StepCharacter(+1);
            _planMinus.clicked   += () => { _planningValue = Mathf.Max(5f, _planningValue - 5f); _planLabel.text = _planningValue + "s"; SyncConfig(); };
            _planPlus.clicked    += () => { _planningValue = Mathf.Min(180f, _planningValue + 5f); _planLabel.text = _planningValue + "s"; SyncConfig(); };

            // Max players (host) — padrão 4, editável na sala.
            _maxMinus.clicked += () => { _maxPlayersValue = Mathf.Max(2, _maxPlayersValue - 1); _maxLabel.text = _maxPlayersValue.ToString(); SyncConfig(); };
            _maxPlus.clicked  += () => { _maxPlayersValue = Mathf.Min(8, _maxPlayersValue + 1); _maxLabel.text = _maxPlayersValue.ToString(); SyncConfig(); };

            // Renomear sala (host) — confirma com Enter.
            _roomNameInput.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    var newName = _roomNameInput.value.Trim();
                    if (!string.IsNullOrEmpty(newName) && RuntimeMultiplayerSession.IsHost)
                        _ = LobbyService.UpdateLobbyNameAsync(newName);
                    evt.StopPropagation();
                }
            });

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

        private static string ProfileName() => PlayerProfile.HasName ? PlayerProfile.Name : "Jogador";

        public void ShowLobbyPanel()
        {
            if (_profileSummary != null)
            {
                var c = PlayerProfile.Selected;
                _profileSummary.text = c != null
                    ? $"Jogando como <b>{ProfileName()}</b> · personagem <b>{c.presetName}</b>"
                    : $"Jogando como <b>{ProfileName()}</b> · sem personagem";
            }
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
            ShowTab("chat");
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
            if (RuntimeMultiplayerSession.IsHost) BuildTuningTab();
            if (_roomNameInput != null)
                _roomNameInput.SetEnabled(RuntimeMultiplayerSession.IsHost);

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
            RoomManager.Instance.OnCharacterRejected += OnCharacterRejected;
            _boundRoom = true;
            SubmitMyCharacter();   // entra na sala já com o personagem em uso no perfil
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
                RoomManager.Instance.OnCharacterRejected -= OnCharacterRejected;
            }
            _boundRoom = false;
        }

        // =====================================================================
        // Personagem do perfil nesta sala
        // =====================================================================

        private void SubmitMyCharacter()
        {
            var c = PlayerProfile.Selected;
            RefreshCharacterPicker();
            if (c == null || RoomManager.Instance == null) return;
            RuntimeMultiplayerSession.LocalCharacterPreset = c;
            RoomManager.Instance.SubmitCharacterServerRpc(JsonUtility.ToJson(c));
            Debug.Log($"[MP] Personagem enviado à sala: {c.presetName} ({c.weaponId})");
        }

        private void StepCharacter(int dir)
        {
            int n = PlayerProfile.Count;
            if (n <= 1) return;
            if (RoomManager.Instance != null && RoomManager.Instance.CurrentPhase != RoomPhase.Lobby) return;
            PlayerProfile.Select((PlayerProfile.Data.selected + dir + n) % n);
            SubmitMyCharacter();
        }

        private void RefreshCharacterPicker()
        {
            var c = PlayerProfile.Selected;
            if (_charLabel != null) _charLabel.text = c != null ? c.presetName : "(nenhum)";
            if (_charInfo != null) _charInfo.text = c != null ? DescribeCharacter(c) : "Crie um personagem no Perfil, no menu principal.";
            bool many = PlayerProfile.Count > 1;
            _charPrev?.SetEnabled(many);
            _charNext?.SetEnabled(many);
        }

        /// <summary>Resumo de uma linha: arma, aparência e os atributos.</summary>
        public static string DescribeCharacter(CharacterPreset c)
        {
            if (c == null || c.stats == null) return "";
            var w = WeaponCatalog.Get(c.weaponId);
            var look = CharacterLookCatalog.All[CharacterLookCatalog.IndexOf(c.modelId)];
            var s = c.stats;
            return $"{(w != null ? w.displayName : "Desarmado")} · {look.displayName}\n" +
                   $"STR {s.STR:0}  VIT {s.VIT:0}  DEX {s.DEX:0}  AGI {s.AGI:0}  INT {s.INT:0}  WIS {s.WIS:0}";
        }

        private void OnCharacterRejected(string reason) =>
            AppendChatMessage("Sistema", $"Seu personagem foi recusado: {reason}.");

        // =====================================================================
        // Callbacks de rede
        // =====================================================================

        private void OnSlotsChanged(NetworkListEvent<PlayerSlot> evt) => RebuildPlayerList();

        private void OnPhaseChanged(RoomPhase phase)
        {
            _phaseLabel.text = phase == RoomPhase.Lobby ? "Aguardando jogadores" : "Partida em andamento";
            SetVisible(true);

            bool isHost = RuntimeMultiplayerSession.IsHost;
            bool isLobby = phase == RoomPhase.Lobby;
            if (_advanceBtn != null) _advanceBtn.SetEnabled(isHost && isLobby);
            _charPrev?.SetEnabled(isLobby && PlayerProfile.Count > 1);
            _charNext?.SetEnabled(isLobby && PlayerProfile.Count > 1);

            if (!isHost && phase != RoomPhase.Lobby)
            {
                _waitingOverlay.style.display = DisplayStyle.Flex;
                _waitingLabel.text = "Carregando a partida...";
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

                string fullName = slot.PlayerName.ToString() + (slot.ClientId == hostId ? "  (host)" : "");
                var nameLbl = new Label(fullName) { tooltip = fullName };   // nome longo: reticências + dica
                nameLbl.AddToClassList("room__player-name");
                row.Add(nameLbl);

                string charName = slot.CharName.ToString();
                var charLbl = new Label(slot.ReadyChar ? charName : "sem personagem") { tooltip = charName };
                charLbl.AddToClassList("room__player-char");
                if (!slot.ReadyChar) charLbl.AddToClassList("room__player-char--missing");
                row.Add(charLbl);

                bool isCoop = RoomManager.Instance.CurrentConfig.GameMode == GameModes.Coop;
                var teamLbl = new Label(isFfa ? "FFA" : isCoop ? "Coop" : (slot.Team == 0 ? "Time A" : "Time B"));
                teamLbl.AddToClassList("pg-badge");
                teamLbl.AddToClassList("room__player-team");
                if (!isFfa) teamLbl.AddToClassList(slot.Team == 0 ? "pg-badge--team-a" : "pg-badge--team-b");
                row.Add(teamLbl);

                // Botão trocar time — só host, só TDM
                if (isHost && !isFfa && !isCoop)
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

            if (_startHint != null)
            {
                string missing = RoomManager.Instance.PlayersWithoutCharacter();
                _startHint.text = missing.Length > 0 ? $"Sem personagem: {missing}" : "Todos prontos.";
                _startHint.EnableInClassList("room__start-hint--missing", missing.Length > 0);
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
            _maxPlayersValue = cfg.MaxPlayers;
            UpdateModeHighlight();
            if (_budgetLabel != null) _budgetLabel.text = _budgetValue.ToString();
            if (_planLabel != null) _planLabel.text = _planningValue + "s";
            if (_maxLabel != null) _maxLabel.text = _maxPlayersValue.ToString();
            RebuildPlayerList(); // rótulos de time dependem do modo
        }

        // =====================================================================
        // Abas da sala (Sala / Tuning) + GameTuning por reflection
        // =====================================================================

        private void ShowTab(string which)
        {
            bool chat = which == "chat";
            bool room = which == "room";
            bool game = which == "game";
            if (_tabChat != null) _tabChat.style.display = chat ? DisplayStyle.Flex : DisplayStyle.None;
            if (_tabRoom != null) _tabRoom.style.display = room ? DisplayStyle.Flex : DisplayStyle.None;
            if (_tabGame != null) _tabGame.style.display = game ? DisplayStyle.Flex : DisplayStyle.None;
            // remove highlight de todas
            if (_tabChatBtn != null)  _tabChatBtn.RemoveFromClassList("pg-button--primary");
            if (_tabRoomBtn != null)  _tabRoomBtn.RemoveFromClassList("pg-button--primary");
            if (_tabGameBtn != null)  _tabGameBtn.RemoveFromClassList("pg-button--primary");
            // destaca a aba ativa
            if (chat && _tabChatBtn != null)  _tabChatBtn.AddToClassList("pg-button--primary");
            if (room && _tabRoomBtn != null)  _tabRoomBtn.AddToClassList("pg-button--primary");
            if (game && _tabGameBtn != null)  _tabGameBtn.AddToClassList("pg-button--primary");
        }

        // --- Tooltip estilo BattleHUD (hover 400ms, flip/clamp) ---
        private void AttachTuningTooltip(VisualElement el, string text)
        {
            el.RegisterCallback<MouseEnterEvent>(evt =>
            {
                CancelPendingTip();
                _tipOwner = el;
                Vector2 mp = evt.mousePosition;
                _pendingTip = el.schedule.Execute(() => ShowTuningTooltip(el, text, mp)).StartingIn(400);
            });
            el.RegisterCallback<MouseLeaveEvent>(_ => CancelPendingTip());
            el.RegisterCallback<ClickEvent>(_ => CancelPendingTip());
        }

        private void CancelPendingTip()
        {
            if (_pendingTip != null) { _pendingTip.Pause(); _pendingTip = null; }
            HideTuningTooltip();
        }

        private void HideTuningTooltip()
        {
            if (_tooltipGo != null) _tooltipGo.style.display = DisplayStyle.None;
            _tipOwner = null;
        }

        private void ShowTuningTooltip(VisualElement el, string text, Vector2 mousePos)
        {
            if (_tooltipGo == null || _tooltipTxt == null || el == null) return;
            _tooltipGo.BringToFront();
            _tooltipTxt.text = text;
            _tooltipGo.style.display = DisplayStyle.Flex;
            float w = _tooltipGo.layout.width > 0 ? _tooltipGo.layout.width : 260f;
            float h = _tooltipGo.layout.height > 0 ? _tooltipGo.layout.height : 60f;
            var root = el.panel != null ? el.panel.visualTree : null;
            float boundW = (root != null && root.layout.width > 0) ? root.layout.width : Screen.width;
            float boundH = (root != null && root.layout.height > 0) ? root.layout.height : Screen.height;
            float x = mousePos.x;
            float y = mousePos.y - h - 3f;
            if (y < 4f) y = mousePos.y + 3f;
            if (x + w > boundW - 4f) x = mousePos.x - w - 3f;
            if (x < 4f) x = 4f;
            if (x + w > boundW - 4f) x = boundW - w - 4f;
            if (y + h > boundH - 4f) y = boundH - h - 4f;
            _tooltipGo.style.left = x;
            _tooltipGo.style.top = y;
        }

        /// <summary>
        /// Constrói a lista de controles do GameTuning via reflection.
        /// O host edita RuntimeTuning.Active localmente; o sync acontece SÓ ao
        /// iniciar a partida (RoomManager.StartMatch).
        /// Clientes veem a lista mas não podem editar.
        /// </summary>
        private void BuildTuningTab()
        {
            if (_tuningList == null) return;
            _tuningList.Clear();

            // Garante que temos um GameTuning ativo (host edita este; clientes recebem por RPC).
            if (RuntimeTuning.Active == null)
                RuntimeTuning.Active = Resources.Load<GameTuning>("GameTuning")
                                       ?? ScriptableObject.CreateInstance<GameTuning>();

            var tuning = RuntimeTuning.Active;
            bool editable = RuntimeMultiplayerSession.IsHost;
            var fields = typeof(GameTuning).GetFields(System.Reflection.BindingFlags.Public
                                                      | System.Reflection.BindingFlags.Instance);

            foreach (var f in fields)
            {
                // pula tipos complexos que não mapeamos em UI simples (StatFormulas, arrays, etc.)
                if (f.FieldType == typeof(StatFormulas)
                    || f.FieldType.IsArray
                    || (f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(List<>)))
                    continue;

                var row = new VisualElement();
                row.AddToClassList("tuning__row");

                var label = new Label(PrettyName(f.Name));
                label.AddToClassList("tuning__label");
                row.Add(label);

                object val = f.GetValue(tuning);

                if (f.FieldType == typeof(bool))
                {
                    var toggle = new Toggle();
                    toggle.value = (bool)val;
                    toggle.SetEnabled(editable);
                    toggle.RegisterValueChangedCallback(evt =>
                    {
                        f.SetValue(RuntimeTuning.Active, evt.newValue);
                        OnTuningChanged();
                    });
                    row.Add(toggle);
                }
                else if (f.FieldType == typeof(int) || f.FieldType == typeof(float))
                {
                    var field = new TextField();
                    field.value = val.ToString();
                    field.AddToClassList("tuning__input");
                    field.SetEnabled(editable);
                    field.RegisterValueChangedCallback(evt =>
                    {
                        if (f.FieldType == typeof(int))
                        {
                            if (int.TryParse(evt.newValue, out int iv)) { f.SetValue(RuntimeTuning.Active, iv); OnTuningChanged(); }
                        }
                        else
                        {
                            if (float.TryParse(evt.newValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fv))
                            { f.SetValue(RuntimeTuning.Active, fv); OnTuningChanged(); }
                        }
                    });
                    row.Add(field);
                }
                else if (f.FieldType == typeof(Color))
                {
                    var field = new TextField();
                    var c = (Color)val;
                    field.value = $"{(int)(c.r*255)},{(int)(c.g*255)},{(int)(c.b*255)}";
                    field.AddToClassList("tuning__input");
                    field.SetEnabled(editable);
                    field.RegisterValueChangedCallback(evt =>
                    {
                        var parts = evt.newValue.Split(',');
                        if (parts.Length == 3
                            && byte.TryParse(parts[0].Trim(), out byte r)
                            && byte.TryParse(parts[1].Trim(), out byte g)
                            && byte.TryParse(parts[2].Trim(), out byte b))
                        {
                            f.SetValue(RuntimeTuning.Active, new Color(r/255f, g/255f, b/255f, ((Color)val).a));
                            OnTuningChanged();
                        }
                    });
                    row.Add(field);
                }
                else if (f.FieldType == typeof(Vector2))
                {
                    var field = new TextField();
                    var v = (Vector2)val;
                    field.value = $"{v.x},{v.y}";
                    field.AddToClassList("tuning__input");
                    field.SetEnabled(editable);
                    field.RegisterValueChangedCallback(evt =>
                    {
                        var parts = evt.newValue.Split(',');
                        if (parts.Length == 2
                            && float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x)
                            && float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y))
                        {
                            f.SetValue(RuntimeTuning.Active, new Vector2(x, y));
                            OnTuningChanged();
                        }
                    });
                    row.Add(field);
                }
                else if (f.FieldType == typeof(string))
                {
                    var field = new TextField();
                    field.value = (string)val;
                    field.AddToClassList("tuning__input");
                    field.SetEnabled(editable);
                    field.RegisterValueChangedCallback(evt =>
                    {
                        f.SetValue(RuntimeTuning.Active, evt.newValue);
                        OnTuningChanged();
                    });
                    row.Add(field);
                }
                else
                {
                    // tipo não mapeado: só mostra o valor (read-only)
                    var valLabel = new Label(val?.ToString() ?? "—");
                    valLabel.AddToClassList("tuning__value");
                    row.Add(valLabel);
                }

                // Tooltip estilo BattleHUD (hover 400ms, flip/clamp)
                string tip = PrettyName(f.Name);
                var tipAttr = (TooltipAttribute)Attribute.GetCustomAttribute(f, typeof(TooltipAttribute));
                if (tipAttr != null && !string.IsNullOrEmpty(tipAttr.tooltip))
                    tip += "\n\n" + tipAttr.tooltip;
                tip += $"\n\nTipo: {f.FieldType.Name}\nValor: {val}";
                AttachTuningTooltip(row, tip);

                _tuningList.Add(row);
            }

            _tuningBuilt = true;
            // a aba principal (Sala) continua sendo a visível
            ShowTab("chat");
        }

        private static string PrettyName(string name)
        {
            // insere espaço antes de maiúsculas (camelCase -> "camel Case")
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i-1]))
                    sb.Append(' ');
                sb.Append(name[i]);
            }
            return sb.ToString();
        }

        private void OnTuningChanged()
        {
            // Host editou localmente. O sync real acontece em RoomManager.StartMatch.
            // Aqui só garantimos que RuntimeTuning.Active está pronto.
            if (RuntimeTuning.Active == null)
                RuntimeTuning.Active = ScriptableObject.CreateInstance<GameTuning>();
        }

        private void ResetTuningToDefault()
        {
            if (!RuntimeMultiplayerSession.IsHost) return;
            RuntimeTuning.Active = Resources.Load<GameTuning>("GameTuning")
                                   ?? ScriptableObject.CreateInstance<GameTuning>();
            _tuningBuilt = false;
            BuildTuningTab();
            OnTuningChanged();
        }

        private void SyncConfig()
        {
            if (!RuntimeMultiplayerSession.IsHost || RoomManager.Instance == null) return;
            var cfg = new RoomConfigNet
            {
                GameMode = _gameMode,
                AttributeBudget = _budgetValue,
                PlanningTime = _planningValue,
                MaxPlayers = _maxPlayersValue
            };
            RoomManager.Instance.SetConfigServerRpc(cfg);
        }

        private void UpdateModeHighlight()
        {
            if (_tdmBtn != null) _tdmBtn.EnableInClassList("room__mode-btn--active", _gameMode == 0);
            if (_ffaBtn != null) _ffaBtn.EnableInClassList("room__mode-btn--active", _gameMode == 1);
            if (_coopBtn != null) _coopBtn.EnableInClassList("room__mode-btn--active", _gameMode == GameModes.Coop);
        }

        private void SetHostControlsInteractable(bool on)
        {
            _tdmBtn?.SetEnabled(on);
            _ffaBtn?.SetEnabled(on);
            _coopBtn?.SetEnabled(on);
            _planMinus?.SetEnabled(on);
            _planPlus?.SetEnabled(on);
            _maxMinus?.SetEnabled(on);
            _maxPlus?.SetEnabled(on);
            _roomNameInput?.SetEnabled(on);
            _advanceBtn?.SetEnabled(on);
        }

        // =====================================================================
        // Handlers de botão
        // =====================================================================

        private async void OnClickCreateRoom()
        {
            MpDiag.Log("RoomHUD", "OnClickCreateRoom INICIO");
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            { SetLobbyStatus("Já conectado — saia da sala antes."); return; }

            SetLobbyStatus("Criando sala...");
            try
            {
                string playerName = ProfileName();
                // Nome da sala automático: "Sala de {playerName}". Editável DENTRO da sala (host).
                string roomName = "Sala de " + playerName;

                // ANTES de StartHost: o slot do host lê RuntimeMultiplayerSession.PlayerName no spawn.
                RuntimeMultiplayerSession.PlayerName = playerName;

                var bootstrap = NetBootstrap.EnsureExists();
                bootstrap.useLoopback = _loopbackToggle != null && _loopbackToggle.value;

                string joinCode;
                if (bootstrap.useLoopback)
                {
                    try
                    {
                        bootstrap.HostLoopback();
                        joinCode = "LOOPBACK";
                    }
                    catch (Exception ex)
                    {
                        MpDiag.Log("RoomHUD", $"HostLoopback FAILED: {ex.GetType().FullName}: {ex.Message}");
                        SetLobbyStatus($"Erro loopback: {ex.Message}");
                        Debug.LogError($"[RoomHUD] HostLoopback: {ex}");
                        return;
                    }
                }
                else
                {
                    MpDiag.Log("RoomHUD", "InitUgsAsync...");
                    try
                    {
                        await bootstrap.InitUgsAsync(playerName);
                    }
                    catch (Exception ex)
                    {
                        MpDiag.Log("RoomHUD", $"InitUgsAsync FAILED: {ex.GetType().FullName}: {ex.Message}");
                        SetLobbyStatus($"Erro init UGS: {ex.Message}");
                        Debug.LogError($"[RoomHUD] InitUgsAsync: {ex}");
                        return;
                    }

                    MpDiag.Log("RoomHUD", "InitUgsAsync OK, chamando HostRelayAsync...");
                    try
                    {
                        joinCode = await bootstrap.HostRelayAsync();
                    }
                    catch (Exception ex)
                    {
                        MpDiag.Log("RoomHUD", $"HostRelayAsync FAILED: {ex.GetType().FullName}: {ex.Message}");
                        SetLobbyStatus($"Erro Relay: {ex.Message}");
                        Debug.LogError($"[RoomHUD] HostRelayAsync: {ex}");
                        return;
                    }

                    MpDiag.Log("RoomHUD", "HostRelayAsync retornou, criando lobby...");

                    try
                    {
                        var (lobbyCode, err) = await LobbyService.CreateLobbyAsync(roomName, _maxPlayersValue, joinCode);
                        if (err != null) Debug.LogWarning($"[RoomHUD] Lobby criado com aviso: {err}");
                    }
                    catch (Exception ex)
                    {
                        MpDiag.Log("RoomHUD", $"CreateLobbyAsync FAILED: {ex.GetType().FullName}: {ex.Message}");
                        Debug.LogError($"[RoomHUD] CreateLobbyAsync: {ex}");
                    }
                }

                RuntimeMultiplayerSession.PlayerName = playerName;
                Debug.Log($"[MP] Sala criada (host): jogador={playerName} sala={roomName} loopback={bootstrap.useLoopback} codigo={joinCode}");
                SetLobbyStatus("Sala criada!");
                EnterRoom(joinCode);
            }
            catch (Exception ex)
            {
                MpDiag.Log("RoomHUD", $"EXCECAO CAPTURADA no OnClickCreateRoom: {ex}");
                SetLobbyStatus($"Erro: {ex.Message}");
                Debug.LogError($"[RoomHUD] OnClickCreateRoom: {ex}");
            }
        }

        // =====================================================================
        // Server Browser — busca salas públicas e popula a lista
        // =====================================================================
        private async void OnClickBrowse()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            { SetLobbyStatus("Já conectado — saia da sala antes."); return; }

            SetLobbyStatus("Conectando ao servidor de salas...");
            try
            {
                // O browser SEMPRE usa UGS (mesmo que o toggle "modo local" esteja marcado
                // para criar sala loopback). Não dá pra listar salas sem auth.
                string playerName = ProfileName();
                RuntimeMultiplayerSession.PlayerName = playerName;

                try { await NetBootstrap.EnsureExists().InitUgsAsync(playerName); }
                catch (Exception ex)
                {
                    SetLobbyStatus($"Erro ao conectar (UGS): {ex.Message}. Verifique se o UGS está configurado no projeto.");
                    Debug.LogError($"[RoomHUD] InitUgsAsync (browser): {ex}");
                    return;
                }

                // Busca imediata + auto-refresh enquanto na tela de lobby.
                await RefreshRoomListAsync();
                StartAutoRefresh();
            }
            catch (Exception ex)
            {
                SetLobbyStatus($"Erro: {ex.Message}");
                Debug.LogError($"[RoomHUD] OnClickBrowse: {ex}");
            }
        }

        private async Task RefreshRoomListAsync()
        {
            try
            {
                var lobbies = await LobbyService.QueryPublicLobbiesAsync();
                Debug.Log($"[RoomHUD] RefreshRoomList: {lobbies.Count} sala(s) recebidas da query");
                PopulateRoomList(lobbies);
                SetLobbyStatus(lobbies.Count == 0
                    ? "Nenhuma sala ativa no momento. Crie uma sala ou aguarde."
                    : $"{lobbies.Count} sala(s) encontrada(s).");
            }
            catch (Exception ex)
            {
                SetLobbyStatus($"Erro ao atualizar: {ex.Message}");
                Debug.LogError($"[RoomHUD] RefreshRoomListAsync: {ex}");
            }
        }

        private float _autoRefreshTimer;
        private const float AutoRefreshInterval = 5f;
        private bool _autoRefreshing;

        private void StartAutoRefresh() => _autoRefreshing = true;
        private void StopAutoRefresh() => _autoRefreshing = false;

        private void Update()
        {
            if (!_autoRefreshing || _roomList == null) return;
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            { _autoRefreshing = false; return; }
            _autoRefreshTimer += Time.deltaTime;
            if (_autoRefreshTimer >= AutoRefreshInterval)
            {
                _autoRefreshTimer = 0f;
                _ = RefreshRoomListAsync();
            }
        }

        private void PopulateRoomList(List<LobbyService.LobbyInfo> lobbies)
        {
            if (_roomList == null) return;
            _roomList.Clear();

            if (lobbies == null || lobbies.Count == 0)
            {
                var empty = new Label("Nenhuma sala ativa no momento.");
                empty.AddToClassList("room__room-empty");
                _roomList.Add(empty);
                return;
            }

            foreach (var info in lobbies)
            {
                // Mostra TODAS as salas (mesmo sem relayCode ainda — o JoinLobbyByIdAsync
                // busca o code fresco ao clicar). Isso evita salas "somerem" por race condition.
                var row = new VisualElement();
                row.AddToClassList("room__room-row");

                var nameLbl = new Label(info.Name);
                nameLbl.AddToClassList("room__room-name");
                row.Add(nameLbl);

                var slotsLbl = new Label($"{info.Players}/{info.MaxPlayers}");
                slotsLbl.AddToClassList("room__room-slots");
                row.Add(slotsLbl);

                // Clique na linha = entrar na sala (sem digitar código).
                string lobbyId = info.LobbyId;
                string relayCode = info.RelayCode;
                row.RegisterCallback<ClickEvent>(_ => OnRoomRowClicked(lobbyId, relayCode));

                _roomList.Add(row);
            }
        }

        private void OnRoomRowClicked(string lobbyId, string relayCode)
        {
            _ = JoinLobbyAsync(lobbyId, relayCode);
        }

        private async Task JoinLobbyAsync(string lobbyId, string relayCode)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            { SetLobbyStatus("Já conectado — saia da sala antes."); return; }

            SetLobbyStatus("Entrando na sala...");
            try
            {
                string playerName = ProfileName();
                RuntimeMultiplayerSession.PlayerName = playerName;

                var bootstrap = NetBootstrap.EnsureExists();
                if (bootstrap.useLoopback)
                {
                    SetLobbyStatus("Modo local não tem browser — use Criar Sala.");
                    return;
                }

                // Confirma o relay code via lobby (caso o browser tenha trazido stale).
                var (freshCode, err) = await LobbyService.JoinLobbyByIdAsync(lobbyId);
                if (err != null) { SetLobbyStatus($"Erro: {err}"); return; }
                string code = freshCode ?? relayCode;

                await bootstrap.JoinRelayAsync(code);

                Debug.Log($"[MP] Entrou na sala (cliente): jogador={playerName} codigo={code}");
                SetLobbyStatus("Conectado!");
                EnterRoom(code);
                StartCoroutine(SendNameAfterConnect(playerName));
            }
            catch (Exception ex)
            {
                SetLobbyStatus($"Erro: {ex.Message}");
                Debug.LogError($"[RoomHUD] JoinLobbyAsync: {ex}");
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
            StopAutoRefresh();
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

        private Label _copyFeedback;
        private TextField _codeInput;

        /// <summary>
        /// Copia o código da sala. GUIUtility.systemCopyBuffer não chega à área de transferência
        /// do navegador no WebGL; o WebBridge usa a API do navegador (com reservas).
        /// </summary>
        private void OnClickCopyCode()
        {
            if (string.IsNullOrEmpty(_joinCode) || _joinCode == "---") return;
            WebBridge.CopyText(_joinCode);
            Debug.Log($"[RoomHUD] código copiado: {_joinCode}");
            if (_copyFeedback != null)
            {
                _copyFeedback.text = "Copiado!";
                _copyFeedback.schedule.Execute(() => _copyFeedback.text = "").StartingIn(1800);
            }
        }

        /// <summary>
        /// Entra numa sala pelo código mostrado no cabeçalho dela (o código do Relay). Não havia
        /// campo para isso: só dava para entrar clicando na lista. Aceita espaços e minúsculas.
        /// </summary>
        private async Task JoinByCodeAsync(string raw)
        {
            string code = (raw ?? "").Trim().Replace(" ", "").Replace("-", "").ToUpperInvariant();
            if (code.Length < 4) { SetLobbyStatus("Digite o código da sala (ex.: ABC123)."); return; }
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            { SetLobbyStatus("Já conectado — saia da sala antes."); return; }

            SetLobbyStatus($"Entrando na sala {code}...");
            Debug.Log($"[RoomHUD] JoinByCode: {code}");
            try
            {
                string playerName = ProfileName();
                RuntimeMultiplayerSession.PlayerName = playerName;

                var bootstrap = NetBootstrap.EnsureExists();
                if (bootstrap.useLoopback) { SetLobbyStatus("Modo local não aceita código — desmarque \"Modo local\"."); return; }

                try { await bootstrap.InitUgsAsync(playerName); }
                catch (Exception ex) { SetLobbyStatus($"Erro UGS: {ex.Message}"); Debug.LogError($"[RoomHUD] InitUgsAsync (código): {ex}"); return; }

                StopAutoRefresh();
                await bootstrap.JoinRelayAsync(code);
                Debug.Log($"[MP] Entrou na sala por código (cliente): jogador={playerName} codigo={code}");
                SetLobbyStatus("Conectado!");
                EnterRoom(code);
                StartCoroutine(SendNameAfterConnect(playerName));
            }
            catch (Exception ex)
            {
                // Código errado ou sala encerrada: o Relay responde "join code not found".
                string msg = ex.Message ?? "";
                bool notFound = msg.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0 || msg.IndexOf("404", StringComparison.Ordinal) >= 0;
                SetLobbyStatus(notFound ? $"Sala {code} não encontrada. Confira o código ou peça um novo ao anfitrião." : $"Erro: {msg}");
                Debug.LogError($"[RoomHUD] JoinByCodeAsync({code}): {ex}");
            }
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
