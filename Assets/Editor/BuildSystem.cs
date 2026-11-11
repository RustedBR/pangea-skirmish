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

            // Configuração canônica (Medium + Explicit + Brotli + decompressionFallback + template):
            // vem do BuildWebGL.cs, a fonte única. O menu já teve uma cópia própria com
            // compressão Disabled e nomes .data/.wasm fixos — gerava um build que o
            // Pages não serve. `global::` porque este tipo tem um método chamado BuildWebGL.
            global::BuildWebGL.ApplyCanonicalConfig();

            var options = new BuildPlayerOptions
            {
                scenes = GetEnabledScenes(),
                locationPathName = buildPath,
                target = target,
                options = BuildOptions.CleanBuildCache
            };

            ExecuteBuild(options, "WebGL");
            global::BuildWebGL.FixWebGLIndexUrls(buildPath);
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
