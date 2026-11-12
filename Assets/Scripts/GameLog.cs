using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PangeaSkirmish
{
    /// <summary>
    /// Registro da partida que funciona NO BUILD (inclusive WebGL, onde não há arquivo de log):
    /// guarda tudo que passa pelo Debug.Log, com erros e exceções, e mostra um painel com as
    /// últimas linhas, botões de copiar/baixar e o reinício rápido. Abre na aba LOG (canto
    /// inferior esquerdo, o único livre em todas as telas) ou com F8. É um UIDocument próprio
    /// com sortingOrder alto: o painel aberto fica sempre por cima da HUD, e a aba fechada
    /// não cobre nada. Criado sozinho ao iniciar o jogo.
    /// </summary>
    public class GameLog : MonoBehaviour
    {
        private struct Line { public float time; public LogType type; public string text; public string stack; }

        private const int Capacity = 3000;
        private static readonly List<Line> _lines = new List<Line>(Capacity);
        private static readonly object _lock = new object();
        private static GameLog _instance;
        private static bool _hooked;

        /// <summary>Erros e exceções desde o início (ou desde o último Clear).</summary>
        public static int ErrorCount { get; private set; }
        public static int LineCount { get { lock (_lock) return _lines.Count; } }
        /// <summary>O ponteiro está sobre o painel ou o botão LOG (para a batalha não tratar o clique como jogada).</summary>
        public static bool PointerOver { get; private set; }
        /// <summary>Ação de "reiniciar partida", definida pela cena atual (null = não disponível).</summary>
        public static Action RestartAction;
        public static string RestartLabel = "Reiniciar partida";

        private bool _open;
        private string _status = "";
        private bool _onlyErrors;
        private UIDocument _doc;
        private Button _tab, _restartBtn;
        private VisualElement _panel;
        private Label _titleLbl, _statusLbl;
        private ScrollView _list;
        private int _shownLines = -1, _shownErrors = -1;
        private bool _shownOnlyErrors, _overTab, _overPanel;
        private Action _shownRestart;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null; _hooked = false; ErrorCount = 0; PointerOver = false; RestartAction = null;
            lock (_lock) _lines.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            Hook();
            if (_instance != null) return;
            var go = new GameObject("GameLog");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<GameLog>();
            Debug.Log($"[GameLog] início — versão {Application.version}, Unity {Application.unityVersion}, {Application.platform}, {SystemInfo.operatingSystem}");
        }

        private static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            // Remove antes de inscrever: no editor sem recarga de domínio os eventos estáticos
            // sobrevivem entre execuções, e cada Play dobrava as linhas e a contagem de erros.
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            RestartAction = null;
            Debug.Log($"[GameLog] cena carregada: {s.name}");
        }

        private static void OnLog(string condition, string stack, LogType type)
        {
            bool bad = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
            lock (_lock)
            {
                if (_lines.Count >= Capacity) _lines.RemoveRange(0, Capacity / 5);
                _lines.Add(new Line { time = Time.realtimeSinceStartup, type = type, text = condition, stack = bad ? stack : null });
            }
            if (bad) ErrorCount++;
        }

        public static void Clear() { lock (_lock) _lines.Clear(); ErrorCount = 0; }

        /// <summary>Log inteiro em texto, com cabeçalho de contexto, pronto para colar num relato de bug.</summary>
        public static string Dump()
        {
            var sb = new StringBuilder(64 * 1024);
            sb.AppendLine("=== Pangea Skirmish — log da partida ===");
            sb.AppendLine($"quando: {DateTime.Now:yyyy-MM-dd HH:mm:ss}  versão: {Application.version}  unity: {Application.unityVersion}  plataforma: {Application.platform}");
            sb.AppendLine($"cena: {SceneManager.GetActiveScene().name}  multiplayer: {RuntimeMultiplayerSession.IsMultiplayer}  host: {RuntimeMultiplayerSession.IsHost}  modo: {RuntimeMultiplayerSession.CurrentConfig?.gameMode}");
            sb.AppendLine($"linhas: {LineCount}  erros: {ErrorCount}");
            sb.AppendLine("========================================");
            lock (_lock)
                foreach (var l in _lines)
                {
                    sb.Append('[').Append(l.time.ToString("0000.00")).Append("] ").Append(Tag(l.type)).Append(' ').AppendLine(l.text);
                    if (!string.IsNullOrEmpty(l.stack)) sb.AppendLine("    " + l.stack.TrimEnd().Replace("\n", "\n    "));
                }
            return sb.ToString();
        }

        private static string Tag(LogType t) =>
            t == LogType.Error ? "ERRO " : t == LogType.Exception ? "EXC  " : t == LogType.Warning ? "aviso" : t == LogType.Assert ? "ASSRT" : "     ";

        // ---------------------------------------------------------------- painel (UI Toolkit)

        private static readonly Color PanelBg = new Color(0.04f, 0.06f, 0.14f, 0.97f);
        private static readonly Color Border = new Color(0.47f, 0.63f, 0.86f);

        private void Start() => BuildUi();

        private static Button MakeButton(string text, Action onClick, float width)
        {
            var b = new Button(onClick) { text = text };
            b.style.width = width; b.style.height = 24; b.style.fontSize = 12;
            b.style.marginRight = 6; b.style.marginLeft = 0; b.style.marginTop = 0; b.style.marginBottom = 0;
            b.style.backgroundColor = new Color(0.10f, 0.15f, 0.30f);
            b.style.color = new Color(0.93f, 0.95f, 1f);
            b.style.borderTopLeftRadius = b.style.borderTopRightRadius = b.style.borderBottomLeftRadius = b.style.borderBottomRightRadius = 4;
            b.style.borderTopWidth = b.style.borderBottomWidth = b.style.borderLeftWidth = b.style.borderRightWidth = 1;
            b.style.borderTopColor = b.style.borderBottomColor = b.style.borderLeftColor = b.style.borderRightColor = new Color(0.26f, 0.36f, 0.62f);
            return b;
        }

        private void BuildUi()
        {
            var settings = Resources.Load<PanelSettings>(UI.PangeaScreen.PanelSettingsResource);
            if (settings == null) return;                       // sem painel de UI (teste mínimo): o log segue capturando
            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = settings;
            _doc.sortingOrder = 5000;                           // acima de toda tela e da HUD de batalha
            var root = _doc.rootVisualElement;
            if (root == null) return;
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.right = 0; root.style.top = 0; root.style.bottom = 0;

            // Aba pequena no canto inferior esquerdo: nenhuma tela põe nada ali.
            _tab = MakeButton("LOG", () => SetOpen(!_open), 44);
            _tab.name = "gamelog-tab";
            _tab.style.position = Position.Absolute; _tab.style.left = 4; _tab.style.bottom = 4;
            _tab.style.height = 18; _tab.style.fontSize = 10; _tab.style.opacity = 0.75f;
            _tab.style.paddingLeft = 4; _tab.style.paddingRight = 4; _tab.style.paddingTop = 0; _tab.style.paddingBottom = 0;
            _tab.RegisterCallback<PointerEnterEvent>(_ => _overTab = true);
            _tab.RegisterCallback<PointerLeaveEvent>(_ => _overTab = false);
            root.Add(_tab);

            _panel = new VisualElement { name = "gamelog-panel" };
            _panel.style.position = Position.Absolute;
            _panel.style.left = 8; _panel.style.bottom = 28; _panel.style.top = 8;
            _panel.style.width = new Length(72, LengthUnit.Percent); _panel.style.maxWidth = 900;
            _panel.style.backgroundColor = PanelBg;
            _panel.style.borderTopWidth = _panel.style.borderBottomWidth = _panel.style.borderLeftWidth = _panel.style.borderRightWidth = 1;
            _panel.style.borderTopColor = _panel.style.borderBottomColor = _panel.style.borderLeftColor = _panel.style.borderRightColor = Border;
            _panel.style.borderTopLeftRadius = _panel.style.borderTopRightRadius = _panel.style.borderBottomLeftRadius = _panel.style.borderBottomRightRadius = 8;
            _panel.style.paddingLeft = _panel.style.paddingRight = 10; _panel.style.paddingTop = _panel.style.paddingBottom = 8;
            _panel.style.display = DisplayStyle.None;
            _panel.RegisterCallback<PointerEnterEvent>(_ => _overPanel = true);
            _panel.RegisterCallback<PointerLeaveEvent>(_ => _overPanel = false);
            root.Add(_panel);

            var head = new VisualElement(); head.style.flexDirection = FlexDirection.Row; head.style.alignItems = Align.Center; head.style.marginBottom = 6;
            _titleLbl = new Label(); _titleLbl.style.flexGrow = 1; _titleLbl.style.fontSize = 14;
            _titleLbl.style.unityFontStyleAndWeight = FontStyle.Bold; _titleLbl.style.color = new Color(0.93f, 0.95f, 1f);
            head.Add(_titleLbl);
            head.Add(MakeButton("Fechar", () => SetOpen(false), 70));
            _panel.Add(head);

            var bar = new VisualElement(); bar.style.flexDirection = FlexDirection.Row; bar.style.alignItems = Align.Center; bar.style.marginBottom = 4;
            bar.Add(MakeButton("Copiar log", () => { WebBridge.CopyText(Dump()); SetStatus("Log copiado. Cole no relato do bug."); }, 96));
            bar.Add(MakeButton("Baixar log", () => SetStatus("Salvo: " + WebBridge.SaveText($"pangea-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt", Dump())), 96));
            Button onlyBtn = null;
            onlyBtn = MakeButton("Só erros: não", () => { _onlyErrors = !_onlyErrors; onlyBtn.text = _onlyErrors ? "Só erros: sim" : "Só erros: não"; }, 110);
            bar.Add(onlyBtn);
            bar.Add(MakeButton("Limpar", () => { Clear(); SetStatus("Log limpo."); }, 70));
            var gap = new VisualElement(); gap.style.flexGrow = 1; bar.Add(gap);
            _restartBtn = MakeButton(RestartLabel, () =>
            {
                Debug.Log("[GameLog] reinício pedido pelo painel de log");
                var act = RestartAction; SetOpen(false); act?.Invoke();
            }, 210);
            _restartBtn.style.backgroundColor = new Color(0.55f, 0.40f, 0.10f);
            _restartBtn.style.marginRight = 0;
            bar.Add(_restartBtn);
            _panel.Add(bar);

            _statusLbl = new Label(); _statusLbl.style.fontSize = 11; _statusLbl.style.color = new Color(0.62f, 0.69f, 0.82f);
            _panel.Add(_statusLbl);

            _list = new ScrollView(ScrollViewMode.Vertical); _list.style.flexGrow = 1;
            _list.style.backgroundColor = new Color(0f, 0f, 0f, 0.3f);
            _panel.Add(_list);
        }

        private void SetStatus(string s) { _status = s; if (_statusLbl != null) _statusLbl.text = s; }

        private void SetOpen(bool open)
        {
            _open = open;
            if (_panel == null) return;
            _panel.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (!open) _overPanel = false;
            _shownLines = -1;                                    // força redesenhar a lista ao abrir
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f8Key.wasPressedThisFrame) SetOpen(!_open);
            PointerOver = _overTab || (_open && _overPanel);
            if (_tab == null) return;

            int errors = ErrorCount, lines = LineCount;
            if (errors != _shownErrors)
            {
                _tab.text = errors > 0 ? $"LOG · {errors}" : "LOG";
                _tab.style.width = errors > 0 ? 66 : 44;
                _tab.style.backgroundColor = errors > 0 ? new Color(0.62f, 0.16f, 0.14f) : new Color(0.10f, 0.15f, 0.30f);
                _tab.style.opacity = errors > 0 ? 1f : 0.75f;
            }
            if (!_open) { _shownErrors = errors; return; }

            if (_shownRestart != RestartAction)
            {
                _shownRestart = RestartAction;
                _restartBtn.text = RestartLabel;
                _restartBtn.style.display = RestartAction != null ? DisplayStyle.Flex : DisplayStyle.None;
            }
            else if (RestartAction == null) _restartBtn.style.display = DisplayStyle.None;

            if (lines == _shownLines && errors == _shownErrors && _onlyErrors == _shownOnlyErrors) return;
            _shownLines = lines; _shownErrors = errors; _shownOnlyErrors = _onlyErrors;
            _titleLbl.text = $"Log da partida — {lines} linhas, {errors} erro(s)   (F8 abre/fecha)";
            _list.Clear();
            lock (_lock)
            {
                int shown = 0;
                for (int i = _lines.Count - 1; i >= 0 && shown < 250; i--)           // mais recentes primeiro
                {
                    var l = _lines[i];
                    bool bad = l.type == LogType.Error || l.type == LogType.Exception || l.type == LogType.Assert;
                    if (_onlyErrors && !bad) continue;
                    shown++;
                    string text = $"[{l.time:0.0}] {l.text}";
                    if (bad && !string.IsNullOrEmpty(l.stack))
                    {
                        string st = l.stack.Length > 500 ? l.stack.Substring(0, 500) + "…" : l.stack;
                        text += "\n    " + st.TrimEnd().Replace("\n", "\n    ");
                    }
                    var row = new Label(text) { enableRichText = false };
                    row.style.fontSize = 11; row.style.whiteSpace = WhiteSpace.Normal;
                    row.style.marginTop = row.style.marginBottom = 0; row.style.paddingTop = row.style.paddingBottom = 1;
                    row.style.color = bad ? new Color(1f, 0.55f, 0.5f) : l.type == LogType.Warning ? new Color(1f, 0.88f, 0.5f) : new Color(0.85f, 0.9f, 1f);
                    _list.Add(row);
                }
            }
        }
    }
}
