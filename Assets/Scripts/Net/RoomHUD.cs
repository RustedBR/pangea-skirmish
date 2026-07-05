// Net/RoomHUD.cs
// HUD de multiplayer (lobby + sala) construído por código seguindo os padrões de
// MainMenuManager (MakeBtn/MakeLabel/MakeInputField/UiSkin.ApplyButtonSkin).
// Vive como painel no Canvas do MainMenu — sem cena nova.
//
// Fluxo:
//   Painel "Multiplayer"  →  Criar Sala / Entrar por código
//   Painel "Sala"         →  Join code, lista de jogadores, chat, controles do host

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace PangeaSkirmish
{
    public class RoomHUD : MonoBehaviour
    {
        // ---- Referência ao canvas pai e font --------------------------------
        private Transform _canvasTransform;
        private Font _font;

        // ---- Painéis --------------------------------------------------------
        private GameObject _mpLobbyPanel;   // entrar/criar
        private GameObject _mpRoomPanel;    // sala ativa

        // ---- Widgets do lobby -----------------------------------------------
        private InputField _nameInput;
        private InputField _roomNameInput;
        private InputField _joinCodeInput;
        private Toggle _loopbackToggle;
        private Text _lobbyStatusText;

        // ---- Widgets da sala ------------------------------------------------
        private Text _joinCodeDisplay;
        private Transform _playerListContent;
        private Text _chatLog;
        private InputField _chatInput;
        private ScrollRect _chatScroll;

        // Host controls
        private Toggle _tdmFfaToggle;   // off=TDM, on=FFA
        private Text _budgetLabel;
        private int _budgetValue = 30;
        private Text _planningLabel;
        private float _planningValue = 15f;
        private Button _advanceBtn;
        private Text _phaseLabel;

        // Waiting overlay (não-host, fase mudou sem UI pronta)
        private GameObject _waitingOverlay;
        private Text _waitingLabel;

        // ---- Estado local ---------------------------------------------------
        private bool _inRoom = false;

        // =========================================================================
        // Inicialização pública
        // =========================================================================

        public void Init(Transform canvasTransform, Font font)
        {
            _canvasTransform = canvasTransform;
            _font = font;
            BuildLobbyPanel();
            BuildRoomPanel();
            _mpLobbyPanel.SetActive(false);
            _mpRoomPanel.SetActive(false);
        }

        public void ShowLobbyPanel()
        {
            _mpLobbyPanel.SetActive(true);
            _mpRoomPanel.SetActive(false);
        }

        public void HideAll()
        {
            _mpLobbyPanel.SetActive(false);
            _mpRoomPanel.SetActive(false);
        }

        // =========================================================================
        // Construção do painel Lobby
        // =========================================================================

        private void BuildLobbyPanel()
        {
            _mpLobbyPanel = MakeFullPanel(_canvasTransform, "MP_LobbyPanel");
            _mpLobbyPanel.AddComponent<Image>().color = new Color(0.05f, 0.06f, 0.10f, 0.98f);

            MakeLabel(_mpLobbyPanel.transform, new Vector2(0, 400), new Vector2(700, 70), 48,
                Color.white).text = "Multiplayer";

            // Nome do jogador
            MakeLabel(_mpLobbyPanel.transform, new Vector2(-250, 310), new Vector2(200, 35), 20,
                new Color(0.8f, 0.8f, 0.8f)).text = "Seu nome:";
            _nameInput = MakeInputField(_mpLobbyPanel.transform, new Vector2(100, 310), new Vector2(280, 40), "Jogador");

            // Nome da sala (host)
            MakeLabel(_mpLobbyPanel.transform, new Vector2(-250, 255), new Vector2(200, 35), 20,
                new Color(0.8f, 0.8f, 0.8f)).text = "Nome da sala:";
            _roomNameInput = MakeInputField(_mpLobbyPanel.transform, new Vector2(100, 255), new Vector2(280, 40), "Sala do Marcus");

            // Botão criar sala
            var btnCreate = MakeBtn(_mpLobbyPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(-110, 185), new Vector2(220, 55));
            UiSkin.ApplyButtonSkin(btnCreate.GetComponent<Image>(), new Color(0.18f, 0.35f, 0.55f));
            MakeLabel(btnCreate.transform, Vector2.zero, new Vector2(220, 55), 22, Color.white).text = "Criar Sala";
            btnCreate.onClick.AddListener(OnClickCreateRoom);

            // Separador
            MakeLabel(_mpLobbyPanel.transform, new Vector2(120, 185), new Vector2(60, 55), 26,
                new Color(0.5f, 0.5f, 0.5f)).text = "ou";

            // Código de entrada
            _joinCodeInput = MakeInputField(_mpLobbyPanel.transform, new Vector2(0, 115), new Vector2(280, 40), "CÓDIGO");

            // Botão entrar
            var btnJoin = MakeBtn(_mpLobbyPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0, 55), new Vector2(280, 55));
            UiSkin.ApplyButtonSkin(btnJoin.GetComponent<Image>(), new Color(0.25f, 0.20f, 0.45f));
            MakeLabel(btnJoin.transform, Vector2.zero, new Vector2(280, 55), 22, Color.white).text = "Entrar na Sala";
            btnJoin.onClick.AddListener(OnClickJoinRoom);

            // Toggle loopback
            var loopRow = new GameObject("LoopbackRow", typeof(RectTransform));
            loopRow.transform.SetParent(_mpLobbyPanel.transform, false);
            var loopRt = loopRow.GetComponent<RectTransform>();
            loopRt.anchorMin = loopRt.anchorMax = loopRt.pivot = new Vector2(0.5f, 0.5f);
            loopRt.anchoredPosition = new Vector2(0, -30);
            loopRt.sizeDelta = new Vector2(360, 30);

            _loopbackToggle = loopRow.AddComponent<Toggle>();
            var loopBg = new GameObject("Background", typeof(RectTransform), typeof(Image));
            loopBg.transform.SetParent(loopRow.transform, false);
            var loopBgRt = loopBg.GetComponent<RectTransform>();
            loopBgRt.anchorMin = loopBgRt.anchorMax = loopBgRt.pivot = new Vector2(0f, 0.5f);
            loopBgRt.anchoredPosition = new Vector2(0, 0);
            loopBgRt.sizeDelta = new Vector2(24, 24);
            loopBg.GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.35f);
            var loopCheck = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            loopCheck.transform.SetParent(loopBg.transform, false);
            var loopCheckRt = loopCheck.GetComponent<RectTransform>();
            loopCheckRt.anchorMin = Vector2.zero; loopCheckRt.anchorMax = Vector2.one;
            loopCheckRt.offsetMin = new Vector2(4, 4); loopCheckRt.offsetMax = new Vector2(-4, -4);
            loopCheck.GetComponent<Image>().color = new Color(0.4f, 0.8f, 0.4f);
            _loopbackToggle.targetGraphic = loopBg.GetComponent<Image>();
            _loopbackToggle.graphic = loopCheck.GetComponent<Image>();
            MakeLabel(loopRow.transform, new Vector2(100, 0), new Vector2(280, 28), 16,
                new Color(0.7f, 0.7f, 0.7f)).text = "Modo local (sem UGS/internet)";

            // Status
            _lobbyStatusText = MakeLabel(_mpLobbyPanel.transform, new Vector2(0, -100), new Vector2(700, 40), 18,
                new Color(0.9f, 0.7f, 0.3f));
            _lobbyStatusText.text = "";

            // Voltar
            var btnBack = MakeBtn(_mpLobbyPanel.transform, new Vector2(0.5f, 0f),
                new Vector2(0, 50), new Vector2(220, 50));
            btnBack.GetComponent<Image>().color = new Color(0.35f, 0.15f, 0.15f);
            MakeLabel(btnBack.transform, Vector2.zero, new Vector2(220, 50), 20, Color.white).text = "← Voltar";
            btnBack.onClick.AddListener(() =>
            {
                HideAll();
                // MainMenuManager.ShowMenu() será chamado via evento
                OnBackToMenu?.Invoke();
            });
        }

        public event Action OnBackToMenu;

        // =========================================================================
        // Construção do painel Sala
        // =========================================================================

        private void BuildRoomPanel()
        {
            _mpRoomPanel = MakeFullPanel(_canvasTransform, "MP_RoomPanel");
            _mpRoomPanel.AddComponent<Image>().color = new Color(0.04f, 0.05f, 0.08f, 0.98f);

            // ---- Título + Join code -----------------------------------------
            MakeLabel(_mpRoomPanel.transform, new Vector2(0, 370), new Vector2(500, 50), 34,
                Color.white).text = "Sala Multiplayer";

            var joinCodeLbl = MakeLabel(_mpRoomPanel.transform, new Vector2(-300, 310), new Vector2(200, 35), 18,
                new Color(0.6f, 0.6f, 0.6f));
            joinCodeLbl.text = "Código:";

            _joinCodeDisplay = MakeLabel(_mpRoomPanel.transform, new Vector2(50, 310), new Vector2(400, 40), 30,
                new Color(0.4f, 0.9f, 0.5f));
            _joinCodeDisplay.text = "---";

            _phaseLabel = MakeLabel(_mpRoomPanel.transform, new Vector2(0, 260), new Vector2(600, 30), 18,
                new Color(0.7f, 0.7f, 0.9f));
            _phaseLabel.text = "Fase: Lobby";

            // ---- Lista de jogadores (lado esquerdo) -------------------------
            MakeLabel(_mpRoomPanel.transform, new Vector2(-540, 290), new Vector2(280, 28), 18,
                new Color(0.8f, 0.8f, 0.8f)).text = "Jogadores";

            var playerListBg = new GameObject("PlayerListBg", typeof(RectTransform), typeof(Image));
            playerListBg.transform.SetParent(_mpRoomPanel.transform, false);
            var plRt = playerListBg.GetComponent<RectTransform>();
            plRt.anchorMin = plRt.anchorMax = plRt.pivot = new Vector2(0.5f, 0.5f);
            plRt.anchoredPosition = new Vector2(-540, -40);
            plRt.sizeDelta = new Vector2(340, 430);
            playerListBg.GetComponent<Image>().color = new Color(0.06f, 0.07f, 0.10f);

            // ScrollRect para lista de jogadores
            var plScroll = new GameObject("PlayerListScroll", typeof(RectTransform), typeof(ScrollRect), typeof(RectMask2D));
            plScroll.transform.SetParent(playerListBg.transform, false);
            var plScrollRt = plScroll.GetComponent<RectTransform>();
            plScrollRt.anchorMin = Vector2.zero; plScrollRt.anchorMax = Vector2.one;
            plScrollRt.offsetMin = new Vector2(4, 4); plScrollRt.offsetMax = new Vector2(-4, -4);

            var plContent = new GameObject("Content", typeof(RectTransform));
            plContent.transform.SetParent(plScroll.transform, false);
            var plContentRt = plContent.GetComponent<RectTransform>();
            plContentRt.anchorMin = new Vector2(0, 1); plContentRt.anchorMax = new Vector2(1, 1);
            plContentRt.pivot = new Vector2(0.5f, 1f);
            plContentRt.anchoredPosition = Vector2.zero;
            plContentRt.sizeDelta = new Vector2(0, 0);
            var plVlg = plContent.AddComponent<VerticalLayoutGroup>();
            plVlg.spacing = 4; plVlg.childForceExpandWidth = true; plVlg.childForceExpandHeight = false;
            plContent.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _playerListContent = plContent.transform;

            var plScrollComp = plScroll.GetComponent<ScrollRect>();
            plScrollComp.content = plContentRt;
            plScrollComp.horizontal = false;
            plScrollComp.vertical = true;
            plScrollComp.scrollSensitivity = 20f;

            // ---- Chat (centro/direita) ---------------------------------------
            MakeLabel(_mpRoomPanel.transform, new Vector2(100, 290), new Vector2(380, 28), 18,
                new Color(0.8f, 0.8f, 0.8f)).text = "Chat";

            var chatBg = new GameObject("ChatBg", typeof(RectTransform), typeof(Image));
            chatBg.transform.SetParent(_mpRoomPanel.transform, false);
            var chatBgRt = chatBg.GetComponent<RectTransform>();
            chatBgRt.anchorMin = chatBgRt.anchorMax = chatBgRt.pivot = new Vector2(0.5f, 0.5f);
            chatBgRt.anchoredPosition = new Vector2(100, -10);
            chatBgRt.sizeDelta = new Vector2(440, 370);
            chatBg.GetComponent<Image>().color = new Color(0.04f, 0.05f, 0.08f);

            // ScrollRect de mensagens
            var chatScrollGo = new GameObject("ChatScroll", typeof(RectTransform), typeof(ScrollRect), typeof(RectMask2D));
            chatScrollGo.transform.SetParent(chatBg.transform, false);
            var csRt = chatScrollGo.GetComponent<RectTransform>();
            csRt.anchorMin = new Vector2(0, 0); csRt.anchorMax = new Vector2(1, 1);
            csRt.offsetMin = new Vector2(4, 50); csRt.offsetMax = new Vector2(-4, -4);
            _chatScroll = chatScrollGo.GetComponent<ScrollRect>();

            var chatContent = new GameObject("ChatContent", typeof(RectTransform));
            chatContent.transform.SetParent(chatScrollGo.transform, false);
            var ccRt = chatContent.GetComponent<RectTransform>();
            ccRt.anchorMin = new Vector2(0, 0); ccRt.anchorMax = new Vector2(1, 0);
            ccRt.pivot = new Vector2(0.5f, 0f);
            ccRt.anchoredPosition = Vector2.zero;
            ccRt.sizeDelta = new Vector2(0, 0);
            var ccVlg = chatContent.AddComponent<VerticalLayoutGroup>();
            ccVlg.spacing = 2; ccVlg.childForceExpandWidth = true; ccVlg.childForceExpandHeight = false;
            ccVlg.padding = new RectOffset(6, 6, 4, 4);
            chatContent.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _chatScroll.content = ccRt;
            _chatScroll.horizontal = false;
            _chatScroll.vertical = true;
            _chatScroll.scrollSensitivity = 20f;

            // Usaremos _chatScroll.content para appender linhas via código
            // Guardamos uma referência para o método AppendChat

            // Input de chat
            _chatInput = MakeInputField(chatBg.transform, new Vector2(0, -160), new Vector2(340, 40), "");
            _chatInput.onEndEdit.AddListener(OnChatSubmit);

            var btnSend = MakeBtn(chatBg.transform, new Vector2(0.5f, 0.5f), new Vector2(175, -160), new Vector2(90, 40));
            UiSkin.ApplyButtonSkin(btnSend.GetComponent<Image>(), new Color(0.18f, 0.28f, 0.52f));
            MakeLabel(btnSend.transform, Vector2.zero, new Vector2(90, 40), 16, Color.white).text = "Enviar";
            btnSend.onClick.AddListener(() => OnChatSubmit(_chatInput.text));

            // ---- Controles do host (lado direito) ----------------------------
            var hostCtrlBg = new GameObject("HostCtrl", typeof(RectTransform), typeof(Image));
            hostCtrlBg.transform.SetParent(_mpRoomPanel.transform, false);
            var hcRt = hostCtrlBg.GetComponent<RectTransform>();
            hcRt.anchorMin = hcRt.anchorMax = hcRt.pivot = new Vector2(0.5f, 0.5f);
            hcRt.anchoredPosition = new Vector2(560, -10);
            hcRt.sizeDelta = new Vector2(260, 370);
            hostCtrlBg.GetComponent<Image>().color = new Color(0.06f, 0.07f, 0.10f);

            MakeLabel(hostCtrlBg.transform, new Vector2(0, 160), new Vector2(240, 28), 16,
                new Color(0.8f, 0.8f, 0.8f)).text = "Modo (host)";

            // Toggle TDM/FFA
            BuildTdmFfaToggle(hostCtrlBg.transform);

            // Budget
            MakeLabel(hostCtrlBg.transform, new Vector2(-70, 80), new Vector2(120, 28), 15,
                new Color(0.7f, 0.7f, 0.7f)).text = "Budget:";
            var budgetMinus = MakeBtn(hostCtrlBg.transform, new Vector2(0.5f, 0.5f), new Vector2(-30, 50), new Vector2(36, 36));
            MakeLabel(budgetMinus.transform, Vector2.zero, new Vector2(36, 36), 20, Color.white).text = "-";
            _budgetLabel = MakeLabel(hostCtrlBg.transform, new Vector2(20, 50), new Vector2(50, 36), 18, Color.white);
            _budgetLabel.text = "30";
            var budgetPlus = MakeBtn(hostCtrlBg.transform, new Vector2(0.5f, 0.5f), new Vector2(65, 50), new Vector2(36, 36));
            MakeLabel(budgetPlus.transform, Vector2.zero, new Vector2(36, 36), 20, Color.white).text = "+";
            budgetMinus.onClick.AddListener(() => { _budgetValue = Mathf.Max(10, _budgetValue - 5); _budgetLabel.text = _budgetValue.ToString(); SyncConfig(); });
            budgetPlus.onClick.AddListener(() => { _budgetValue = Mathf.Min(100, _budgetValue + 5); _budgetLabel.text = _budgetValue.ToString(); SyncConfig(); });

            // Planning time
            MakeLabel(hostCtrlBg.transform, new Vector2(-60, 0), new Vector2(140, 28), 15,
                new Color(0.7f, 0.7f, 0.7f)).text = "Planejamento:";
            var planMinus = MakeBtn(hostCtrlBg.transform, new Vector2(0.5f, 0.5f), new Vector2(-30, -30), new Vector2(36, 36));
            MakeLabel(planMinus.transform, Vector2.zero, new Vector2(36, 36), 20, Color.white).text = "-";
            _planningLabel = MakeLabel(hostCtrlBg.transform, new Vector2(20, -30), new Vector2(55, 36), 18, Color.white);
            _planningLabel.text = "15s";
            var planPlus = MakeBtn(hostCtrlBg.transform, new Vector2(0.5f, 0.5f), new Vector2(65, -30), new Vector2(36, 36));
            MakeLabel(planPlus.transform, Vector2.zero, new Vector2(36, 36), 20, Color.white).text = "+";
            planMinus.onClick.AddListener(() => { _planningValue = Mathf.Max(5f, _planningValue - 5f); _planningLabel.text = _planningValue + "s"; SyncConfig(); });
            planPlus.onClick.AddListener(() => { _planningValue = Mathf.Min(120f, _planningValue + 5f); _planningLabel.text = _planningValue + "s"; SyncConfig(); });

            // Avançar fase
            _advanceBtn = MakeBtn(hostCtrlBg.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -110), new Vector2(230, 55));
            UiSkin.ApplyButtonSkin(_advanceBtn.GetComponent<Image>(), new Color(0.15f, 0.40f, 0.20f));
            MakeLabel(_advanceBtn.transform, Vector2.zero, new Vector2(230, 55), 18, Color.white).text = "Iniciar (Criar Personagens)";
            _advanceBtn.onClick.AddListener(OnClickAdvancePhase);

            // ---- Waiting overlay (não-host) ----------------------------------
            _waitingOverlay = MakeFullPanel(_mpRoomPanel.transform, "WaitingOverlay");
            _waitingOverlay.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);
            _waitingLabel = MakeLabel(_waitingOverlay.transform, Vector2.zero, new Vector2(700, 80), 32,
                new Color(0.9f, 0.9f, 0.5f));
            _waitingLabel.text = "Aguardando o host...";
            _waitingOverlay.SetActive(false);

            // ---- Botão sair da sala ------------------------------------------
            var btnLeave = MakeBtn(_mpRoomPanel.transform, new Vector2(0.5f, 0f),
                new Vector2(0, 50), new Vector2(200, 50));
            btnLeave.GetComponent<Image>().color = new Color(0.45f, 0.15f, 0.15f);
            MakeLabel(btnLeave.transform, Vector2.zero, new Vector2(200, 50), 20, Color.white).text = "Sair da Sala";
            btnLeave.onClick.AddListener(OnClickLeaveRoom);
        }

        private void BuildTdmFfaToggle(Transform parent)
        {
            var row = new GameObject("TdmFfaRow", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var rowRt = row.GetComponent<RectTransform>();
            rowRt.anchorMin = rowRt.anchorMax = rowRt.pivot = new Vector2(0.5f, 0.5f);
            rowRt.anchoredPosition = new Vector2(0, 125);
            rowRt.sizeDelta = new Vector2(240, 34);

            MakeLabel(row.transform, new Vector2(-70, 0), new Vector2(80, 32), 16, Color.white).text = "TDM";

            _tdmFfaToggle = row.AddComponent<Toggle>();
            var bg = new GameObject("BG", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(row.transform, false);
            var bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = bgRt.anchorMax = bgRt.pivot = new Vector2(0.5f, 0.5f);
            bgRt.anchoredPosition = new Vector2(0, 0);
            bgRt.sizeDelta = new Vector2(44, 24);
            bg.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.35f);
            var check = new GameObject("Check", typeof(RectTransform), typeof(Image));
            check.transform.SetParent(bg.transform, false);
            var checkRt = check.GetComponent<RectTransform>();
            checkRt.anchorMin = checkRt.anchorMax = checkRt.pivot = new Vector2(0f, 0.5f);
            checkRt.anchoredPosition = new Vector2(2, 0);
            checkRt.sizeDelta = new Vector2(20, 20);
            check.GetComponent<Image>().color = new Color(0.4f, 0.7f, 1f);
            _tdmFfaToggle.targetGraphic = bg.GetComponent<Image>();
            _tdmFfaToggle.graphic = check.GetComponent<Image>();

            MakeLabel(row.transform, new Vector2(75, 0), new Vector2(80, 32), 16, Color.white).text = "FFA";

            _tdmFfaToggle.onValueChanged.AddListener(isOn =>
            {
                SyncConfig();
            });
        }

        // =========================================================================
        // Entrada na sala: vincular ao RoomManager
        // =========================================================================

        public void EnterRoom(string joinCode)
        {
            _inRoom = true;
            _joinCodeDisplay.text = joinCode;
            _mpLobbyPanel.SetActive(false);
            _mpRoomPanel.SetActive(true);

            // Controles do host visíveis só pro host
            bool isHost = RuntimeMultiplayerSession.IsHost;
            // O hostCtrlBg foi criado como filho direto do _mpRoomPanel — achamos pelo nome
            var hostCtrl = _mpRoomPanel.transform.Find("HostCtrl");
            if (hostCtrl != null) hostCtrl.gameObject.SetActive(isHost);
            if (_advanceBtn != null) _advanceBtn.interactable = isHost;

            // Assinar eventos do RoomManager. No CLIENTE, o RoomManager é replicado alguns
            // frames APÓS conectar — tentamos já e, se ainda não existir, uma corrotina fica
            // tentando até ele aparecer (senão a lista de jogadores e o chat nunca populam).
            _boundRoom = false;
            BindRoomManager();
            if (!_boundRoom) StartCoroutine(BindWhenReady());
        }

        private bool _boundRoom;

        private void BindRoomManager()
        {
            if (_boundRoom || RoomManager.Instance == null) return;
            RoomManager.Instance.Slots.OnListChanged += OnSlotsChanged;
            RoomManager.Instance.OnPhaseChanged += OnPhaseChanged;
            RoomManager.Instance.OnChatMessage += AppendChatMessage;
            _boundRoom = true;
            RebuildPlayerList();
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
                RoomManager.Instance.OnChatMessage -= AppendChatMessage;
            }
            _boundRoom = false;
        }

        // =========================================================================
        // Callbacks de rede
        // =========================================================================

        private void OnSlotsChanged(NetworkListEvent<PlayerSlot> evt)
        {
            RebuildPlayerList();
        }

        private void OnPhaseChanged(RoomPhase phase)
        {
            _phaseLabel.text = "Fase: " + phase.ToString();

            bool isHost = RuntimeMultiplayerSession.IsHost;
            bool isLobby = phase == RoomPhase.Lobby;

            if (_advanceBtn != null) _advanceBtn.interactable = isHost && isLobby;

            // Não-host: mostrar overlay de "aguardando" quando fase muda além do lobby
            if (!isHost && phase != RoomPhase.Lobby)
            {
                _waitingOverlay.SetActive(true);
                _waitingLabel.text = $"Aguardando o host...\nFase: {phase}";
            }
            else
            {
                _waitingOverlay.SetActive(false);
            }
        }

        private void AppendChatMessage(string senderName, string msg)
        {
            if (_chatScroll == null || _chatScroll.content == null) return;

            var lineGo = new GameObject("ChatLine", typeof(RectTransform));
            lineGo.transform.SetParent(_chatScroll.content, false);
            var txt = lineGo.AddComponent<Text>();
            txt.font = _font;
            txt.fontSize = 15;
            txt.color = Color.white;
            txt.supportRichText = false;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            var le = lineGo.AddComponent<LayoutElement>();
            le.preferredHeight = 22;
            txt.text = $"[{senderName}] {msg}";

            // Auto-scroll para o fim
            StartCoroutine(ScrollChatToBottom());
        }

        private IEnumerator ScrollChatToBottom()
        {
            yield return null; // aguarda layout rebuild
            _chatScroll.normalizedPosition = new Vector2(0, 0);
        }

        private void RebuildPlayerList()
        {
            if (_playerListContent == null) return;

            // Limpar lista
            foreach (Transform child in _playerListContent)
                Destroy(child.gameObject);

            if (RoomManager.Instance == null) return;

            bool isHost = RuntimeMultiplayerSession.IsHost;
            var slots = RoomManager.Instance.Slots;

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                var row = new GameObject("PlayerRow", typeof(RectTransform));
                row.transform.SetParent(_playerListContent, false);
                var rowRt = row.GetComponent<RectTransform>();
                rowRt.sizeDelta = new Vector2(320, 44);
                row.AddComponent<LayoutElement>().preferredHeight = 44;

                var bg = row.AddComponent<Image>();
                bg.color = i % 2 == 0 ? new Color(0.08f, 0.09f, 0.13f) : new Color(0.10f, 0.11f, 0.15f);

                // Nome
                var nameLbl = MakeLabel(row.transform, new Vector2(-70, 0), new Vector2(140, 38), 15, Color.white);
                nameLbl.text = slot.PlayerName.ToString();
                nameLbl.alignment = TextAnchor.MiddleLeft;

                // Time
                string teamStr = slot.Team == 0 ? "Time A" : (slot.Team == 1 ? "Time B" : $"T{slot.Team}");
                var teamLbl = MakeLabel(row.transform, new Vector2(60, 0), new Vector2(80, 38), 14,
                    slot.Team == 0 ? new Color(0.4f, 0.7f, 1f) : new Color(1f, 0.5f, 0.4f));
                teamLbl.text = teamStr;

                // Botão mudar time (só host, só TDM)
                if (isHost && RoomManager.Instance.CurrentConfig.GameMode == 0)
                {
                    var slotCapture = slot;
                    int iCapture = i;
                    var btnTeam = MakeBtn(row.transform, new Vector2(0.5f, 0.5f), new Vector2(130, 0), new Vector2(60, 34));
                    btnTeam.GetComponent<Image>().color = new Color(0.22f, 0.22f, 0.30f);
                    MakeLabel(btnTeam.transform, Vector2.zero, new Vector2(60, 34), 12, Color.white).text = "Troca";
                    btnTeam.onClick.AddListener(() =>
                    {
                        int newTeam = (slotCapture.Team == 0) ? 1 : 0;
                        RoomManager.Instance?.SetTeamServerRpc(slotCapture.ClientId, newTeam);
                    });
                }
            }
        }

        private void SyncConfig()
        {
            if (!RuntimeMultiplayerSession.IsHost) return;
            if (RoomManager.Instance == null) return;

            var cfg = new RoomConfigNet
            {
                GameMode = _tdmFfaToggle != null && _tdmFfaToggle.isOn ? 1 : 0,
                AttributeBudget = _budgetValue,
                PlanningTime = _planningValue,
                MaxPlayers = 4
            };
            RoomManager.Instance.SetConfigServerRpc(cfg);
        }

        // =========================================================================
        // Handlers de botão
        // =========================================================================

        private async void OnClickCreateRoom()
        {
            SetLobbyStatus("Criando sala...");
            try
            {
                string playerName = _nameInput.text.Trim();
                if (string.IsNullOrEmpty(playerName)) playerName = "Jogador";
                string roomName = _roomNameInput.text.Trim();
                if (string.IsNullOrEmpty(roomName)) roomName = "Sala";

                var bootstrap = NetBootstrap.EnsureExists();
                bootstrap.useLoopback = _loopbackToggle != null && _loopbackToggle.isOn;

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

                    // Criar lobby UGS (porta de entrada)
                    var (lobbyCode, err) = await LobbyService.CreateLobbyAsync(roomName, 4, joinCode);
                    if (err != null) Debug.LogWarning($"[RoomHUD] Lobby criado com aviso: {err}");
                }

                RuntimeMultiplayerSession.PlayerName = playerName;
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
            string code = _joinCodeInput.text.Trim().ToUpper();
            if (string.IsNullOrEmpty(code)) { SetLobbyStatus("Digite o código."); return; }

            SetLobbyStatus("Conectando...");
            try
            {
                string playerName = _nameInput.text.Trim();
                if (string.IsNullOrEmpty(playerName)) playerName = "Jogador";
                RuntimeMultiplayerSession.PlayerName = playerName;

                var bootstrap = NetBootstrap.EnsureExists();
                bootstrap.useLoopback = _loopbackToggle != null && _loopbackToggle.isOn;

                if (bootstrap.useLoopback)
                {
                    bootstrap.JoinLoopback();
                }
                else
                {
                    await bootstrap.InitUgsAsync(playerName);
                    await bootstrap.JoinRelayAsync(code);
                }

                SetLobbyStatus("Conectado!");
                EnterRoom(code);

                // Enviar nome ao RoomManager assim que conectar
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
            // Aguarda RoomManager estar disponível (spawn do host pode demorar 1-2 frames)
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
                RoomManager.Instance.OnChatMessage -= AppendChatMessage;
            }
            _boundRoom = false;
            NetBootstrap.Instance?.Shutdown();
            _ = LobbyService.LeaveAsync();
            _mpRoomPanel.SetActive(false);
            _mpLobbyPanel.SetActive(true);
            SetLobbyStatus("Saiu da sala.");
        }

        private void OnClickAdvancePhase()
        {
            RoomManager.Instance?.AdvancePhaseServerRpc();
        }

        private void OnChatSubmit(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            RoomManager.Instance?.SendChatServerRpc(text);
            _chatInput.text = "";
            _chatInput.ActivateInputField();
        }

        private void SetLobbyStatus(string msg)
        {
            if (_lobbyStatusText != null) _lobbyStatusText.text = msg;
        }

        // =========================================================================
        // Helpers construtores (padrão MainMenuManager)
        // =========================================================================

        private GameObject MakeFullPanel(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return go;
        }

        private Button MakeBtn(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject("Btn", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            go.AddComponent<Image>().color = new Color(0.25f, 0.25f, 0.30f);
            return go.AddComponent<Button>();
        }

        private Text MakeLabel(Transform parent, Vector2 pos, Vector2 size, int fontSize, Color color)
        {
            var go = new GameObject("Lbl", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = go.AddComponent<Text>();
            t.font = _font;
            t.fontSize = fontSize;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = color;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private InputField MakeInputField(Transform parent, Vector2 pos, Vector2 size, string initial)
        {
            var go = new GameObject("InputField", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            go.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.20f);
            var input = go.AddComponent<InputField>();

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10, 0); trt.offsetMax = new Vector2(-10, 0);
            var txt = textGo.AddComponent<Text>();
            txt.font = _font; txt.fontSize = 18; txt.alignment = TextAnchor.MiddleLeft;
            txt.color = Color.white; txt.supportRichText = false;

            input.textComponent = txt;
            input.text = initial;
            return input;
        }
    }
}
