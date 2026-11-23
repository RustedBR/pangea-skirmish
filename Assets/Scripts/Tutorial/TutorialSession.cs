using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Sessão do tutorial (GDD-027): flag global lida pelo RoundManager/Unit, progresso
    /// persistido em PlayerPrefs e limpeza do mapa de tutorial ao sair.
    /// Estático porque sobrevive ao LoadScene (o tutorial troca de cena para a batalha).
    /// </summary>
    public static class TutorialSession
    {
        private const string CompletedKey = "pangea.tutorial.completed";
        private const string LastPhaseKey = "pangea.tutorial.lastPhase";
        private const string LastLessonKey = "pangea.tutorial.lastLesson";

        /// <summary>True durante a batalha de tutorial. Lido por RoundManager (timer/vitória) e Unit (segurança).</summary>
        public static bool Active { get; private set; }

        public static bool Completed => PlayerPrefs.GetInt(CompletedKey, 0) == 1;
        public static int LastPhase => Mathf.Max(0, PlayerPrefs.GetInt(LastPhaseKey, 0));
        public static int LastLesson => Mathf.Max(0, PlayerPrefs.GetInt(LastLessonKey, 0));

        /// <summary>Entra no modo tutorial. O mapa de tutorial é montado pelo TutorialLauncher.</summary>
        public static void Begin()
        {
            Active = true;
        }

        /// <summary>Salva a fase/lição atual para "Continuar tutorial".</summary>
        public static void SaveProgress(int phase, int lesson)
        {
            PlayerPrefs.SetInt(LastPhaseKey, Mathf.Max(0, phase));
            PlayerPrefs.SetInt(LastLessonKey, Mathf.Max(0, lesson));
            PlayerPrefs.Save();
        }

        public static void MarkCompleted()
        {
            PlayerPrefs.SetInt(CompletedKey, 1);
            PlayerPrefs.SetInt(LastPhaseKey, 0);
            PlayerPrefs.SetInt(LastLessonKey, 0);
            PlayerPrefs.Save();
        }

        /// <summary>Sai do modo tutorial e devolve o mapa padrão ao jogo normal.</summary>
        public static void End()
        {
            Active = false;
            if (RuntimeMap.Selected != null && RuntimeMap.Selected.mapName == TutorialArena.MapName)
                RuntimeMap.Selected = null;
        }
    }
}
