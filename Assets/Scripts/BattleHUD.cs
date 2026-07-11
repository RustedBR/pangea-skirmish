// BattleHUD.cs — versão UI Toolkit (PangeaScreen)
// Migrado de UGUI (GameObjects/RectTransform) para UI Toolkit.
// Mantém 100% da API pública original (LogAction, SetWaitingText, Bind*, etc.)
// para não quebrar os ~40 callers (RoundManager, PlanningController, PlacementSync...).
// A lógica de negócio é idêntica; só a camada de apresentação mudou.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using PangeaSkirmish.UI;

namespace PangeaSkirmish
{
    public struct LogEntry
    {
        public string displayText;
        public string tooltipText;
        public Team   team;
        public Unit   unit; // unidade associada (câmera + inspect ao clicar)
    }

    /// <summary>
    /// UI do combate — tema "Final Fantasy Tactics" (janelas azuis com moldura clara).
    /// Topo (fase/timer), esquerda (histórico), inferior esq. (status), centro inf.
    /// (fila de ações), inferior dir. (menu de comandos cascata).
    /// </summary>
    public class BattleHUD : PangeaScreen
    {
        protected override string UxmlResource => "UI/Screens/BattleHUD";

        private const float HUNDO = 38f;

        // ── Referências UI Toolkit ──────────
        private Label  _phaseText, _timerText, _cameraModeText;
        private ScrollView _logScroll;
        private VisualElement _logContent, _tooltipGo;
        private Label  _tooltipTxt;
        private VisualElement _statusContent, _portraitImg, _hpFill, _hpBarBg;
        private Label  _unitNameText, _unitHPText, _apText, _unitStatsText, _unitHintText;
        private VisualElement _commandMenu, _breadcrumbBar, _breadcrumbTextVE;
        private Label  _breadcrumbText;
        private VisualElement _mainMenuPanel, _actionsMenuPanel, _attackMenuPanel,
                              _magicMenuPanel, _spellTypeMenuPanel, _manaStepperPanel, _bonusMenuPanel;
 private VisualElement _reactionMenu, _reactionButtons;
 private Label _reactionTimer, _reactionText;
        private VisualElement _seqBar, _seqContent;
        private VisualElement _promptPanel, _endPanel, _endWin;
        private Label  _promptText, _bonusTimerText, _endText, _manaValueText, _manaPotencyText, _manaRangeValueText, _manaPowerValueText, _manaPowerCostLabel, _manaRangeCostLabel;
        private Button _confirmButton, _moveButton, _attackUnitButton, _attackTileButton,
                       _magicButton, _concentrateButton, _incrementButton, _aimButton, _miraMagiaButton,
                       _undoButton, _clearButton, _powerStrikeButton, _quickStepButton,
                       _spellSelfButton, _spellUnitButton, _spellTileButton,
                       _manaRangeMinusButton, _manaRangePlusButton, _manaPowerMinusButton, _manaPowerPlusButton,
                       _manaCastButton, _manaBackButton,
                       _simButton, _naoButton, _endMenuButton;
        private VisualElement _mpWaitingOverlay;
        private Label  _mpWaitingText;

        // ── Tooltip de botões (hover > 3s) ──
        private IVisualElementScheduledItem _pendingTip;
        private VisualElement _tipOwner;

        // ── Dados do log (preservados p/ recolhimento por round) ──
        private readonly List<LogEntry> _logEntries = new List<LogEntry>();
        private sealed class LogLine
        {
            public VisualElement ve;
            public int   entryIdx;
            public bool  isHeader;
            public int   sectionIdx;
            public VisualElement hoverBg;
            public Unit  unit;
        }
        private readonly List<LogLine> _logLines = new List<LogLine>();
        private sealed class LogSection
        {
            public bool expanded = true;
            public Label chevron;
            public readonly List<int> lineIndices = new List<int>();
        }
        private readonly List<LogSection> _sections = new List<LogSection>();
        public Action<Unit> OnLogLineClicked;
        public Action<int> OnSeqChipToggled;
        public Action OnUndoClicked;
        public Action OnClearClicked;

        private int _hoveredLineIdx = -1;
        private Unit _inspectedUnit;

        // ── Menu cascata ──
        private Stack<VisualElement> _menuStack = new Stack<VisualElement>();
        private readonly List<Button> _magicElementButtons = new List<Button>();
        private readonly List<VisualElement> _magicElementImages = new List<VisualElement>();
        private readonly List<Label> _magicElementLabels = new List<Label>();
        private System.Action<SpellElement> _magicElementClick;
        private readonly List<VisualElement> _seqChips = new List<VisualElement>();
        private readonly List<VisualElement> _seqChipImages = new List<VisualElement>();
        private readonly List<Color> _seqChipBaseColors = new List<Color>();
        private int _selectedSeqIndex = -1;

        // ── Cores (do GameTuning) ──
        private static Color CorNormal     => Tuning.Get().hudButtonNormalColor;
        private static Color CorAtivo      => Tuning.Get().hudButtonActiveColor;
        private static Color CorConfirmado => Tuning.Get().hudButtonConfirmedColor;
        private static Color CorDisabled   => Tuning.Get().hudButtonDisabledColor;
        private static readonly Color CorConfirmBtn = new Color(0.14f, 0.42f, 0.22f);
        private static Color CorPlayer     => Tuning.Get().logPlayerColor;
        private static Color CorEnemy      => Tuning.Get().logEnemyColor;
        private static Color CorSystem     => Tuning.Get().logSystemColor;
        private static Color CorHover      => Tuning.Get().logHoverColor;
        private static readonly Color CorRoundHdr = new Color(0.10f, 0.16f, 0.34f, 1.00f);
        private static Color CorChipMove   => Tuning.Get().seqChipMoveColor;
        private static Color CorChipAtk    => Tuning.Get().seqChipAttackColor;
        private static readonly Color CorChipBonus = new Color(1.00f, 0.95f, 0.45f, 1.00f);
        private static Color CorChipMoveBn => Tuning.Get().seqChipMoveBonusColor;
        private static Color CorChipAtkBn  => Tuning.Get().seqChipAttackBonusColor;
        private static readonly Color CorFFTTitBg = new Color(0.16f, 0.24f, 0.46f, 1.00f);
        private static readonly Color CorFFTTitulo = new Color(0.74f, 0.85f, 1.00f);
        private static readonly Color CorFFTBorda = new Color(0.47f, 0.63f, 0.86f);

