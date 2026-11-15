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

        /// <summary>Jogador escolheu uma magia no menu (id do SpellCatalog).</summary>
        public Action<string> OnSpellPick;
        private Unit _spellMenuUnit;

        /// <summary>Unidade cujo catálogo de magias a tela de magias mostra.</summary>
        public void SetSpellMenuUnit(Unit u)
        {
            _spellMenuUnit = u;
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
        /// <summary>Grupos da tela de magias (2026-11-14): por elemento; pares entram no grupo do 1º elemento.</summary>
        private static readonly (SpellElement el, string icon)[] SpellGroups =
        {
            (SpellElement.Fire, "burn"), (SpellElement.Water, "soaked"), (SpellElement.Earth, "stone"),
            (SpellElement.Air, "wind"), (SpellElement.Magic, "eye"), (SpellElement.Physical, "fury"),
        };
        private static string SpellIcon(SpellElement e)
        {
            foreach (var g in SpellGroups) if (g.el == e) return g.icon;
            return "cast";
        }

        private static string Fold(string t) => UIHelper.Fold(t);

        /// <summary>
        /// Catálogo de magias (2026-11-14): grade de blocos pequenos agrupada por elemento, com busca por nome
        /// ou elemento. Cada bloco: sprite do elemento, nome, custo e ticks (pares mostram o 2º elemento).
        /// A descrição fica numa faixa de altura fixa embaixo: antes ela crescia com o texto e, como o painel
        /// é ancorado embaixo, empurrava a tela inteira para cima.
        /// </summary>
        private void OpenSpellMenu()
        {
            if (_actionTip != null) _actionTip.style.display = DisplayStyle.None;
            CloseIncrementPopup();
            var u = _spellMenuUnit;
            if (_ticksBar == null || u == null) return;
            var pop = new VisualElement { name = "spell-menu" };
            pop.AddToClassList("bh-window"); pop.AddToClassList("bh-spellmenu");

            var top = new VisualElement(); top.AddToClassList("bh-spellmenu-top");
            var head = new Label("MAGIA"); head.AddToClassList("bh-title"); head.style.flexGrow = 1; head.style.marginBottom = 0;
            var search = new TextField { name = "spell-search" }; search.AddToClassList("bh-spellmenu-search");
            search.textEdition.placeholder = "Buscar magia ou elemento…";
            var res = new Label($"Mana {u.AvailableMana} · Stamina {u.remainingBAP}") { pickingMode = PickingMode.Ignore };
            res.AddToClassList("bh-spellmenu-res");
            top.Add(head); top.Add(res); top.Add(search);
            pop.Add(top);

            const string idleText = "Toque ou passe o mouse numa magia para ver o efeito.";
            var desc = new Label(idleText) { name = "spell-desc", pickingMode = PickingMode.Ignore };
            desc.AddToClassList("bh-spellmenu-desc");

            // Tamanho e posição em % da tela, no USS (.bh-spellmenu); a lista rola dentro do espaço que sobra.
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "spell-scroll" };

            var blocks = new List<(VisualElement block, string key, VisualElement group)>();
            var groups = new List<(VisualElement group, Label count)>();
            foreach (var (el, icon) in SpellGroups)
            {
                var spells = new List<SpellDef>();
                foreach (var s in SpellCatalog.All())
                    if ((el == SpellElement.Physical && s.IsPhysical) || (!s.IsPhysical && s.PrimaryElement == el)) spells.Add(s);
                if (spells.Count == 0) continue;

                var g = new VisualElement(); g.AddToClassList("bh-spellgroup");
                var gh = new VisualElement(); gh.AddToClassList("bh-spellgroup-head");
                var gi = new VisualElement(); gi.AddToClassList("bh-spellgroup-icon");
                var gtex = IconTex(icon); if (gtex != null) gi.style.backgroundImage = new StyleBackground(gtex);
                var gl = new Label(el == SpellElement.Physical ? "Física" : ElementInfo.Name(el)); gl.AddToClassList("bh-spellgroup-name");
                gl.style.color = Color.Lerp(ElementInfo.Color(el), Color.black, 0.55f);   // escuro: tons claros (Ar) sumiam no pergaminho
                var gc = new Label(""); gc.AddToClassList("bh-spellgroup-count");
                gh.Add(gi); gh.Add(gl); gh.Add(gc);
                var grid = new VisualElement(); grid.AddToClassList("bh-spellgrid");
                g.Add(gh); g.Add(grid);
                groups.Add((g, gc));

                foreach (var s in spells)
                {
                    var sp = s;
                    bool ok = u.CanFitSpell(s, out string why);
                    int ticks = TickRules.Duration(u, ActionType.Spell, false, s);
                    var b = new Button(() => { AudioManager.I?.PlayNamed("sfx_spell_pick", AudioManager.I.sfxUIClick); CloseIncrementPopup(); OnSpellPick?.Invoke(sp.id); })
                    { name = "spell-" + s.id };
                    b.AddToClassList("bh-spellblock");
                    b.style.borderLeftColor = ElementInfo.Color(s.PrimaryElement);
                    var ic = new VisualElement { pickingMode = PickingMode.Ignore }; ic.AddToClassList("bh-spellblock-icon");
                    var tx = IconTex(SpellIcon(s.IsPhysical ? SpellElement.Physical : s.PrimaryElement)); if (tx != null) ic.style.backgroundImage = new StyleBackground(tx);
                    var nm = new Label(s.displayName) { pickingMode = PickingMode.Ignore }; nm.AddToClassList("bh-spellblock-name");
                    var meta = new Label($"{s.CostText} · {ticks}t") { pickingMode = PickingMode.Ignore }; meta.AddToClassList("bh-spellblock-meta");   // quebra linha em bloco estreito
                    b.Add(ic); b.Add(nm); b.Add(meta);
                    if (s.IsPair && s.elements.Length > 1)
                    {
                        var dot = new VisualElement { pickingMode = PickingMode.Ignore }; dot.AddToClassList("bh-spellblock-pair");
                        var tx2 = IconTex(SpellIcon(s.elements[1])); if (tx2 != null) dot.style.backgroundImage = new StyleBackground(tx2);
                        dot.tooltip = ElementInfo.Name(s.elements[1]);
                        b.Add(dot);
                    }
                    if (!ok) b.AddToClassList("bh-spellblock--off");
                    b.SetEnabled(ok);
                    string eff = $"<b>{s.displayName}</b> <color=#5f6072>[{s.MontageText()}]</color> — {s.CostText}, {ticks} tick{(ticks == 1 ? "" : "s")}. " + SpellResolver.Describe(u, s);
                    if (!ok) eff = $"<color=#a82c2c>Não cabe: {why}.</color>  " + eff;
                    if (!s.IsPhysical) eff += $"  <color=#5a3fa8>Interrompível: {SpellResolver.InterruptThreshold(u)}+ de dano.</color>";
                    var captured = eff;
                    b.RegisterCallback<PointerEnterEvent>(_ => desc.text = captured);
                    b.RegisterCallback<FocusInEvent>(_ => desc.text = captured);
                    grid.Add(b);
                    string names = s.displayName + " " + s.MontageText() + " " + (s.IsPhysical ? "fisica" : "");
                    foreach (var e in s.elements ?? System.Array.Empty<SpellElement>()) names += " " + ElementInfo.Name(e);
                    blocks.Add((b, Fold(names), g));
                }
                scroll.Add(g);
            }

            void Filter(string q)
            {
                string f = Fold(q).Trim();
                var shown = new Dictionary<VisualElement, int>();
                foreach (var (block, key, group) in blocks)
                {
                    bool on = f.Length == 0 || key.Contains(f);
                    block.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                    shown.TryGetValue(group, out int n); shown[group] = n + (on ? 1 : 0);
                }
                foreach (var (group, count) in groups)
                {
                    shown.TryGetValue(group, out int n);
                    group.style.display = n > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                    count.text = $"{n}";
                }
            }
            search.RegisterValueChangedCallback(e => Filter(e.newValue));
            Filter("");

            pop.Add(scroll);
            pop.Add(desc);
            var close = new Button(CloseIncrementPopup) { text = "Fechar" };
            close.AddToClassList("bh-intent-btn"); close.style.marginLeft = close.style.marginRight = 0;
            close.style.height = 30; close.style.fontSize = 12; close.style.marginTop = 4;
            pop.Add(close);
            (Root.Q<VisualElement>("root") ?? Root).Add(pop);
            _incPopup = pop;
        }

        protected override void Bind()
        {
            // Celular (2026-11-14): classe para layout de toque (intenções à esquerda, botões maiores).
            if (MobileHUD.Active) Root?.AddToClassList("bh-mobile");
            // Tela mais quadrada que 16:9 (tablet 4:3, janela estreita): painéis laterais ficam mais largos, senão a
            // grade de ações e o ROUND encolhem a ponto de os rótulos não caberem (2026-11-15).
            Root?.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                var lay = Root.layout;
                if (lay.height > 1f) Root.EnableInClassList("bh-narrow", lay.width / lay.height < 1.7f);
            });
            var r = Root;
            _phaseText        = r.Q<Label>("phase-text");
            _timerText        = r.Q<Label>("timer-text");
            _cameraModeText   = r.Q<Label>("camera-mode-text");
            _compassRose      = r.Q<VisualElement>("compass-rose");
            KeepAspect(r.Q<VisualElement>("compass"), 1.25f);         // rosa quadrada + linha do modo da câmera
            KeepAspect(r.Q<VisualElement>("compass-rose"), 1f);
            _compassLabels    = new[] { r.Q<Label>("compass-n"), r.Q<Label>("compass-e"),
                                        r.Q<Label>("compass-s"), r.Q<Label>("compass-w") };
            // Toque/clique na bússola gira a vista (GDD-022): metade esquerda = anti-horário, direita = horário.
            var compass = r.Q<VisualElement>("compass");
            if (compass != null)
            {
                compass.pickingMode = PickingMode.Position;
                compass.RegisterCallback<ClickEvent>(e =>
                {
                    var cam = FindAnyObjectByType<CameraController>();
                    if (cam != null) cam.CycleView(e.localPosition.x > compass.layout.width * 0.5f);
                });
            }

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
            // Intenções automáticas (Investir, Golpear e recuar, Recuar) saíram em 2026-11-15: só o modo livre.
            for (int n = 1; n <= 5; n++) { var ib = r.Q<Button>("btn-intent-" + n); if (ib != null) ib.style.display = DisplayStyle.None; }
            HookUiPointerDown();
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
            // Celular (2026-11-15): a dica é de teclado ([1], [Enter], [Esc]) — fica escondida.
            band.style.display = MobileHUD.Active ? DisplayStyle.None : DisplayStyle.Flex;
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
            if (GameLog.PointerOver || MobileHUD.PointerOver) return true;          // painel/botão de LOG (IMGUI) por cima do jogo
            if (Root == null || Root.panel == null) return false;
            HookUiPointerDown();
            if (!PointerInput.Available) return false;
            Vector2 screen = PointerInput.Position;
            Vector2 local = UIHelper.ScreenToPanel(Root.panel, screen);
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
            // Inspeção some enquanto o jogador move a câmera e volta quando ela para (2026-11-14).
            if (_inspectPanel != null)
                _inspectPanel.style.visibility = Time.unscaledTime - CameraController.LastManualMoveTime < 0.3f
                    ? Visibility.Hidden : Visibility.Visible;
            if (_inspectPanel == null || _inspectedUnit == null || _inspectPanel.resolvedStyle.display != DisplayStyle.Flex) return;
            var cam = Camera.main; var panel = _inspectPanel.panel; var parent = _inspectPanel.parent;
            if (cam == null || panel == null || parent == null) return;
            float w = _inspectPanel.layout.width, h = _inspectPanel.layout.height;
            float W = parent.layout.width, H = parent.layout.height;
            if (float.IsNaN(w) || float.IsNaN(H) || w <= 0f || W <= 0f) return;
            // Celular (2026-11-15): fixa no canto de cima à esquerda — perto da unidade cobria o mapa.
            if (MobileHUD.Active)
            {
                _inspectPanel.style.left = W * 0.01f;
                _inspectPanel.style.top = H * 0.02f;
                return;
            }
            Vector2 Screen(Unit x) => RuntimePanelUtils.CameraTransformWorldToPanel(panel, x.HeadWorld, cam);
            Vector2 me = Screen(_inspectedUnit);
            float gap = W * 0.02f, top = H * 0.11f, bottomReserve = H * 0.18f;   // em % da tela: em px fixos a bolha saía por baixo no celular   // topo: barra de fase e dica   // topo: barra de fase; baixo: barras de ação e ticks
            var cands = new List<Vector2>
            {
                new Vector2(me.x - gap - w, me.y - h * 0.3f),   // à esquerda da unidade
                new Vector2(me.x - gap - w, me.y - h),          // à esquerda, acima
                new Vector2(me.x - gap - w, me.y + 10f),        // à esquerda, abaixo
                new Vector2(me.x + gap,     me.y - h * 0.3f),   // à direita da unidade
                new Vector2(W * 0.01f, top),                          // colada na borda esquerda, no topo
                new Vector2(12f, H - bottomReserve - h),        // colada na borda esquerda, embaixo
            };
            Vector2 best = cands[0]; int bestScore = int.MaxValue;
            for (int i = 0; i < cands.Count; i++)
            {
                var c = new Vector2(Mathf.Clamp(cands[i].x, 12f, Mathf.Max(12f, W - w - 12f)), Mathf.Clamp(cands[i].y, Mathf.Min(top, Mathf.Max(0f, H - h)), Mathf.Max(Mathf.Min(top, Mathf.Max(0f, H - h)), Mathf.Min(H - bottomReserve - h, H - h))));
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
            // Mesmo deslizando (ex.: a tela mudou de tamanho), nunca fica fora da tela.
            _inspectPanel.style.left = Mathf.Clamp(Mathf.Lerp(cx, best.x, k), 0f, Mathf.Max(0f, W - w));
            _inspectPanel.style.top = Mathf.Clamp(Mathf.Lerp(cy, best.y, k), 0f, Mathf.Max(0f, H - h));
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
                if (u.IsDead) _unitNameText.text += "  <color=#6f6a5e>+</color>";
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
            var row = new VisualElement();
            foreach (var c in cls.Split(' ')) if (c.Length > 0) row.AddToClassList(c);   // uma classe por chamada
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
                $"{u.currentStamina}/{s.MaxStamina}", $"Stamina: paga reações, golpe poderoso, Mirar e metade das magias Físicas. Volta 1 a cada {s.StaminaRegenTicks} ticks; Respirar dá +{T.breatheStamina}."));

            // Combate em ícones. No celular, compacta: só aparecem na ficha aberta (2026-11-15).
            bool compactPhone = MobileHUD.Active && !_inspectExpanded;
            var combat = new VisualElement(); combat.AddToClassList("bh-igrid");
            if (compactPhone) combat.style.display = DisplayStyle.None;
            var w = WeaponCatalog.Get(u.weaponId);
            combat.Add(Chip("dmg", $"{s.PhysicalDamage}", $"Golpe: {s.DamagePerTile:0.#} por tile × {s.ShapeTiles} tiles ({AttackShape.Describe(w)}) num alvo inteiro."));
            combat.Add(Chip("range", $"{u.EffectiveAttackRange}", "Alcance da arma em células (uma unidade ocupa 3)."));
            combat.Add(Chip("hit", $"{u.EffectiveHitChance * 100:0}%", "Chance de acertar (DEX)."));
            combat.Add(Chip("dodge", $"{s.DodgeChance * 100:0}%", $"Chance de esquiva (AGI); a esquiva tira {AttackResolver.DodgeFrac(u) * 100:0}% dos tiles do golpe."));
            combat.Add(Chip("def", $"−{s.PhysicalDefense}", $"Defesa física fixa por golpe (VIT × {F.armorPerVIT:0.##} + armadura). Bloquear soma {RoundManager.BlockAmount(u)}."));
            combat.Add(Chip("mdef", $"−{s.MagicDefense}", $"Defesa contra magia (WIS + armadura {s.ArmorMag})."));
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
                    eq.Add(Chip("stone", wt > lim ? $"<color=#a82c2c>{wt}/{lim}</color>" : $"{wt}/{lim}",
                        $"Peso {wt} de {lim} (2 + VIT/2).{(wt > lim ? $" {wt - lim} acima: −{wt - lim} na andada e no Esquivar, −{(wt - lim) * 4}% de esquiva." : "")}"));
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
                A("vit", "VIT", s.VIT, "Vitalidade: vida, defesa física fixa, Bloquear, Stamina, limite de peso.");
                A("dex", "DEX", s.DEX, $"Destreza: −1 tick em magias, Concentrar e Respirar a cada {Tuning.Get().dexPointsPerTick} pontos; acerto, crítico, dano à distância, Mirar, iniciativa, Stamina.");
                A("agi", "AGI", s.AGI, $"Agilidade: andada, esquiva (fração de tiles), Esquivar anda {RoundManager.DodgeMoveTiles(u)} tiles, anda antes de inimigo com menos AGI, Stamina mais rápida acima de {Tuning.Get().agiRegenFrom}, iniciativa.");
                A("int", "INT", s.INT, "Inteligência: dano, cura e alcance das magias Elementais (não das Físicas).");
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
            if (u.stoneSkinTicks > 0) St("stone", $"{u.stoneSkinTicks}t", $"Pele de pedra: defesa física fixa ×{u.stoneSkinMult:0.#}.");
            if (u.furyReady > 0f) St("fury", "Fúria", $"Próximo golpe ×{u.furyReady:0.#}.");
            if (u.curveReady) St("eye", "Curva", "Próximo ataque à distância segue o alvo e contorna obstáculos.");
            if (u.foresightTicks > 0) St("cast", $"{u.foresightTicks}t", "Visão arcana: vê a fila inimiga mais à frente.");
            if (u.IsDulled) St("dulled", "Embotado", $"Pouca Mana: −{T.dullRangePenalty} de alcance (ataques e magias) e −{T.dullHitPenalty * 100:0}% de acerto.");
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
        /// Esc alterna o quanto a tela mostra (2026-11-14). Padrão (0): só a inspeção — o histórico e as
        /// ações das outras unidades ficam escondidos. 1: mostra tudo. 2: esconde também a inspeção.
        /// </summary>
        public void GoBack()
        {
            _escLevel = (_escLevel + 1) % 3;
            ApplyEscLevel();
            RebuildTickRows();
            string[] msg = { "Só a inspeção (Esc mostra tudo)", "Mostrando tudo: histórico e ações das outras unidades (Esc)", "Tudo escondido (Esc volta ao padrão)" };
            ShowPlanNote(msg[_escLevel]);
        }
        /// <summary>O histórico e as linhas das outras unidades aparecem? (só no nível "mostrando tudo")</summary>
        public static bool ShowsOthers => _escLevel == 1;
        private static int _escLevel;
        public static int EscLevel => _escLevel;

        private void ApplyEscLevel()
        {
            if (_miniLog != null && _escLevel != 1) _miniLog.style.display = DisplayStyle.None;
            else if (_miniLog != null && _inspectedUnit != null) _miniLog.style.display = DisplayStyle.Flex;
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
        private VisualElement _actionBar, _stanceRow;
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
            // Dois polegares (2026-11-14): ações em grade à esquerda; Postura e Reação à direita,
            // dentro do painel de intenções, logo acima de Confirmar.
            if (_stanceRow == null && _intentsPanel != null)
            {
                _stanceRow = new VisualElement { name = "stance-row" };
                _stanceRow.AddToClassList("bh-stance");
                _intentsPanel.Insert(Mathf.Min(1, _intentsPanel.childCount), _stanceRow);
            }
            _actionBar.Clear();
            _stanceRow?.Clear();
            _barButtons.Clear();
            foreach (var it in items)
            {
                if (it == null) continue;   // separadores saíram: a grade já agrupa
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
                string shortName = item.Name ?? "";
                int cut = shortName.IndexOf(" ("); if (cut > 0) shortName = shortName.Substring(0, cut);
                if (item.Id == "reaction") shortName = "Reação";
                var cap = new Label(shortName) { pickingMode = PickingMode.Ignore }; cap.AddToClassList("bh-abtn-cap"); UIHelper.FitText(cap);
                btn.Add(glyph); btn.Add(key); btn.Add(badge); btn.Add(cap);
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
                bool stance = item.Id == "posture" || item.Id == "reaction";
                KeepAspect(btn, stance ? 0.5f : 0.82f);
                if (stance && _stanceRow != null) { btn.AddToClassList("bh-abtn--stance"); _stanceRow.Add(btn); }
                else _actionBar.Add(btn);
                _barButtons.Add((item, btn, badge));
            }
            RefreshActionBar();
        }

        private void ShowActionTip(ActionBarItem item)
        {
            if (_actionTip == null) return;
            string tip = item.Tip != null ? item.Tip() : "";
            _actionTip.text = $"<b>{item.Name}</b>  <color=#5f6072>[{item.Key}]</color>\n{tip}";
            _actionTip.style.display = DisplayStyle.Flex;
        }

        /// <summary>Espaço que os popups da barra de ticks deixam para a barra de ações (que fica logo acima).</summary>
        private static void KeepAspect(VisualElement e, float ratio) => UIHelper.KeepAspect(e, ratio);

        private float ActionBarClearance()
        {
            return 0f;   // a barra de ações saiu de cima dos ticks (grade no canto esquerdo)
        }

        private void PlaceActionBar()
        {
            if (_actionBar == null) return;
            // A grade fica fixa no canto inferior esquerdo (USS); a dica aparece logo acima dela.
            float h = _actionBar.layout.height;
            if (float.IsNaN(h) || h <= 0f) h = 150f;
            if (_actionTip != null) _actionTip.style.bottom = 30 + h + 8;
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
            // Ações em sequência a partir do 1 (2026-11-15): 1 Andar, 2 Atacar, 3 Mirar livre…
            if (label != null && label.Length == 1 && label[0] >= '1' && label[0] <= '9') return Key.Digit1 + (label[0] - '1');
            return label == "0" ? Key.Digit0 : (Key?)null;
        }

        /// <summary>Fecha a janela aberta (tela de magias, incremento). Devolve true se havia uma: o Esc para nela.</summary>
        public bool CloseOpenPopup()
        {
            if (_incPopup == null) return false;
            CloseIncrementPopup();
            return true;
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
            // No celular o canto de cima é da inspeção (2026-11-15): o topo com fase e tempo não aparece.
            if (top != null) top.style.display = string.IsNullOrEmpty(s) || MobileHUD.Active ? DisplayStyle.None : DisplayStyle.Flex;
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
            _ticksRowsHeader.style.color = note ? new Color(0.56f, 0.35f, 0.05f) : new Color(0.14f, 0.16f, 0.27f);
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
                _ticksRowsHeader.style.fontSize = 11; _ticksRowsHeader.style.color = new Color(0.14f, 0.16f, 0.27f);
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
                _previewLine.style.fontSize = 11; _previewLine.style.color = new Color(0.37f, 0.38f, 0.45f);
                _previewLine.style.marginTop = 4; _previewLine.style.whiteSpace = WhiteSpace.Normal;
                _ticksBar.Add(_headerRow);
                _ticksBar.Add(_ticksRows);
                _ticksBar.Add(_previewLine);
                _ticksBar.schedule.Execute(FadeRowFlashes).Every(120);
            }
            SetPlanEditorVisible(false);
            _ticksRows.Clear();
            _segTouched.Clear();
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
            {
                int Rh = Mathf.Max(1, Tuning.Get().ticksPerRound);
                int roundN = _tlTick / Rh + 1, k = ((_tlTick % Rh) + Rh) % Rh + 1;
                _headBase = TimelinePlanning ? $"ROUND {roundN}  •  PLANEJAMENTO" : $"ROUND {roundN}  •  TICK {k}/{Rh}";
            }
            if (planning)
            {
                int carriedNow = planUnit.carriedTicks;
                int free = Mathf.Max(0, maxTicks - carriedNow - planTotal);
                int over = Mathf.Max(0, carriedNow + planTotal - maxTicks);
                _headBase += $"  •  PLANO {Mathf.Min(planTotal, maxTicks - carriedNow)}/{maxTicks - carriedNow} ticks  " +
                             (over > 0 ? $"(↷ {over} no próximo round)" : $"({free} livre{(free == 1 ? "" : "s")})");
                _headCosts = $"Custo: Andar {TickRules.Duration(planUnit, ActionType.Move, false)} tick  •  " +
                             $"Atacar {TickRules.Duration(planUnit, ActionType.Attack, false)}  •  Golpe poderoso {TickRules.Duration(planUnit, ActionType.Attack, true)}";
            }
            else _headCosts = null;
            RefreshHeader();
            // ── Régua do round (2026-11-15): os ticks deste round (1..R) e os do próximo, com uma linha no meio.
            // Fica claro quando o round acaba: o que passa da linha cai no próximo round.
            var TB = Tuning.Get();
            int R = Mathf.Max(1, TB.ticksPerRound);
            int cols = R + Mathf.Clamp(TB.tickBarNextRoundTicks, 0, R);   // só um pedaço do próximo round: blocos legíveis
            // Anima só no planejamento (bloco entrando/ficando mais caro empurra os outros). Durante o tick a barra
            // se reorganiza a cada tick e a animação deixaria os blocos fora da régua por um instante.
            _growSeconds = TimelinePlanning ? Mathf.Max(0f, TB.tickBarPushAnimSeconds) : 0f;
            bool flagCross = TB.tickBarFlagRoundCrossing;
            bool planPhase = TimelinePlanning;
            int inRound = ((_tlTick % R) + R) % R;
            // Planejando, a fila começa no 1º tick do round. Resolvendo, _tlTick já avançou (LogTimeline vem depois
            // do AdvanceGlobalTick): inRound é o próximo tick a rodar e a fila começa nele.
            int start = planPhase ? 0 : inRound;
            int nowCol = start;
            var ruler = new VisualElement { pickingMode = PickingMode.Ignore };
            ruler.style.flexDirection = FlexDirection.Row; ruler.style.marginBottom = 2;
            var rpad = new Label("") { pickingMode = PickingMode.Ignore };
            rpad.style.width = 78; rpad.style.fontSize = 9; rpad.style.unityTextAlign = TextAnchor.MiddleLeft;
            rpad.style.color = new Color(0.37f, 0.38f, 0.45f);
            ruler.Add(rpad);
            var rtrack = new VisualElement { pickingMode = PickingMode.Ignore };
            rtrack.style.flexDirection = FlexDirection.Row; rtrack.style.flexGrow = 1; rtrack.style.position = Position.Relative;
            for (int k = 0; k < cols; k++)
            {
                bool next = k >= R, past = k < nowCol, now = k == nowCol;
                var rl = new Label(next ? (k == R ? "próx. 1" : (k - R + 1).ToString()) : (k + 1).ToString()) { pickingMode = PickingMode.Ignore };
                rl.style.flexGrow = 1; rl.style.flexBasis = 0; rl.style.fontSize = 9; rl.style.height = 12;
                rl.style.unityTextAlign = TextAnchor.MiddleCenter; rl.style.marginRight = 0;
                rl.style.color = now ? new Color(0.06f, 0.08f, 0.16f) : next ? new Color(0.55f, 0.52f, 0.47f) : past ? new Color(0.62f, 0.60f, 0.56f) : new Color(0.30f, 0.31f, 0.38f);
                rl.style.unityFontStyleAndWeight = now ? FontStyle.Bold : FontStyle.Normal;
                if (now) { rl.style.backgroundColor = new Color(1f, 0.85f, 0.45f, 0.85f); rl.style.borderTopLeftRadius = rl.style.borderTopRightRadius = 3; }
                rl.style.borderBottomWidth = now ? 2 : 1;
                rl.style.borderBottomColor = next ? new Color(0.62f, 0.56f, 0.46f) : new Color(0.50f, 0.40f, 0.27f);
                rtrack.Add(rl);
            }
            if (cols > R) rtrack.Add(RoundDivider(R, cols));
            ruler.Add(rtrack);
            _ticksRows.Add(ruler);

            BuildPips(planning ? planUnit : null);
            UpdatePreviewLine(planning ? planUnit : null);

            // Quem é "eu" e quem é aliado: aliados mostram a fila inteira sempre (2026-11-15).
            Unit meRef = null;
            foreach (var o in _tlUnits) if (o != null && !o.IsDead && _tlIsMine != null && _tlIsMine(o)) { meRef = o; break; }
            if (meRef == null) meRef = planUnit;
            bool IsMine(Unit o) => _tlIsMine != null && _tlIsMine(o);
            bool IsAlly(Unit o) => TB.tickBarShowAllies && o != null && !IsMine(o) && meRef != null && o != meRef && !o.IsHostileTo(meRef);

            // Visão mágica (Visão arcana): com uma unidade sua sob o efeito, vê as ações de TODAS as unidades
            // durante o tick e, no planejamento, o que estão preparando (feras: plano previsto pela mesma IA).
            bool vision = false;
            foreach (var o in _tlUnits) if (o != null && !o.IsDead && IsMine(o) && o.foresightTicks > 0) vision = true;
            bool visionAll = vision && TB.visionRevealsAllQueues;     // opção da sala
            bool visionPlan = vision && TB.visionShowsPlanning;

            // Ameaças: ataques inimigos em unidades suas — só os que você enxerga (a ação atual, ou tudo com visão).
            var threats = new List<(Unit target, int endTick, Unit by)>();
            foreach (var o in _tlUnits)
            {
                if (o == null || o.IsDead || IsMine(o) || IsAlly(o)) continue;
                int at = 0, qi = 0;
                foreach (var q in o.queue)
                {
                    at += q.Remaining;
                    if ((qi == 0 || visionAll) && q.Type == ActionType.Attack && q.Attack.TargetUnit != null && IsMine(q.Attack.TargetUnit))
                        threats.Add((q.Attack.TargetUnit, at, o));
                    qi++;
                }
            }

            foreach (var u in _tlUnits)
            {
                if (u == null || u.IsDead) continue;
                bool mine = IsMine(u);
                bool ally = IsAlly(u);
                bool isPlanRow = planning && u == planUnit;
                bool enemy = !mine && !ally && !isPlanRow;
                // Inimigos: durante o tick todos veem a ação atual; no planejamento só com visão (Esc ainda mostra tudo).
                if (enemy && planPhase && !visionPlan && !ShowsOthers) continue;
                if (enemy && !planPhase && !visionAll && !TB.tickBarEnemyCurrentAction && !ShowsOthers) continue;
                bool fullView = mine || ally || isPlanRow || visionAll;

                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Stretch;
                row.style.marginBottom = 3;

                var name = new Label((visionAll && enemy ? "👁 " : "") + u.unitName) { pickingMode = PickingMode.Ignore };
                name.style.width = 78; name.style.fontSize = 11; name.style.overflow = Overflow.Hidden;
                name.style.unityTextAlign = TextAnchor.MiddleLeft;
                name.style.color = enemy ? new Color(0.55f, 0.16f, 0.14f) : (u.IsBusy || isPlanRow) ? new Color(0.14f, 0.16f, 0.27f) : new Color(0.13f, 0.45f, 0.27f);
                name.style.unityFontStyleAndWeight = mine ? FontStyle.Bold : FontStyle.Normal;
                row.Add(name);

                var track = new VisualElement { pickingMode = PickingMode.Ignore };
                track.style.flexDirection = FlexDirection.Row; track.style.flexGrow = 1; track.style.height = 28;
                track.style.position = Position.Relative;
                string uid = UnitRegistry.GetId(u).ToString();
                int used = start;          // coluna absoluta (0..cols) onde o próximo bloco começa
                int segN = 0;
                bool firstSeg = true;

                if (start > 0)
                {   // Ticks que já passaram neste round.
                    var past = MakeSegment(start, new Color(0.80f, 0.74f, 0.62f, 0.45f), "", false);
                    past.style.borderTopWidth = past.style.borderBottomWidth = past.style.borderLeftWidth = past.style.borderRightWidth = 0;
                    track.Add(past);
                    AnimateGrow(past, uid + ":past", start, false);
                }

                // Coloca um bloco, marcando quando passa da linha do round (↷ = vai para o próximo round).
                VisualElement Put(VisualElement seg, int d, string key, bool enter)
                {
                    int s0 = used, e0 = used + d;
                    if (flagCross && e0 > R && s0 < cols)
                    {
                        var l = seg.childCount > 0 ? seg.ElementAt(0) as Label : null;
                        if (l != null && !l.text.StartsWith("↷")) l.text = "↷ " + l.text;
                        string why = s0 >= R ? "Fica inteira para o próximo round." : $"Começa neste round e termina no próximo (tick {e0 - R} dele).";
                        seg.tooltip = string.IsNullOrEmpty(seg.tooltip) ? why : seg.tooltip + "\n" + why;
                        if (s0 >= R) seg.style.opacity = 0.78f;
                    }
                    track.Add(seg);
                    AnimateGrow(seg, key, Mathf.Max(1, d), enter);
                    used += d;
                    segN++;
                    return seg;
                }

                if (isPlanRow)
                {
                    // Plano em edição: blocos clicáveis (mesmo clique de antes p/ golpe poderoso e passo rápido).
                    Vector2Int prevPos = planUnit.PlanStartAnchor;
                    int planUsed = 0;
                    // O que veio do round anterior aparece primeiro, travado: o plano novo começa depois.
                    if (planUnit.carriedTicks > 0)
                    {
                        int cd = Mathf.Min(planUnit.carriedTicks, cols - used);
                        var carried = MakeSegment(cd, TlWait, flagCross ? "↷ do round anterior" : "em andamento", true);
                        carried.tooltip = "Ação que atravessou o round: termina primeiro, e o seu plano novo entra depois dela.";
                        Put(carried, cd, uid + ":carried", false);
                        planUsed += cd;
                    }
                    for (int i = 0; i < _planSeq.Count && used < cols; i++)
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
                        int dd = Mathf.Min(d, cols - used);
                        var seg = MakeSegment(dd, SegColor(_planSeq[i], bonus, planSpell), $"{idx + 1} {planText}", true);
                        if (planSpell != null && !planSpell.IsPhysical)
                        {   // borda clara fina = interrompível (GDD-019 §9.1)
                            seg.style.borderTopWidth = seg.style.borderBottomWidth = 1;
                            seg.style.borderTopColor = seg.style.borderBottomColor = new Color(1f, 1f, 1f, 0.55f);
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
                        // Bloco novo entra crescendo do zero e empurra os outros; bloco que ficou mais caro cresce.
                        Put(seg, dd, $"plan:{uid}:{i}:{_planSeq[i]}", true);
                        _seqChips.Add(seg);
                        planUsed += d;
                    }
                    int planFree = Mathf.Max(0, maxTicks - planUsed);
                    if (used < cols)
                    {
                        var rest = MakeSegment(cols - used, TlEmpty, planFree > 0 ? $"+ {planFree} livre{(planFree == 1 ? "" : "s")}" : "", false);
                        ((Label)rest.ElementAt(0)).style.color = new Color(0.55f, 0.62f, 0.80f);
                        track.Add(rest);
                        AnimateGrow(rest, uid + ":rest", cols - used, false);
                        used = cols;
                    }
                }
                else
                {
                    // Fila comprometida (o que a unidade está fazendo e vai fazer).
                    int qi = 0, qOff = 0;
                    bool hiddenTail = false;
                    foreach (var q in u.queue)
                    {
                        bool fromPrev = planPhase && qOff < u.carriedTicks;   // começou (ou foi empurrada) no round anterior
                        qOff += Mathf.Max(0, q.Remaining);
                        if (used >= cols) break;
                        int d = Mathf.Min(q.Remaining, cols - used);
                        if (d <= 0) { qi++; continue; }
                        bool seen = fullView || qi == 0;   // sem visão, só a ação atual de um inimigo aparece
                        if (!seen) { hiddenTail = true; break; }
                        bool threat = false;
                        foreach (var th in threats) if (th.by == u && q.Type == ActionType.Attack && q.Attack.TargetUnit == th.target) threat = true;
                        SpellDef qSpell = q.Type == ActionType.Spell ? q.Spell.Def : null;
                        string label = qSpell != null ? SpellBlockText(u, qSpell, q.Spell.TargetUnit)
                                     : BlockText(u, q.Type, q.IsBonus, q.IsAimed, 0, q.Type == ActionType.Attack ? q.Attack.TargetUnit : null);
                        if (threat && !mine) label = "⚠ " + label;
                        if (flagCross && fromPrev) label = "↷ " + label;   // veio do round anterior
                        bool colored = mine || ally || threat || visionAll;
                        var baseCol = threat && !mine ? new Color(0.72f, 0.20f, 0.18f) : colored ? SegColor(q.Type, q.IsBonus, qSpell) : TlWait;
                        var seg = MakeSegment(d, baseCol, label, mine || threat);
                        if (threat && !mine) ((Label)seg.ElementAt(0)).style.color = new Color(1f, 0.82f, 0.80f);
                        if (enemy && !visionAll) seg.tooltip = "Ação que este inimigo está fazendo agora. O resto da fila dele fica escondido (Visão arcana revela).";
                        Put(seg, d, $"{uid}:q{qi}:{q.Type}", false);
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
                            _rowRefs[u] = new RowRef { seg = seg, label = (Label)seg.ElementAt(0), defaultText = ((Label)seg.ElementAt(0)).text, baseColor = baseCol };
                            firstSeg = false;
                        }
                        qi++;
                    }
                    if (hiddenTail && used < cols)
                    {   // O resto da fila de um inimigo: existe, mas não dá para ver o quê.
                        int left = 0; int k2 = 0;
                        foreach (var q in u.queue) { if (k2++ >= qi) left += q.Remaining; }
                        int d = Mathf.Min(Mathf.Max(1, left), cols - used);
                        var hid = MakeSegment(d, TlWait, "?", false);
                        hid.style.opacity = 0.6f;
                        hid.tooltip = "Fila escondida. Com Visão arcana você vê as ações de todas as unidades.";
                        Put(hid, d, uid + ":hidden", false);
                    }

                    // Visão no planejamento: o que a unidade está preparando (fera: plano previsto pela própria IA).
                    if (planPhase && visionPlan && enemy && !u.IsBusy && used < cols)
                    {
                        foreach (var (pt, pd, ptext, pcol) in PredictPlan(u))
                        {
                            if (used >= cols) break;
                            int d = Mathf.Min(pd, cols - used);
                            var seg = MakeSegment(d, pcol, "👁 " + ptext, false);
                            seg.style.opacity = 0.85f;
                            seg.tooltip = BeastAI.Controls(u) ? "Previsão: é o que a fera vai fazer com o mapa como está agora. Pode mudar se o seu plano mudar o que ela vê."
                                                             : "Plano que este jogador já confirmou.";
                            Put(seg, d, $"{uid}:pred{segN}:{pt}", true);
                        }
                    }

                    if (used < cols)
                    {
                        bool deciding = !u.IsBusy && (!enemy || !planPhase || !visionPlan);
                        string restText = deciding ? (planPhase ? "decide agora" : "livre") : "";
                        var rest = MakeSegment(cols - used, TlEmpty, restText, false);
                        if (deciding)
                        {
                            var l = (Label)rest.ElementAt(0);
                            l.style.color = new Color(0.52f, 0.90f, 0.60f);
                            if (firstSeg) _rowRefs[u] = new RowRef { seg = rest, label = l, defaultText = restText, baseColor = TlEmpty };
                        }
                        track.Add(rest);
                        AnimateGrow(rest, uid + ":rest", cols - used, false);
                        used = cols;
                    }
                }

                if (cols > R) track.Add(RoundDivider(R, cols));
                if (mine)
                    foreach (var th in threats)
                    {
                        int col = start + th.endTick;
                        if (th.target != u || col > cols) continue;
                        var mark = new VisualElement { pickingMode = PickingMode.Ignore };
                        mark.style.position = Position.Absolute; mark.style.top = 0; mark.style.bottom = 0;
                        mark.style.left = Length.Percent(col * 100f / cols); mark.style.width = 3;
                        mark.style.marginLeft = -3; mark.style.backgroundColor = new Color(1f, 0.38f, 0.33f);
                        // Etiqueta legível: quem ataca e que o golpe chega neste tick.
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
            // Esquece larguras de blocos que não existem mais (outro round, outra unidade, ordem mudada).
            if (_segGrow.Count > _segTouched.Count)
            {
                var drop = new List<string>();
                foreach (var k in _segGrow.Keys) if (!_segTouched.Contains(k)) drop.Add(k);
                foreach (var k in drop) _segGrow.Remove(k);
            }
        }

        /// <summary>A linha planejando ou resolvendo? (a régua começa no 1º tick do round ao planejar)</summary>
        public bool TimelinePlanning { get; set; }

        /// <summary>Linha vertical que separa este round do próximo na barra de ticks.</summary>
        private static VisualElement RoundDivider(int R, int cols)
        {
            var div = new VisualElement { pickingMode = PickingMode.Ignore, name = "round-divider" };
            div.style.position = Position.Absolute; div.style.top = -2; div.style.bottom = -2;
            div.style.left = Length.Percent(R * 100f / cols); div.style.width = 3; div.style.marginLeft = -2;
            div.style.backgroundColor = new Color(0.36f, 0.22f, 0.08f);
            div.style.borderTopLeftRadius = div.style.borderTopRightRadius = div.style.borderBottomLeftRadius = div.style.borderBottomRightRadius = 1;
            return div;
        }

        // ── Animação dos blocos (2026-11-15): cada bloco lembra a largura (em ticks) da última vez que apareceu.
        // Um bloco novo entra crescendo do zero e, como a barra é flex, empurra os outros para os ticks seguintes;
        // um bloco que ficou mais caro (golpe poderoso, incremento) cresce no lugar.
        private readonly Dictionary<string, float> _segGrow = new Dictionary<string, float>();
        private static readonly List<StylePropertyName> GrowProp = new List<StylePropertyName> { new StylePropertyName("flex-grow") };
        private float _growSeconds = 0.3f;   // opção da sala (tickBarPushAnimSeconds)
        private readonly HashSet<string> _segTouched = new HashSet<string>();
        private static readonly List<EasingFunction> GrowEase = new List<EasingFunction> { new EasingFunction(EasingMode.EaseOutCubic) };

        private void AnimateGrow(VisualElement seg, string key, float target, bool enterFromZero)
        {
            _segTouched.Add(key);
            float from = _segGrow.TryGetValue(key, out var f) ? f : (enterFromZero ? 0f : target);
            _segGrow[key] = target;
            if (_growSeconds <= 0.01f) from = target;   // animação desligada na sala
            seg.style.flexGrow = from;
            if (Mathf.Approximately(from, target)) return;
            seg.style.transitionProperty = GrowProp;
            seg.style.transitionDuration = new List<TimeValue> { new TimeValue(_growSeconds, TimeUnit.Second) };
            seg.style.transitionTimingFunction = GrowEase;
            // Um quadro depois: o bloco nasce na largura antiga e a transição leva até a nova.
            seg.schedule.Execute(() => seg.style.flexGrow = target).StartingIn(16);
        }

        private GridManager _gridCache;
        /// <summary>
        /// Plano previsto de uma unidade que ainda não agiu (Visão arcana no planejamento). A fera roda a mesma IA
        /// determinística do início do round (sem sorteio) e o plano é desfeito logo depois; jogadores mostram o
        /// plano já confirmado, se chegou.
        /// </summary>
        private List<(ActionType type, int dur, string text, Color color)> PredictPlan(Unit u)
        {
            var list = new List<(ActionType, int, string, Color)>();
            bool beast = BeastAI.Controls(u);
            if (beast)
            {
                if (_gridCache == null) _gridCache = UnityEngine.Object.FindFirstObjectByType<GridManager>();
                if (_gridCache == null) return list;
                BeastAI.Plan(u, _tlUnits, _gridCache);
            }
            Vector2Int prev = u.PlanStartAnchor;
            foreach (var a in u.actionSequence)
            {
                int d = TickRules.Duration(u, a);
                Unit target = null; int tiles = 0;
                if (a.Type == ActionType.Attack && a.Index >= 0 && a.Index < u.plannedAttacks.Count) target = u.plannedAttacks[a.Index].TargetUnit;
                if (a.Type == ActionType.Move && a.Index >= 0 && a.Index < u.plannedPath.Count)
                {
                    var dest = u.plannedPath[a.Index];
                    tiles = Mathf.Max(Mathf.Abs(dest.x - prev.x), Mathf.Abs(dest.y - prev.y)); prev = dest;
                }
                SpellDef sp = a.Type == ActionType.Spell ? TickRules.SpellOf(u, a) : null;
                string text = sp != null ? SpellBlockText(u, sp) : BlockText(u, a.Type, a.IsBonus, a.IsAimed, tiles, target);
                list.Add((a.Type, Mathf.Max(1, d), text, SegColor(a.Type, a.IsBonus, sp)));
            }
            if (beast) u.ResetPlan();   // a previsão não fica no plano: o round planeja de novo com o mesmo resultado
            return list;
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
            lab.style.fontSize = 10; lab.style.color = new Color(0.37f, 0.38f, 0.45f); lab.style.marginRight = 5;
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
                float c = u.stats.CritChance; float h = Mathf.Clamp01(c + (1f - c) * u.EffectiveHitChance);
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
            // Largura = só a fatia de ticks (2026-11-15): sem padding/borda/margem na horizontal, que o flex somaria
            // fora da proporção e tiraria os blocos do alinhamento com a régua e a linha do round.
            ss.flexGrow = Mathf.Max(1, ticks); ss.flexBasis = 0; ss.flexShrink = 1; ss.marginRight = 0;
            ss.backgroundColor = color; ss.justifyContent = Justify.Center;
            ss.borderTopLeftRadius = ss.borderTopRightRadius = ss.borderBottomLeftRadius = ss.borderBottomRightRadius = 4;
            ss.borderTopWidth = ss.borderBottomWidth = 1; ss.borderLeftWidth = ss.borderRightWidth = 0;
            var bc = new Color(color.r * 0.7f, color.g * 0.7f, color.b * 0.7f);
            ss.borderTopColor = ss.borderBottomColor = ss.borderLeftColor = ss.borderRightColor = bc;
            ss.paddingLeft = ss.paddingRight = 0; ss.overflow = Overflow.Hidden;
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.style.paddingLeft = 5; l.style.paddingRight = 5;
            l.style.fontSize = 11; l.style.whiteSpace = WhiteSpace.NoWrap; l.style.overflow = Overflow.Hidden;
            l.style.unityTextAlign = TextAnchor.MiddleCenter; l.style.textOverflow = TextOverflow.Ellipsis;
            l.style.unityFontStyleAndWeight = strong ? FontStyle.Bold : FontStyle.Normal;
            l.style.color = (color.r + color.g + color.b) > 1.9f ? new Color(0.06f, 0.08f, 0.16f) : Color.white;
            seg.Add(l);
            // Vão entre blocos desenhado por dentro (não conta na largura).
            var gap = new VisualElement { pickingMode = PickingMode.Ignore, name = "seg-gap" };
            gap.style.position = Position.Absolute; gap.style.right = 0; gap.style.top = -1; gap.style.bottom = -1; gap.style.width = 2;
            gap.style.backgroundColor = new Color(0.82f, 0.74f, 0.57f);
            seg.Add(gap);
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
                    $"<size=11><color=#5f6072>Se o tempo acabar: {PlanningController.ReactionName(reactor.defaultReaction)}" +
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
            if (_reactionMenu != null) { _reactionMenu.style.display = DisplayStyle.Flex; _reactionMenu.BringToFront(); PlaceChoiceAboveActionBar(); }
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
            PlaceChoiceAboveActionBar();
        }

        /// <summary>
        /// A janela de escolha (reação, incrementar magia) fica na frente e logo acima da grade de ações.
        /// A grade cresce com a largura da tela (KeepAspect), então o "bottom" fixo em % deixava a janela
        /// por baixo dela (2026-11-15).
        /// </summary>
        private void PlaceChoiceAboveActionBar()
        {
            if (_reactionMenu == null) return;
            _reactionMenu.BringToFront();
            void Place()
            {
                var parent = _reactionMenu.parent;
                if (parent == null || _actionBar == null || _actionBar.resolvedStyle.display == DisplayStyle.None) return;
                float top = _actionBar.worldBound.yMin, pb = parent.worldBound.yMax;
                if (float.IsNaN(top) || top <= 0f) return;
                float H = parent.layout.height, h = _reactionMenu.layout.height;
                float bottom = pb - top + H * 0.01f;
                // Tela baixa (celular deitado): não cabe acima da grade; fica na frente dela, sem sair pelo topo.
                if (!float.IsNaN(h) && h > 0f) bottom = Mathf.Min(bottom, Mathf.Max(H * 0.01f, H - h - H * 0.01f));
                _reactionMenu.style.bottom = bottom;
            }
            _placeChoice = Place;
            Place();
            _reactionMenu.schedule.Execute(Place);   // depois do layout do frame
            if (!_choicePlaceHooked)
            {
                _choicePlaceHooked = true;   // o texto muda a altura da janela: reposiciona quando o layout mudar
                _reactionMenu.RegisterCallback<GeometryChangedEvent>(_ => { if (_reactionMenu.resolvedStyle.display == DisplayStyle.Flex) _placeChoice?.Invoke(); });
            }
        }
        private bool _choicePlaceHooked;
        private System.Action _placeChoice;
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
            string L(string effect) => $"<b>{baseText}</b>\n<size=12><color=#6b4a1e>{effect} · {cost} Stamina</color></size>";
            switch (k)
            {
                case ReactionKind.Dodge:         return L($"anda até {RoundManager.DodgeMoveTiles(u)} tile{(RoundManager.DodgeMoveTiles(u) == 1 ? "" : "s")} para fora do golpe");
                case ReactionKind.Block:         return L($"segura {RoundManager.BlockAmount(u)} de dano; se absorver tudo, desequilibra o atacante");
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
