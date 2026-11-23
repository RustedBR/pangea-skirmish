using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using PangeaSkirmish.UI;

namespace PangeaSkirmish
{
    /// <summary>
    /// Overlay do tutorial (GDD-027 §7 + anexo §2): card de lição, anel de realce (spotlight),
    /// card de feedback/falha e mapa de capítulos. Construído em código (UIDocument próprio), aditivo —
    /// não toca na BattleHUD. Raiz com pickingMode Ignore: o jogo continua recebendo input; só os cards
    /// interceptam cliques. Tamanhos e posições SEMPRE em % da tela (regra do Marcus).
    /// </summary>
    public class TutorialOverlay : MonoBehaviour
    {
        // ── Tokens (anexo §2.9) ───────────────────────────────────────────────
        // Mesmas cores do tema do jogo (PangeaTheme.uss, grafite + âmbar do redesign).
        static readonly Color CText      = new Color32(216, 208, 190, 255);
        static readonly Color CStrong    = new Color32(255, 248, 234, 255);
        static readonly Color CMuted     = new Color32(185, 176, 156, 255);
        static readonly Color COk        = new Color32(95, 178, 122, 255);
        static readonly Color COkText    = new Color32(143, 201, 162, 255);
        static readonly Color CAccent    = new Color32(242, 181, 68, 255);
        static readonly Color CInk       = new Color32(242, 181, 68, 255);
        static readonly Color COnAccent  = new Color32(27, 20, 8, 255);
        static readonly Color CDanger    = new Color32(224, 113, 74, 255);
        static readonly Color CFuture    = new Color32(58, 69, 64, 255);
        static readonly Color CLineBtn   = new Color32(74, 86, 80, 255);

        /// <summary>Nomes curtos dos capítulos (anexo §1.1) — header do card e mapa de capítulos.</summary>
        public static readonly string[] ChapterShort =
            { "ORIENTAÇÃO", "PLANEJAR", "MOVIMENTO", "ATAQUE", "REAÇÕES", "MAGIA", "ELEMENTOS", "CORPO", "PROGRESSÃO", "COOP" };

        private UIDocument _doc;
        private VisualElement _root, _card, _segments, _steps, _cardButtons;
        private Label _header, _counter, _title, _instruction, _hint;
        private VisualElement _fbCard, _fbIcon;
        private Label _fbTag, _fbTitle, _fbText, _fbKey;
        private Button _fbPrimary, _fbSecondary, _repeat, _skip, _continue;
        private VisualElement _ring;
        private Label _ringTag;
        private VisualElement _map;

        private bool _feedbackOpen, _feedbackOk;
        private int _unblockFrame = -1;
        private string _highlight;
        private System.Func<VisualElement> _highlightResolver;

        public System.Action OnSkip, OnRepeat, OnNext, OnContinue, OnOpenMenu, OnCloseMenu, OnAction;
        private Button _action;
        /// <summary>Jogador escolheu uma lição no menu (índice na lista de lições).</summary>
        public System.Action<int> OnPickLesson;
        private VisualElement _menu;
        private bool _menuHadLesson;

