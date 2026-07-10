#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Build;
using System.IO;

namespace PangeaSkirmish.Editor
{
    /// <summary>
    /// Build configuration and automation for Pangea Skirmish.
    /// Provides menu items for quick builds across platforms.
    /// </summary>
    public static class BuildSystem
    {
        private const string BUILD_FOLDER = "Builds";

        [MenuItem(PangeaMenu.Build + "WebGL")]
        public static void BuildWebGL()
        {
            BuildTarget target = BuildTarget.WebGL;
            // PASTA FIXA canonica: Build/WebGL (template PROJECT:PangeaSkirmish espera WebGL.*).
            string buildPath = "Build/WebGL";
            Directory.CreateDirectory(buildPath);

            // === CONFIGURACAO WEBGL CANONICA (validada 2026-07-10, skill unity-mcp-workflow) ===
            // Medium stripping OBRIGATORIO (56MB, <100MB Pages) + ExplicitlyThrownExceptionsOnly
            // (pega excecao do SDK Relay/Lobby em vez de abort() silencioso) + compression Disabled
            // (sem .br) + template PROJECT:PangeaSkirmish. NAO usar exceptionSupport=None (aborta MP).
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.WebGL, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(
                UnityEditor.Build.NamedBuildTarget.WebGL, ManagedStrippingLevel.Medium);
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.template = "PROJECT:PangeaSkirmish";
            AssetDatabase.SaveAssets();
            // =========================================================================================

            var options = new BuildPlayerOptions
            {
                scenes = GetEnabledScenes(),
                locationPathName = buildPath,
                target = target,
                options = BuildOptions.CleanBuildCache
            };

            ExecuteBuild(options, "WebGL");
            FixWebGLIndexUrls(buildPath);
        }

        // CORRECAO OBRIGATORIA: o template PROJECT:PangeaSkirmish gera index.html com
        // loaderUrl/dataUrl/frameworkUrl/codeUrl apontando para a PASTA "Build/" (vazia),
        // em vez dos arquivos WebGL.*. Isso da 404 + "Falha no carregamento do <script>".
        // Corrige os 4 caminhos para WebGL.loader.js/.data/.framework.js/.wasm.
        private static void FixWebGLIndexUrls(string locationPathName)
        {
            var indexPath = System.IO.Path.Combine(locationPathName, "index.html");
            if (!System.IO.File.Exists(indexPath))
            {
                Debug.LogWarning("[BuildSystem] index.html nao encontrado para post-fix.");
                return;
            }
            var html = System.IO.File.ReadAllText(indexPath);
            var original = html;
            html = html.Replace("var loaderUrl = buildUrl + \"/\";", "var loaderUrl = buildUrl + \"/WebGL.loader.js\";")
                       .Replace("dataUrl: buildUrl + \"/\",", "dataUrl: buildUrl + \"/WebGL.data\",")
                       .Replace("frameworkUrl: buildUrl + \"/\",", "frameworkUrl: buildUrl + \"/WebGL.framework.js\",")
                       .Replace("codeUrl: buildUrl + \"/\",", "codeUrl: buildUrl + \"/WebGL.wasm\",");
            if (html != original)
            {
                System.IO.File.WriteAllText(indexPath, html);
                Debug.Log("[BuildSystem] Index URLs corrigidas para WebGL.* (canonico).");
            }
        }

        [MenuItem(PangeaMenu.Build + "Windows")]
        public static void BuildWindows()
        {
            BuildTarget target = BuildTarget.StandaloneWindows64;
            string buildPath = GetBuildPath(target);

            var options = new BuildPlayerOptions
            {
                scenes = GetEnabledScenes(),
                locationPathName = buildPath,
                target = target,
                options = BuildOptions.CleanBuildCache
            };

            ExecuteBuild(options, "Windows");
        }

        [MenuItem(PangeaMenu.Build + "Linux")]
        public static void BuildLinux()
        {
            BuildTarget target = BuildTarget.StandaloneLinux64;
            string buildPath = GetBuildPath(target);

            var options = new BuildPlayerOptions
            {
                scenes = GetEnabledScenes(),
                locationPathName = buildPath,
                target = target,
                options = BuildOptions.CleanBuildCache
            };

            ExecuteBuild(options, "Linux");
        }

        [MenuItem(PangeaMenu.Build + "All Platforms")]
        public static void BuildAll()
        {
            BuildWebGL();
            BuildWindows();
            BuildLinux();

            Debug.Log("All builds completed!");
        }

        private static void ExecuteBuild(BuildPlayerOptions options, string platformName)
        {
            Debug.Log($"Starting {platformName} build...");

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"{platformName} build succeeded: {summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:F1}s");
            }
            else
            {
                Debug.LogError($"{platformName} build failed: {summary.result}");
                foreach (var step in report.steps)
                {
                    foreach (var message in step.messages)
                    {
                        if (message.type == LogType.Error)
                            Debug.LogError(message.content);
                    }
                }
            }
        }

        private static string GetBuildPath(BuildTarget target)
        {
            string folder = Path.Combine(BUILD_FOLDER, target.ToString());
            Directory.CreateDirectory(folder);

            switch (target)
            {
                case BuildTarget.WebGL:
                    return Path.Combine(folder, "PangeaSkirmish");
                case BuildTarget.StandaloneWindows64:
                    return Path.Combine(folder, "PangeaSkirmish.exe");
                case BuildTarget.StandaloneLinux64:
                    return Path.Combine(folder, "PangeaSkirmish");
                default:
                    return Path.Combine(folder, "PangeaSkirmish");
            }
        }

        private static string[] GetEnabledScenes()
        {
            var scenes = new System.Collections.Generic.List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                    scenes.Add(scene.path);
            }
            return scenes.ToArray();
        }

        [MenuItem(PangeaMenu.Build + "Open Build Folder")]
        public static void OpenBuildFolder()
        {
            string path = Path.GetFullPath(BUILD_FOLDER);
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            EditorUtility.RevealInFinder(path);
        }

        [MenuItem(PangeaMenu.Build + "Clean Builds")]
        public static void CleanBuilds()
        {
            if (Directory.Exists(BUILD_FOLDER))
            {
                Directory.Delete(BUILD_FOLDER, true);
                Debug.Log("Builds folder cleaned.");
            }
        }
    }
}
#endif