        // ============================================================
        // API pública — mantida idêntica à versão UGUI
        // ============================================================
        // Build(Canvas) mantido por compatibilidade: o PangeaScreen já carrega o UXML
        // no Awake/OnEnable. O canvas UGUI não é mais necessário (UIDocument full-screen).
        [Obsolete("Migrado para UI Toolkit: o PangeaScreen carrega o UXML automaticamente.")]
        public void Build(Canvas canvas) { }

        protected override void Bind()
        {
            var r = Root;
            _phaseText        = r.Q<Label>("phase-text");
            _timerText        = r.Q<Label>("timer-text");
            _cameraModeText   = r.Q<Label>("camera-mode-text");
            _logScroll        = r.Q<ScrollView>("log-scroll");
            _logContent       = r.Q<VisualElement>("log-content");
            _tooltipGo        = r.Q<VisualElement>("log-tooltip");
            _tooltipTxt       = r.Q<Label>("tooltip-txt");
            _statusContent    = r.Q<VisualElement>("status-content");
            _portraitImg      = r.Q<VisualElement>("portrait-img");
            _hpFill           = r.Q<VisualElement>("hp-fill");
            _hpBarBg          = r.Q<VisualElement>("hp-bg");
            _unitNameText     = r.Q<Label>("unit-name");
            _unitHPText       = r.Q<Label>("hp-text");
            _apText           = r.Q<Label>("ap-text");
            _unitStatsText    = r.Q<Label>("stats-text");
            _unitHintText     = r.Q<Label>("hint-text");
            _commandMenu      = r.Q<VisualElement>("command-menu");
            _breadcrumbBar    = r.Q<VisualElement>("breadcrumb");
            _breadcrumbText   = r.Q<Label>("breadcrumb");
            _mainMenuPanel    = r.Q<VisualElement>("main-menu");
            _actionsMenuPanel = r.Q<VisualElement>("actions-menu");
            _attackMenuPanel  = r.Q<VisualElement>("attack-menu");
            _magicMenuPanel   = r.Q<VisualElement>("magic-menu");
            _spellTypeMenuPanel = r.Q<VisualElement>("spelltype-menu");
            _manaStepperPanel = r.Q<VisualElement>("mana-menu");
            _bonusMenuPanel   = r.Q<VisualElement>("bonus-menu");
            _seqBar           = r.Q<VisualElement>("seq-bar");
            _seqContent       = r.Q<VisualElement>("seq-content");
            _promptPanel      = r.Q<VisualElement>("prompt-panel");
            _endPanel         = r.Q<VisualElement>("end-panel");
            _endWin           = r.Q<VisualElement>("end-win");
            _promptText       = r.Q<Label>("prompt-text");
            _bonusTimerText   = r.Q<Label>("bonus-timer");
            _endText          = r.Q<Label>("end-text");
            _manaValueText    = r.Q<Label>("mana-value");
            _manaPotencyText  = r.Q<Label>("mana-potency");
            _manaRangeValueText = r.Q<Label>("mana-range-value");
            _manaPowerValueText = r.Q<Label>("mana-power-value");
            _manaPowerCostLabel = r.Q<Label>("mana-power-cost-label");
            _manaRangeCostLabel = r.Q<Label>("mana-range-cost-label");
            _mpWaitingOverlay = r.Q<VisualElement>("mp-waiting");
            _mpWaitingText    = r.Q<Label>("mp-waiting-text");
            _reactionMenu     = r.Q<VisualElement>("reaction-menu");
            _reactionButtons  = r.Q<VisualElement>("reaction-buttons");
            _reactionTimer    = r.Q<Label>("reaction-timer");
            _reactionText     = r.Q<Label>("reaction-text");

            // Botões (via name)
            _confirmButton    = r.Q<Button>("btn-confirm");
            _moveButton       = r.Q<Button>("btn-move");
            _attackUnitButton = r.Q<Button>("btn-attack-unit");
            _attackTileButton = r.Q<Button>("btn-attack-tile");
            _magicButton      = r.Q<Button>("btn-magic");
            _concentrateButton= r.Q<Button>("btn-concentrate");
            _incrementButton  = r.Q<Button>("btn-increment");
            _aimButton        = r.Q<Button>("btn-aim");
            _miraMagiaButton  = r.Q<Button>("btn-mira-magia");
            _undoButton       = r.Q<Button>("btn-undo");
            _clearButton      = r.Q<Button>("btn-clear");
            _spellSelfButton  = r.Q<Button>("btn-spell-self");
            _spellUnitButton  = r.Q<Button>("btn-spell-unit");
            _spellTileButton  = r.Q<Button>("btn-spell-tile");
            _manaRangeMinusButton = r.Q<Button>("mana-range-minus");
            _manaRangePlusButton  = r.Q<Button>("mana-range-plus");
            _manaPowerMinusButton = r.Q<Button>("mana-power-minus");
            _manaPowerPlusButton  = r.Q<Button>("mana-power-plus");
            _manaCastButton   = r.Q<Button>("mana-cast");
            _manaBackButton   = r.Q<Button>("mana-back");
            _simButton        = r.Q<Button>("btn-sim");
            _naoButton        = r.Q<Button>("btn-nao");
            _endMenuButton    = r.Q<Button>("btn-end-menu");

            // Magic elements (6 botões, ordem: Physical, Magic, Fire, Water, Air, Earth)
            _magicElementButtons.Add(r.Q<Button>("btn-elem-physical"));
            _magicElementButtons.Add(r.Q<Button>("btn-elem-magic"));
            _magicElementButtons.Add(r.Q<Button>("btn-elem-fire"));
            _magicElementButtons.Add(r.Q<Button>("btn-elem-water"));
            _magicElementButtons.Add(r.Q<Button>("btn-elem-air"));
            _magicElementButtons.Add(r.Q<Button>("btn-elem-earth"));
            for (int i = 0; i < _magicElementButtons.Count; i++)
            {
                int captured = i;
                _magicElementButtons[i].RegisterCallback<ClickEvent>(_ =>
                {
                    AudioManager.I?.Play(AudioManager.I.sfxUIClick);
                    OnMagicElementClick((SpellElement)(captured + 1));
                });
            }

            // ── Tooltips de botões (hover > 3s) ──
            AttachMainButtonTooltips();

            // Callbacks dos botões principais
            r.Q<Button>("btn-actions").RegisterCallback<ClickEvent>(_ =>
            { AudioManager.I?.Play(AudioManager.I.sfxUIClick); ShowSubMenu(_actionsMenuPanel, "Ações"); });
            r.Q<Button>("btn-bonus").RegisterCallback<ClickEvent>(_ =>
            { AudioManager.I?.Play(AudioManager.I.sfxUIClick); ShowSubMenu(_bonusMenuPanel, "Bônus"); });
            r.Q<Button>("btn-magic").RegisterCallback<ClickEvent>(_ =>
            { AudioManager.I?.Play(AudioManager.I.sfxUIClick); ShowSubMenu(_magicMenuPanel, "Magia"); });
            r.Q<Button>("btn-attack").RegisterCallback<ClickEvent>(_ =>
            { AudioManager.I?.Play(AudioManager.I.sfxUIClick); ShowSubMenu(_attackMenuPanel, "Ações > Atacar"); });
            _endMenuButton.RegisterCallback<ClickEvent>(_ => SceneManager.LoadScene("MainMenu"));

            // Estado inicial dos submenus
            ShowMainMenu();
            _commandMenu.style.display = DisplayStyle.None;
            _seqBar.style.display = DisplayStyle.None;
            _promptPanel.style.display = DisplayStyle.None;
            _endPanel.style.display = DisplayStyle.None;
            _tooltipGo.style.display = DisplayStyle.None;
            _mpWaitingOverlay.style.display = DisplayStyle.None;
            SetInfoVisible(false);
            if (_reactionMenu != null) _reactionMenu.style.display = DisplayStyle.None;

            // Tooltip no indicador de câmera
            r.Q<VisualElement>("camera-mode").RegisterCallback<PointerEnterEvent>(_ => ShowCameraTooltip());
            r.Q<VisualElement>("camera-mode").RegisterCallback<PointerLeaveEvent>(_ => HideCameraTooltip());
        }

