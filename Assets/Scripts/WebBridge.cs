using System.IO;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace PangeaSkirmish
{
    /// <summary>
    /// Ponte com o navegador (Assets/WebGL/PangeaWeb.jslib): copiar texto e baixar arquivo.
    /// Fora do WebGL usa a área de transferência do sistema e grava em persistentDataPath.
    /// </summary>
    public static class WebBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void PangeaCopyText(string text);
        [DllImport("__Internal")] private static extern void PangeaDownloadText(string name, string text);
#endif
        /// <summary>Último texto copiado (testes e diagnóstico).</summary>
        public static string LastCopied { get; private set; }

        public static void CopyText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            LastCopied = text;
#if UNITY_WEBGL && !UNITY_EDITOR
            PangeaCopyText(text);
#else
            GUIUtility.systemCopyBuffer = text;
#endif
        }

        /// <summary>Baixa (web) ou grava (desktop) um arquivo de texto. Devolve onde foi parar.</summary>
        public static string SaveText(string fileName, string text)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            PangeaDownloadText(fileName, text ?? "");
            return "download: " + fileName;
#else
            try
            {
                string path = Path.Combine(Application.persistentDataPath, fileName);
                File.WriteAllText(path, text ?? "");
                return path;
            }
            catch (System.Exception e) { return "erro: " + e.Message; }
#endif
        }
    }
}
