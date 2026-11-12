using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace PangeaSkirmish
{
    /// <summary>
    /// Registro da partida que funciona NO BUILD (inclusive WebGL, onde não há arquivo de log):
    /// guarda tudo que passa pelo Debug.Log, com erros e exceções, e mostra um painel com as
    /// últimas linhas, botões de copiar/baixar e o reinício rápido. Abre no botão LOG (canto
    /// superior esquerdo) ou com F8. Criado sozinho ao iniciar o jogo.
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
        private Vector2 _scroll;
        private string _status = "";
        private bool _onlyErrors;
        private GUIStyle _lineStyle, _btnStyle, _boxStyle, _titleStyle;
        private Texture2D _bgTex;

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
            Application.logMessageReceived += OnLog;
            SceneManager.sceneLoaded += (s, m) => { RestartAction = null; Debug.Log($"[GameLog] cena carregada: {s.name}"); };
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

        // ---------------------------------------------------------------- painel (IMGUI)

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f8Key.wasPressedThisFrame) _open = !_open;
        }

        private void EnsureStyles()
        {
            if (_lineStyle != null) return;
            _bgTex = new Texture2D(1, 1); _bgTex.SetPixel(0, 0, new Color(0.04f, 0.06f, 0.14f, 0.96f)); _bgTex.Apply();
            _boxStyle = new GUIStyle(GUI.skin.box); _boxStyle.normal.background = _bgTex;
            _lineStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, richText = false };
            _lineStyle.padding = new RectOffset(2, 2, 0, 0); _lineStyle.margin = new RectOffset(0, 0, 0, 0);
            _btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 13 };
            _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
        }

        private void OnGUI()
        {
            EnsureStyles();
            float s = Mathf.Max(1f, Screen.height / 900f);
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float W = Screen.width / s, H = Screen.height / s;
            var mouse = Event.current.mousePosition;

            // Botão sempre visível: fica vermelho e conta os erros.
            var btn = new Rect(6, 6, ErrorCount > 0 ? 118 : 58, 24);
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = ErrorCount > 0 ? new Color(1f, 0.35f, 0.3f) : new Color(0.6f, 0.7f, 1f);
            if (GUI.Button(btn, ErrorCount > 0 ? $"LOG · {ErrorCount} erro{(ErrorCount == 1 ? "" : "s")}" : "LOG", _btnStyle)) _open = !_open;
            GUI.backgroundColor = prev;
            bool over = btn.Contains(mouse);

            if (_open)
            {
                var panel = new Rect(6, 36, Mathf.Min(W - 12, 860), Mathf.Min(H - 48, 560));
                over |= panel.Contains(mouse);
                GUI.Box(panel, GUIContent.none, _boxStyle);
                GUILayout.BeginArea(new Rect(panel.x + 8, panel.y + 6, panel.width - 16, panel.height - 12));
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Log da partida — {LineCount} linhas, {ErrorCount} erro(s)   (F8 abre/fecha)", _titleStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Fechar", _btnStyle, GUILayout.Width(70))) _open = false;
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Copiar log", _btnStyle, GUILayout.Width(110))) { WebBridge.CopyText(Dump()); _status = "Log copiado. Cole no relato do bug."; }
                if (GUILayout.Button("Baixar log", _btnStyle, GUILayout.Width(110)))
                    _status = "Salvo: " + WebBridge.SaveText($"pangea-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt", Dump());
                _onlyErrors = GUILayout.Toggle(_onlyErrors, " só erros", GUILayout.Width(90));
                if (GUILayout.Button("Limpar", _btnStyle, GUILayout.Width(70))) { Clear(); _status = "Log limpo."; }
                GUILayout.FlexibleSpace();
                if (RestartAction != null)
                {
                    GUI.backgroundColor = new Color(1f, 0.8f, 0.35f);
                    if (GUILayout.Button(RestartLabel, _btnStyle, GUILayout.Width(190)))
                    {
                        Debug.Log("[GameLog] reinício pedido pelo painel de log");
                        var act = RestartAction; _open = false; act?.Invoke();
                    }
                    GUI.backgroundColor = prev;
                }
                GUILayout.EndHorizontal();
                if (!string.IsNullOrEmpty(_status)) GUILayout.Label(_status, _lineStyle);

                _scroll = GUILayout.BeginScrollView(_scroll);
                lock (_lock)
                {
                    int shown = 0;
                    for (int i = _lines.Count - 1; i >= 0 && shown < 250; i--)       // mais recentes primeiro
                    {
                        var l = _lines[i];
                        bool bad = l.type == LogType.Error || l.type == LogType.Exception || l.type == LogType.Assert;
                        if (_onlyErrors && !bad) continue;
                        shown++;
                        GUI.contentColor = bad ? new Color(1f, 0.55f, 0.5f) : l.type == LogType.Warning ? new Color(1f, 0.88f, 0.5f) : new Color(0.85f, 0.9f, 1f);
                        GUILayout.Label($"[{l.time:0.0}] {l.text}", _lineStyle);
                        if (bad && !string.IsNullOrEmpty(l.stack))
                        {
                            GUI.contentColor = new Color(0.75f, 0.6f, 0.6f);
                            string st = l.stack.Length > 500 ? l.stack.Substring(0, 500) + "…" : l.stack;
                            GUILayout.Label("    " + st.TrimEnd().Replace("\n", "\n    "), _lineStyle);
                        }
                    }
                    GUI.contentColor = Color.white;
                }
                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }
            PointerOver = over;
            GUI.matrix = old;
        }
    }
}