        private void Awake()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null) _doc = gameObject.AddComponent<UIDocument>();
            var panel = Resources.Load<PanelSettings>("UI/PangeaPanelSettings");
            if (panel != null) _doc.panelSettings = panel;
            _doc.sortingOrder = 9000;   // acima do HUD
            Build();
        }

        // ── Helpers de estilo ─────────────────────────────────────────────────
        static void Radius(VisualElement e, float px) =>
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = px;
        static void Border(VisualElement e, float w, Color c)
        {
            e.style.borderTopWidth = e.style.borderBottomWidth = e.style.borderLeftWidth = e.style.borderRightWidth = w;
            e.style.borderTopColor = e.style.borderBottomColor = e.style.borderLeftColor = e.style.borderRightColor = c;
        }
        static void PadPct(VisualElement e, float v, float h)
        {
            e.style.paddingTop = e.style.paddingBottom = Length.Percent(v);
            e.style.paddingLeft = e.style.paddingRight = Length.Percent(h);
        }
        static Label L(string t, int size, Color c, bool bold = false)
        {
            var l = new Label(t) { pickingMode = PickingMode.Ignore };
            l.style.fontSize = size; l.style.color = c; l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginLeft = l.style.marginRight = l.style.marginTop = l.style.marginBottom = 0;
            l.style.paddingLeft = l.style.paddingRight = 0;
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.enableRichText = true;
            return l;
        }
        /// <summary>Botão com as classes do jogo (.pg-button / .pg-button--primary): mesma cor, efeito e tamanho.</summary>
        static Button Btn(string text, System.Action onClick, bool primary)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("pg-button");
            if (primary) b.AddToClassList("pg-button--primary");
            b.style.flexGrow = 1;
            return b;
        }

        private static bool IsTouch => Touchscreen.current != null && (Mouse.current == null || UnityEngine.Device.Application.isMobilePlatform);

        // ── Construção ────────────────────────────────────────────────────────
        private void Build()
        {
            _root = _doc.rootVisualElement;
            _root.Clear();
            _root.pickingMode = PickingMode.Ignore;
            _root.style.width = Length.Percent(100);
            _root.style.height = Length.Percent(100);
            // Mesmas folhas de estilo da HUD de batalha: o tutorial usa as classes do jogo.
            var theme = Resources.Load<StyleSheet>("UI/Theme/PangeaTheme");
            var hudSheet = Resources.Load<StyleSheet>("UI/Screens/BattleHUD");
            if (theme != null) _root.styleSheets.Add(theme);
            if (hudSheet != null) _root.styleSheets.Add(hudSheet);
            _root.AddToClassList("pg-theme");
            bool mobile = UnityEngine.Device.Application.isMobilePlatform;

            // Anel de realce (spotlight) — posicionado a cada frame sobre o alvo.
            _ring = new VisualElement { pickingMode = PickingMode.Ignore };
            _ring.style.position = Position.Absolute;
            Border(_ring, 3, CAccent); Radius(_ring, 12);
            _ring.style.backgroundColor = new Color(CAccent.r, CAccent.g, CAccent.b, 0.08f);
            _ring.style.display = DisplayStyle.None;
            _root.Add(_ring);
            _ringTag = L("Clique aqui", 13, COnAccent, true);
            _ringTag.style.position = Position.Absolute;
            _ringTag.style.backgroundColor = CAccent;
            _ringTag.style.paddingLeft = _ringTag.style.paddingRight = 10;
            _ringTag.style.paddingTop = _ringTag.style.paddingBottom = 4;
            Radius(_ringTag, 8);
            _ringTag.style.display = DisplayStyle.None;
            _root.Add(_ringTag);

            // Card de lição (card-esq, anexo §2.1).
            _card = new VisualElement { pickingMode = PickingMode.Position };
            _card.style.position = Position.Absolute;
            _card.style.left = Length.Percent(mobile ? 1f : 2.5f);
            _card.style.top = Length.Percent(mobile ? 12f : 6f);
            _card.style.width = Length.Percent(mobile ? 40f : 30f);
            _card.style.maxHeight = Length.Percent(55);
            _card.AddToClassList("bh-window");
            HudLayout.Register(_card, "tutorial.card");
            _root.Add(_card);

            var head = new VisualElement { pickingMode = PickingMode.Ignore };
            head.style.flexDirection = FlexDirection.Row; head.style.justifyContent = Justify.SpaceBetween;
            _header = new Label("") { pickingMode = PickingMode.Ignore }; _header.AddToClassList("bh-title");
            _header.style.flexGrow = 1; _header.style.marginBottom = 0;
            _counter = L("", 11, CMuted, true); _counter.style.marginLeft = 8; _counter.style.alignSelf = Align.Center;
            head.Add(_header); head.Add(_counter);
            _card.Add(head);

            _segments = new VisualElement { pickingMode = PickingMode.Ignore };
            _segments.style.flexDirection = FlexDirection.Row;
            _segments.style.height = 5; _segments.style.marginTop = 8; _segments.style.marginBottom = 10;
            _card.Add(_segments);

            _title = L("", 17, CStrong, true); _title.style.marginBottom = 6;
            _card.Add(_title);
            _instruction = L("", 13, CText);
            _card.Add(_instruction);

            _steps = new VisualElement { pickingMode = PickingMode.Ignore };
            _steps.style.marginTop = 8; _steps.AddToClassList("pg-card");
            _steps.style.display = DisplayStyle.None;
            _card.Add(_steps);

            _continue = Btn("Entendi", () => OnContinue?.Invoke(), true);
            _continue.style.marginTop = 8; _continue.style.flexGrow = 0;
            _continue.style.display = DisplayStyle.None;
            _card.Add(_continue);

            _action = Btn("", () => OnAction?.Invoke(), true);
            _action.style.marginTop = 8; _action.style.flexGrow = 0;
            _action.style.display = DisplayStyle.None;
            _card.Add(_action);

            _hint = L("", 12, CDanger, true); _hint.style.marginTop = 8; _hint.style.display = DisplayStyle.None;
            _card.Add(_hint);

            _cardButtons = new VisualElement { pickingMode = PickingMode.Ignore };
            _cardButtons.style.flexDirection = FlexDirection.Row; _cardButtons.style.marginTop = 8;
            _repeat = Btn("Repetir", () => OnRepeat?.Invoke(), false);
            _skip = Btn("Pular lição", () => OnSkip?.Invoke(), false);
            var lessonsBtn = Btn("Lições", () => OnOpenMenu?.Invoke(), false);
            _cardButtons.Add(lessonsBtn); _cardButtons.Add(_repeat); _cardButtons.Add(_skip);
            _card.Add(_cardButtons);

            // Card de feedback (centralizado, anexo §2.5).
            _fbCard = new VisualElement { pickingMode = PickingMode.Position };
            _fbCard.style.position = Position.Absolute;
            _fbCard.style.width = Length.Percent(mobile ? 70f : 36f);
            _fbCard.style.left = Length.Percent(mobile ? 15f : 32f);
            _fbCard.style.top = Length.Percent(28);
            _fbCard.style.maxHeight = Length.Percent(50);
            _fbCard.style.alignItems = Align.Center;
            _fbCard.AddToClassList("bh-window");
            _fbCard.style.display = DisplayStyle.None;
            _root.Add(_fbCard);

            _fbIcon = new VisualElement { pickingMode = PickingMode.Ignore };
            _fbIcon.style.width = 40; _fbIcon.style.height = 40; Radius(_fbIcon, 20);
            _fbIcon.style.alignItems = Align.Center; _fbIcon.style.justifyContent = Justify.Center;
            _fbIcon.Add(L("✓", 22, new Color32(16, 20, 15, 255), true));
            _fbCard.Add(_fbIcon);
            _fbTag = L("MECÂNICA APRENDIDA", 11, COkText, true); _fbTag.style.marginTop = 6; _fbTag.style.letterSpacing = 1;
            _fbCard.Add(_fbTag);
            _fbTitle = L("", 18, CStrong, true); _fbTitle.style.marginTop = 6; _fbTitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            _fbCard.Add(_fbTitle);
            _fbText = L("", 13, CText); _fbText.style.marginTop = 8; _fbText.style.unityTextAlign = TextAnchor.MiddleCenter;
            _fbCard.Add(_fbText);
            _fbKey = L("", 11, CMuted); _fbKey.style.marginTop = 10;
            _fbCard.Add(_fbKey);
            var fbRow = new VisualElement { pickingMode = PickingMode.Ignore };
            fbRow.style.flexDirection = FlexDirection.Row; fbRow.style.marginTop = 14; fbRow.style.width = Length.Percent(100);
            _fbSecondary = Btn("Pular", () => OnSkip?.Invoke(), false);
            _fbPrimary = Btn("Próxima lição", PrimaryFeedback, true);
            fbRow.Add(_fbSecondary); fbRow.Add(_fbPrimary);
            _fbCard.Add(fbRow);

            // Menu de escolha de lição (antes de começar e pelo botão "Lições").
            _menu = new VisualElement { pickingMode = PickingMode.Position };
            _menu.style.position = Position.Absolute;
            _menu.style.left = _menu.style.right = Length.Percent(4);
            _menu.style.top = _menu.style.bottom = Length.Percent(5);
            _menu.AddToClassList("pg-panel");
            _menu.style.display = DisplayStyle.None;
            _root.Add(_menu);

            // Mapa de capítulos (relatório final).
            _map = new VisualElement { pickingMode = PickingMode.Position };
            _map.style.position = Position.Absolute;
            _map.style.left = _map.style.right = Length.Percent(4);
            _map.style.top = _map.style.bottom = Length.Percent(6);
            _map.AddToClassList("pg-panel");
            _map.style.display = DisplayStyle.None;
            _root.Add(_map);
        }

        private void PrimaryFeedback()
        {
            if (_feedbackOk) OnNext?.Invoke(); else OnRepeat?.Invoke();
        }

        // ── API usada pelo TutorialDirector ───────────────────────────────────

        /// <summary>Mostra a instrução da lição (estado esperando o jogador agir).</summary>
        public void ShowLesson(TutorialLesson l, int lessonAt, int lessonTotal, int chapterCount)
        {
            if (l == null) return;
            int ch = Mathf.Clamp(l.chapter, 0, ChapterShort.Length - 1);
            _header.text = $"CAPÍTULO {l.chapter} · {(string.IsNullOrEmpty(l.phaseTitle) ? ChapterShort[ch] : l.phaseTitle.ToUpperInvariant())}";
            _counter.text = $"Lição {lessonAt} de {Mathf.Max(1, lessonTotal)}";
            _segments.Clear();
            int n = Mathf.Max(chapterCount, l.chapter + 1);
            for (int i = 0; i < n; i++)
            {
                var s = new VisualElement { pickingMode = PickingMode.Ignore };
                s.style.flexGrow = 1; s.style.marginRight = i < n - 1 ? 4 : 0; Radius(s, 3);
                s.style.backgroundColor = i < l.chapter ? COk : i == l.chapter ? CInk : CFuture;
                _segments.Add(s);
            }
            _title.text = l.title;
            _instruction.text = l.instruction;
            SetSteps(l.steps);
            _action.text = l.actionLabel ?? "";
            _action.style.display = string.IsNullOrEmpty(l.actionLabel) ? DisplayStyle.None : DisplayStyle.Flex;
            _continue.text = l.kind == TutorialLessonKind.Demo ? "Ver demonstração" : "Entendi";
            _continue.style.display = l.kind == TutorialLessonKind.Action ? DisplayStyle.None : DisplayStyle.Flex;
            SetHint(null);
            _cardButtons.style.display = DisplayStyle.Flex;
            _card.style.display = DisplayStyle.Flex;
            CloseFeedback();
            _map.style.display = DisplayStyle.None;
            _menu.style.display = DisplayStyle.None;
            SetHighlight(l.highlightTarget, l.highlightResolver);
            gameObject.SetActive(true);
        }

        /// <summary>Checklist de passos do jogador (anexo §2.8). null/vazio esconde.</summary>
        public void SetSteps(string[] steps, int doneCount = 0)
        {
            _steps.Clear();
            if (steps == null || steps.Length == 0) { _steps.style.display = DisplayStyle.None; return; }
            _steps.style.display = DisplayStyle.Flex;
            for (int i = 0; i < steps.Length; i++)
            {
                bool done = i < doneCount, cur = i == doneCount;
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center;
                row.style.marginTop = i == 0 ? 0 : 6;
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.style.width = 16; dot.style.height = 16; Radius(dot, 8); dot.style.marginRight = 10;
                if (done) dot.style.backgroundColor = COk; else Border(dot, 2, cur ? CAccent : CLineBtn);
                var t = L(steps[i], 14, done ? new Color32(159, 174, 159, 255) : CText);
                row.Add(dot); row.Add(t);
                _steps.Add(row);
            }
        }

        /// <summary>Dica inline no card (erro do jogador que não fecha a lição, anexo §2.5).</summary>
        public void SetHint(string hint)
        {
            _hint.text = hint ?? "";
            _hint.style.display = string.IsNullOrEmpty(hint) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>Realce: nome de VisualElement da HUD (ex.: "ticks-bar") ou resolvedor custom. null = sem anel.</summary>
        public void SetHighlight(string target, System.Func<VisualElement> resolver = null)
        {
            _highlight = target;
            _highlightResolver = resolver;
            if (string.IsNullOrEmpty(target) && resolver == null) { _ring.style.display = DisplayStyle.None; _ringTag.style.display = DisplayStyle.None; }
        }

        /// <summary>Resultado da mecânica (gate fechou). ok=false = asserção falhou (vermelho).</summary>
        public void ShowFeedback(string title, string feedback, bool ok)
        {
            _feedbackOpen = true; _feedbackOk = ok;
            TutorialHud.BlockPlanInput = true;
            _fbIcon.style.backgroundColor = ok ? COk : CDanger;
            ((Label)_fbIcon[0]).text = ok ? "✓" : "!";
            _fbTag.text = ok ? "MECÂNICA APRENDIDA" : "NÃO FUNCIONOU";
            _fbTag.style.color = ok ? COkText : CDanger;
            _fbTitle.text = title;
            _fbText.text = feedback;
            _fbKey.text = IsTouch ? "" : (ok ? "<b>Espaço</b> continua" : "<b>Espaço</b> tenta de novo");
            _fbPrimary.text = ok ? "Próxima lição" : "Tentar de novo";
            _fbSecondary.style.display = ok ? DisplayStyle.None : DisplayStyle.Flex;
            _fbCard.style.display = DisplayStyle.Flex;
            _card.style.display = DisplayStyle.None;
            SetHighlight(null);
        }

        private void CloseFeedback()
        {
            if (_feedbackOpen) _unblockFrame = Time.frameCount + 1;   // a tecla que fechou não confirma o plano
            _feedbackOpen = false;
            _fbCard.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// Menu de escolha de lição: um cartão por capítulo, cada lição é um botão. Capítulos já concluídos
        /// (checkpoint) aparecem em verde. canClose = true quando aberto no meio do tutorial ("Voltar").
        /// </summary>
        public void ShowMenu(IList<TutorialLesson> lessons, int chaptersDone, int currentIndex, bool canClose)
        {
            _menuHadLesson = canClose;
            CloseFeedback();
            _card.style.display = DisplayStyle.None;
            _map.style.display = DisplayStyle.None;
            SetHighlight(null);
            _menu.Clear();

            var head = new VisualElement { pickingMode = PickingMode.Ignore };
            head.style.flexDirection = FlexDirection.Row; head.style.justifyContent = Justify.SpaceBetween; head.style.alignItems = Align.FlexEnd;
            var titles = new VisualElement { pickingMode = PickingMode.Ignore };
            titles.Add(L("TUTORIAL", 12, CMuted, true));
            var h = L("Escolha uma lição", 28, CStrong, true); h.style.marginTop = 2;
            titles.Add(h);
            head.Add(titles);
            var startBtn = Btn(chaptersDone > 0 && chaptersDone < ChapterShort.Length ? $"Continuar: capítulo {chaptersDone}" : "Começar do início", () =>
            {
                int idx = 0;
                if (chaptersDone > 0 && chaptersDone < ChapterShort.Length)
                    for (int i = 0; i < lessons.Count; i++) if (lessons[i].chapter >= chaptersDone) { idx = i; break; }
                OnPickLesson?.Invoke(idx);
            }, true);
            startBtn.style.flexGrow = 0; startBtn.style.width = Length.Percent(24);
            head.Add(startBtn);
            _menu.Add(head);

            var scroll = new ScrollView(ScrollViewMode.Vertical) { pickingMode = PickingMode.Position };
            scroll.style.flexGrow = 1; scroll.style.marginTop = 12;
            scroll.contentContainer.style.flexDirection = FlexDirection.Row;
            scroll.contentContainer.style.flexWrap = Wrap.Wrap;
            _menu.Add(scroll);

            var byCh = new SortedDictionary<int, List<int>>();
            for (int i = 0; i < lessons.Count; i++)
            {
                int c = lessons[i].chapter;
                if (!byCh.TryGetValue(c, out var li)) byCh[c] = li = new List<int>();
                li.Add(i);
            }
            foreach (var kv in byCh)
            {
                int ch = Mathf.Clamp(kv.Key, 0, ChapterShort.Length - 1);
                bool done = kv.Key < chaptersDone;
                var card = new VisualElement { pickingMode = PickingMode.Position };
                card.style.width = Length.Percent(18.4f); card.style.marginRight = Length.Percent(1.6f); card.style.marginBottom = Length.Percent(1.6f);
                card.AddToClassList("pg-card");
                var top = new VisualElement { pickingMode = PickingMode.Ignore };
                top.style.flexDirection = FlexDirection.Row; top.style.justifyContent = Justify.SpaceBetween;
                top.Add(L($"CAP. {kv.Key}", 13, done ? COkText : CAccent, true));
                if (done) top.Add(L("✓ concluído", 11, COkText, true));
                card.Add(top);
                var nm = L(ChapterShort[ch], 16, CStrong, true); nm.style.marginTop = 2; nm.style.marginBottom = 8;
                card.Add(nm);
                foreach (int idx in kv.Value)
                {
                    int pick = idx;
                    var b = Btn(lessons[idx].title, () => OnPickLesson?.Invoke(pick), idx == currentIndex && canClose);
                    b.style.flexGrow = 0; b.style.minHeight = 36; b.style.marginLeft = b.style.marginRight = 0; b.style.marginBottom = 4;
                    b.style.fontSize = 12; b.style.unityTextAlign = TextAnchor.MiddleLeft;
                    card.Add(b);
                }
                scroll.Add(card);
            }

            if (canClose)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.style.flexDirection = FlexDirection.Row; row.style.justifyContent = Justify.FlexEnd; row.style.marginTop = 8;
                var back = Btn("Voltar à lição", () => OnCloseMenu?.Invoke(), false);
                back.style.flexGrow = 0; back.style.width = Length.Percent(24);
                row.Add(back); _menu.Add(row);
            }
            _menu.style.display = DisplayStyle.Flex;
            gameObject.SetActive(true);
        }

        /// <summary>Fecha o menu; com restoreCard volta o card da lição que estava aberta.</summary>
        public void HideMenu(bool restoreCard)
        {
            _menu.style.display = DisplayStyle.None;
            if (restoreCard && _menuHadLesson) _card.style.display = DisplayStyle.Flex;
        }

        /// <summary>Mapa de capítulos com o resultado de cada lição (relatório final, anexo §1.1).</summary>
        public void ShowReport(IList<TutorialLesson> lessons, string textReport)
        {
            CloseFeedback();
            _card.style.display = DisplayStyle.None;
            SetHighlight(null);
            _map.Clear();
            int ok = 0, total = lessons?.Count ?? 0;
            if (lessons != null) foreach (var l in lessons) if (l.result == TutorialResult.OK) ok++;
            _map.Add(L($"TUTORIAL · {ok} DE {total} MECÂNICAS OK", 12, CMuted, true));
            var h = L("Sua jornada em Pangea", 34, CStrong, true); h.style.marginTop = 4; h.style.marginBottom = 16;
            _map.Add(h);

            var grid = new VisualElement { pickingMode = PickingMode.Ignore };
            grid.style.flexDirection = FlexDirection.Row; grid.style.flexWrap = Wrap.Wrap; grid.style.flexGrow = 1;
            _map.Add(grid);
            var byCh = new SortedDictionary<int, List<TutorialLesson>>();
            if (lessons != null)
                foreach (var l in lessons) { if (!byCh.TryGetValue(l.chapter, out var li)) byCh[l.chapter] = li = new List<TutorialLesson>(); li.Add(l); }
            foreach (var kv in byCh)
            {
                var c = new VisualElement { pickingMode = PickingMode.Ignore };
                c.style.width = Length.Percent(18.4f); c.style.marginRight = Length.Percent(1.6f); c.style.marginBottom = Length.Percent(1.6f);
                c.AddToClassList("pg-card");
                int ch = Mathf.Clamp(kv.Key, 0, ChapterShort.Length - 1);
                c.Add(L($"CAP. {kv.Key}", 13, COkText, true));
                var nm = L(ChapterShort[ch], 17, CStrong, true); nm.style.marginTop = 4; nm.style.marginBottom = 10;
                c.Add(nm);
                var dots = new VisualElement { pickingMode = PickingMode.Ignore };
                dots.style.flexDirection = FlexDirection.Row; dots.style.flexWrap = Wrap.Wrap;
                foreach (var l in kv.Value)
                {
                    var d = new VisualElement { pickingMode = PickingMode.Ignore, tooltip = $"{l.title}: {l.result}" };
                    d.style.width = 12; d.style.height = 12; Radius(d, 6); d.style.marginRight = 4; d.style.marginBottom = 4;
                    switch (l.result)
                    {
                        case TutorialResult.OK: d.style.backgroundColor = COk; break;
                        case TutorialResult.Failed: d.style.backgroundColor = CDanger; break;
                        case TutorialResult.Skipped: Border(d, 2, new Color32(138, 131, 114, 255)); break;
                        default: d.style.backgroundColor = CFuture; break;
                    }
                    dots.Add(d);
                }
                c.Add(dots);
                foreach (var l in kv.Value)
                    if (l.result == TutorialResult.Failed)
                    { var f = L($"✗ {l.mechanic}: {l.detail}", 12, CDanger); f.style.marginTop = 6; c.Add(f); }
                grid.Add(c);
            }
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.style.flexDirection = FlexDirection.Row; row.style.justifyContent = Justify.FlexEnd;
            var back = Btn("Voltar ao menu", () => OnNext?.Invoke(), true);
            back.style.flexGrow = 0; back.style.width = Length.Percent(24);
            row.Add(back);
            _map.Add(row);
            _map.style.display = DisplayStyle.Flex;
            if (!string.IsNullOrEmpty(textReport)) Debug.Log("[Tutorial] relatório\n" + textReport);
        }

        public void HideOverlay() => gameObject.SetActive(false);

        // ── Frame ─────────────────────────────────────────────────────────────
        private void Update()
        {
            if (_unblockFrame >= 0 && Time.frameCount >= _unblockFrame && !_feedbackOpen)
            { TutorialHud.BlockPlanInput = false; _unblockFrame = -1; }

            var kb = Keyboard.current;
            if (_feedbackOpen && kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
                PrimaryFeedback();

            UpdateRing();
            UpdateCardSide();
        }

        private bool _cardRight;
        /// <summary>card-dir (anexo §2.1): se a janela REAGIR/escolha da HUD está aberta à esquerda, o card vai para a direita.</summary>
        private void UpdateCardSide()
        {
            if (HudLayout.IsCustom(_card)) return;   // o jogador posicionou o card: nunca muda sozinho
            var hud = FindFirstObjectByType<BattleHUD>();
            var menu = hud != null && hud.Document != null ? hud.Document.rootVisualElement.Q<VisualElement>("reaction-menu") : null;
            bool right = TutorialHud.EditorOpen || (menu != null && menu.resolvedStyle.display == DisplayStyle.Flex);
            if (right == _cardRight) return;
            _cardRight = right;
            bool mobile = UnityEngine.Device.Application.isMobilePlatform;
            float m = mobile ? 1f : 2.5f;
            if (right) { _card.style.left = StyleKeyword.Auto; _card.style.right = Length.Percent(m); }
            else { _card.style.right = StyleKeyword.Auto; _card.style.left = Length.Percent(m); }
        }

        private VisualElement ResolveTarget()
        {
            if (_highlightResolver != null) return _highlightResolver();
            if (string.IsNullOrEmpty(_highlight)) return null;
            var hud = FindFirstObjectByType<BattleHUD>();
            var r = hud != null && hud.Document != null ? hud.Document.rootVisualElement : null;
            return r?.Q<VisualElement>(_highlight);
        }

        private void UpdateRing()
        {
            if (_ring == null || _ringTag == null || _root == null) return;   // recarga de scripts em Play zera os campos
            var t = ResolveTarget();
            bool show = t != null && t.resolvedStyle.display != DisplayStyle.None && t.worldBound.width > 1 && !_feedbackOpen;
            _ring.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            _ringTag.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;

            // worldBound está no espaço do painel da HUD; converte em fração da tela e aplica em %.
            var tp = t.panel; var rp = _root.panel;
            Rect wb = t.worldBound;
            if (tp != null && rp != null && tp != rp)
            {
                Vector2 a = RuntimePanelUtils.ScreenToPanel(rp, PanelToScreen(tp, new Vector2(wb.xMin, wb.yMin)));
                Vector2 b = RuntimePanelUtils.ScreenToPanel(rp, PanelToScreen(tp, new Vector2(wb.xMax, wb.yMax)));
                wb = Rect.MinMaxRect(a.x, a.y, b.x, b.y);
            }
            float W = Mathf.Max(1, _root.layout.width), H = Mathf.Max(1, _root.layout.height);
            float pad = 0.006f * H;
            _ring.style.left = Length.Percent((wb.xMin - pad) / W * 100f);
            _ring.style.top = Length.Percent((wb.yMin - pad) / H * 100f);
            _ring.style.width = Length.Percent((wb.width + 2 * pad) / W * 100f);
            _ring.style.height = Length.Percent((wb.height + 2 * pad) / H * 100f);
            float pulse = 0.25f + 0.2f * Mathf.Sin(Time.unscaledTime * 4f);
            _ring.style.backgroundColor = new Color(CAccent.r, CAccent.g, CAccent.b, pulse * 0.3f);

            _ringTag.text = IsTouch ? "Toque aqui" : "Clique aqui";
            float tagY = wb.yMin - pad - 0.05f * H;
            if (tagY < 0) tagY = wb.yMax + pad + 0.01f * H;
            _ringTag.style.left = Length.Percent(Mathf.Clamp01(wb.xMin / W) * 100f);
            _ringTag.style.top = Length.Percent(tagY / H * 100f);
        }

        private static Vector2 PanelToScreen(IPanel p, Vector2 pos)
        {
            // Inverso aproximado de ScreenToPanel: usa a escala da origem/1px para mapear.
            Vector2 o = RuntimePanelUtils.ScreenToPanel(p, Vector2.zero);
            Vector2 u = RuntimePanelUtils.ScreenToPanel(p, Vector2.one) - o;
            return new Vector2((pos.x - o.x) / (u.x == 0 ? 1 : u.x), (pos.y - o.y) / (u.y == 0 ? 1 : u.y));
        }
    }
}