        // ── INDICADOR DE MODO CÂMERA ──
        public void SetCameraMode(CameraMode mode)
        {
            if (_cameraModeText == null) return;
            bool isAuto = mode == CameraMode.Auto;
            _cameraModeText.text = isAuto ? "Auto" : "Manual";
            var T = Tuning.Get();
            _cameraModeText.style.color = isAuto ? T.cameraAutoColor : T.cameraManualColor;
        }
        private void ShowCameraTooltip()
        {
            if (_tooltipGo == null || _tooltipTxt == null) return;
            _tooltipGo.BringToFront();
            _tooltipGo.style.display = DisplayStyle.Flex;
            _tooltipTxt.text = "Câmera automática segue as ações.\n" +
                               $"Arrastar/zoom ativa modo Manual ({Tuning.Get().camManualTimeout:0.#}s).";
        }
        private void HideCameraTooltip() { if (_tooltipGo != null) _tooltipGo.style.display = DisplayStyle.None; }

        // ── STATUS DA UNIDADE ──
        private void SetInfoVisible(bool visible)
        {
            if (_statusContent == null) return;
            _portraitImg.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _unitNameText.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _hpBarBg.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _unitHPText.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _apText.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _unitStatsText.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _unitHintText.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ── FILA DE AÇÕES ──
        public void SetActionSequence(List<ActionType> seq, List<ScheduledAction> scheduled = null)
        {
            foreach (var c in _seqChips) if (c != null) c.RemoveFromHierarchy();
            _seqChips.Clear();
            _seqChipImages.Clear();
            _seqChipBaseColors.Clear();

            if (seq == null || seq.Count == 0)
            {
                _selectedSeqIndex = -1;
                if (_seqBar != null) _seqBar.style.display = DisplayStyle.None;
                return;
            }
            if (_selectedSeqIndex >= seq.Count) _selectedSeqIndex = -1;

            _seqBar.style.display = DisplayStyle.Flex;
            for (int i = 0; i < seq.Count; i++)
            {
                int idx = i;
                bool isAtk = seq[i] == ActionType.Attack;
                bool isSpell = seq[i] == ActionType.Spell;
                bool isConc = seq[i] == ActionType.Concentrate;
                bool isBonus = scheduled != null && i < scheduled.Count && scheduled[i].IsBonus;
                Color baseCol;
                if (isSpell) baseCol = Tuning.Get().seqChipSpellColor;
                else if (isConc) baseCol = Tuning.Get().seqChipConcentrateColor;
                else baseCol = isBonus ? (isAtk ? CorChipAtkBn : CorChipMoveBn) : (isAtk ? CorChipAtk : CorChipMove);

                var chip = new VisualElement();
                chip.AddToClassList("bh-seq-chip");
                chip.style.backgroundColor = baseCol;
                chip.style.width = 90; chip.style.height = 24; chip.style.marginRight = 4;
                var t = new Label($"{(isBonus ? "◆ " : "")}{i + 1}  {(isSpell ? "Magia" : isAtk ? "Atacar" : isConc ? "Concentrar" : "Mover")}");
                t.style.color = Color.white; t.style.unityFontStyleAndWeight = FontStyle.Bold; t.style.fontSize = 12;
                t.style.unityTextAlign = TextAnchor.MiddleCenter;
                chip.Add(t);
                chip.RegisterCallback<ClickEvent>(_ => OnSeqChipClicked(idx));

                _seqContent.Add(chip);
                _seqChips.Add(chip);
                _seqChipImages.Add(chip);
                _seqChipBaseColors.Add(baseCol);
            }
            RefreshSeqChipHighlight();
        }
        public void HideActionSequence()
        {
            foreach (var c in _seqChips) if (c != null) c.RemoveFromHierarchy();
            _seqChips.Clear(); _seqChipImages.Clear(); _seqChipBaseColors.Clear();
            _selectedSeqIndex = -1;
            if (_seqBar != null) _seqBar.style.display = DisplayStyle.None;
        }
        private void OnSeqChipClicked(int index)
        {
            if (index < 0 || index >= _seqChips.Count) return;
            AudioManager.I?.Play(AudioManager.I.sfxUIClick);
            if (OnSeqChipToggled != null) OnSeqChipToggled.Invoke(index);
            _selectedSeqIndex = (_selectedSeqIndex == index) ? -1 : index;
            RefreshSeqChipHighlight();
        }
        private void RefreshSeqChipHighlight()
        {
            var T = Tuning.Get();
            bool skinned = T.uiButtonSkinEnabled;
            for (int i = 0; i < _seqChipImages.Count; i++)
            {
                if (_seqChipImages[i] == null) continue;
                Color resting = skinned ? Color.Lerp(_seqChipBaseColors[i], Color.white, T.uiButtonFrameTintLerp) : _seqChipBaseColors[i];
                _seqChipImages[i].style.backgroundColor = (i == _selectedSeqIndex)
                    ? Color.Lerp(resting, Color.white, T.seqChipSelectedLighten) : resting;
            }
        }
        public int SelectedSeqIndex => _selectedSeqIndex;
        public void SelectSeqIndex(int idx) => _selectedSeqIndex = idx;

        // ── BIND DE AÇÕES (preservado) ──
        public void BindConfirm(Action a) { BindButton(_confirmButton, a); }
        public void BindMove(Action a)    { BindButton(_moveButton, a); }
        public void BindAttackUnit(Action a)  { BindButton(_attackUnitButton, a); }
        public void BindAttackTile(Action a)  { BindButton(_attackTileButton, a); }
        public void BindPowerStrike(Action a) { BindButton(_powerStrikeButton, a); }
        public void BindQuickStep(Action a)   { BindButton(_quickStepButton, a); }
        public void BindMagic(Action a)       { BindButton(_magicButton, a); }
        public void BindConcentrate(Action a) { BindButton(_concentrateButton, a); }
        public void BindIncrement(Action a)   { BindButton(_incrementButton, a); }
        public void BindAim(Action a)         { BindButton(_aimButton, a); }
        public void BindMiraMagia(Action a)   { BindButton(_miraMagiaButton, a); }
        public void BindUndo(Action a)        { BindButton(_undoButton, a); }
        public void BindClear(Action a)       { BindButton(_clearButton, a); }
        public void BindMagicElement(System.Action<SpellElement> a) { _magicElementClick = a; }
        public void BindSpellSelf(Action a) { BindButton(_spellSelfButton, a); }
        public void BindSpellUnit(Action a) { BindButton(_spellUnitButton, a); }
        public void BindSpellTile(Action a) { BindButton(_spellTileButton, a); }
        public void BindManaRangeMinus(Action a) { BindButton(_manaRangeMinusButton, a); }
        public void BindManaRangePlus(Action a)  { BindButton(_manaRangePlusButton, a); }
        public void BindManaPowerMinus(Action a) { BindButton(_manaPowerMinusButton, a); }
        public void BindManaPowerPlus(Action a)  { BindButton(_manaPowerPlusButton, a); }
        public void BindManaCast(Action a)  { BindButton(_manaCastButton, a); }
        public void BindManaBack(Action a)  { BindButton(_manaBackButton, a); }
        public VisualElement SpellTypeMenuPanel => _spellTypeMenuPanel;
        public VisualElement ManaStepperPanel   => _manaStepperPanel;
        public VisualElement MagicMenuPanel => _magicMenuPanel;

        public void SetManaPreview(int manaRange, int manaPower, int max, int value, int range, bool isBuff)
        {
            bool isSelf = isBuff; // Self buff => o "alcance" é duração em rounds
            if (_manaValueText != null)     _manaValueText.text     = $"{manaRange + manaPower} / {max} MP";
            if (_manaRangeValueText != null) _manaRangeValueText.text = $"{manaRange}";
            if (_manaPowerValueText != null) _manaPowerValueText.text = $"{manaPower}";

            // Tabela 2x3 (só menu de conjurar magia): custo de mana por parte + total
            string rangeWord = isSelf ? "Duração" : "Alcance";
            if (_manaPowerCostLabel != null) _manaPowerCostLabel.text = $"Potência — mana: {manaPower}";
            if (_manaRangeCostLabel != null) _manaRangeCostLabel.text = $"{rangeWord} — mana: {manaRange}";

            string label = isBuff ? $"Buff: +{value}" : $"Dano: {value}";
            int paCost = 1 + manaPower;
            if (_manaPotencyText != null)
                _manaPotencyText.text = $"{paCost} PA ({manaRange} MP + {manaPower} PA)";

            // Botões +/- mostram só o símbolo (custo detalhado está no preview + tooltip).
            if (_manaRangePlusButton != null)  _manaRangePlusButton.text  = "+";
            if (_manaRangeMinusButton != null) _manaRangeMinusButton.text = "−";
            if (_manaPowerPlusButton != null)  _manaPowerPlusButton.text  = "+";
            if (_manaPowerMinusButton != null) _manaPowerMinusButton.text = "−";

            if (_manaRangeMinusButton != null) _manaRangeMinusButton.SetEnabled(manaRange > 0);
            if (_manaRangePlusButton != null)  _manaRangePlusButton.SetEnabled(manaRange + manaPower < max);
            if (_manaPowerMinusButton != null) _manaPowerMinusButton.SetEnabled(manaPower > 1);
            if (_manaPowerPlusButton != null)  _manaPowerPlusButton.SetEnabled(manaRange + manaPower < max);
        }

        private static readonly Dictionary<Button, Action> _btnHandlers = new Dictionary<Button, Action>();
        private static readonly Dictionary<Button, Action> _bonusHandlers = new Dictionary<Button, Action>();
        private static void BindButton(Button b, Action a)
        {
            if (b == null) return;
            if (_btnHandlers.TryGetValue(b, out var prev)) b.clicked -= prev;
            Action handler = () => { AudioManager.I?.Play(AudioManager.I.sfxUIClick); a?.Invoke(); };
            _btnHandlers[b] = handler;
            b.clicked += handler;
        }

        // ── NAVEGAÇÃO CASCATA ──
        public void ShowMainMenu()
        {
            _actionsMenuPanel.style.display = DisplayStyle.None;
            _attackMenuPanel.style.display = DisplayStyle.None;
            _magicMenuPanel.style.display = DisplayStyle.None;
            _spellTypeMenuPanel.style.display = DisplayStyle.None;
            _manaStepperPanel.style.display = DisplayStyle.None;
            _bonusMenuPanel.style.display = DisplayStyle.None;
            _mainMenuPanel.style.display = DisplayStyle.Flex;
            _menuStack.Clear();
            UpdateBreadcrumb("");
        }
        public void ShowSubMenu(VisualElement panel, string path)
        {
            foreach (var go in _menuStack)
                if (go.style.display == DisplayStyle.Flex) go.style.display = DisplayStyle.None;
            _mainMenuPanel.style.display = DisplayStyle.None;
            _menuStack.Push(panel);
            panel.style.display = DisplayStyle.Flex;
            UpdateBreadcrumb(path);
        }
        public void GoBack()
        {
            if (_menuStack.Count == 0) return;
            var current = _menuStack.Pop();
            current.style.display = DisplayStyle.None;
            if (_menuStack.Count > 0) _menuStack.Peek().style.display = DisplayStyle.Flex;
            else _mainMenuPanel.style.display = DisplayStyle.Flex;
            UpdateBreadcrumb(GetCurrentPath());
        }
        private string GetCurrentPath()
        {
            if (_menuStack.Count == 0) return "";
            var parts = new List<string>();
            foreach (var go in _menuStack)
            {
                if (go == _actionsMenuPanel) parts.Add("Ações");
                else if (go == _attackMenuPanel) parts.Add("Atacar");
                else if (go == _magicMenuPanel) parts.Add("Magia");
                else if (go == _spellTypeMenuPanel) parts.Add("Alvo");
                else if (go == _manaStepperPanel) parts.Add("Mana");
                else if (go == _bonusMenuPanel) parts.Add("Bônus");
            }
            parts.Reverse();
            return string.Join(" > ", parts);
        }
        private void UpdateBreadcrumb(string path)
        {
            if (_breadcrumbBar != null)
                _breadcrumbBar.style.display = string.IsNullOrEmpty(path) ? DisplayStyle.None : DisplayStyle.Flex;
            if (_breadcrumbText != null) _breadcrumbText.text = path;
        }
        public void OnMagicElementClick(SpellElement element) => _magicElementClick?.Invoke(element);

        // ── ESTADOS DE BOTÕES ──
        public void SetMoveState(bool canAdd, int count, bool isPicking)
        {
            bool hasAny = count > 0;
            _moveButton.SetEnabled(canAdd || hasAny || isPicking);
            _moveButton.style.backgroundColor = SkinTint(isPicking ? CorAtivo : hasAny ? CorConfirmado : canAdd ? CorNormal : CorDisabled);
            _moveButton.text = (isPicking ? "▶ " : "") + "1 - Mover" + (count > 0 ? $" x{count}" : "");
        }
        public void SetAttackUnitState(bool canAdd, int count, bool isPicking = false)
        {
            bool hasAny = count > 0;
            _attackUnitButton.SetEnabled(canAdd || hasAny || isPicking);
            _attackUnitButton.style.backgroundColor = SkinTint(isPicking ? CorAtivo : hasAny ? CorConfirmado : canAdd ? CorNormal : CorDisabled);
            _attackUnitButton.text = (isPicking ? "▶ " : "") + "1 - Atacar Unidade" + (count > 0 ? $" x{count}" : "");
        }
        public void SetAttackTileState(bool canAdd, int count, bool isPicking = false)
        {
            _attackTileButton.SetEnabled(canAdd || isPicking);
            _attackTileButton.style.backgroundColor = SkinTint(isPicking ? CorAtivo : canAdd ? CorNormal : CorDisabled);
            _attackTileButton.text = (isPicking ? "▶ " : "") + "2 - Atacar Tile";
        }
        public void SetMagicState(bool canAdd, bool isPicking = false)
        {
            for (int i = 0; i < _magicElementButtons.Count; i++)
            {
                _magicElementButtons[i].SetEnabled(canAdd || isPicking);
                if (isPicking) _magicElementButtons[i].style.backgroundColor = SkinTint(CorAtivo);
                else if (canAdd) _magicElementButtons[i].style.backgroundColor = SkinTint(SpellBook.ElementColor((SpellElement)(i + 1)));
                else _magicElementButtons[i].style.backgroundColor = SkinTint(CorDisabled);
            }
        }
        public void SetConcentrateState(bool canAdd, bool isPicking = false)
        {
            _concentrateButton.SetEnabled(canAdd || isPicking);
            _concentrateButton.style.backgroundColor = SkinTint(isPicking ? CorAtivo : canAdd ? CorNormal : CorDisabled);
            _concentrateButton.text = (isPicking ? "▶ " : "") + "1 - Concentrar" + (isPicking ? "..." : "");
        }
        public void SetIncrementState(bool canAdd, bool isPicking = false)
        {
            _incrementButton.SetEnabled(canAdd || isPicking);
            _incrementButton.style.backgroundColor = SkinTint(isPicking ? CorAtivo : canAdd ? CorNormal : CorDisabled);
            _incrementButton.text = (isPicking ? "▶ " : "") + "2 - Incremento" + (isPicking ? "..." : "");
        }
        public void SetAimState(bool canAdd, bool isPicking = false)
        {
            if (_aimButton == null) return;
            _aimButton.SetEnabled(canAdd || isPicking);
            _aimButton.style.backgroundColor = SkinTint(isPicking ? CorAtivo : canAdd ? CorNormal : CorDisabled);
            _aimButton.text = (isPicking ? "▶ " : "") + "3 - Mirar" + (isPicking ? "..." : "");
        }

        // ── VISIBILIDADE ──
        public void SetActionBarVisible(bool v, Unit u = null)
        {
            _commandMenu.style.display = v ? DisplayStyle.Flex : DisplayStyle.None;
            if (v) { ShowMainMenu(); if (u != null) UpdateAPDisplay(u); }
        }
        public void UpdateAPDisplay(Unit u)
        {
            _apText.text = $"<b>PA</b> {u.remainingAP}/{u.stats.ActionPoints}   " +
                           $"<b>PAB</b> {u.remainingBAP}/{u.stats.BonusActionPoints}";
        }

        // ── TEXTOS PÚBLICOS ──
        public void SetPhase(string s)          => _phaseText.text = s;
        public void SetTimer(float sec)         => _timerText.text = sec.ToString("0.0") + "s";
        public void SetTimerWarning(bool warn)  => _timerText.style.color = warn ? Tuning.Get().timerWarningColor : Tuning.Get().timerNormalColor;
        public void HideTimer()                 => _timerText.text = "";
        public void SetTimerVisible(bool v)     => _timerText.style.display = v ? DisplayStyle.Flex : DisplayStyle.None;
        public void SetConfirmVisible(bool v)   => _confirmButton.style.display = v ? DisplayStyle.Flex : DisplayStyle.None;
        public bool IsPromptVisible             => _promptPanel.style.display == DisplayStyle.Flex;

        // ── INFO DA UNIDADE ──
        public void ShowUnitInfo(Unit u) { _inspectedUnit = u; RefreshUnitInfo(); }
        public void RefreshUnitInfo() => ApplyUnitInfo(_inspectedUnit);
        private void ApplyUnitInfo(Unit u)
        {
            if (u == null) { SetInfoVisible(false); return; }
            SetInfoVisible(true);
            var s = u.stats;
            if (_portraitImg != null)
                _portraitImg.style.backgroundImage = new StyleBackground(u.CurrentSprite);
            string team = u.team == Team.Player
                ? $"<color=#{ColorUtility.ToHtmlStringRGB(CorPlayer)}>Aliado</color>"
                : $"<color=#{ColorUtility.ToHtmlStringRGB(CorEnemy)}>Inimigo</color>";
            _unitNameText.text = $"{u.unitName}  <size=11>{team}</size>";
            if (u.IsDead) _unitNameText.text += "  <color=#888>+</color>";
            float ratio = (float)u.currentHP / Mathf.Max(1, s.MaxHP);
            _hpFill.style.width = new Length(Mathf.Clamp01(ratio) * 100, LengthUnit.Percent);
            var Thp = Tuning.Get();
            _hpFill.style.backgroundColor = ratio > Thp.hpBarYellowThreshold ? Thp.hpBarHighColor
                                : ratio > Thp.hpBarRedThreshold    ? Thp.hpBarMidColor
                                                                   : Thp.hpBarLowColor;
            _unitHPText.text = $"HP  {u.currentHP} / {s.MaxHP}";
            _apText.text = $"<b>PA</b> {u.remainingAP}/{s.ActionPoints}   <b>PAB</b> {u.remainingBAP}/{s.BonusActionPoints}";
            var buffs = StatusEffectSystem.Summary(u.statusEffects);
            _unitStatsText.text =
                $"<b>STR</b> {s.STR:0.#}  <b>VIT</b> {s.VIT:0.#}  <b>DEX</b> {s.DEX:0.#}  <b>AGI</b> {s.AGI:0.#}\n" +
                $"<b>MOV</b> {s.MoveBudget}  <b>INI</b> {s.Initiative}  <b>ATQ</b> {s.PhysicalDamage}  <b>ALC</b> {s.AttackRange}\n" +
                $"<b>MANA</b> {u.currentMana}/{s.MaxMana}  <b>CONC</b> {u.plannedConcentrations}" +
                (string.IsNullOrEmpty(buffs) ? "" : $"\n<color=#FFD700><b>BUFFS</b> {buffs}</color>");
            _unitNameText.text = $"{u.unitName}  <size=11>{team}</size>  <color=#{ColorUtility.ToHtmlStringRGB(Tuning.Get().manaTextColor)}>MP {u.currentMana}</color>";
        }

        // ── LOG DE BATALHA ──
        public void LogAction(string line, Unit unit = null)
        {
            LogAction(new LogEntry { displayText = line, tooltipText = line,
                team = unit != null ? unit.team : Team.Player, unit = unit });
        }
        public void LogAction(LogEntry entry)
        {
            int idx = _logEntries.Count;
            _logEntries.Add(entry);
            AppendLineUI(entry, idx);
            if (_logScroll != null) _logScroll.verticalScroller.value = 0f;
            Debug.Log("[Battle] " + entry.tooltipText);
        }
        public void LogRound(int round)
        {
            if (_sections.Count > 0) AppendSpacerUI();
            int sIdx = _sections.Count;
            var section = new LogSection();
            _sections.Add(section);
            var hGo = new VisualElement();
            hGo.AddToClassList("bh-round-hdr");
            var t = new Label($"v  Round {round}");
            t.AddToClassList("bh-round-hdr-text");
            hGo.Add(t);
            section.chevron = t;
            hGo.RegisterCallback<ClickEvent>(_ => ToggleSection(sIdx));
            _logContent.Add(hGo);
            _logLines.Add(new LogLine { ve = hGo, entryIdx = -1, isHeader = true, sectionIdx = sIdx, hoverBg = null, unit = null });
            if (_logScroll != null) _logScroll.verticalScroller.value = 0f;
        }
        private void AppendSpacerUI()
        {
            var go = new VisualElement();
            go.style.height = 5;
            int sIdx = _sections.Count - 1;
            _logContent.Add(go);
            _logLines.Add(new LogLine { ve = go, entryIdx = -1, isHeader = false, sectionIdx = sIdx, hoverBg = null, unit = null });
            if (sIdx >= 0) _sections[sIdx].lineIndices.Add(_logLines.Count - 1);
        }
        private void AppendLineUI(LogEntry entry, int entryIdx)
        {
            int sIdx = _sections.Count - 1;
            var go = new VisualElement();
            go.AddToClassList("bh-log-line");
            var txt = new Label(entry.displayText);
            txt.AddToClassList("bh-log-line-text");
            txt.style.color = LogColor(entry.team);
            go.Add(txt);
            go.RegisterCallback<PointerEnterEvent>(_ => SetHoverLine(_logLines.Count));
            go.RegisterCallback<PointerLeaveEvent>(_ => SetHoverLine(-1));
            go.RegisterCallback<ClickEvent>(_ =>
            {
                var ll = _logLines[_logLines.FindIndex(x => x.ve == go)];
                if (ll.isHeader) ToggleSection(ll.sectionIdx);
                else if (ll.unit != null) OnLogLineClicked?.Invoke(ll.unit);
            });
            _logContent.Add(go);
            var ll = new LogLine { ve = go, entryIdx = entryIdx, isHeader = false, sectionIdx = sIdx, hoverBg = go, unit = entry.unit };
            _logLines.Add(ll);
            if (sIdx >= 0) _sections[sIdx].lineIndices.Add(_logLines.Count - 1);
        }
        private Color LogColor(Team team) => team switch
        {
            Team.Player => CorPlayer,
            Team.Enemy  => CorEnemy,
            _           => CorSystem,
        };
        public void Log(string line) => Debug.Log("[Battle] " + line);

        private void SetHoverLine(int newIdx)
        {
            if (_hoveredLineIdx == newIdx) return;
            if (_hoveredLineIdx >= 0 && _hoveredLineIdx < _logLines.Count && _logLines[_hoveredLineIdx].hoverBg != null)
                _logLines[_hoveredLineIdx].hoverBg.style.backgroundColor = Color.clear;
            _hoveredLineIdx = newIdx;
            if (newIdx >= 0 && newIdx < _logLines.Count && _logLines[newIdx].hoverBg != null)
                _logLines[newIdx].hoverBg.style.backgroundColor = CorHover;
        }
        private void ToggleSection(int sIdx)
        {
            if (sIdx < 0 || sIdx >= _sections.Count) return;
            var s = _sections[sIdx];
            s.expanded = !s.expanded;
            foreach (int li in s.lineIndices)
                if (li < _logLines.Count && _logLines[li].ve != null)
                    _logLines[li].ve.style.display = s.expanded ? DisplayStyle.Flex : DisplayStyle.None;
            if (s.chevron != null)
            {
                string t = s.chevron.text;
                if (t.Length > 0 && (t[0] == 'v' || t[0] == '>'))
                    s.chevron.text = (s.expanded ? "v" : ">") + t.Substring(1);
            }
        }

        // ── Tooltip ──
        private void ShowTooltip(int entryIdx)
        {
            if (entryIdx < 0 || entryIdx >= _logEntries.Count) return;
            string tip = _logEntries[entryIdx].tooltipText;
            if (string.IsNullOrEmpty(tip)) return;
            _tooltipGo.BringToFront();
            _tooltipTxt.text = tip;
            _tooltipGo.style.display = DisplayStyle.Flex;
        }
        private void HideTooltip() { if (_tooltipGo != null) _tooltipGo.style.display = DisplayStyle.None; }

        // ── Tooltip de botões (hover > 3s) ──
        private void AttachMainButtonTooltips()
        {
            // Comandos principais
            AttachButtonTooltip(_moveButton, "Move a unidade pelo mapa. Custa PA por tile de deslocamento.");
            AttachButtonTooltip(_attackUnitButton, "Ataque direto a uma unidade inimiga. Custa PA; dano vem de STR/DEX.");
            AttachButtonTooltip(_attackTileButton, "Ataque a um tile (chão/área). Custa PA.");
            AttachButtonTooltip(_magicButton, "Conjura magia de 1 dos 6 elementos. Custa PA + Mana.");
            AttachButtonTooltip(_concentrateButton, "Ação Bônus: acumula concentração (+dano em ataques futuros).");
            AttachButtonTooltip(_incrementButton, "Ação Bônus: +1 no próximo dano causado (incremento).");
            AttachButtonTooltip(_aimButton, "Ação Bônus: Mira tradicional (+precisão no próximo ataque).");
            AttachButtonTooltip(_miraMagiaButton, "Ação Bônus (1 PAB): +INT na potência da PRÓXIMA magia.");
            AttachButtonTooltip(_confirmButton, "Confirma o plano de ações da unidade.");
            AttachButtonTooltip(_undoButton, "Remove a última ação adicionada ao plano.");
            AttachButtonTooltip(_clearButton, "Remove TODAS as ações do plano.");

            // Alvo da magia
            AttachButtonTooltip(_spellSelfButton, "Buff em SI MESMO: +atributos do elemento por N rounds (ManaRange = rounds).");
            AttachButtonTooltip(_spellUnitButton, "Dano mágico a uma unidade inimiga. Potência = atributos × ManaPower.");
            AttachButtonTooltip(_spellTileButton, "Efeito no tile: varia por elemento (fogo, água, vento, teleporte, orbe, elevar).");

            // Stepper de mana (2 pools)
            AttachButtonTooltip(_manaRangeMinusButton, "Duração/Alcance: -1 round de buff OU -1 tile de alcance (só gasta Mana).");
            AttachButtonTooltip(_manaRangePlusButton, "Duração/Alcance: +1 round de buff OU +1 tile de alcance (só gasta Mana).");
            AttachButtonTooltip(_manaPowerMinusButton, "Buff/Dano: -1 (menos atributo de buff ou menos dano; -1 PA).");
            AttachButtonTooltip(_manaPowerPlusButton, "Buff/Dano: +1 (mais atributo de buff ou mais dano; +1 PA).");
            AttachButtonTooltip(_manaCastButton, "Conjura a magia com os parâmetros de mana escolhidos.");
            AttachButtonTooltip(_manaBackButton, "Volta ao menu de alvo da magia.");

            // Elementos (índice 0..5 = Physical, Magic, Fire, Water, Air, Earth)
            if (_magicElementButtons.Count == 6)
            {
                AttachButtonTooltip(_magicElementButtons[0], "FÍSICO — Self: +DEX/+STR por rounds. Tile: teleporte. Unidade: dano físico (DEX+STR).");
                AttachButtonTooltip(_magicElementButtons[1], "MÁGICO — Self: +INT/+WIS por rounds. Tile: orbe de mana. Unidade: dano mágico (INT+WIS).");
                AttachButtonTooltip(_magicElementButtons[2], "FOGO — Self: +INT/+VIT. Tile: fogo (dano por round). Unidade: dano de fogo.");
                AttachButtonTooltip(_magicElementButtons[3], "ÁGUA — Self: +VIT/+INT. Tile: água (terreno molhado). Unidade: dano de água.");
                AttachButtonTooltip(_magicElementButtons[4], "AR — Self: +AGI/+INT. Tile: vento (empurra unidades). Unidade: dano + empurrão.");
                AttachButtonTooltip(_magicElementButtons[5], "TERRA — Self: +VIT/+STR. Tile: eleva pedra. Unidade: dano de terra.");
            }

            // Submenus (abrem cascata)
            var btnActions = Root.Q<Button>("btn-actions");
            var btnBonus = Root.Q<Button>("btn-bonus");
            var btnAttack = Root.Q<Button>("btn-attack");
            AttachButtonTooltip(btnActions, "Ações de movimento e ataque (Mover, Atacar).");
            AttachButtonTooltip(btnBonus, "Ações Bônus (custam PAB): Concentrar, Incremento, Mirar, Mira Mágica.");
            AttachButtonTooltip(btnAttack, "Menu de ataque: Unidade ou Tile.");
        }

        private void AttachButtonTooltip(VisualElement el, string text)
        {
            if (el == null) return;
            // MouseEnterEvent é mais confiável que PointerEnterEvent no WebGL build.
            el.RegisterCallback<MouseEnterEvent>(evt =>
            {
                CancelPendingTip();
                _tipOwner = el;
                Vector2 mp = evt.mousePosition;   // posição do mouse no painel (root)
                _pendingTip = el.schedule.Execute(() => ShowButtonTooltip(el, text, mp)).StartingIn(400);
            });
            el.RegisterCallback<MouseLeaveEvent>(_ => CancelPendingTip());
            el.RegisterCallback<ClickEvent>(_ => CancelPendingTip());
        }

        private void CancelPendingTip()
        {
            if (_pendingTip != null) { _pendingTip.Pause(); _pendingTip = null; }
            HideButtonTooltip();
        }

        private void ShowButtonTooltip(VisualElement el, string text, Vector2 mousePos)
        {
            if (_tooltipGo == null || _tooltipTxt == null || el == null) return;
            _tooltipGo.BringToFront();   // garante que fica ACIMA dos botões (z-order)
            _tooltipTxt.text = text;
            _tooltipGo.style.display = DisplayStyle.Flex;
            // Posiciona 3px ACIMA do cursor (não do botão).
            float w = _tooltipGo.layout.width > 0 ? _tooltipGo.layout.width : 260f;
            float h = _tooltipGo.layout.height > 0 ? _tooltipGo.layout.height : 60f;
            // Limites em COORDENADAS DO PANEL (não Screen.width, que difere se há scaling de resolução).
            var root = el.panel != null ? el.panel.visualTree : null;
            float boundW = (root != null && root.layout.width > 0) ? root.layout.width : Screen.width;
            float boundH = (root != null && root.layout.height > 0) ? root.layout.height : Screen.height;
            float x = mousePos.x;
            float y = mousePos.y - h - 3f;   // 3px acima do mouse
            // Clamps pra ficar DENTRO da área da câmera (não vaza à direita do BattleHUD).
            if (y < 4f) y = mousePos.y + 3f;                       // não cabe acima → abaixo
            if (x + w > boundW - 4f) x = mousePos.x - w - 3f;       // vaza à direita → à esquerda do mouse
            if (x < 4f) x = 4f;                                     // não deixa sair à esquerda
            if (x + w > boundW - 4f) x = boundW - w - 4f;           // garante dentro da largura
            if (y + h > boundH - 4f) y = boundH - h - 4f;           // não sai embaixo
            _tooltipGo.style.left = x;
            _tooltipGo.style.top = y;
        }

        private void HideButtonTooltip()
        {
            if (_tooltipGo != null) _tooltipGo.style.display = DisplayStyle.None;
        }

        // ── PROMPT DE BÔNUS ──
        public void ShowPrompt(string text)
        {
            _promptText.text = text;
            _bonusTimerText.text = "";
            _simButton.style.display = DisplayStyle.None;
            _naoButton.style.display = DisplayStyle.None;
            _promptPanel.style.display = DisplayStyle.Flex;
        }
        public void ShowBonusPrompt(string text, Action onSim, Action onNao)
        {
            _promptText.text = text;
            _bonusTimerText.text = "";
            _simButton.style.display = DisplayStyle.Flex;
            _naoButton.style.display = DisplayStyle.Flex;
            if (_bonusHandlers.TryGetValue(_simButton, out var prevSim)) _simButton.clicked -= prevSim;
            if (_bonusHandlers.TryGetValue(_naoButton, out var prevNao)) _naoButton.clicked -= prevNao;
            Action hSim = () => onSim?.Invoke();
            Action hNao = () => onNao?.Invoke();
            _bonusHandlers[_simButton] = hSim;
            _bonusHandlers[_naoButton] = hNao;
            _simButton.clicked += hSim;
            _naoButton.clicked += hNao;
            _promptPanel.style.display = DisplayStyle.Flex;
        }
        public void HidePrompt() => _promptPanel.style.display = DisplayStyle.None;

        // ── REAÇÕES (Ações Bônus rework) ──
        private System.Action<ReactionKind> _reactionPick;
        public void ShowReactionMenu(Unit reactor, List<ReactionKind> options, float timer, System.Action<ReactionKind> onPick)
        {
            _reactionPick = onPick;
            if (_reactionText != null) _reactionText.text = $"⚡ {reactor.unitName} pode reagir!";
            if (_reactionTimer != null) _reactionTimer.text = timer.ToString("0.0") + "s";

            // Monta botões de reação dinamicamente no reaction-menu (fora do command-menu)
            if (_reactionButtons != null)
            {
                _reactionButtons.Clear();
                _reactionButtons.style.display = DisplayStyle.Flex;
                foreach (var opt in options)
                {
                    var btn = new Button(() => _reactionPick?.Invoke(opt))
                    {
                        text = ReactionLabel(opt)
                    };
                    btn.AddToClassList("pg-button");
                    _reactionButtons.Add(btn);
                }
                // Botão "Não reagir"
                var skip = new Button(() => _reactionPick?.Invoke(ReactionKind.None))
                {
                    text = "Não reagir"
                };
                skip.AddToClassList("pg-button");
                skip.style.backgroundColor = CorDisabled;
                _reactionButtons.Add(skip);
            }
            if (_reactionMenu != null) _reactionMenu.style.display = DisplayStyle.Flex;
        }
        public void UpdateBonusTimer(float sec)
        {
            if (_reactionMenu != null && _reactionMenu.style.display == DisplayStyle.Flex && _reactionTimer != null)
                _reactionTimer.text = sec > 0f ? sec.ToString("0.0") + "s" : "";
            else
                _bonusTimerText.text = sec > 0f ? sec.ToString("0.0") + "s" : "";
        }
        public void HideReactionMenu()
        {
            _reactionPick = null;
            if (_reactionButtons != null)
            {
                _reactionButtons.Clear();
                _reactionButtons.style.display = DisplayStyle.None;
            }
            if (_reactionMenu != null) _reactionMenu.style.display = DisplayStyle.None;
        }
        private static string ReactionLabel(ReactionKind k) => k switch
        {
            ReactionKind.CounterAttack => "⚔ Contra-ataque",
            ReactionKind.Dodge         => "↯ Esquiva",
            ReactionKind.Block         => "🛡 Bloqueio",
            _ => "?"
        };

        public void ShowEndScreen(string msg)
        {
            _endText.text = msg;
            _endPanel.style.display = DisplayStyle.Flex;
        }

        // ── MULTIPLAYER: aguardando posicionamento ──
        public void ShowWaitingForPlacement()
        {
            if (_mpWaitingOverlay == null) return;
            _mpWaitingOverlay.style.display = DisplayStyle.Flex;
        }
        public void SetWaitingText(string s) { if (_mpWaitingText != null) _mpWaitingText.text = s; }
        public void HideWaitingForPlacement()
        {
            if (_mpWaitingOverlay != null) _mpWaitingOverlay.style.display = DisplayStyle.None;
        }

        private static Color SkinTint(Color c)
        {
            var T = Tuning.Get();
            return T.uiButtonSkinEnabled ? Color.Lerp(c, Color.white, T.uiButtonFrameTintLerp) : c;
        }
    }
}
