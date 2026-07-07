using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PangeaSkirmish.Editor
{
    /// <summary>
    /// Prepara a base de UI Toolkit do projeto (idempotente — seguro rodar de novo):
    ///  - cria as pastas Assets/Resources/UI/{Theme,Screens} e Assets/Scripts/UI
    ///  - cria o PanelSettings compartilhado em Resources/UI/PangeaPanelSettings.asset
    ///    (Scale With Screen Size, ref 1920×1080), atribuindo um ThemeStyleSheet se houver.
    /// Rode uma vez antes de criar a primeira tela (Pangea Skirmish/UI/New UI Screen…).
    /// </summary>
    public static class UIToolkitSetup
    {
        private const string ResUI       = "Assets/Resources/UI";
        private const string PanelPath   = ResUI + "/PangeaPanelSettings.asset";
        private const string ScriptsUI   = "Assets/Scripts/UI";

        [MenuItem(PangeaMenu.UI + "Setup UI Toolkit")]
        public static void Setup()
        {
            EnsureFolder("Assets/Resources");
            EnsureFolder(ResUI);
            EnsureFolder(ResUI + "/Theme");
            EnsureFolder(ResUI + "/Screens");
            EnsureFolder("Assets/Scripts");
            EnsureFolder(ScriptsUI);

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                panel.scaleMode          = PanelScaleMode.ScaleWithScreenSize;
                panel.referenceResolution = new Vector2Int(1920, 1080);
                panel.screenMatchMode    = PanelScreenMatchMode.MatchWidthOrHeight;
                panel.match              = 0.5f; // equilibra largura/altura (bom p/ WebGL/ultrawide)

                var theme = FindDefaultTheme();
                if (theme != null) panel.themeStyleSheet = theme;

                AssetDatabase.CreateAsset(panel, PanelPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[UIToolkitSetup] PanelSettings criado em {PanelPath}.");
                if (theme == null)
                    Debug.LogWarning("[UIToolkitSetup] Nenhum ThemeStyleSheet encontrado. Crie um " +
                        "(Assets ▸ Create ▸ UI Toolkit ▸ TSS Theme File) e atribua ao PangeaPanelSettings, " +
                        "OU deixe assim — as telas já importam PangeaTheme.uss diretamente.");
            }
            else
            {
                Debug.Log("[UIToolkitSetup] PanelSettings já existe — nada a fazer.");
            }

            AssetDatabase.Refresh();
            Selection.activeObject = panel;
            EditorGUIUtility.PingObject(panel);
        }

        /// <summary>Tenta achar um ThemeStyleSheet já no projeto (o runtime theme default do Unity).</summary>
        private static ThemeStyleSheet FindDefaultTheme()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:ThemeStyleSheet"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var tss = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(path);
                if (tss != null) return tss;
            }
            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            var leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
