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
        public bool IsPointerOverUI()
        {
            if (Root == null || Root.panel == null) return false;
            if (Mouse.current == null) return false;
            Vector2 screen = Mouse.current.position.ReadValue();
            Vector2 local = RuntimePanelUtils.ScreenToPanel(Root.panel, screen);
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
            foreach (var c in _seqChips) if (c != null) c.RemoveFromHierarchy();
            _seqChips.Clear();
            if (seq == null || seq.Count == 0)
            {
                _selectedSeqIndex = -1;
                if (_ticksBar != null) _ticksBar.style.display = DisplayStyle.None;
                return;
            }
            if (_selectedSeqIndex >= seq.Count) _selectedSeqIndex = -1;
            if (_ticksBar != null) _ticksBar.style.display = DisplayStyle.Flex;

            // Duração de cada ação (GDD-016: DEX acelera; Move/Wait não escalam)
            Unit u = _lastUnit;
            var durations = new List<int>();
            int total = 0;
            for (int i = 0; i < seq.Count; i++)
            {
                bool isBonus = scheduled != null && i < scheduled.Count && scheduled[i].IsBonus;
                int d = TickRules.Duration(u, seq[i], isBonus);
                durations.Add(d);
                total += d;
            }
            int maxTicks = Mathf.Max(1, Tuning.Get().maxQueueTicks);

            if (_ticksLabel != null)
                _ticksLabel.text = $"FILA — TEMPO {Mathf.Min(total, maxTicks)}/{maxTicks} TICKS";

            // Track: célula por tick, cor pela ação dona do tick (move/atk/bônus/concentrar)
            if (_ticksTrack != null)
            {
                _ticksTrack.Clear();
                int tick = 0;
                for (int i = 0; i < durations.Count && tick < maxTicks; i++)
                {
                    ActionType t = seq[i];
                    bool isBonus = scheduled != null && i < scheduled.Count && scheduled[i].IsBonus;
                    bool isConc  = t == ActionType.Concentrate;
                    for (int dTick = 0; dTick < durations[i] && tick < maxTicks; dTick++, tick++)
                    {
                        var cell = new VisualElement();
                        cell.AddToClassList("bh-tick");
                        if (isConc) cell.AddToClassList("used-conc");
                        else if (isBonus) cell.AddToClassList("used-bonus");
                        else if (t == ActionType.Attack) cell.AddToClassList("used-atk");
                        else cell.AddToClassList("used-move");
                        _ticksTrack.Add(cell);
                    }
                }
                while (tick < maxTicks)
                {
                    var cell = new VisualElement();
                    cell.AddToClassList("bh-tick");
                    _ticksTrack.Add(cell);
                    tick++;
                }
            }

            // Chips da fila: número + nome + custo (mesmo clique p/ toggle BAP)
            if (_ticksQueue != null)
            {
                _ticksQueue.Clear();
                for (int i = 0; i < seq.Count; i++)
                {
                    int idx = i;
                    bool isBonus = scheduled != null && i < scheduled.Count && scheduled[i].IsBonus;
                    bool isConc  = seq[i] == ActionType.Concentrate;
                    string name = isConc ? "Concentrar" : seq[i] == ActionType.Attack
                                  ? (isBonus ? "Golpe Poderoso" : "Atacar") : "Andar";
                    string prefix = isBonus ? "◆ " : "";

                    var chip = new VisualElement();
                    chip.AddToClassList("bh-qact");
                    if (isConc) chip.AddToClassList("conc");
                    else if (isBonus) chip.AddToClassList("bonus");
                    else if (seq[i] == ActionType.Attack) chip.AddToClassList("atk");
                    else chip.AddToClassList("move");

                    var n = new Label((idx + 1).ToString());
                    n.AddToClassList("bh-qact-n");
                    chip.Add(n);
                    var txt = new Label(prefix + name);
                    txt.AddToClassList("bh-qact-txt");
                    chip.Add(txt);
                    var cost = new Label("·" + durations[i].ToString());
                    cost.AddToClassList("bh-qact-cost");
                    chip.Add(cost);

                    var c = chip;
                    c.RegisterCallback<ClickEvent>(_ => OnSeqChipClicked(idx));
                    _ticksQueue.Add(c);
                    _seqChips.Add(c);
                }
            }
            RefreshSeqChipHighlight();
        }
        public void HideActionSequence()
        {
            foreach (var c in _seqChips) if (c != null) c.RemoveFromHierarchy();
            _seqChips.Clear();
            _selectedSeqIndex = -1;
            if (_ticksBar != null) _ticksBar.style.display = DisplayStyle.None;
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
        public void LogRound(int round)
        {
            var hGo = new VisualElement();
            hGo.AddToClassList("bh-round-hdr");
            var t = new Label($"v  Round {round}");
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
