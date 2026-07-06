// Net/ApplicationQuitTracker.cs
// Flag global "a sessão está encerrando" (app fechando OU saindo do Play Mode no Editor).
// Usado pelos NetworkBehaviours de MP para pular limpeza manual (desinscrições, etc.)
// nesse momento — o próprio NGO destrói NetworkObjects DontDestroyOnLoad em ordem
// não-determinística, e tentar fazer cleanup manual em cima disso pode disparar
// NullReferenceException DENTRO do pacote Netcode for GameObjects
// (NetworkObject.OnNetworkBehaviourDestroyed). Isso é cosmético (só polui o console;
// não afeta o gameplay), mas evitamos contribuir para o problema.
//
// IMPORTANTE: Application.quitting só dispara quando o PROCESSO inteiro fecha (build
// standalone saindo, ou fechar o Editor). Apertar "Stop" para sair do Play Mode NO
// EDITOR é um evento DIFERENTE (EditorApplication.isPlaying muda, mas o processo do
// Editor continua rodando) — sem tratar isso também, a cascata de NRE aparecia sempre
// que o Marcus saía do Play e entrava de novo para testar uma nova sessão MP.
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PangeaSkirmish
{
    public static class ApplicationQuitTracker
    {
        public static bool IsQuitting { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            IsQuitting = false;
            Application.quitting += () => IsQuitting = true;
        }

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void InitEditor()
        {
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.ExitingPlayMode)
                    IsQuitting = true;
                else if (change == PlayModeStateChange.EnteredPlayMode)
                    IsQuitting = false; // nova sessão de Play — reseta a flag
            };
        }
#endif
    }
}
