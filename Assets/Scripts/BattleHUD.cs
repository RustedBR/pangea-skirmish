// BattleHUD.cs — HUD reformulada (2026-11-10, mockup v4.1 aprovado).
// UI Toolkit (PangeaScreen). Mantém 100% da API pública original (LogAction,
// SetPhase, Bind*, SetActionSequence, ShowPrompt, etc.) para não quebrar os
// ~40 callers (RoundManager, PlanningController, PlacementSync...).
// Layout novo: topbar + banda de intenção (topo), câmera/compasso (direita),
// mini-log + inspeção sob demanda, barra de ticks (centro inf), painel de
// INTENÇÕES (direita), reação como modal central. O menu cascata manual foi
// substituído pelas intenções (GDD-016); os bind de ações manuais ficam como
// no-ops de compatibilidade.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
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
    /// UI do combate — tema FFT (janelas azuis com moldura clara).
    /// </summary>
    public class BattleHUD : PangeaScreen
    {
        protected override string UxmlResource => "UI/Screens/BattleHUD";

        // ── Referências UI Toolkit ──────────
        private Label  _phaseText, _timerText, _cameraModeText;
        private VisualElement _compassRose;
        private Label[] _compassLabels;
        private VisualElement _logContent, _tooltipGo;
        private Label  _tooltipTxt;
        private VisualElement _inspectPanel, _portraitImg, _hpFill;
        private Label  _unitNameText, _unitHPText, _apText, _unitStatsText;
        private VisualElement _miniLog;
        private VisualElement _ticksBar, _ticksTrack, _ticksQueue;
        private Label  _ticksLabel;
        private VisualElement _intentsPanel;
        private Button[] _intentButtons;
        private VisualElement _reactionMenu, _reactionButtons;
        private Label _reactionTimer, _reactionText;
        private VisualElement _promptPanel, _endPanel, _endWin;
        private Label  _promptText, _bonusTimerText, _endText;
        private Button _confirmButton, _undoButton, _clearButton,
                       _simButton, _naoButton, _endMenuButton;
        private VisualElement _mpWaitingOverlay;
        private Label  _mpWaitingText;

        // ── Tooltip de botões (hover) ──
        private IVisualElementScheduledItem _pendingTip;

        // ── Dados do log (mini-log) ──
        private readonly List<LogEntry> _logEntries = new List<LogEntry>();
        private sealed class LogLine
        {
            public VisualElement ve;
            public Unit  unit;
        }
        private readonly List<LogLine> _logLines = new List<LogLine>();
        public Action<Unit> OnLogLineClicked;
        public Action<int> OnSeqChipToggled;

        private int _hoveredLineIdx = -1;
        private Unit _inspectedUnit;
        private Unit _lastUnit; // unidade do planejamento atual (custos da barra de ticks)

        // ── Chips da fila (barra de ticks) ──
        private readonly List<VisualElement> _seqChips = new List<VisualElement>();
        private int _selectedSeqIndex = -1;

        // ── Cores (do GameTuning) ──
        private static Color CorPlayer     => Tuning.Get().logPlayerColor;
        private static Color CorEnemy      => Tuning.Get().logEnemyColor;
        private static Color CorSystem     => Tuning.Get().logSystemColor;
        private static Color CorHover      => Tuning.Get().logHoverColor;
        private static Color CorChipMove   => Tuning.Get().seqChipMoveColor;
        private static Color CorChipAtk    => Tuning.Get().seqChipAttackColor;
        private static Color CorChipMoveBn => Tuning.Get().seqChipMoveBonusColor;
        private static Color CorChipAtkBn  => Tuning.Get().seqChipAttackBonusColor;

        // ============================================================
        // API pública — mantida idêntica à versão anterior
        // ============================================================
        [Obsolete("Migrado para UI Toolkit: o PangeaScreen carrega o UXML automaticamente.")]
        public void Build(Canvas canvas) { }

        /// <summary>Disparado quando o jogador clica numa intenção do painel (slot 0..2).</summary>
        public Action<int> OnIntentSelected;
        public Action OnFreeModeToggled;
        private Button _freeBtn;

        public void SetFreeModeLabel(bool on)
        {
            if (_freeBtn == null && _intentsPanel != null)
            {
                _freeBtn = new Button(() => { AudioManager.I?.Play(AudioManager.I.sfxUIClick); OnFreeModeToggled?.Invoke(); });
                _freeBtn.AddToClassList("bh-intent-btn");
                _freeBtn.style.marginBottom = 4;
                _intentsPanel.Insert(Mathf.Min(1, _intentsPanel.childCount), _freeBtn);
            }
            if (_freeBtn == null) return;
            _freeBtn.text = on ? "✎ Modo livre: LIGADO (Tab)" : "✎ Modo livre (Tab)";
            _freeBtn.EnableInClassList("armed", on);
        }
        /// <summary>Marca o botão de intenção atualmente armado (slot 0..2, ou -1).</summary>
        public void SetIntentArmed(int slot)
        {
            if (_intentButtons == null) return;
            for (int i = 0; i < _intentButtons.Length; i++)
            {
                if (_intentButtons[i] == null) continue;
                if (i == slot) { _intentButtons[i].AddToClassList("armed"); }
                else           { _intentButtons[i].RemoveFromClassList("armed"); }
            }
        }

        protected override void Bind()
        {
            var r = Root;
            _phaseText        = r.Q<Label>("phase-text");
            _timerText        = r.Q<Label>("timer-text");
            _cameraModeText   = r.Q<Label>("camera-mode-text");
            _compassRose      = r.Q<VisualElement>("compass-rose");
            _compassLabels    = new[] { r.Q<Label>("compass-n"), r.Q<Label>("compass-e"),
                                        r.Q<Label>("compass-s"), r.Q<Label>("compass-w") };

            // Minilog + inspeção (substituem log fixo e painel de status)
            _miniLog          = r.Q<VisualElement>("mini-log");
            _logContent       = r.Q<VisualElement>("mini-log-content");
            _inspectPanel     = r.Q<VisualElement>("inspect-panel");
            _portraitImg      = r.Q<VisualElement>("portrait-img");
            _hpFill           = r.Q<VisualElement>("hp-fill");
            _unitNameText     = r.Q<Label>("unit-name");
            _unitHPText       = r.Q<Label>("hp-text");
            _apText           = r.Q<Label>("ap-text");
            _unitStatsText    = r.Q<Label>("stats-text");

            // Barra de ticks (substitui o seq-bar de chips fixos)
            _ticksBar         = r.Q<VisualElement>("ticks-bar");
            _ticksTrack       = r.Q<VisualElement>("ticks-track");
            _ticksQueue       = r.Q<VisualElement>("ticks-queue");
            _ticksLabel       = r.Q<Label>("ticks-label");

            // Painel de intenções (substitui o command-menu cascata)
            _intentsPanel     = r.Q<VisualElement>("intents-panel");
            HookUiPointerDown();
            _intentButtons    = new[]
            {
                r.Q<Button>("btn-intent-1"), r.Q<Button>("btn-intent-2"), r.Q<Button>("btn-intent-3")
            };
            _confirmButton    = r.Q<Button>("btn-confirm");
            _undoButton       = r.Q<Button>("btn-undo");
            _clearButton      = r.Q<Button>("btn-clear");
            _simButton        = r.Q<Button>("btn-sim");
            _naoButton        = r.Q<Button>("btn-nao");
            _endMenuButton    = r.Q<Button>("btn-end-menu");

            _tooltipGo        = r.Q<VisualElement>("log-tooltip");
            _tooltipTxt       = r.Q<Label>("tooltip-txt");
            _reactionMenu     = r.Q<VisualElement>("reaction-menu");
            _reactionButtons  = r.Q<VisualElement>("reaction-buttons");
            _reactionTimer    = r.Q<Label>("reaction-timer");
            _reactionText     = r.Q<Label>("reaction-text");
            _promptPanel      = r.Q<VisualElement>("prompt-panel");
            _endPanel         = r.Q<VisualElement>("end-panel");
            _endWin           = r.Q<VisualElement>("end-win");
            _promptText       = r.Q<Label>("prompt-text");
            _bonusTimerText   = r.Q<Label>("bonus-timer");
            _endText          = r.Q<Label>("end-text");
            _mpWaitingOverlay = r.Q<VisualElement>("mp-waiting");
            _mpWaitingText    = r.Q<Label>("mp-waiting-text");

            // ── Tooltips de botões ──
            AttachMainButtonTooltips();

            // Callbacks dos botões de intenção (1-3; 4-5 são reservadas/dim)
            for (int i = 0; i < 3; i++)
            {
                int slot = i;
                var btn = _intentButtons[i];
                if (btn == null) continue;
                btn.RegisterCallback<ClickEvent>(evt =>
                {
                    AudioManager.I?.Play(AudioManager.I.sfxUIClick);
                    OnIntentSelected?.Invoke(slot);
                });
            }
            _endMenuButton.RegisterCallback<ClickEvent>(_ => SceneManager.LoadScene("MainMenu"));

            // Estado inicial
            if (_intentsPanel != null) _intentsPanel.style.display = DisplayStyle.None;
            if (_ticksBar != null) _ticksBar.style.display = DisplayStyle.None;
            if (_promptPanel != null) _promptPanel.style.display = DisplayStyle.None;
            if (_endPanel != null) _endPanel.style.display = DisplayStyle.None;
            if (_tooltipGo != null) _tooltipGo.style.display = DisplayStyle.None;
            if (_mpWaitingOverlay != null) _mpWaitingOverlay.style.display = DisplayStyle.None;
            HideUnitInfo();
            if (_reactionMenu != null) _reactionMenu.style.display = DisplayStyle.None;
            HideIntentBand();

            // Tooltip no indicador de câmera
            r.Q<VisualElement>("camera-mode").RegisterCallback<PointerEnterEvent>(_ => ShowCameraTooltip());
            r.Q<VisualElement>("camera-mode").RegisterCallback<PointerLeaveEvent>(_ => HideCameraTooltip());
        }

        // ── BANDA DE INTENÇÃO (GDD-016) — faixa própria do topo, não cobre o Confirmar ──
        public void ShowIntentBand(string tag, string hint)
        {
            var band = Root?.Q<VisualElement>("intent-band");
            if (band == null) return;
            var tagLbl = band.Q<Label>("intent-tag");
            if (tagLbl != null) tagLbl.text = tag;
            var hintLbl = band.Q<Label>("intent-hint");
            if (hintLbl != null) hintLbl.text = hint;
            band.style.display = DisplayStyle.Flex;
        }
        public void HideIntentBand()
        {
            var band = Root?.Q<VisualElement>("intent-band");
            if (band != null) band.style.display = DisplayStyle.None;
        }

        // ── HIT-TEST REAL DE UI TOOLKIT (fix do bug de confirmação) ──
        /// <summary>True quando o cursor está sobre QUALQUER elemento do painel do HUD.
        /// O antigo EventSystem.IsPointerOverGameObject(U GUI) não enxerga UI Toolkit —
        /// por isso os cliques nos botões (Confirmar etc.) vazavam pro grid.</summary>
        private int _uiDownFrame = -10;
        private bool _uiDownHooked;
        private void HookUiPointerDown()
        {
            if (_uiDownHooked || Root == null) return;
            _uiDownHooked = true;
            Root.RegisterCallback<PointerDownEvent>(e =>
            {
                var t = e.target as VisualElement;
                if (t != null && t != Root && t.name != "root" && !t.ClassListContains("bh-root")) _uiDownFrame = Time.frameCount;
            }, TrickleDown.TrickleDown);
        }

        public bool IsPointerOverUI()
        {
            if (Root == null || Root.panel == null) return false;
            HookUiPointerDown();
            if (Mouse.current == null) return false;
            Vector2 screen = Mouse.current.position.ReadValue();
            Vector2 local = RuntimePanelUtils.ScreenToPanel(Root.panel, screen);
            // O menu de bloco fica ACIMA da barra, fora da área dela: o Pick não o enxerga.
            if (_incPopup != null && _incPopup.worldBound.Contains(local)) return true;
            // O mesmo toque que a HUD consumiu não pode vazar para o mapa (o menu some ao clicar).
            if (Mathf.Abs(Time.frameCount - _uiDownFrame) <= 1) return true;
            var picked = Root.panel.Pick(local);
            // O UXML tem um "root" de tela inteira dentro do contêiner do documento: ele não é
            // UI clicável. Sem esta exceção o cursor estaria SEMPRE "sobre a HUD" e todo
            // clique no mapa seria engolido.
            return picked != null && picked != Root && picked.name != "root" && !picked.ClassListContains("bh-root");
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

        // ── COMPASSO ──

        protected override void OnEnable()
        {
            base.OnEnable();
            GridManager.OnViewOrientationChanged += SetCompassOrientation;
            if (GridManager.Instance != null)
                SetCompassOrientation(GridManager.Instance.ViewOrientation);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            GridManager.OnViewOrientationChanged -= SetCompassOrientation;
        }

        /// <summary>Aponta o compasso para o norte do mapa na orientação de vista dada.</summary>
        public void SetCompassOrientation(int degrees)
        {
            if (_compassRose == null) return;
            _compassRose.style.rotate = new StyleRotate(new Rotate(-degrees));
            if (_compassLabels == null) return;
            var upright = new StyleRotate(new Rotate(degrees));
            foreach (var lbl in _compassLabels)
                if (lbl != null) lbl.style.rotate = upright;
        }

        // ── INSPEÇÃO DA UNIDADE (bolha sob demanda — somou o painel fixo) ──
        public void ShowUnitInfo(Unit u)
        {
            _inspectedUnit = u;
            if (u == null) { HideUnitInfo(); return; }
            ApplyUnitInfo(u);
            if (_inspectPanel != null) _inspectPanel.style.display = DisplayStyle.Flex;
            if (_miniLog != null) _miniLog.style.display = DisplayStyle.Flex;
        }
        public void HideUnitInfo()
        {
            _inspectedUnit = null;
            if (_inspectPanel != null) _inspectPanel.style.display = DisplayStyle.None;
            if (_miniLog != null) _miniLog.style.display = DisplayStyle.None;
            if (_logContent != null && _logContent.childCount == 0)
            {
                // mantém as linhas (o mini-log recolhe e expande ao inspecionar)
            }
        }
        public void RefreshUnitInfo() { if (_inspectedUnit != null) ApplyUnitInfo(_inspectedUnit); }
        private void ApplyUnitInfo(Unit u)
        {
            if (u == null) return;
            var s = u.stats;
            if (_portraitImg != null)
                _portraitImg.style.backgroundImage = new StyleBackground(u.CurrentSprite);
            string team = u.team == Team.Player
                ? $"<color=#{ColorUtility.ToHtmlStringRGB(CorPlayer)}>Aliado</color>"
                : $"<color=#{ColorUtility.ToHtmlStringRGB(CorEnemy)}>Inimigo</color>";
            if (_unitNameText != null)
            {
                _unitNameText.text = $"{u.unitName}  <size=11>{team}</size>";
                if (u.IsDead) _unitNameText.text += "  <color=#888>+</color>";
            }
            float ratio = (float)u.currentHP / Mathf.Max(1, s.MaxHP);
            if (_hpFill != null)
            {
                _hpFill.style.width = new Length(Mathf.Clamp01(ratio) * 100, LengthUnit.Percent);
                var Thp = Tuning.Get();
                _hpFill.style.backgroundColor = ratio > Thp.hpBarYellowThreshold ? Thp.hpBarHighColor
                                    : ratio > Thp.hpBarRedThreshold    ? Thp.hpBarMidColor
                                                                       : Thp.hpBarLowColor;
            }
            if (_unitHPText != null) _unitHPText.text = $"HP  {u.currentHP} / {s.MaxHP}";
            if (_apText != null) _apText.text = $"<b>Instinto</b> {u.remainingBAP}/{s.MaxInstinct}";
            var buffs = StatusEffectSystem.Summary(u.statusEffects);
            if (_unitStatsText != null)
                _unitStatsText.text =
                    $"<b>STR</b> {s.STR:0.#}  <b>VIT</b> {s.VIT:0.#}  <b>DEX</b> {s.DEX:0.#}  <b>AGI</b> {s.AGI:0.#}\n" +
                    $"<b>MOV</b> {s.MoveStep}  <b>INI</b> {s.Initiative}  <b>ATQ</b> {s.PhysicalDamage}  <b>ALC</b> {s.AttackRange}\n" +
                    $"<b>MANA</b> {u.currentMana}/{s.MaxMana}  <b>CONC</b> {u.plannedConcentrations}" +
                    (string.IsNullOrEmpty(buffs) ? "" : $"\n<color=#FFD700><b>BUFFS</b> {buffs}</color>");
        }

        // ── BARRA DE TICKS (GDD-016 §7) — substitui o seq-bar de chips fixos ──
        public void SetActionSequence(List<ActionType> seq, List<ScheduledAction> scheduled = null)
        {
            if (seq == null || seq.Count == 0)
            {
                _selectedSeqIndex = -1;
                _planSeq = null; _planSched = null;
            }
            else
            {
                if (_selectedSeqIndex >= seq.Count) _selectedSeqIndex = -1;
                _planSeq = new List<ActionType>(seq);
                _planSched = scheduled != null ? new List<ScheduledAction>(scheduled) : null;
            }
            RebuildTickRows();
        }
        public void HideActionSequence()
        {
            foreach (var c in _seqChips) if (c != null) c.RemoveFromHierarchy();
            _seqChips.Clear();
            _selectedSeqIndex = -1;
            _planSeq = null; _planSched = null;
            RebuildTickRows();
        }

        /// <summary>O editor antigo (track + chips + rótulo) foi absorvido pelas fileiras: fica sempre oculto.</summary>
        private void SetPlanEditorVisible(bool on)
        {
            var d = DisplayStyle.None;
            if (_ticksTrack != null) _ticksTrack.style.display = d;
            if (_ticksQueue != null) _ticksQueue.style.display = d;
            if (_ticksLabel != null) _ticksLabel.style.display = d;
        }
        private void OnSeqChipClicked(int index)
        {
            if (index < 0 || index >= _seqChips.Count || _seqChips[index] == null) return;
            AudioManager.I?.Play(AudioManager.I.sfxUIClick);
            if (OnSeqChipToggled != null) OnSeqChipToggled.Invoke(index);
            _selectedSeqIndex = (_selectedSeqIndex == index) ? -1 : index;
            RefreshSeqChipHighlight();
        }
        private void RefreshSeqChipHighlight()
        {
            for (int i = 0; i < _seqChips.Count; i++)
            {
                if (_seqChips[i] == null) continue;
                _seqChips[i].style.opacity = (i == _selectedSeqIndex) ? 1f : 0.75f;
            }
        }
        public int SelectedSeqIndex => _selectedSeqIndex;
        public void SelectSeqIndex(int idx) => _selectedSeqIndex = idx;

        // ── BIND DE AÇÕES (compatibilidade; ações manuais foram substituídas pelas intenções) ──
        public void BindConfirm(Action a) { BindButton(_confirmButton, a); }
        public void BindMove(Action a)    { /* removido na reformulação */ }
        public void BindAttackUnit(Action a)  { /* removido na reformulação */ }
        public void BindAttackTile(Action a)  { /* removido na reformulação */ }
        public void BindPowerStrike(Action a) { /* removido na reformulação */ }
        public void BindQuickStep(Action a)   { /* removido na reformulação */ }
        public void BindConcentrate(Action a) { /* removido na reformulação */ }
        public void BindIncrement(Action a)   { /* removido na reformulação */ }
        public void BindAim(Action a)         { /* removido na reformulação */ }
        public void BindUndo(Action a)        { BindButton(_undoButton, a); }
        public void BindClear(Action a)       { BindButton(_clearButton, a); }

        private static readonly Dictionary<Button, Action> _btnHandlers = new Dictionary<Button, Action>();
        private static void BindButton(Button b, Action a)
        {
            if (b == null) return;
            if (_btnHandlers.TryGetValue(b, out var prev)) b.clicked -= prev;
            Action handler = () => { AudioManager.I?.Play(AudioManager.I.sfxUIClick); a?.Invoke(); };
            _btnHandlers[b] = handler;
            b.clicked += handler;
        }

        // ── NAVEGAÇÃO CASCATA (removida; no-ops de compatibilidade) ──
        public void ShowMainMenu() { }
        public void GoBack() { }

        // ── ESTADOS DE BOTÕES MANUAIS (no-ops de compatibilidade) ──
        public void SetMoveState(bool canAdd, int count, bool isPicking) { }
        public void SetAttackUnitState(bool canAdd, int count, bool isPicking = false) { }
        public void SetAttackTileState(bool canAdd, int count, bool isPicking = false) { }
        public void SetConcentrateState(bool canAdd, bool isPicking = false) { }
        public void SetIncrementState(bool canAdd, bool isPicking = false) { }
        public void SetAimState(bool canAdd, bool isPicking = false) { }

        // ── VISIBILIDADE ──
        public void SetActionBarVisible(bool v, Unit u = null)
        {
            if (u != null) _lastUnit = u;
            if (_intentsPanel != null)
                _intentsPanel.style.display = v ? DisplayStyle.Flex : DisplayStyle.None;
            if (v && u != null) UpdateAPDisplay(u);
            if (!v) { HideUnitInfo(); HideIntentBand(); }
        }
        public void UpdateAPDisplay(Unit u)
        {
            if (u != null) _lastUnit = u;
            if (_apText != null && u != null)
                _apText.text = $"<b>Instinto</b> {u.remainingBAP}/{u.stats.MaxInstinct}";
        }

        // ── TEXTOS PÚBLICOS ──
        public void SetPhase(string s)          { if (_phaseText != null) _phaseText.text = s; }
        public void SetTimer(float sec)         { if (_timerText != null) _timerText.text = sec.ToString("0.0") + "s"; }
        public void SetTimerWarning(bool warn)  { if (_timerText != null) _timerText.style.color = warn ? Tuning.Get().timerWarningColor : Tuning.Get().timerNormalColor; }
        public void HideTimer()                 { if (_timerText != null) _timerText.text = ""; }
        public void SetTimerVisible(bool v)     { if (_timerText != null) _timerText.style.display = v ? DisplayStyle.Flex : DisplayStyle.None; }
        public void SetConfirmVisible(bool v)   { if (_confirmButton != null) _confirmButton.style.display = v ? DisplayStyle.Flex : DisplayStyle.None; }
        public bool IsPromptVisible             { get { if (_promptPanel == null) return false; return _promptPanel.style.display == DisplayStyle.Flex; } }

        // ── LOG DE BATALHA (mini-log, mostrado junto da inspeção) ──
        public void LogAction(string line, Unit unit = null)
        {
            SetRowEvent(unit, line);
            LogAction(new LogEntry { displayText = line, tooltipText = line,
                team = unit != null ? unit.team : Team.Player, unit = unit });
        }
        public void LogAction(LogEntry entry)
        {
            _logEntries.Add(entry);
            AppendLineUI(entry);
            Debug.Log("[Battle] " + entry.tooltipText);
            TrimMiniLog();
        }
        // ── FILEIRAS DE TICKS (GDD-014 §7): a fila e as informações são a MESMA coisa ──
        // Uma fileira por unidade viva: nome + faixa de ticks. Cada bloco é uma ação com o tamanho
        // do seu custo em ticks, e o texto do que acontece aparece DENTRO do bloco em andamento.
        private VisualElement _ticksRows, _headerRow, _pipsRow;
        private Label _previewLine;
        private Label _ticksRowsHeader;
        private sealed class RowRef { public VisualElement seg; public Label label; public string defaultText; public Color baseColor; }
        private readonly Dictionary<Unit, RowRef> _rowRefs = new Dictionary<Unit, RowRef>();
        private readonly Dictionary<Unit, (string text, float time)> _rowEvents = new Dictionary<Unit, (string, float)>();
        private const float RowFlashSeconds = 2.2f;
        private List<Unit> _tlUnits; private System.Func<Unit, bool> _tlIsMine; private int _tlTick;
        private List<ActionType> _planSeq; private List<ScheduledAction> _planSched;
        private static Color TlMove   => new Color(0.29f, 0.56f, 0.89f);
        private static Color TlAttack => new Color(0.89f, 0.34f, 0.29f);
        private static Color TlBonus  => new Color(1.00f, 0.80f, 0.33f);
        private static Color TlConc   => new Color(0.59f, 0.47f, 1.00f);
        private static Color TlWait   => new Color(0.36f, 0.38f, 0.46f);
        private static Color TlEmpty  => new Color(0.07f, 0.10f, 0.22f);
        private static readonly System.Text.RegularExpressions.Regex RichTag =
            new System.Text.RegularExpressions.Regex("<[^>]+>");

        private string _headBase, _headCosts, _planNote; private float _planNoteUntil;
        private void RefreshHeader()
        {
            if (_ticksRowsHeader == null) return;
            string t = _headBase ?? "";
            if (!string.IsNullOrEmpty(_headCosts)) t += "\n" + _headCosts;
            bool note = !string.IsNullOrEmpty(_planNote) && Time.unscaledTime < _planNoteUntil;
            if (note) t += "\n" + _planNote;
            _ticksRowsHeader.text = t;
            _ticksRowsHeader.style.color = note ? new Color(1f, 0.82f, 0.45f) : new Color(0.74f, 0.85f, 1f);
        }

        /// <summary>Aviso curto no cabeçalho da barra (ex.: sem tick livre para o incremento).</summary>
        public void ShowPlanNote(string text)
        {
            _planNote = text; _planNoteUntil = Time.unscaledTime + 3f;
            RefreshHeader();
        }

        // ── Incremento clicando nos blocos de ataque (plano e janela entre ticks) ──
        public Action<int, int> OnIncrementPick;           // (índice no plano, tipo: 0 golpe poderoso, 1 mirar)
        private Unit _windowUnit; private Action<int> _windowPick;
        private VisualElement _incPopup;

        /// <summary>Abre/fecha o modo "janela entre ticks": o bloco do ataque seguinte vira clicável.</summary>
        public void SetIncrementWindow(Unit u, Action<int> onPick)
        {
            _windowUnit = u; _windowPick = onPick;
            CloseIncrementPopup();
            RebuildTickRows();
        }

        private void CloseIncrementPopup()
        {
            if (_incPopup != null) { _incPopup.RemoveFromHierarchy(); _incPopup = null; }
        }

        /// <summary>Menu de um bloco do plano: editar (destino, ordem, remover) e incrementar.</summary>
        private void OpenBlockMenu(VisualElement anchor, int idx, ActionType type, bool powerOn, bool aimOn, int count)
        {
            CloseIncrementPopup();
            if (_ticksBar == null) return;
            var pop = new VisualElement();
            var ps = pop.style;
            ps.position = Position.Absolute;
            ps.backgroundColor = new Color(0.07f, 0.10f, 0.22f, 0.98f);
            ps.paddingLeft = ps.paddingRight = 8; ps.paddingTop = ps.paddingBottom = 6;
            ps.borderTopLeftRadius = ps.borderTopRightRadius = ps.borderBottomLeftRadius = ps.borderBottomRightRadius = 6;
            ps.borderTopWidth = ps.borderBottomWidth = ps.borderLeftWidth = ps.borderRightWidth = 1;
            var bc = new Color(0.42f, 0.58f, 1f);
            ps.borderTopColor = ps.borderBottomColor = ps.borderLeftColor = ps.borderRightColor = bc;
            var head = new Label($"Ação {idx + 1}: {ActionName(type, false)}"); head.style.fontSize = 11;
            head.style.color = new Color(0.74f, 0.85f, 1f); head.style.unityFontStyleAndWeight = FontStyle.Bold; head.style.marginBottom = 4;
            pop.Add(head);
            void Opt(int kind, string text, bool on, bool enabled)
            {
                var b = new Button(() => { AudioManager.I?.Play(AudioManager.I.sfxUIClick); CloseIncrementPopup(); OnIncrementPick?.Invoke(idx, kind); }) { text = text };
                b.style.marginLeft = b.style.marginRight = 0; b.style.marginBottom = 3; b.style.height = 24;
                b.style.fontSize = 11; b.style.unityTextAlign = TextAnchor.MiddleLeft;
                b.style.backgroundColor = on ? new Color(1f, 0.80f, 0.33f) : new Color(0.10f, 0.14f, 0.30f);
                b.style.color = on ? new Color(0.06f, 0.08f, 0.16f) : new Color(0.93f, 0.95f, 1f);
                b.SetEnabled(enabled);
                pop.Add(b);
            }
            if (type == ActionType.Attack)
            {
                Opt(0, $"{(powerOn ? "✔ " : "")}◆ Golpe poderoso  (−{Tuning.Get().instinctPowerStrike} Instinto, +{Tuning.Get().ticksPowerStrikeExtra} tick)", powerOn, true);
                Opt(1, $"{(aimOn ? "✔ " : "")}◎ Mirar  (−{Tuning.Get().instinctAim} Instinto)", aimOn, true);
            }
            if (type == ActionType.Move) Opt(13, "⌖ Mudar destino", false, true);
            Opt(10, "◀ Mover para antes", false, idx > 0);
            Opt(11, "▶ Mover para depois", false, idx < count - 1);
            Opt(12, "✕ Remover", false, true);
            var close = new Button(CloseIncrementPopup) { text = "Fechar" };
            close.style.marginLeft = close.style.marginRight = 0; close.style.height = 22; close.style.fontSize = 10;
            pop.Add(close);
            _ticksBar.Add(pop);
            var ab = anchor.worldBound; var bb = _ticksBar.worldBound;
            ps.left = Mathf.Max(0f, ab.x - bb.x);
            ps.bottom = bb.height + 4;
            _incPopup = pop;
        }

        private void OpenIncrementPopup(VisualElement anchor, string title, bool powerOn, bool aimOn,
                                        bool powerEnabled, bool aimEnabled, Action<int> pick)
        {
            CloseIncrementPopup();
            if (_ticksBar == null) return;
            var pop = new VisualElement();
            var ps = pop.style;
            ps.position = Position.Absolute;
            ps.backgroundColor = new Color(0.07f, 0.10f, 0.22f, 0.98f);
            ps.paddingLeft = ps.paddingRight = 8; ps.paddingTop = ps.paddingBottom = 6;
            ps.borderTopLeftRadius = ps.borderTopRightRadius = ps.borderBottomLeftRadius = ps.borderBottomRightRadius = 6;
            ps.borderTopWidth = ps.borderBottomWidth = ps.borderLeftWidth = ps.borderRightWidth = 1;
            var bc = new Color(0.42f, 0.58f, 1f);
            ps.borderTopColor = ps.borderBottomColor = ps.borderLeftColor = ps.borderRightColor = bc;
            var head = new Label(title); head.style.fontSize = 11; head.style.color = new Color(0.74f, 0.85f, 1f);
            head.style.unityFontStyleAndWeight = FontStyle.Bold; head.style.marginBottom = 4;
            pop.Add(head);

            VisualElement Opt(int kind, string text, bool on, bool enabled)
            {
                var b = new Button(() => { AudioManager.I?.Play(AudioManager.I.sfxUIClick); CloseIncrementPopup(); pick?.Invoke(kind); }) { text = text };
                b.style.marginLeft = b.style.marginRight = 0; b.style.marginBottom = 3; b.style.height = 26;
                b.style.fontSize = 11; b.style.unityTextAlign = TextAnchor.MiddleLeft;
                b.style.backgroundColor = on ? new Color(1f, 0.80f, 0.33f) : new Color(0.10f, 0.14f, 0.30f);
                b.style.color = on ? new Color(0.06f, 0.08f, 0.16f) : new Color(0.93f, 0.95f, 1f);
                b.SetEnabled(enabled);
                return b;
            }
            pop.Add(Opt(0, $"{(powerOn ? "✔ " : "")}◆ Golpe poderoso  (−{Tuning.Get().instinctPowerStrike} Instinto, +{Tuning.Get().ticksPowerStrikeExtra} tick)", powerOn, powerEnabled));
            pop.Add(Opt(1, $"{(aimOn ? "✔ " : "")}◎ Mirar  (−{Tuning.Get().instinctAim} Instinto)", aimOn, aimEnabled));
            var close = new Button(CloseIncrementPopup) { text = "Fechar" };
            close.style.marginLeft = close.style.marginRight = 0; close.style.height = 22; close.style.fontSize = 10;
            pop.Add(close);

            _ticksBar.Add(pop);
            // Acima da barra, alinhado ao bloco clicado.
            var ab = anchor.worldBound; var bb = _ticksBar.worldBound;
            ps.left = Mathf.Max(0f, ab.x - bb.x);
            ps.bottom = bb.height + 4;
            _incPopup = pop;
        }

        public void UpdateTimeline(int tick, List<Unit> units, System.Func<Unit, bool> isMine)
        {
            _tlTick = tick; _tlUnits = units; _tlIsMine = isMine;
            RebuildTickRows();
        }

        private static string ActionName(ActionType t, bool bonus) =>
            t == ActionType.Move ? (bonus ? "Passo rápido" : "Andar")
          : t == ActionType.Attack ? (bonus ? "Golpe poderoso" : "Atacar")
          : t == ActionType.Concentrate ? "Concentrar" : "Esperar";

        private void RebuildTickRows()
        {
            if (_ticksBar == null || _tlUnits == null) return;
            _ticksBar.style.display = DisplayStyle.Flex;
            if (_ticksRows == null)
            {
                _ticksRowsHeader = new Label { pickingMode = PickingMode.Ignore };
                _ticksRowsHeader.style.fontSize = 11; _ticksRowsHeader.style.color = new Color(0.74f, 0.85f, 1f);
                _ticksRowsHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                _ticksRowsHeader.style.unityTextAlign = TextAnchor.UpperLeft;
                _ticksRowsHeader.style.flexGrow = 1; _ticksRowsHeader.style.whiteSpace = WhiteSpace.Normal;
                _headerRow = new VisualElement { pickingMode = PickingMode.Ignore };
                _headerRow.style.flexDirection = FlexDirection.Row; _headerRow.style.justifyContent = Justify.SpaceBetween;
                _headerRow.style.marginBottom = 4;
                _pipsRow = new VisualElement { pickingMode = PickingMode.Ignore };
                _pipsRow.style.flexDirection = FlexDirection.Row; _pipsRow.style.alignItems = Align.Center;
                _headerRow.Add(_ticksRowsHeader); _headerRow.Add(_pipsRow);
                _ticksRows = new VisualElement { name = "ticks-rows" };
                _previewLine = new Label { pickingMode = PickingMode.Ignore };
                _previewLine.style.fontSize = 11; _previewLine.style.color = new Color(0.62f, 0.69f, 0.82f);
                _previewLine.style.marginTop = 4; _previewLine.style.whiteSpace = WhiteSpace.Normal;
                _ticksBar.Add(_headerRow);
                _ticksBar.Add(_ticksRows);
                _ticksBar.Add(_previewLine);
                _ticksBar.schedule.Execute(FadeRowFlashes).Every(120);
            }
            SetPlanEditorVisible(false);
            _ticksRows.Clear();
            _rowRefs.Clear();
            _seqChips.Clear();
            int maxTicks = Mathf.Max(1, Tuning.Get().maxQueueTicks);

            Unit planUnit = _lastUnit;
            bool planning = _planSeq != null && _planSeq.Count > 0 && planUnit != null;
            int planTotal = 0;
            if (planning)
            {
                for (int i = 0; i < _planSeq.Count; i++)
                    planTotal += TickRules.Duration(planUnit, _planSeq[i], _planSched != null && i < _planSched.Count && _planSched[i].IsBonus);
            }
            _headBase = $"TICK {_tlTick}";
            if (planning)
            {
                int free = Mathf.Max(0, maxTicks - planTotal);
                _headBase += $"  •  PLANO {Mathf.Min(planTotal, maxTicks)}/{maxTicks} ticks  ({free} livre{(free == 1 ? "" : "s")})";
                _headCosts = $"Custo: Andar {TickRules.Duration(planUnit, ActionType.Move, false)} tick  •  " +
                             $"Atacar {TickRules.Duration(planUnit, ActionType.Attack, false)}  •  Golpe poderoso {TickRules.Duration(planUnit, ActionType.Attack, true)}";
            }
            else _headCosts = null;
            RefreshHeader();
            // Régua de ticks: mostra a capacidade da fila (1..N) alinhada com as faixas.
            var ruler = new VisualElement { pickingMode = PickingMode.Ignore };
            ruler.style.flexDirection = FlexDirection.Row; ruler.style.marginBottom = 2;
            var rpad = new VisualElement { pickingMode = PickingMode.Ignore }; rpad.style.width = 78; ruler.Add(rpad);
            var rtrack = new VisualElement { pickingMode = PickingMode.Ignore };
            rtrack.style.flexDirection = FlexDirection.Row; rtrack.style.flexGrow = 1;
            for (int k = 1; k <= maxTicks; k++)
            {
                var rl = new Label(k == 1 ? "agora" : "+" + (k - 1)) { pickingMode = PickingMode.Ignore };
                rl.style.flexGrow = 1; rl.style.flexBasis = 0; rl.style.fontSize = 9; rl.style.height = 11;
                rl.style.unityTextAlign = TextAnchor.MiddleCenter; rl.style.marginRight = 2;
                rl.style.color = new Color(0.55f, 0.62f, 0.80f);
                rl.style.borderBottomWidth = 1; rl.style.borderBottomColor = new Color(0.30f, 0.38f, 0.60f);
                rtrack.Add(rl);
            }
            ruler.Add(rtrack);
            _ticksRows.Add(ruler);

            BuildPips(planning ? planUnit : null);
            UpdatePreviewLine(planning ? planUnit : null);

            // Ameaças (elemento da linha do tempo compartilhada): ataques de outros em unidades suas.
            var threats = new List<(Unit target, int endTick, Unit by)>();
            foreach (var o in _tlUnits)
            {
                if (o == null || o.IsDead || (_tlIsMine != null && _tlIsMine(o))) continue;
                int at = 0;
                foreach (var q in o.queue)
                {
                    at += q.Remaining;
                    if (q.Type == ActionType.Attack && q.Attack.TargetUnit != null && _tlIsMine != null && _tlIsMine(q.Attack.TargetUnit))
                        threats.Add((q.Attack.TargetUnit, at, o));
                }
            }

            foreach (var u in _tlUnits)
            {
                if (u == null || u.IsDead) continue;
                bool mine = _tlIsMine != null && _tlIsMine(u);
                bool isPlanRow = planning && u == planUnit;

                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Stretch;
                row.style.marginBottom = 3;

                var name = new Label(u.unitName) { pickingMode = PickingMode.Ignore };
                name.style.width = 78; name.style.fontSize = 11; name.style.overflow = Overflow.Hidden;
                name.style.unityTextAlign = TextAnchor.MiddleLeft;
                name.style.color = (u.IsBusy || isPlanRow) ? new Color(0.80f, 0.82f, 0.88f) : new Color(0.52f, 0.90f, 0.60f);
                name.style.unityFontStyleAndWeight = mine ? FontStyle.Bold : FontStyle.Normal;
                row.Add(name);

                var track = new VisualElement { pickingMode = PickingMode.Ignore };
                track.style.flexDirection = FlexDirection.Row; track.style.flexGrow = 1; track.style.height = 28;
                track.style.position = Position.Relative;
                int used = 0;
                bool firstSeg = true;

                if (isPlanRow)
                {
                    // Plano em edição: blocos clicáveis (mesmo clique de antes p/ golpe poderoso e passo rápido).
                    Vector2Int prevPos = planUnit.anchor;
                    for (int i = 0; i < _planSeq.Count && used < maxTicks; i++)
                    {
                        int idx = i;
                        bool bonus = _planSched != null && i < _planSched.Count && _planSched[i].IsBonus;
                        bool aimed = _planSched != null && i < _planSched.Count && _planSched[i].IsAimed;
                        int d = TickRules.Duration(planUnit, _planSeq[i], bonus);
                        int tiles = 0;
                        if (_planSeq[i] == ActionType.Move && _planSched != null && i < _planSched.Count && _planSched[i].Index < planUnit.plannedPath.Count)
                        {
                            var dest = planUnit.plannedPath[_planSched[i].Index];
                            tiles = Mathf.Max(Mathf.Abs(dest.x - prevPos.x), Mathf.Abs(dest.y - prevPos.y));
                            prevPos = dest;
                        }
                        var seg = MakeSegment(Mathf.Min(d, maxTicks - used), SegColor(_planSeq[i], bonus),
                                              $"{idx + 1} {BlockText(planUnit, _planSeq[i], bonus, aimed, tiles)}", true);
                        seg.pickingMode = PickingMode.Position;
                        {
                            bool aimOn = _planSched != null && i < _planSched.Count && _planSched[i].IsAimed;
                            bool pw = bonus, am = aimOn; var tp = _planSeq[i]; int cnt = _planSeq.Count;
                            var segRef = seg;
                            // PointerDown (não ClickEvent): o bloco é refeito com frequência e o clique completo se perdia.
                            seg.RegisterCallback<PointerDownEvent>(ev =>
                            {
                                ev.StopPropagation();
                                if (_incPopup != null) { CloseIncrementPopup(); return; }
                                _selectedSeqIndex = idx;
                                OpenBlockMenu(segRef, idx, tp, pw, am, cnt);
                            });
                        }
                        seg.style.opacity = idx == _selectedSeqIndex ? 1f : 0.82f;
                        track.Add(seg); _seqChips.Add(seg);
                        used += d;
                    }
                }
                else if (u.IsBusy)
                {
                    foreach (var q in u.queue)
                    {
                        if (used >= maxTicks) break;
                        int d = Mathf.Min(q.Remaining, maxTicks - used);
                        bool threat = false;
                        foreach (var th in threats) if (th.by == u && q.Type == ActionType.Attack && q.Attack.TargetUnit == th.target) threat = true;
                        string label = mine ? BlockText(u, q.Type, q.IsBonus, q.IsAimed, 0)
                                     : threat ? "⚠ ataca você" : "ocupada";
                        var seg = MakeSegment(d, mine ? SegColor(q.Type, q.IsBonus) : threat ? new Color(0.55f, 0.20f, 0.20f) : TlWait, label, mine || threat);
                        if (threat) ((Label)seg.ElementAt(0)).style.color = new Color(1f, 0.82f, 0.80f);
                        track.Add(seg);
                        if (firstSeg && mine && u == _windowUnit && q.Type == ActionType.Attack)
                        {
                            seg.pickingMode = PickingMode.Position;
                            var segRef = seg; var wu = u;
                            seg.style.borderTopColor = seg.style.borderBottomColor = seg.style.borderLeftColor = seg.style.borderRightColor = new Color(1f, 0.9f, 0.5f);
                            ((Label)seg.ElementAt(0)).text = label + "  ▾";
                            seg.RegisterCallback<PointerDownEvent>(ev =>
                            {
                                ev.StopPropagation();
                                if (_incPopup != null) { CloseIncrementPopup(); return; }
                                OpenIncrementPopup(segRef, "Incrementar o próximo ataque", wu.queue.Count > 0 && wu.queue[0].IsBonus,
                                    wu.queue.Count > 0 && wu.queue[0].IsAimed, wu.CanIncrementNext(0), wu.CanIncrementNext(1),
                                    k => _windowPick?.Invoke(k));
                            });
                        }
                        if (firstSeg)
                        {
                            _rowRefs[u] = new RowRef { seg = seg, label = (Label)seg.ElementAt(0), defaultText = label,
                                                       baseColor = mine ? SegColor(q.Type, q.IsBonus) : TlWait };
                            firstSeg = false;
                        }
                        used += d;
                    }
                }

                if (used < maxTicks)
                {
                    string restText = (!u.IsBusy && !isPlanRow) ? "decide agora"
                                    : isPlanRow ? $"+ {maxTicks - used} livre{(maxTicks - used == 1 ? "" : "s")}" : "";
                    var rest = MakeSegment(maxTicks - used, TlEmpty, restText, false);
                    if (isPlanRow) ((Label)rest.ElementAt(0)).style.color = new Color(0.55f, 0.62f, 0.80f);
                    if (!u.IsBusy && !isPlanRow)
                    {
                        var l = (Label)rest.ElementAt(0);
                        l.style.color = new Color(0.52f, 0.90f, 0.60f);
                        _rowRefs[u] = new RowRef { seg = rest, label = l, defaultText = "decide agora", baseColor = TlEmpty };
                    }
                    track.Add(rest);
                }
                if (mine)
                    foreach (var th in threats)
                    {
                        if (th.target != u || th.endTick > maxTicks) continue;
                        var mark = new VisualElement { pickingMode = PickingMode.Ignore };
                        mark.style.position = Position.Absolute; mark.style.top = 0; mark.style.bottom = 0;
                        mark.style.left = Length.Percent(th.endTick * 100f / maxTicks); mark.style.width = 2;
                        mark.style.marginLeft = -2; mark.style.backgroundColor = new Color(1f, 0.38f, 0.33f);
                        var ml = new Label("!") { pickingMode = PickingMode.Ignore };
                        ml.style.position = Position.Absolute; ml.style.top = -2; ml.style.left = -4; ml.style.fontSize = 11;
                        ml.style.color = new Color(1f, 0.55f, 0.50f); ml.style.unityFontStyleAndWeight = FontStyle.Bold;
                        mark.Add(ml);
                        track.Add(mark);
                    }
                row.Add(track);
                _ticksRows.Add(row);
                ApplyRowEvent(u);
            }
        }

        private static int ExpectedDamage(Unit u, bool bonus, bool aimed) =>
            u.stats.PhysicalDamage + (bonus ? AttributeStats.Formulas.incrementAttackDamage : 0)
            + (aimed ? Mathf.RoundToInt(u.stats.DEX * Tuning.Get().aimDexMultiplier) : 0);

        /// <summary>Texto de um bloco: ícone, nome e efeito (tiles que anda, dano esperado).</summary>
        private static string BlockText(Unit u, ActionType t, bool bonus, bool aimed, int tiles)
        {
            switch (t)
            {
                case ActionType.Move:   return tiles > 0 ? $"→ Andar {tiles}" : bonus ? "→ Passo rápido" : "→ Andar";
                case ActionType.Attack: return $"{(bonus ? "◆ Golpe" : "⚔ Atacar")}{(aimed ? " ◎" : "")} ~{ExpectedDamage(u, bonus, aimed)}";
                case ActionType.Concentrate: return "✦ Concentrar";
                default: return "… Esperar";
            }
        }

        /// <summary>Instinto como pips: dourado = livre após o plano, vazado = reservado pelo plano, apagado = gasto.</summary>
        private void BuildPips(Unit u)
        {
            _pipsRow.Clear();
            if (u == null) return;
            var lab = new Label("Instinto") { pickingMode = PickingMode.Ignore };
            lab.style.fontSize = 10; lab.style.color = new Color(0.62f, 0.69f, 0.82f); lab.style.marginRight = 5;
            _pipsRow.Add(lab);
            int max = u.stats.MaxInstinct, free = Mathf.Clamp(u.remainingBAP, 0, max), have = Mathf.Clamp(u.currentInstinct, 0, max);
            for (int i = 0; i < max; i++)
            {
                var p = new VisualElement { pickingMode = PickingMode.Ignore };
                p.style.width = 9; p.style.height = 9; p.style.marginRight = 2;
                p.style.borderTopLeftRadius = p.style.borderTopRightRadius = p.style.borderBottomLeftRadius = p.style.borderBottomRightRadius = 2;
                var gold = new Color(1f, 0.80f, 0.33f);
                if (i < free) p.style.backgroundColor = gold;
                else if (i < have)
                {
                    p.style.borderTopWidth = p.style.borderBottomWidth = p.style.borderLeftWidth = p.style.borderRightWidth = 1;
                    p.style.borderTopColor = p.style.borderBottomColor = p.style.borderLeftColor = p.style.borderRightColor = gold;
                }
                else p.style.backgroundColor = new Color(0.23f, 0.29f, 0.48f);
                _pipsRow.Add(p);
            }
        }

        /// <summary>Linha de prévia do plano: dano e chance de acerto do primeiro ataque.</summary>
        private void UpdatePreviewLine(Unit u)
        {
            if (_previewLine == null) return;
            if (u == null || u.plannedAttacks.Count == 0) { _previewLine.text = ""; return; }
            var a = u.plannedAttacks[0];
            int n = u.plannedAttacks.Count;
            int total = 0;
            if (_planSched != null)
                foreach (var sa in _planSched)
                    if (sa.Type == ActionType.Attack) total += ExpectedDamage(u, sa.IsBonus, sa.IsAimed);
            string who = a.TargetUnit != null ? a.TargetUnit.unitName : "o alvo";
            string hit = "";
            if (a.TargetUnit != null)
            {
                float h = Mathf.Clamp01(u.stats.HitChance - a.TargetUnit.stats.DodgeChance);
                hit = $" • {Mathf.RoundToInt(h * 100f)}% de acerto";
            }
            _previewLine.text = $"Plano: {n} ataque{(n > 1 ? "s" : "")} em {who} • ~{total} de dano no total{hit}";
        }

        private static Color SegColor(ActionType t, bool bonus) =>
            bonus ? TlBonus : t == ActionType.Move ? TlMove : t == ActionType.Attack ? TlAttack
          : t == ActionType.Concentrate ? TlConc : TlWait;

        /// <summary>Bloco de ação: largura proporcional aos ticks, texto dentro.</summary>
        private static VisualElement MakeSegment(int ticks, Color color, string text, bool strong)
        {
            var seg = new VisualElement { pickingMode = PickingMode.Ignore };
            var ss = seg.style;
            ss.flexGrow = Mathf.Max(1, ticks); ss.flexBasis = 0; ss.marginRight = 2;
            ss.backgroundColor = color; ss.justifyContent = Justify.Center;
            ss.borderTopLeftRadius = ss.borderTopRightRadius = ss.borderBottomLeftRadius = ss.borderBottomRightRadius = 4;
            ss.borderTopWidth = ss.borderBottomWidth = ss.borderLeftWidth = ss.borderRightWidth = 1;
            var bc = new Color(color.r * 0.7f, color.g * 0.7f, color.b * 0.7f);
            ss.borderTopColor = ss.borderBottomColor = ss.borderLeftColor = ss.borderRightColor = bc;
            ss.paddingLeft = ss.paddingRight = 6; ss.overflow = Overflow.Hidden;
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.style.fontSize = 11; l.style.whiteSpace = WhiteSpace.NoWrap; l.style.overflow = Overflow.Hidden;
            l.style.unityTextAlign = TextAnchor.MiddleCenter; l.style.textOverflow = TextOverflow.Ellipsis;
            l.style.unityFontStyleAndWeight = strong ? FontStyle.Bold : FontStyle.Normal;
            l.style.color = (color.r + color.g + color.b) > 1.9f ? new Color(0.06f, 0.08f, 0.16f) : Color.white;
            seg.Add(l);
            return seg;
        }

        /// <summary>Texto do que a unidade fez: aparece DENTRO do bloco em andamento e vai apagando.</summary>
        public void SetRowEvent(Unit u, string richText)
        {
            if (u == null) return;
            string clean = RichTag.Replace(richText ?? "", "");
            // Cabe num bloco pequeno: tira o prefixo, o "(vida/vida HP)" e os espaços duplos.
            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\(\d+/\d+ HP\)", "");
            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"^(?:[>\s*✦▼↯⚔🛡💥💨~!]|x\s)+", "");
            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s{2,}", " ").Trim();
            // "Fera 1 -> Jogador: 6" vira "→ Jogador −6": cabe até num bloco de 1 tick.
            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"^.*?->\s*(.*?):\s*(\d+)\b.*$", "→ $1 −$2");
            if (clean.Length == 0) return;
            _rowEvents[u] = (clean, Time.unscaledTime);
            ApplyRowEvent(u);
        }

        private void ApplyRowEvent(Unit u)
        {
            if (!_rowRefs.TryGetValue(u, out var r)) return;
            float k = 0f;
            if (_rowEvents.TryGetValue(u, out var e))
            {
                float age = Time.unscaledTime - e.time;
                k = Mathf.Clamp01(1f - age / RowFlashSeconds);   // 1 = acabou de acontecer
                r.label.text = k > 0f ? e.text : r.defaultText;
            }
            else r.label.text = r.defaultText;
            // Destaque: o bloco clareia e ganha contorno dourado enquanto o evento é recente.
            r.seg.style.backgroundColor = Color.Lerp(r.baseColor, Color.Lerp(r.baseColor, Color.white, 0.38f), k);
            var gold = Color.Lerp(new Color(r.baseColor.r * 0.7f, r.baseColor.g * 0.7f, r.baseColor.b * 0.7f), new Color(1f, 0.9f, 0.5f), k);
            r.seg.style.borderTopColor = r.seg.style.borderBottomColor = r.seg.style.borderLeftColor = r.seg.style.borderRightColor = gold;
        }

        private void FadeRowFlashes()
        {
            foreach (var kv in _rowRefs) ApplyRowEvent(kv.Key);
            if (!string.IsNullOrEmpty(_planNote)) RefreshHeader();
        }

        private static string ActionLetter(ActionType t) =>
            t == ActionType.Move ? "M" : t == ActionType.Attack ? "A" : t == ActionType.Concentrate ? "C" : "E";
        private static Color ActionColor(ActionType t) =>
            t == ActionType.Move ? TlMove : t == ActionType.Attack ? TlAttack : t == ActionType.Concentrate ? TlConc : TlWait;

        public void LogRound(int round)
        {
            var hGo = new VisualElement();
            hGo.AddToClassList("bh-round-hdr");
            var t = new Label($"Tick {round}");
            t.AddToClassList("bh-round-hdr-text");
            hGo.Add(t);
            if (_logContent != null) _logContent.Add(hGo);
            TrimMiniLog();
        }
        private void AppendLineUI(LogEntry entry)
        {
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
                if (entry.unit != null) OnLogLineClicked?.Invoke(entry.unit);
            });
            if (_logContent != null) _logContent.Add(go);
            _logLines.Add(new LogLine { ve = go, unit = entry.unit });
        }
        /// <summary>Mini-log mostra só os últimos eventos (reformulação: sem log fixo gigante).</summary>
        private const int MaxMiniLogLines = 6;
        private void TrimMiniLog()
        {
            if (_logContent == null) return;
            bool removed = true;
            while (removed && _logContent.childCount > MaxMiniLogLines)
            {
                removed = false;
                for (int i = 0; i < _logContent.childCount; i++)
                {
                    var c = _logContent[i];
                    if (c != null && !c.ClassListContains("bh-round-hdr"))
                    {
                        _logContent.Remove(c);
                        for (int j = 0; j < _logLines.Count; j++)
                            if (_logLines[j].ve == c) { _logLines.RemoveAt(j); break; }
                        removed = true;
                        break;
                    }
                }
            }
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
            if (_hoveredLineIdx >= 0 && _hoveredLineIdx < _logLines.Count && _logLines[_hoveredLineIdx].ve != null)
                _logLines[_hoveredLineIdx].ve.style.backgroundColor = Color.clear;
            _hoveredLineIdx = newIdx;
            if (newIdx >= 0 && newIdx < _logLines.Count && _logLines[newIdx].ve != null)
                _logLines[newIdx].ve.style.backgroundColor = CorHover;
        }

        // ── Tooltip de botões (hover) ──
        private void AttachMainButtonTooltips()
        {
            AttachButtonTooltip(_confirmButton, "Confirma o plano de ações da unidade.");
            AttachButtonTooltip(_undoButton, "Remove a última ação adicionada ao plano.");
            AttachButtonTooltip(_clearButton, "Remove TODAS as ações do plano.");
            if (_intentButtons != null)
            {
                string[] names = { "Investir", "Golpear e recuar", "Recuar" };
                for (int i = 0; i < _intentButtons.Length && i < names.Length; i++)
                {
                    if (_intentButtons[i] != null)
                        AttachButtonTooltip(_intentButtons[i],
                            $"{names[i]}: arma a intenção. Depois clique no inimigo p/ montar a fila. Tecla {i + 1}.");
                }
            }
        }
        private void AttachButtonTooltip(VisualElement el, string text)
        {
            if (el == null) return;
            el.RegisterCallback<MouseEnterEvent>(evt =>
            {
                CancelPendingTip();
                Vector2 mp = evt.mousePosition;
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
        private void HideButtonTooltip()
        {
            if (_tooltipGo != null) _tooltipGo.style.display = DisplayStyle.None;
        }

        // ── PROMPT DE BÔNUS / ESCOLHA ──
        public void ShowPrompt(string text)
        {
            if (_promptText != null) _promptText.text = text;
            if (_bonusTimerText != null) _bonusTimerText.text = "";
            if (_simButton != null) _simButton.style.display = DisplayStyle.None;
            if (_naoButton != null) _naoButton.style.display = DisplayStyle.None;
            if (_promptPanel != null) _promptPanel.style.display = DisplayStyle.Flex;
        }
        public void ShowBonusPrompt(string text, Action onSim, Action onNao)
        {
            if (_promptText != null) _promptText.text = text;
            if (_bonusTimerText != null) _bonusTimerText.text = "";
            if (_simButton != null) _simButton.style.display = DisplayStyle.Flex;
            if (_naoButton != null) _naoButton.style.display = DisplayStyle.Flex;
            if (_simButton != null)
            {
                if (_btnHandlers.TryGetValue(_simButton, out var prevSim)) _simButton.clicked -= prevSim;
                Action hSim = () => onSim?.Invoke();
                _btnHandlers[_simButton] = hSim;
                _simButton.clicked += hSim;
            }
            if (_naoButton != null)
            {
                if (_btnHandlers.TryGetValue(_naoButton, out var prevNao)) _naoButton.clicked -= prevNao;
                Action hNao = () => onNao?.Invoke();
                _btnHandlers[_naoButton] = hNao;
                _naoButton.clicked += hNao;
            }
            if (_promptPanel != null) _promptPanel.style.display = DisplayStyle.Flex;
        }
        public void HidePrompt() { if (_promptPanel != null) _promptPanel.style.display = DisplayStyle.None; }

        // ── REAÇÕES (modal central) ──
        private System.Action<ReactionKind> _reactionPick;
        public void ShowReactionMenu(Unit reactor, List<ReactionKind> options, float timer, System.Action<ReactionKind> onPick)
        {
            _reactionPick = onPick;
            if (_reactionText != null) _reactionText.text = $"⚡ {reactor.unitName} pode reagir!";
            if (_reactionTimer != null) _reactionTimer.text = timer.ToString("0.0") + "s";

            if (_reactionButtons != null)
            {
                _reactionButtons.Clear();
                _reactionButtons.style.display = DisplayStyle.Flex;
                foreach (var opt in options)
                {
                    var btn = new Button(() => _reactionPick?.Invoke(opt))
                    {
                        text = options.Count == 1 ? $"Sim — {ReactionLabel(opt)}" : ReactionLabel(opt)
                    };
                    btn.AddToClassList("pg-button");
                    _reactionButtons.Add(btn);
                }
                var skip = new Button(() => _reactionPick?.Invoke(ReactionKind.None))
                { text = options.Count == 1 ? "Não" : "Não reagir" };
                skip.AddToClassList("pg-button");
                _reactionButtons.Add(skip);
            }
            if (_reactionMenu != null) _reactionMenu.style.display = DisplayStyle.Flex;
        }
        public void UpdateBonusTimer(float sec)
        {
            if (_reactionMenu != null && _reactionMenu.style.display == DisplayStyle.Flex && _reactionTimer != null)
                _reactionTimer.text = sec > 0f ? sec.ToString("0.0") + "s" : "";
            else if (_bonusTimerText != null)
                _bonusTimerText.text = sec > 0f ? sec.ToString("0.0") + "s" : "";
        }
        public void ShowChoice(string text, params (string label, System.Action onPick)[] options)
        {
            _reactionPick = null;
            if (_reactionText != null) _reactionText.text = text;
            if (_reactionTimer != null) _reactionTimer.text = "";
            if (_reactionButtons != null)
            {
                _reactionButtons.Clear();
                _reactionButtons.style.display = options.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                foreach (var (label, onPick) in options)
                {
                    var act = onPick;
                    var btn = new Button(() => { AudioManager.I?.Play(AudioManager.I.sfxUIConfirm); act?.Invoke(); }) { text = label };
                    btn.AddToClassList("pg-button");
                    _reactionButtons.Add(btn);
                }
            }
            if (_reactionMenu != null) _reactionMenu.style.display = DisplayStyle.Flex;
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
            if (_endText != null) _endText.text = msg;
            if (_endPanel != null) _endPanel.style.display = DisplayStyle.Flex;
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
    }
}
