using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PangeaSkirmish.Editor
{
    /// <summary>
    /// Scaffolder de telas de UI Toolkit. Gera, para um nome (ex.: "MainMenu"), o trio:
    ///   - Assets/Resources/UI/Screens/{Nome}.uxml   (layout — abra no UI Builder p/ desenhar)
    ///   - Assets/Resources/UI/Screens/{Nome}.uss    (estilos específicos da tela)
    ///   - Assets/Scripts/UI/{Nome}Screen.cs         (controller: PangeaScreen + Bind())
    /// É assim que se cria uma UI nova: rode isto, desenhe no UI Builder, ligue os elementos no Bind().
    /// </summary>
    public class NewUIScreenWindow : EditorWindow
    {
        private const string ScreensDir = "Assets/Resources/UI/Screens";
        private const string ScriptsDir = "Assets/Scripts/UI";

        private string _screenName = "MinhaTela";

        [MenuItem(PangeaMenu.UI + "New UI Screen…")]
        public static void Open()
        {
            var w = GetWindow<NewUIScreenWindow>(true, "Nova Tela de UI");
            w.minSize = new Vector2(420, 180);
        }

        [MenuItem(PangeaMenu.UI + "Open UI Builder")]
        public static void OpenUiBuilder()
        {
            if (!EditorApplication.ExecuteMenuItem("Window/UI Toolkit/UI Builder"))
                Debug.LogWarning("[UI] Não achei o menu do UI Builder. Abra por Window ▸ UI Toolkit ▸ UI Builder.");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Cria o trio UXML + USS + Controller de uma tela nova.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();
            _screenName = EditorGUILayout.TextField("Nome da tela", _screenName);

            string safe = Sanitize(_screenName);
            bool valid = !string.IsNullOrEmpty(safe);
            if (valid && safe != _screenName)
                EditorGUILayout.HelpBox($"Será criada como: {safe}", MessageType.Info);
            if (!valid)
                EditorGUILayout.HelpBox("Nome inválido — use letras/dígitos, começando com letra.", MessageType.Warning);

            bool exists = valid && File.Exists($"{ScreensDir}/{safe}.uxml");
            if (exists)
                EditorGUILayout.HelpBox($"Já existe {safe}.uxml — a criação não vai sobrescrever.", MessageType.Warning);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!valid || exists))
            {
                if (GUILayout.Button("Criar tela", GUILayout.Height(32)))
                    Create(safe);
            }
        }

        private static void Create(string name)
        {
            UIToolkitSetup.Setup(); // garante pastas + PanelSettings

            string uxmlPath   = $"{ScreensDir}/{name}.uxml";
            string ussPath    = $"{ScreensDir}/{name}.uss";
            string scriptPath = $"{ScriptsDir}/{name}Screen.cs";

            if (File.Exists(uxmlPath) || File.Exists(scriptPath))
            {
                Debug.LogWarning($"[UI] Abortado: já existe {name}. Nada foi sobrescrito.");
                return;
            }

            File.WriteAllText(uxmlPath,   Uxml.Replace("__NAME__", name), new UTF8Encoding(false));
            File.WriteAllText(ussPath,    Uss.Replace("__NAME__", name),  new UTF8Encoding(false));
            File.WriteAllText(scriptPath, Controller.Replace("__NAME__", name), new UTF8Encoding(false));

            AssetDatabase.Refresh();
            var uxmlAsset = AssetDatabase.LoadAssetAtPath<Object>(uxmlPath);
            EditorGUIUtility.PingObject(uxmlAsset);
            Debug.Log($"[UI] Tela '{name}' criada:\n  {uxmlPath}\n  {ussPath}\n  {scriptPath}\n" +
                      "Abra o .uxml no UI Builder para desenhar, depois preencha o Bind() no controller.");
        }

        /// <summary>Mantém só letras/dígitos e força inicial maiúscula (identificador C# válido).</summary>
        private static string Sanitize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var sb = new StringBuilder();
            foreach (var ch in raw.Trim())
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            var s = sb.ToString();
            if (s.Length == 0 || !char.IsLetter(s[0])) return "";
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        // ── Templates ─────────────────────────────────────────────────────────
        private const string Uxml =
@"<ui:UXML xmlns:ui=""UnityEngine.UIElements"" xmlns:uie=""UnityEditor.UIElements"" editor-extension-mode=""False"">
    <Style src=""../Theme/PangeaTheme.uss"" />
    <ui:VisualElement name=""root"" class=""pg-theme"" style=""flex-grow: 1; align-items: center; justify-content: center;"">
        <ui:VisualElement name=""panel"" class=""pg-panel"" style=""min-width: 420px;"">
            <ui:Label text=""__NAME__"" name=""title"" class=""pg-title"" />
            <!-- Desenhe a tela aqui no UI Builder. Dê 'name' aos elementos que o controller usa. -->
        </ui:VisualElement>
    </ui:VisualElement>
</ui:UXML>
";

        private const string Uss =
@"/* Estilos específicos da tela __NAME__ (o visual base vem de PangeaTheme.uss). */
";

        private const string Controller =
@"using UnityEngine.UIElements;

namespace PangeaSkirmish.UI
{
    /// <summary>
    /// Controller da tela __NAME__. Layout em Resources/UI/Screens/__NAME__.uxml (edite no UI Builder).
    /// Ligue os elementos nomeados do UXML à lógica do jogo dentro de Bind().
    /// </summary>
    public class __NAME__Screen : PangeaScreen
    {
        protected override string UxmlResource => ""UI/Screens/__NAME__"";

        protected override void Bind()
        {
            // Exemplo de fiação:
            // var play = Root.Q<Button>(""play"");
            // if (play != null) play.clicked += OnPlay;
        }
    }
}
";
    }
}
