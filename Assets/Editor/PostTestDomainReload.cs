using UnityEditor;

namespace PangeaSkirmish.Editor
{
    /// <summary>
    /// Os testes de toque usam o InputTestFixture, que troca a instância do Input System. Com o Play sem domain
    /// reload (EnterPlayModeOptions), as ações de UI ficavam presas à instância do teste e o Play seguinte enchia o
    /// console de erros do Input System ao mexer o mouse (2026-11-15). Quando esses testes rodaram, recarrega o
    /// domínio ao voltar para a edição — o Input System volta limpo.
    /// </summary>
    [InitializeOnLoad]
    internal static class PostTestDomainReload
    {
        private const string Key = "PangeaSkirmish.ReloadAfterInputTests";

        static PostTestDomainReload()
        {
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        private static void OnPlayMode(PlayModeStateChange st)
        {
            if (st != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Key, false)) return;
            SessionState.EraseBool(Key);
            // Espera uns segundos: o Test Runner (e quem o chamou, ex.: o MCP) ainda está entregando o resultado.
            double at = EditorApplication.timeSinceStartup + 8.0;
            void Tick()
            {
                if (EditorApplication.timeSinceStartup < at || EditorApplication.isPlayingOrWillChangePlaymode) return;
                EditorApplication.update -= Tick;
                EditorUtility.RequestScriptReload();
            }
            EditorApplication.update += Tick;
        }
    }
}
