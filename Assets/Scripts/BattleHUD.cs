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
        private CharacterPreview3D _unitPreview;          // retrato 3D da inspeção (câmera própria, igual à criação)
        private string _previewKey;                        // última combinação corpo+arma+time já renderizada
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
        /// <summary>Jogador escolheu uma magia no menu (id do SpellCatalog).</summary>
        public Action<string> OnSpellPick;
        private Button _spellBtn;
        private Unit _spellMenuUnit;

        /// <summary>Botão "Magia" no painel de intenções; abre o catálogo para a unidade controlada.</summary>
        public void SetSpellMenuUnit(Unit u)
        {
            _spellMenuUnit = u;
            if (false && _spellBtn == null && _intentsPanel != null)   // Magia agora fica na barra de ações (GDD-020 §9)
            {
                _spellBtn = new Button(() => { AudioManager.I?.Play(AudioManager.I.sfxUIClick); ToggleSpellMenu(); }) { text = SpellGlyph + " Magia" };
                _spellBtn.AddToClassList("bh-intent-btn");
                _spellBtn.style.marginBottom = 4;
                int at = _freeBtn != null ? _intentsPanel.IndexOf(_freeBtn) + 1 : Mathf.Min(1, _intentsPanel.childCount);
                _intentsPanel.Insert(Mathf.Clamp(at, 0, _intentsPanel.childCount), _spellBtn);
            }
            if (_spellBtn != null) _spellBtn.SetEnabled(u != null);
        }

        public void ToggleSpellMenu()
        {
            if (_incPopup != null && _incPopup.name == "spell-menu") { CloseIncrementPopup(); return; }
            OpenSpellMenu();
        }

        /// <summary>
        /// Catálogo de magias (GDD-019 §6): dois grupos (Física em Stamina, Elemental em Mana).
        /// Cada linha mostra nome, montagem, custo, ticks com a DEX atual e alcance/área;
        /// o hover escreve o efeito na linha de baixo. Linhas que não cabem ficam apagadas com o motivo.
        /// </summary>
        private void OpenSpellMenu()
        {
            if (_actionTip != null) _actionTip.style.display = DisplayStyle.None;
            CloseIncrementPopup();
            var u = _spellMenuUnit;
            if (_ticksBar == null || u == null) return;
            var pop = new VisualElement { name = "spell-menu" };
            var ps = pop.style;
            pop.AddToClassList("bh-window");
            ps.position = Position.Absolute;
            ps.minWidth = 700; ps.maxWidth = 900; ps.paddingLeft = ps.paddingRight = 12; ps.paddingTop = ps.paddingBottom = 10;
            var head = new Label("MAGIA"); head.AddToClassList("bh-title"); head.style.marginBottom = 6; pop.Add(head);

            var hover = new Label("Passe o mouse sobre uma magia para ver o efeito.") { pickingMode = PickingMode.Ignore };
            hover.style.fontSize = 12; hover.style.whiteSpace = WhiteSpace.Normal; hover.style.marginTop = 6;
            hover.style.color = new Color(0.80f, 0.86f, 1f); hover.style.minHeight = 34;

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.maxHeight = Mathf.Max(220f, Screen.height - 180f);
            var cols = new VisualElement(); cols.style.flexDirection = FlexDirection.Row;
            void Group(string title, string resName, int have, System.Func<SpellDef, bool> pick)
            {
                var col = new VisualElement(); col.style.flexGrow = 1; col.style.flexBasis = 0; col.style.marginRight = 8;
                var t = new Label($"{title}  <color=#9fb3d9>({resName} {have})</color>") { pickingMode = PickingMode.Ignore };
                t.style.fontSize = 12; t.style.unityFontStyleAndWeight = FontStyle.Bold; t.style.marginBottom = 4;
                t.style.color = new Color(0.74f, 0.85f, 1f);
                col.Add(t);
                foreach (var s in SpellCatalog.All())
                {
                    if (!pick(s)) continue;
                    bool ok = u.CanFitSpell(s, out string why);
                    int ticks = TickRules.Duration(u, ActionType.Spell, false, s);
                    string reach = s.effect == SpellEffect.Leap ? $"alc {SpellResolver.Reach(u, s)}" : s.range < 0 ? "sem alvo" : s.range == 0 ? "em si" : $"alc {SpellResolver.Reach(u, s)}";
                    string area  = s.area > 0 ? (s.effect == SpellEffect.Barrier ? $"linha {s.area * AttributeStats.DefaultFootprint}" : $"raio {SpellResolver.AreaRadius(s)}") : "";
                    string meta  = $"{reach}{(area != "" ? " · " + area : "")}";
                    var b = new Button(() => { AudioManager.I?.PlayNamed("sfx_spell_pick", AudioManager.I.sfxUIClick); CloseIncrementPopup(); OnSpellPick?.Invoke(s.id); });
                    b.AddToClassList("bh-intent-btn");
                    b.style.marginLeft = b.style.marginRight = 0; b.style.marginBottom = 3; b.style.height = 36;
                    b.style.fontSize = 12; b.style.unityTextAlign = TextAnchor.MiddleLeft; b.style.paddingLeft = 8;
                    b.style.borderLeftWidth = 4; b.style.borderLeftColor = ElementInfo.Color(s.PrimaryElement);
                    b.text = $"{SpellGlyph} <b>{s.displayName}</b>  <color=#9fb3d9>[{s.MontageText()}]</color>\n" +
                             $"<size=11>−{s.CostText} · {ticks} tick{(ticks == 1 ? "" : "s")} · {meta}</size>";
                    b.SetEnabled(ok);
                    if (!ok) b.style.opacity = 0.45f;
                    string eff = $"<b>{s.displayName}</b> — {s.CostText}. " + SpellResolver.Describe(u, s);
                    if (!ok) eff = $"<color=#ff9a8a>Não cabe: {why}.</color>  " + eff;
                    if (!s.IsPhysical) eff += $"  <color=#c9b8ff>Interrompível: {SpellResolver.InterruptThreshold(u)}+ de dano durante a conjuração corta.</color>";
                    var captured = eff;
                    b.RegisterCallback<PointerEnterEvent>(_ => hover.text = captured);
                    col.Add(b);
                }
                cols.Add(col);
            }
            Group("FÍSICA", $"Mana {u.AvailableMana} · Stamina", u.remainingBAP, s => s.IsPhysical);
            Group("ELEMENTAL · PURAS", "Mana", u.AvailableMana, s => !s.IsPhysical && s.IsPure);
            Group("ELEMENTAL · PARES", "Mana", u.AvailableMana, s => !s.IsPhysical && s.IsPair);
            scroll.Add(cols);
            pop.Add(scroll);
            pop.Add(hover);
            var close = new Button(CloseIncrementPopup) { text = "Fechar" };
            close.AddToClassList("bh-intent-btn"); close.style.marginLeft = close.style.marginRight = 0;
            close.style.height = 30; close.style.fontSize = 12; close.style.marginTop = 4;
            pop.Add(close);
            _ticksBar.Add(pop);
            var bb = _ticksBar.worldBound;
            ps.left = 0; ps.bottom = bb.height + 4 + ActionBarClearance();
            _incPopup = pop;
        }

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
            _endMenuButton.RegisterCallback<ClickEvent>(_ => { if (_endMenuAction != null) _endMenuAction(); else SceneManager.LoadScene("MainMenu"); });

            // Ordem de desenho (UI Toolkit não tem z-index): a bolha de inspeção vai para o fundo,
            // embaixo de todos os painéis de ação; menus modais sobem ao abrir (BringToFront).
            _inspectPanel?.SendToBack();

            // Placas de vida na tela, presas acima de cada unidade (substituem as barras no mundo).
            TextIcons.Attach();   // símbolos viram sprites em todos os textos
            if (GetComponent<UnitPlates>() == null) gameObject.AddComponent<UnitPlates>().Init(r.Q<VisualElement>("root") ?? r);

            var topbar = r.Q<VisualElement>("topbar");
            if (topbar != null) topbar.style.display = DisplayStyle.None;   // aparece no primeiro SetPhase

            // Manter distância / Proteger saíram (GDD-020 §8): a postura virou bônus de atributo.
            foreach (var n in new[] { "btn-intent-4", "btn-intent-5" })
            { var b = r.Q<Button>(n); if (b != null) b.style.display = DisplayStyle.None; }

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
            if (GameLog.PointerOver) return true;          // painel/botão de LOG (IMGUI) por cima do jogo
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
            ApplyEscLevel();
        }
        private VisualElement _inspectToggleIcon;
        private Label _inspectToggleText;

        /// <summary>
        /// Inspeção ao lado da unidade inspecionada (2026-11-13): de preferência à esquerda dela,
        /// sem cobrir unidades. Testa algumas posições e fica com a que esconde menos unidades;
        /// o fallback é colada na borda esquerda.
        /// </summary>
        private void LateUpdate()
        {
            if (_inspectPanel == null || _inspectedUnit == null || _inspectPanel.resolvedStyle.display != DisplayStyle.Flex) return;
            var cam = Camera.main; var panel = _inspectPanel.panel; var parent = _inspectPanel.parent;
            if (cam == null || panel == null || parent == null) return;
            float w = _inspectPanel.layout.width, h = _inspectPanel.layout.height;
            float W = parent.layout.width, H = parent.layout.height;
            if (float.IsNaN(w) || float.IsNaN(H) || w <= 0f || W <= 0f) return;
            Vector2 Screen(Unit x) => RuntimePanelUtils.CameraTransformWorldToPanel(panel, x.HeadWorld, cam);
            Vector2 me = Screen(_inspectedUnit);
            const float gap = 40f, top = 120f, bottomReserve = 200f;   // topo: barra de fase e dica   // topo: barra de fase; baixo: barras de ação e ticks
            var cands = new List<Vector2>
            {
                new Vector2(me.x - gap - w, me.y - h * 0.3f),   // à esquerda da unidade
                new Vector2(me.x - gap - w, me.y - h),          // à esquerda, acima
                new Vector2(me.x - gap - w, me.y + 10f),        // à esquerda, abaixo
                new Vector2(me.x + gap,     me.y - h * 0.3f),   // à direita da unidade
                new Vector2(12f, top),                          // colada na borda esquerda, no topo
                new Vector2(12f, H - bottomReserve - h),        // colada na borda esquerda, embaixo
            };
            Vector2 best = cands[0]; int bestScore = int.MaxValue;
            for (int i = 0; i < cands.Count; i++)
            {
                var c = new Vector2(Mathf.Clamp(cands[i].x, 12f, Mathf.Max(12f, W - w - 12f)), Mathf.Clamp(cands[i].y, top, Mathf.Max(top, H - bottomReserve - h)));
                var rect = new Rect(c.x - 16f, c.y - 16f, w + 32f, h + 32f);
                int covered = 0;
                foreach (var u in UnitRegistry.Tracked)
                {
                    if (u == null || u.IsDead) continue;
                    Vector2 sp = Screen(u);
                    if (rect.Contains(sp) || rect.Contains(sp + new Vector2(0f, 60f))) covered += u == _inspectedUnit ? 100 : 10;
                }
                int score = covered * 10 + i;   // empate: a ordem de preferência
                if (score < bestScore) { bestScore = score; best = c; }
            }
            // Desliza em vez de pular, para não tremer com a câmera.
            float cx = _inspectPanel.resolvedStyle.left, cy = _inspectPanel.resolvedStyle.top;
            float k = Mathf.Clamp01(Time.unscaledDeltaTime * 12f);
            if (float.IsNaN(cx) || Mathf.Abs(cx - best.x) > 600f) { cx = best.x; cy = best.y; k = 1f; }
            _inspectPanel.style.left = Mathf.Lerp(cx, best.x, k);
            _inspectPanel.style.top = Mathf.Lerp(cy, best.y, k);
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
            // Retrato = MESMO visual que a unidade usa no campo hoje: o modelo 3D (com a arma).
            // Reusa o CharacterPreview3D da criação; só re-renderiza quando corpo/arma/time mudam.
            if (_portraitImg != null)
            {
                var m = u.Model3D;
                if (m != null && !string.IsNullOrEmpty(m.ModelName))
                {
                    Color tc = u.team == Team.Player ? CorPlayer : CorEnemy;
                    string key = m.ModelName + "|" + (m.WeaponModelName ?? "") + "|" + tc.ToString();
                    if (_unitPreview == null || key != _previewKey)
                    {
                        if (_unitPreview == null) _unitPreview = CharacterPreview3D.Create(256);
                        _unitPreview.Show(m.ModelName, m.WeaponModelName, tc);
                        _previewKey = key;
                    }
                    _portraitImg.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(_unitPreview.Texture));
                }
                else
                    _portraitImg.style.backgroundImage = new StyleBackground(u.CurrentSprite);
            }
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
            if (_unitHPText != null) _unitHPText.text = $"{u.currentHP} / {s.MaxHP}";
            if (_apText != null) _apText.style.display = DisplayStyle.None;
            if (_unitStatsText != null) _unitStatsText.style.display = DisplayStyle.None;
            BuildInspectBody(u);
        }

        // ── INSPEÇÃO COM ÍCONES (2026-11-13): compacta por padrão, expande para a ficha inteira ──
        private void OnDestroy()
        {
            // A prévia 3D do retrato vive fora da HUD: sem isto ficava órfã na cena.
            if (_unitPreview != null) Destroy(_unitPreview.gameObject);
        }

        private static bool _inspectExpanded;
        private VisualElement _inspectBody;
        private Button _inspectToggle;
        private Label _inspectHint;
        private static readonly Dictionary<string, Texture2D> _iconTex = new Dictionary<string, Texture2D>();

        private static Texture2D IconTex(string name)
        {
            if (_iconTex.TryGetValue(name, out var t) && t != null) return t;
            t = Resources.Load<Texture2D>("UI/Icons/" + name);
            if (t != null) t.filterMode = FilterMode.Point;
            _iconTex[name] = t;
            return t;
        }

        private VisualElement Chip(string icon, string text, string tip, string cls = "bh-ichip")
        {
            var row = new VisualElement(); row.AddToClassList(cls);
            var img = new VisualElement(); img.AddToClassList("bh-ichip-icon");
            var tex = IconTex(icon);
            if (tex != null) img.style.backgroundImage = new StyleBackground(tex);
            row.Add(img);
            var lbl = new Label(text) { pickingMode = PickingMode.Ignore }; lbl.AddToClassList("bh-ichip-text");
            row.Add(lbl);
            row.RegisterCallback<PointerEnterEvent>(_ => { if (_inspectHint != null) _inspectHint.text = tip; });
            row.RegisterCallback<PointerLeaveEvent>(_ => { if (_inspectHint != null) _inspectHint.text = ""; });
            return row;
        }

        private VisualElement Bar(string icon, float ratio, Color col, string text, string tip)
        {
            var row = new VisualElement(); row.AddToClassList("bh-ibar");
            var img = new VisualElement(); img.AddToClassList("bh-ichip-icon");
            var tex = IconTex(icon); if (tex != null) img.style.backgroundImage = new StyleBackground(tex);
            row.Add(img);
            var bg = new VisualElement(); bg.AddToClassList("bh-ibar-bg");
            var fill = new VisualElement(); fill.AddToClassList("bh-ibar-fill");
            fill.style.width = new Length(Mathf.Clamp01(ratio) * 100f, LengthUnit.Percent);
            fill.style.backgroundColor = col;
            bg.Add(fill); row.Add(bg);
            var lbl = new Label(text) { pickingMode = PickingMode.Ignore }; lbl.AddToClassList("bh-ibar-text");
            row.Add(lbl);
            row.RegisterCallback<PointerEnterEvent>(_ => { if (_inspectHint != null) _inspectHint.text = tip; });
            row.RegisterCallback<PointerLeaveEvent>(_ => { if (_inspectHint != null) _inspectHint.text = ""; });
            return row;
        }

        private void BuildInspectBody(Unit u)
        {
            var content = _inspectPanel?.Q<VisualElement>("inspect-content");
            if (content == null) return;
            if (_inspectBody == null)
            {
                _inspectBody = new VisualElement { name = "inspect-body" };
                content.Add(_inspectBody);
                _inspectHint = new Label("") { name = "inspect-hint", pickingMode = PickingMode.Ignore };
                _inspectHint.AddToClassList("bh-ihint");
                content.Add(_inspectHint);
                _inspectToggle = new Button(() => { _inspectExpanded = !_inspectExpanded; RefreshUnitInfo(); }) { name = "inspect-toggle" };
                _inspectToggle.AddToClassList("bh-itoggle");
                _inspectPanel.Add(_inspectToggle);
            }
            // Ícone e texto como filhos lado a lado (o texto do próprio botão ficava por baixo do ícone).
            _inspectToggle.text = "";
            if (_inspectToggleIcon == null)
            {
                _inspectToggleIcon = new VisualElement { pickingMode = PickingMode.Ignore };
                _inspectToggleIcon.AddToClassList("bh-itoggle-icon");
                _inspectToggle.Add(_inspectToggleIcon);
                _inspectToggleText = new Label { pickingMode = PickingMode.Ignore };
                _inspectToggleText.AddToClassList("bh-itoggle-text");
                _inspectToggle.Add(_inspectToggleText);
            }
            _inspectToggleText.text = _inspectExpanded ? "menos" : "ficha";
            var chev = IconTex(_inspectExpanded ? "chev_up" : "chev_down");
            if (chev != null) _inspectToggleIcon.style.backgroundImage = new StyleBackground(chev);
            _inspectBody.Clear();
            var s = u.stats; var T = Tuning.Get(); var F = AttributeStats.Formulas ?? T.statFormulas;

            // Recursos: Mana em barra, Stamina em barra curta (a Vida já está no topo).
            _inspectBody.Add(Bar("mana", s.MaxMana > 0 ? (float)u.currentMana / s.MaxMana : 0f, new Color(0.31f, 0.59f, 1f),
                $"{u.currentMana}/{s.MaxMana}", $"Mana: paga magias Elementais e metade das Físicas. Volta 1 a cada {s.ManaRegenTicks} ticks; Concentrar dá +{s.ConcentrateMana}."));
            _inspectBody.Add(Bar("stamina", s.MaxStamina > 0 ? (float)u.currentStamina / s.MaxStamina : 0f, new Color(1f, 0.82f, 0.30f),
                $"{u.currentStamina}/{s.MaxStamina}", $"Stamina: paga reações, golpe poderoso e metade das magias Físicas. Volta 1 a cada {s.StaminaRegenTicks} ticks; Respirar dá +{T.breatheStamina}."));

            // Combate em ícones.
            var combat = new VisualElement(); combat.AddToClassList("bh-igrid");
            var w = WeaponCatalog.Get(u.weaponId);
            combat.Add(Chip("dmg", $"{s.PhysicalDamage}", $"Golpe: {s.DamagePerTile:0.#} por tile × {s.ShapeTiles} tiles ({AttackShape.Describe(w)}) num alvo inteiro."));
            combat.Add(Chip("range", $"{u.EffectiveAttackRange}", "Alcance da arma em células (uma unidade ocupa 3)."));
            combat.Add(Chip("hit", $"{u.EffectiveHitChance * 100:0}%", "Chance de acertar (DEX)."));
            combat.Add(Chip("dodge", $"{s.DodgeChance * 100:0}%", $"Chance de esquiva (AGI); a esquiva tira {AttackResolver.DodgeFrac(u) * 100:0}% dos tiles do golpe."));
            combat.Add(Chip("def", $"−{s.PhysicalDefense}", $"Defesa física fixa por golpe (VIT × {F.armorPerVIT:0.##}). Bloquear soma {RoundManager.BlockAmount(u)}."));
            combat.Add(Chip("mdef", $"−{s.MagicDefense}", "Defesa contra magia (WIS)."));
            if (_inspectExpanded) combat.Add(Chip("move", $"{u.EffectiveMoveStep}", "Células por andada (AGI)."));
            _inspectBody.Add(combat);

            // Equipamento (GDD-021): os 5 slots, com a borda na cor da raridade; o hover diz o que dá.
            if (!BeastAI.Controls(u))
            {
                var eq = new VisualElement(); eq.AddToClassList("bh-igrid");
                void Slot(string id, ItemSlot slot)
                {
                    var it = ItemCatalog.Get(id); var w = WeaponCatalog.Get(id);
                    string icon = it != null ? it.Icon : w != null ? "dmg" : slot == ItemSlot.Head ? "helm" : slot == ItemSlot.Torso ? "chest" : slot == ItemSlot.Legs ? "boots" : "dmg";
                    string name = it != null ? it.displayName : w != null ? w.displayName + (Equipment.IsTwoHanded(w) ? " (2 mãos)" : "") : "—";
                    string tip = it != null ? $"<b>{it.displayName}</b> · {Equipment.RarityName(it.rarity)}\n{it.Summary()}"
                               : w != null ? $"<b>{w.displayName}</b> · {Equipment.SlotName(slot)}" + (slot == ItemSlot.LeftHand ? $"\nGolpe extra de {Tuning.Get().offhandDamageMult * 100:0}% junto com o ataque." : "")
                               : $"{Equipment.SlotName(slot)}: vazio";
                    var c = Chip(icon, name, tip);
                    var col = it != null ? Equipment.RarityColor(it.rarity) : Equipment.RarityColor(Rarity.Common);
                    c.style.borderTopColor = c.style.borderBottomColor = c.style.borderLeftColor = c.style.borderRightColor = col;
                    if (it == null && w == null) c.style.opacity = 0.45f;
                    eq.Add(c);
                }
                Slot(u.weaponId, ItemSlot.RightHand);
                if (!Equipment.IsTwoHanded(u.weaponId)) Slot(u.offhandId, ItemSlot.LeftHand);
                if (_inspectExpanded)
                {
                    Slot(u.headId, ItemSlot.Head); Slot(u.torsoId, ItemSlot.Torso); Slot(u.legsId, ItemSlot.Legs);
                    int wt = Equipment.TotalWeight(Equipment.Of(u)), lim = Equipment.WeightLimit(s.VIT);
                    eq.Add(Chip("stone", wt > lim ? $"<color=#ff8a7a>{wt}/{lim}</color>" : $"{wt}/{lim}",
                        $"Peso {wt} de {lim} (2 + VIT/2).{(wt > lim ? $" {wt - lim} acima: −{wt - lim} na andada, na esquiva e no Esquivar." : "")}"));
                }
                _inspectBody.Add(eq);
            }

            // Ficha completa: atributos com a postura destacada.
            if (_inspectExpanded)
            {
                var (up, down) = PostureRules.Attrs(u.posture);
                var attrs = new VisualElement(); attrs.AddToClassList("bh-igrid");
                void A(string icon, string key, float v, string tip)
                {
                    string col = key == up ? "#7fe08a" : key == down ? "#ff8a7a" : "#eef2ff";
                    attrs.Add(Chip(icon, $"<color={col}>{key} {v:0}</color>", tip));
                }
                A("str", "STR", s.STR, "Força: dano por tile das armas de perto, Stamina.");
                A("vit", "VIT", s.VIT, "Vitalidade: vida, defesa fixa, bloqueio, Stamina.");
                A("dex", "DEX", s.DEX, "Destreza: ações mais rápidas, acerto, crítico, dano à distância, Stamina.");
                A("agi", "AGI", s.AGI, "Agilidade: andada, esquiva (fração de tiles), iniciativa.");
                A("int", "INT", s.INT, "Inteligência: potência e alcance das magias.");
                A("wis", "WIS", s.WIS, "Sabedoria: Mana, regeneração e defesa mágica.");
                _inspectBody.Add(attrs);
            }

            // Estados e postura em ícones.
            var states = new VisualElement(); states.AddToClassList("bh-igrid");
            if (u.posture != Posture.Neutral)
            {
                string pi = "p_" + u.posture.ToString().ToLowerInvariant();
                states.Add(Chip(pi, PostureRules.Name(u.posture), $"Postura {PostureRules.Name(u.posture)}: {PostureRules.Effect(u.posture)}.", "bh-ichip bh-ichip--state"));
            }
            void St(string icon, string name, string tip) => states.Add(Chip(icon, name, tip, "bh-ichip bh-ichip--state"));
            if (u.burningTicks > 0) St("burn", $"{u.burningTicks}t", $"Queimando: −{u.burningDamage} por tick.");
            if (u.soakedTicks > 0) St("soaked", $"{u.soakedTicks}t", "Encharcado: −1 tile de andada. Apaga Queimando.");
            if (u.stoneSkinTicks > 0) St("stone", $"{u.stoneSkinTicks}t", $"Pele de pedra: defesa da VIT ×{u.stoneSkinMult:0.#}.");
            if (u.furyReady > 0f) St("fury", "Fúria", $"Próximo golpe ×{u.furyReady:0.#}.");
            if (u.curveReady) St("eye", "Curva", "Próximo ataque à distância segue o alvo e contorna obstáculos.");
            if (u.foresightTicks > 0) St("cast", $"{u.foresightTicks}t", "Visão arcana: vê a fila inimiga mais à frente.");
            if (u.IsDulled) St("dulled", "Embotado", "Pouca Mana: magias com menos alcance.");
            if (u.IsDead) St("hp", "Morto", "Fora da luta.");
            if (states.childCount > 0) _inspectBody.Add(states);
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
        /// <summary>
        /// Esc vai limpando a tela, uma parte por vez (2026-11-13): 1º Histórico, 2º inspeção,
        /// 3º as unidades dos outros na barra de ticks; o 4º mostra tudo de novo.
        /// </summary>
        public void GoBack()
        {
            _escLevel = (_escLevel + 1) % 4;
            ApplyEscLevel();
            RebuildTickRows();
            string[] msg = { "Mostrando tudo", "Histórico escondido (Esc)", "Inspeção escondida (Esc)", "Barra de ticks: só você (Esc mostra tudo)" };
            ShowPlanNote(msg[_escLevel]);
        }
        private static int _escLevel;
        public static int EscLevel => _escLevel;

        private void ApplyEscLevel()
        {
            if (_miniLog != null && _escLevel >= 1) _miniLog.style.display = DisplayStyle.None;
            else if (_miniLog != null && _escLevel == 0 && _inspectedUnit != null) _miniLog.style.display = DisplayStyle.Flex;
            if (_inspectPanel != null && _escLevel >= 2) _inspectPanel.style.display = DisplayStyle.None;
            else if (_inspectPanel != null && _inspectedUnit != null) _inspectPanel.style.display = DisplayStyle.Flex;
        }

        // ── ESTADOS DE BOTÕES MANUAIS (no-ops de compatibilidade) ──
        public void SetMoveState(bool canAdd, int count, bool isPicking) { }
        public void SetAttackUnitState(bool canAdd, int count, bool isPicking = false) { }
        public void SetAttackTileState(bool canAdd, int count, bool isPicking = false) { }
        public void SetConcentrateState(bool canAdd, bool isPicking = false) { }
        public void SetIncrementState(bool canAdd, bool isPicking = false) { }
        public void SetAimState(bool canAdd, bool isPicking = false) { }

        // ── VISIBILIDADE ──
        // ── BARRA DE AÇÕES (GDD-020 §9): ícones com tecla; o hover mostra nome, custo e explicação ──
        public class ActionBarItem
        {
            public string Id, Glyph, Key, Name;
            public Func<string> Tip;          // explicação com custo (rich text)
            public Func<string> Badge;        // texto pequeno embaixo do ícone (contagem, postura...)
            public Func<bool> Enabled;
            public Func<bool> Active;         // destacado (modo armado / postura escolhida)
            public Func<string> GlyphFn;      // ícone que muda (postura atual)
            public string Icon;               // sprite em Resources/UI/Icons (a fonte do WebGL não tem os símbolos)
            public Func<string> IconFn;       // sprite que muda (postura, reação)
            public Action<bool> OnClick;      // bool = Shift pressionado
        }
        private VisualElement _actionBar;
        private Label _actionTip;
        private readonly List<(ActionBarItem item, Button btn, Label badge)> _barButtons = new List<(ActionBarItem, Button, Label)>();
        private readonly Dictionary<ActionBarItem, Label> _barGlyphs = new Dictionary<ActionBarItem, Label>();
        private readonly Dictionary<ActionBarItem, (VisualElement el, string name)> _barIcons = new Dictionary<ActionBarItem, (VisualElement, string)>();
        private bool _actionBarWanted;

        public void SetActionBarItems(List<ActionBarItem> items)
        {
            if (Root == null) return;
            if (_actionBar == null)
            {
                _actionBar = new VisualElement { name = "action-bar" };
                _actionBar.AddToClassList("bh-window"); _actionBar.AddToClassList("bh-abar");
                _actionTip = new Label { name = "action-tip", pickingMode = PickingMode.Ignore };
                _actionTip.AddToClassList("bh-window"); _actionTip.AddToClassList("bh-abar-tip");
                Root.Q<VisualElement>("root")?.Add(_actionBar);
                if (_actionBar.parent == null) Root.Add(_actionBar);
                _actionBar.parent.Add(_actionTip);
                _actionTip.style.display = DisplayStyle.None;
                // Fica logo acima da barra de ticks, acompanhando a altura dela.
                if (_ticksBar != null) _ticksBar.RegisterCallback<GeometryChangedEvent>(_ => PlaceActionBar());
            }
            _actionBar.Clear();
            _barButtons.Clear();
            foreach (var it in items)
            {
                if (it == null) { var sep = new VisualElement(); sep.AddToClassList("bh-abar-sep"); _actionBar.Add(sep); continue; }
                var item = it;
                var btn = new Button { name = "abtn-" + item.Id };
                btn.AddToClassList("bh-abtn");
                var glyph = new Label(item.Glyph) { pickingMode = PickingMode.Ignore }; glyph.AddToClassList("bh-abtn-glyph");
                var key = new Label(item.Key) { pickingMode = PickingMode.Ignore };     key.AddToClassList("bh-abtn-key");
                var badge = new Label("") { pickingMode = PickingMode.Ignore };         badge.AddToClassList("bh-abtn-badge");
                string iconName = item.IconFn != null ? item.IconFn() : item.Icon;
                if (!string.IsNullOrEmpty(iconName))
                {
                    var ic = new VisualElement { pickingMode = PickingMode.Ignore }; ic.AddToClassList("bh-abtn-icon");
                    var tx = IconTex(iconName); if (tx != null) ic.style.backgroundImage = new StyleBackground(tx);
                    btn.Add(ic); _barIcons[item] = (ic, iconName);
                    glyph.style.display = DisplayStyle.None;
                }
                btn.Add(glyph); btn.Add(key); btn.Add(badge);
                _barGlyphs[item] = glyph;
                btn.RegisterCallback<ClickEvent>(e =>
                {
                    if (item.Enabled != null && !item.Enabled()) return;
                    AudioManager.I?.Play(AudioManager.I.sfxUIClick);
                    item.OnClick?.Invoke(e.shiftKey || (Keyboard.current != null && Keyboard.current.shiftKey.isPressed));
                    RefreshActionBar();
                    ShowActionTip(item);
                });
                btn.RegisterCallback<PointerEnterEvent>(_ => ShowActionTip(item));
                btn.RegisterCallback<PointerLeaveEvent>(_ => { if (_actionTip != null) _actionTip.style.display = DisplayStyle.None; });
                _actionBar.Add(btn);
                _barButtons.Add((item, btn, badge));
            }
            RefreshActionBar();
        }

        private void ShowActionTip(ActionBarItem item)
        {
            if (_actionTip == null) return;
            string tip = item.Tip != null ? item.Tip() : "";
            _actionTip.text = $"<b>{item.Name}</b>  <color=#9fb3d9>[{item.Key}]</color>\n{tip}";
            _actionTip.style.display = DisplayStyle.Flex;
        }

        /// <summary>Espaço que os popups da barra de ticks deixam para a barra de ações (que fica logo acima).</summary>
        private float ActionBarClearance()
        {
            if (_actionBar == null || !_actionBarWanted || _barButtons.Count == 0) return 0f;
            float h = _actionBar.layout.height;
            return (float.IsNaN(h) || h <= 0f ? 60f : h) + 10f;
        }

        private void PlaceActionBar()
        {
            if (_actionBar == null) return;
            float h = (_ticksBar != null && _ticksBar.resolvedStyle.display == DisplayStyle.Flex) ? _ticksBar.layout.height : 0f;
            if (float.IsNaN(h)) h = 0f;
            _actionBar.style.bottom = 12 + h + 8;
            if (_actionTip != null) _actionTip.style.bottom = 12 + h + 8 + 62;
        }

        /// <summary>Atualiza habilitado / destacado / rótulos (chamar após mudar o plano).</summary>
        public void RefreshActionBar()
        {
            if (_actionBar == null) return;
            _actionBar.style.display = _actionBarWanted && _barButtons.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            PlaceActionBar();
            if (!_actionBarWanted && _actionTip != null) _actionTip.style.display = DisplayStyle.None;
            foreach (var (item, btn, badge) in _barButtons)
            {
                bool en = item.Enabled == null || item.Enabled();
                btn.EnableInClassList("bh-abtn--off", !en);
                btn.EnableInClassList("bh-abtn--on", item.Active != null && item.Active());
                badge.text = item.Badge != null ? item.Badge() : "";
                if (item.GlyphFn != null && _barGlyphs.TryGetValue(item, out var gl)) gl.text = item.GlyphFn();
                if (item.IconFn != null && _barIcons.TryGetValue(item, out var bi))
                {
                    string n = item.IconFn();
                    if (n != bi.name) { var tx = IconTex(n); bi.el.style.backgroundImage = tx != null ? new StyleBackground(tx) : new StyleBackground(StyleKeyword.None); _barIcons[item] = (bi.el, n); }
                }
            }
        }

        /// <summary>Teclas da barra (chamado pelo planejamento a cada frame). Devolve true se usou a tecla.</summary>
        public bool HandleActionBarKeys()
        {
            if (!_actionBarWanted || Keyboard.current == null) return false;
            var kb = Keyboard.current;
            foreach (var (item, _, _) in _barButtons)
            {
                var k = KeyFor(item.Key);
                if (k == null || !kb[k.Value].wasPressedThisFrame) continue;
                if (item.Enabled != null && !item.Enabled()) return true;
                item.OnClick?.Invoke(kb.shiftKey.isPressed);
                RefreshActionBar();
                return true;
            }
            return false;
        }

        private static Key? KeyFor(string label)
        {
            switch (label)
            {
                case "4": return Key.Digit4; case "5": return Key.Digit5; case "6": return Key.Digit6;
                case "7": return Key.Digit7; case "8": return Key.Digit8; case "9": return Key.Digit9;
                case "0": return Key.Digit0; case "-": return Key.Minus;
                default:  return null;
            }
        }

        public void SetActionBarVisible(bool v, Unit u = null)
        {
            _actionBarWanted = v;
            RefreshActionBar();
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
                _apText.text = $"<b>Stamina</b> {u.remainingBAP}/{u.stats.MaxStamina}";
        }

        // ── TEXTOS PÚBLICOS ──
        public void SetPhase(string s)
        {
            if (_phaseText != null) _phaseText.text = s;
            // O topo só aparece quando a partida escreve a fase (antes mostrava "Phase/Timer" de exemplo).
            var top = Root?.Q<VisualElement>("topbar");
            if (top != null) top.style.display = string.IsNullOrEmpty(s) ? DisplayStyle.None : DisplayStyle.Flex;
        }
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
            pop.AddToClassList("bh-window");                 // mesmo painel azul da HUD
            ps.position = Position.Absolute;
            ps.minWidth = 290; ps.paddingLeft = ps.paddingRight = 12; ps.paddingTop = ps.paddingBottom = 10;
            var head = new Label($"AÇÃO {idx + 1} · {ActionName(type, false).ToUpperInvariant()}");
            head.AddToClassList("bh-title"); head.style.marginBottom = 8;
            pop.Add(head);
            void Opt(int kind, string text, bool on, bool enabled)
            {
                var b = new Button(() => { AudioManager.I?.Play(AudioManager.I.sfxUIClick); CloseIncrementPopup(); OnIncrementPick?.Invoke(idx, kind); }) { text = text };
                b.AddToClassList("bh-intent-btn");
                if (on) b.AddToClassList("armed");
                b.style.marginLeft = b.style.marginRight = 0; b.style.marginBottom = 5; b.style.height = 36;
                b.style.fontSize = 14; b.style.unityTextAlign = TextAnchor.MiddleLeft; b.style.paddingLeft = 10;
                b.SetEnabled(enabled);
                if (!enabled) b.style.opacity = 0.45f;
                pop.Add(b);
            }
            if (type == ActionType.Attack)
            {
                Opt(0, $"{(powerOn ? "✔ " : "")}◆ Golpe poderoso  (−{Tuning.Get().staminaPowerStrike} Stamina, +{Tuning.Get().ticksPowerStrikeExtra} tick)", powerOn, true);
                Opt(1, $"{(aimOn ? "✔ " : "")}◎ Mirar  (−{Tuning.Get().staminaAim} Stamina)", aimOn, true);
            }
            if (type == ActionType.Move) Opt(13, "⌖ Mudar destino", false, true);
            Opt(10, "◀ Mover para antes", false, idx > 0);
            Opt(11, "▶ Mover para depois", false, idx < count - 1);
            Opt(12, "✕ Remover", false, true);
            var close = new Button(CloseIncrementPopup) { text = "Fechar" };
            close.AddToClassList("bh-intent-btn"); close.style.marginLeft = close.style.marginRight = 0;
            close.style.height = 30; close.style.fontSize = 12; close.style.marginTop = 2;
            pop.Add(close);
            _ticksBar.Add(pop);
            var ab = anchor.worldBound; var bb = _ticksBar.worldBound;
            ps.left = Mathf.Max(0f, ab.x - bb.x);
            ps.bottom = bb.height + 4 + ActionBarClearance();
            _incPopup = pop;
        }

        private void OpenIncrementPopup(VisualElement anchor, string title, bool powerOn, bool aimOn,
                                        bool powerEnabled, bool aimEnabled, Action<int> pick)
        {
            CloseIncrementPopup();
            if (_ticksBar == null) return;
            var pop = new VisualElement();
            var ps = pop.style;
            pop.AddToClassList("bh-window");                 // mesmo painel azul da HUD
            ps.position = Position.Absolute;
            ps.minWidth = 290; ps.paddingLeft = ps.paddingRight = 12; ps.paddingTop = ps.paddingBottom = 10;
            var head = new Label(title.ToUpperInvariant());
            head.AddToClassList("bh-title"); head.style.marginBottom = 8;
            pop.Add(head);

            VisualElement Opt(int kind, string text, bool on, bool enabled)
            {
                var b = new Button(() => { AudioManager.I?.Play(AudioManager.I.sfxUIClick); CloseIncrementPopup(); pick?.Invoke(kind); }) { text = text };
                b.AddToClassList("bh-intent-btn");
                if (on) b.AddToClassList("armed");
                b.style.marginLeft = b.style.marginRight = 0; b.style.marginBottom = 5; b.style.height = 36;
                b.style.fontSize = 14; b.style.unityTextAlign = TextAnchor.MiddleLeft; b.style.paddingLeft = 10;
                b.SetEnabled(enabled);
                if (!enabled) b.style.opacity = 0.45f;
                return b;
            }
            pop.Add(Opt(0, $"{(powerOn ? "✔ " : "")}◆ Golpe poderoso  (−{Tuning.Get().staminaPowerStrike} Stamina, +{Tuning.Get().ticksPowerStrikeExtra} tick)", powerOn, powerEnabled));
            pop.Add(Opt(1, $"{(aimOn ? "✔ " : "")}◎ Mirar  (−{Tuning.Get().staminaAim} Stamina)", aimOn, aimEnabled));
            var close = new Button(CloseIncrementPopup) { text = "Fechar" };
            close.AddToClassList("bh-intent-btn"); close.style.marginLeft = close.style.marginRight = 0;
            close.style.height = 30; close.style.fontSize = 12; close.style.marginTop = 2;
            pop.Add(close);

            _ticksBar.Add(pop);
            // Acima da barra, alinhado ao bloco clicado.
            var ab = anchor.worldBound; var bb = _ticksBar.worldBound;
            ps.left = Mathf.Max(0f, ab.x - bb.x);
            ps.bottom = bb.height + 4 + ActionBarClearance();
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
          : t == ActionType.Concentrate ? "Concentrar" : t == ActionType.Spell ? "Magia" : "Esperar";

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
                    planTotal += (_planSched != null && i < _planSched.Count)
                               ? TickRules.Duration(planUnit, _planSched[i])
                               : TickRules.Duration(planUnit, _planSeq[i], false);
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

            // Visão arcana (GDD-019 §3.1): com uma unidade sua sob o efeito, as filas inimigas aparecem até +2 ticks.
            bool foresight = false;
            foreach (var o in _tlUnits) if (o != null && !o.IsDead && _tlIsMine != null && _tlIsMine(o) && o.foresightTicks > 0) foresight = true;

            foreach (var u in _tlUnits)
            {
                if (u == null || u.IsDead) continue;
                bool mine = _tlIsMine != null && _tlIsMine(u);
                bool isPlanRow = planning && u == planUnit;
                if (_escLevel >= 3 && !mine && !isPlanRow) continue;   // Esc: só as minhas unidades

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
                        int d = (_planSched != null && i < _planSched.Count) ? TickRules.Duration(planUnit, _planSched[i])
                                                                             : TickRules.Duration(planUnit, _planSeq[i], bonus);
                        int tiles = 0;
                        if (_planSeq[i] == ActionType.Move && _planSched != null && i < _planSched.Count && _planSched[i].Index < planUnit.plannedPath.Count)
                        {
                            var dest = planUnit.plannedPath[_planSched[i].Index];
                            tiles = Mathf.Max(Mathf.Abs(dest.x - prevPos.x), Mathf.Abs(dest.y - prevPos.y));
                            prevPos = dest;
                        }
                        SpellDef planSpell = (_planSeq[i] == ActionType.Spell && _planSched != null && i < _planSched.Count)
                                           ? TickRules.SpellOf(planUnit, _planSched[i]) : null;
                        Unit planTarget = null;
                        if (_planSched != null && i < _planSched.Count)
                        {
                            int ix = _planSched[i].Index;
                            if (_planSeq[i] == ActionType.Attack && ix >= 0 && ix < planUnit.plannedAttacks.Count) planTarget = planUnit.plannedAttacks[ix].TargetUnit;
                            else if (_planSeq[i] == ActionType.Spell && ix >= 0 && ix < planUnit.plannedSpells.Count) planTarget = planUnit.plannedSpells[ix].TargetUnit;
                        }
                        string planText = planSpell != null ? SpellBlockText(planUnit, planSpell, planTarget) : BlockText(planUnit, _planSeq[i], bonus, aimed, tiles, planTarget);
                        var seg = MakeSegment(Mathf.Min(d, maxTicks - used), SegColor(_planSeq[i], bonus, planSpell),
                                              $"{idx + 1} {planText}", true);
                        if (planSpell != null && !planSpell.IsPhysical)
                        {   // borda tracejada = interrompível (GDD-019 §9.1); UI Toolkit não tem dash, então borda clara fina
                            seg.style.borderTopWidth = seg.style.borderBottomWidth = seg.style.borderLeftWidth = seg.style.borderRightWidth = 1;
                            seg.style.borderTopColor = seg.style.borderBottomColor = seg.style.borderLeftColor = seg.style.borderRightColor = new Color(1f, 1f, 1f, 0.55f);
                            seg.tooltip = $"Interrompida se tomar ≥ {Mathf.CeilToInt(planUnit.stats.MaxHP * Tuning.Get().spellInterruptHpFraction)} de dano antes de terminar. A Mana é gasta mesmo assim.";
                        }
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
                        SpellDef qSpell = q.Type == ActionType.Spell ? q.Spell.Def : null;
                        bool seen = mine || (foresight && used <= 2);
                        string label = seen ? (qSpell != null ? SpellBlockText(u, qSpell, q.Spell.TargetUnit) : BlockText(u, q.Type, q.IsBonus, q.IsAimed, 0, q.Type == ActionType.Attack ? q.Attack.TargetUnit : null))
                                     : threat ? "⚠ ataca você" : (qSpell != null && !qSpell.IsPhysical) ? SpellGlyph + " conjurando" : "ocupada";
                        if (seen && !mine) label = "👁 " + label;
                        var seg = MakeSegment(d, mine ? SegColor(q.Type, q.IsBonus, qSpell) : threat ? new Color(0.72f, 0.20f, 0.18f) : TlWait, label, mine || threat);
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
                                                       baseColor = mine ? SegColor(q.Type, q.IsBonus, qSpell) : TlWait };
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
                        mark.style.left = Length.Percent(th.endTick * 100f / maxTicks); mark.style.width = 3;
                        mark.style.marginLeft = -3; mark.style.backgroundColor = new Color(1f, 0.38f, 0.33f);
                        // Etiqueta legível no lugar do "!" solto (que ficava cortado e parecia defeito):
                        // diz quem ataca e que o golpe chega neste tick. Fica à esquerda da linha,
                        // dentro do tick em que o golpe está vindo.
                        string who = th.by != null ? th.by.unitName : "inimigo";
                        if (who.Length > 9) who = who.Substring(0, 8) + "…";
                        var ml = new Label($"⚠ {who} acerta") { pickingMode = PickingMode.Ignore };
                        ml.style.position = Position.Absolute; ml.style.top = 3; ml.style.right = 5; ml.style.fontSize = 10;
                        ml.style.whiteSpace = WhiteSpace.NoWrap;
                        ml.style.color = new Color(1f, 0.90f, 0.88f); ml.style.unityFontStyleAndWeight = FontStyle.Bold;
                        ml.style.backgroundColor = new Color(0.62f, 0.16f, 0.14f, 0.95f);
                        ml.style.paddingLeft = ml.style.paddingRight = 4; ml.style.paddingTop = ml.style.paddingBottom = 1;
                        ml.style.borderTopLeftRadius = ml.style.borderBottomLeftRadius = ml.style.borderTopRightRadius = ml.style.borderBottomRightRadius = 3;
                        mark.Add(ml);
                        track.Add(mark);
                    }
                row.Add(track);
                _ticksRows.Add(row);
                ApplyRowEvent(u);
            }
        }

        private static int ExpectedDamage(Unit u, bool bonus, bool aimed) =>
            AttackResolver.PowerDamage(u, bonus, aimed ? Mathf.RoundToInt(u.stats.DEX * Tuning.Get().aimDexMultiplier) : 0);

        /// <summary>Texto de um bloco: ícone, nome e efeito (tiles que anda, dano esperado).</summary>
        // Blocos (2026-11-13): ícone + verbo + alvo. O dano previsto fica na janela de incremento e no hover.
        private static string BlockText(Unit u, ActionType t, bool bonus, bool aimed, int tiles, Unit target = null)
        {
            string on = target != null ? " " + target.unitName : "";
            switch (t)
            {
                case ActionType.Move:   return tiles > 0 ? $"→ Andar {tiles}" : bonus ? "→ Passo rápido" : "→ Andar";
                case ActionType.Attack: return $"{(bonus ? "◆" : "⚔")}{(aimed ? "◎" : "")} {(bonus ? "Golpear" : "Atacar")}{on}";
                case ActionType.Concentrate: return "✦ Concentrar";
                default: return "… Esperar";
            }
        }

        /// <summary>Stamina como pips: dourado = livre após o plano, vazado = reservado pelo plano, apagado = gasto.</summary>
        private void BuildPips(Unit u)
        {
            _pipsRow.Clear();
            if (u == null) return;
            var lab = new Label("Stamina") { pickingMode = PickingMode.Ignore };
            lab.style.fontSize = 10; lab.style.color = new Color(0.62f, 0.69f, 0.82f); lab.style.marginRight = 5;
            _pipsRow.Add(lab);
            int max = u.stats.MaxStamina, free = Mathf.Clamp(u.remainingBAP, 0, max), have = Mathf.Clamp(u.currentStamina, 0, max);
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
                float h = Mathf.Clamp01(u.EffectiveHitChance - a.TargetUnit.stats.DodgeChance);
                hit = $" • {Mathf.RoundToInt(h * 100f)}% de acerto";
            }
            _previewLine.text = $"Plano: {n} ataque{(n > 1 ? "s" : "")} em {who} • ~{total} de dano no total{hit}";
        }

        private static Color SegColor(ActionType t, bool bonus) =>
            bonus ? TlBonus : t == ActionType.Move ? TlMove : t == ActionType.Attack ? TlAttack
          : t == ActionType.Concentrate || t == ActionType.Spell ? TlConc : TlWait;

        /// <summary>Bloco de magia na cor do elemento principal (Física = cor do Físico). GDD-019 §6.</summary>
        private static Color SegColor(ActionType t, bool bonus, SpellDef spell)
        {
            if (t != ActionType.Spell || spell == null) return SegColor(t, bonus);
            var c = ElementInfo.Color(spell.PrimaryElement);
            return Color.Lerp(c, Color.black, 0.25f);
        }

        /// <summary>Glifo das magias na HUD. A fonte desenha ✦ como "+" (confunde com "+ 3 livres"); ◈ existe no mesmo bloco de ◆/◎.</summary>
        public const string SpellGlyph = "◈";

        private static string SpellBlockText(Unit u, SpellDef s, Unit target = null)
        {
            if (s == null) return SpellGlyph + " Magia";
            return $"{SpellGlyph} {s.displayName}{(target != null ? " em " + target.unitName : "")}";
        }

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
            // O que aconteceu, no mesmo jeito dos blocos (2026-11-13): ícone + verbo + alvo.
            // "Maga — Explosão [Fogo] -> Alvo: 26" vira "◈ Explosão em Alvo"; "Fera 1 -> Jogador: 6" vira "⚔ Atacou Jogador".
            var sm = System.Text.RegularExpressions.Regex.Match(clean, @"—\s*(.+?)\s*\[.*?->\s*(.*?):");
            if (sm.Success) clean = $"{SpellGlyph} {sm.Groups[1].Value} em {sm.Groups[2].Value}";
            else clean = System.Text.RegularExpressions.Regex.Replace(clean, @"^.*?->\s*(.*?):\s*(\d+)\b.*$", "⚔ Atacou $1");
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
            t == ActionType.Move ? "M" : t == ActionType.Attack ? "A" : t == ActionType.Concentrate ? "C" : t == ActionType.Spell ? SpellGlyph : "E";
        private static Color ActionColor(ActionType t) =>
            t == ActionType.Move ? TlMove : t == ActionType.Attack ? TlAttack : t == ActionType.Concentrate || t == ActionType.Spell ? TlConc : TlWait;

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
            if (_promptPanel != null) { _promptPanel.style.display = DisplayStyle.Flex; _promptPanel.BringToFront(); }
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
            if (_promptPanel != null) { _promptPanel.style.display = DisplayStyle.Flex; _promptPanel.BringToFront(); }
        }
        public void HidePrompt() { if (_promptPanel != null) _promptPanel.style.display = DisplayStyle.None; }

        // ── REAÇÕES (modal central) ──
        private System.Action<ReactionKind> _reactionPick;
        public void ShowReactionMenu(Unit reactor, List<ReactionKind> options, float timer, System.Action<ReactionKind> onPick)
        {
            var ttl = _reactionMenu?.Q<Label>("reaction-title"); if (ttl != null) ttl.text = "REAGIR";
            _reactionPick = onPick;
            if (_actionTip != null) _actionTip.style.display = DisplayStyle.None;
            if (_reactionText != null)
                _reactionText.text = $"⚡ {reactor.unitName} pode reagir (Stamina {reactor.currentStamina}).\n" +
                    $"<size=11><color=#9fb3d9>Se o tempo acabar: {PlanningController.ReactionName(reactor.defaultReaction)}" +
                    (options.Contains(reactor.defaultReaction) || reactor.defaultReaction == ReactionKind.None ? "" : " (não cabe aqui → não reage)") + "</color></size>";
            if (_reactionTimer != null) _reactionTimer.text = timer.ToString("0.0") + "s";

            if (_reactionButtons != null)
            {
                _reactionButtons.Clear();
                _reactionButtons.style.display = DisplayStyle.Flex;
                foreach (var opt in options)
                {
                    var btn = new Button(() => _reactionPick?.Invoke(opt))
                    {
                        text = ReactionLabelFor(opt, reactor)
                    };
                    btn.AddToClassList("pg-button");
                    btn.AddToClassList("bh-reaction-btn");
                    _reactionButtons.Add(btn);
                }
                var skip = new Button(() => _reactionPick?.Invoke(ReactionKind.None))
                { text = options.Count == 1 ? "Não" : "Não reagir" };
                skip.AddToClassList("pg-button");
                _reactionButtons.Add(skip);
            }
            if (_reactionMenu != null) { _reactionMenu.style.display = DisplayStyle.Flex; _reactionMenu.BringToFront(); }
        }
        public void UpdateBonusTimer(float sec)
        {
            if (_reactionMenu != null && _reactionMenu.style.display == DisplayStyle.Flex && _reactionTimer != null)
                _reactionTimer.text = sec > 0f ? sec.ToString("0.0") + "s" : "";
            else if (_bonusTimerText != null)
                _bonusTimerText.text = sec > 0f ? sec.ToString("0.0") + "s" : "";
        }
        public void ShowChoice(string text, params (string label, System.Action onPick)[] options) => ShowChoiceTitled("ESCOLHA", text, options);

        /// <summary>Janela de escolha no canto inferior esquerdo, com título próprio (ex.: INCREMENTAR).</summary>
        public void ShowChoiceTitled(string title, string text, params (string label, System.Action onPick)[] options)
        {
            _reactionPick = null;
            var ttl = _reactionMenu?.Q<Label>("reaction-title"); if (ttl != null) ttl.text = title;
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
                    btn.AddToClassList("bh-reaction-btn");
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
        private static string ReactionLabelFor(ReactionKind k, Unit u)
        {
            string baseText = ReactionLabel(k);
            if (u == null) return baseText;
            var T = Tuning.Get();
            int cost = Unit.ReactionCost(k);
            string L(string effect) => $"<b>{baseText}</b>\n<size=12><color=#9fb3d9>{effect} · {cost} Stamina</color></size>";
            switch (k)
            {
                case ReactionKind.Dodge:         return L($"anda até {RoundManager.DodgeMoveTiles(u)} tile{(RoundManager.DodgeMoveTiles(u) == 1 ? "" : "s")} para fora do golpe");
                case ReactionKind.Block:         return L($"segura {RoundManager.BlockAmount(u)} de dano; se absorver tudo, desequilibra");
                case ReactionKind.CounterAttack: return L("golpe da arma; se acertar, interrompe a ação dele");
                case ReactionKind.Intercept:     return L("anda até o aliado, entra na frente e divide os tiles");
                case ReactionKind.TrueAim:       return L($"o tiro que ia errar acerta, +{Tuning.Get().trueAimCritBonus * 100:0}% de crítico");
                default:                         return baseText;
            }
        }
        private static string ReactionLabel(ReactionKind k) => k switch
        {
            ReactionKind.CounterAttack => "⚔ Contra-atacar",
            ReactionKind.Dodge         => "↯ Esquivar",
            ReactionKind.Intercept     => "▣ Interpor",
            ReactionKind.Block         => "■ Bloquear",
            ReactionKind.TrueAim       => "◎ Olho certeiro",
            _ => "?"
        };

        public void ShowEndScreen(string msg)
        {
            if (_endText != null) _endText.text = msg;
            if (_endPanel != null) { _endPanel.style.display = DisplayStyle.Flex; _endPanel.BringToFront(); }
        }

        private Button _endRestartButton;
        private Action _endMenuAction;

        /// <summary>
        /// Ações da tela de fim: <paramref name="restart"/> (null = sem botão; só o anfitrião reinicia)
        /// e <paramref name="menu"/> (sai da sala direito antes de voltar ao menu).
        /// </summary>
        public void SetEndActions(Action restart, Action menu, string hint = null)
        {
            _endMenuAction = menu;
            if (_endMenuButton == null) return;
            if (_endRestartButton == null)
            {
                _endRestartButton = new Button { text = "Jogar de novo" };
                _endRestartButton.AddToClassList("bh-confirm-btn");
                _endRestartButton.style.marginBottom = 8;
                var parent = _endMenuButton.parent;
                parent.Insert(parent.IndexOf(_endMenuButton), _endRestartButton);
            }
            _endRestartButton.clickable = new Clickable(() => { AudioManager.I?.Play(AudioManager.I.sfxUIConfirm); restart?.Invoke(); });
            _endRestartButton.style.display = restart != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (!string.IsNullOrEmpty(hint) && _endText != null && !_endText.text.Contains(hint)) _endText.text += "\n" + hint;
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
