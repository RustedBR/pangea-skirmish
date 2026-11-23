using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Checkpoint do tutorial por capítulo (anexo §2.10). Guarda só o último capítulo concluído em
    /// PlayerPrefs (no navegador vai para o IndexedDB), separado do perfil do jogador.
    /// O menu pode oferecer "Continuar capítulo N"; o tutorial começa pelo primeiro capítulo não concluído.
    /// </summary>
    public static class TutorialProgress
    {
        private const string Key = "tutorial.chaptersDone";

        /// <summary>Quantos capítulos já foram concluídos em sequência (0 = nenhum).</summary>
        public static int ChaptersDone
        {
            get { try { return Mathf.Max(0, PlayerPrefs.GetInt(Key, 0)); } catch { return 0; } }
        }

        /// <summary>Marca o capítulo (0-based) como concluído. Nunca volta atrás.</summary>
        public static void MarkChapterDone(int chapter)
        {
            try
            {
                if (chapter + 1 > ChaptersDone) { PlayerPrefs.SetInt(Key, chapter + 1); PlayerPrefs.Save(); }
            }
            catch (System.Exception e) { Debug.LogWarning("[Tutorial] não salvou o checkpoint: " + e.Message); }
        }

        public static void Reset()
        {
            try { PlayerPrefs.DeleteKey(Key); PlayerPrefs.Save(); } catch { }
        }
    }
}
