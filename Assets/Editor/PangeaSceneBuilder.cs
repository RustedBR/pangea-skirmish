using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PangeaSkirmish.EditorTools
{
    public static class PangeaSceneBuilder
    {
        [MenuItem("Pangea/Setup All Scenes")]
        public static void Execute()
        {
            CreateMainMenuScene();
            CreateBattleScene();
            CreateSandboxScene();
            SetupBuildSettings();
            EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");   // menu = cena aberta/principal
            Debug.Log("[Pangea] Setup completo: MainMenu (0) → Battle (1) → Sandbox (2)");
        }

        private static void CreateMainMenuScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("MainMenu");
            go.AddComponent<MainMenuManager>();
            const string path = "Assets/Scenes/MainMenu.unity";
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log("[Pangea] MainMenu.unity criado em " + path);
        }

        private static void CreateBattleScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var boot = new GameObject("Bootstrap");
            boot.AddComponent<GameBootstrap>();
            const string path = "Assets/Scenes/Battle.unity";
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log("[Pangea] Battle.unity criado em " + path);
        }

        private static void CreateSandboxScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Sandbox");
            go.AddComponent<SandboxController>();
            const string path = "Assets/Scenes/Sandbox.unity";
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log("[Pangea] Sandbox.unity criado em " + path);
        }

        private static void SetupBuildSettings()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/Scenes/MainMenu.unity", true), // 0 — principal
                new EditorBuildSettingsScene("Assets/Scenes/Battle.unity",   true), // 1
                new EditorBuildSettingsScene("Assets/Scenes/Sandbox.unity",  true), // 2
            };
        }
    }
}
