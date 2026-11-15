using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PangeaSkirmish.Editor
{
    /// <summary>
    /// O modo celular e o tamanho da interface mexem no PanelSettings compartilhado em Play (como no aparelho).
    /// No editor isso acabava salvo no asset (2026-11-15: ficou em 1120×630). Guarda os valores ao entrar em Play
    /// e devolve ao voltar para a edição, por qualquer caminho que o Play tenha terminado.
    /// </summary>
    [InitializeOnLoad]
    internal static class PanelSettingsGuard
    {
        private const string Key = "PangeaSkirmish.PanelSettingsGuard";

        static PanelSettingsGuard()
        {
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        private static PanelSettings Asset() => Resources.Load<PanelSettings>(UI.PangeaScreen.PanelSettingsResource);

        private static void OnPlayMode(PlayModeStateChange st)
        {
            var ps = Asset();
            if (ps == null) return;
            if (st == PlayModeStateChange.ExitingEditMode)
            {
                var r = ps.referenceResolution;
                SessionState.SetString(Key, $"{r.x};{r.y};{ps.scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            }
            else if (st == PlayModeStateChange.EnteredEditMode)
            {
                var saved = SessionState.GetString(Key, "");
                if (saved == "") return;
                SessionState.EraseString(Key);
                var p = saved.Split(';');
                var res = new Vector2Int(int.Parse(p[0]), int.Parse(p[1]));
                float scale = float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture);
                ps.referenceResolution = res;
                ps.scale = scale;
                // Grava sempre: a memória pode já ter voltado (restauração ao sair do Play) com o disco ainda
                // na versão salva durante o Play. Depois de um quadro, quando o editor terminou de voltar.
                EditorApplication.delayCall += () =>
                {
                    if (ps == null) return;
                    EditorUtility.SetDirty(ps);
                    AssetDatabase.SaveAssetIfDirty(ps);
                };
            }
        }
    }
}
