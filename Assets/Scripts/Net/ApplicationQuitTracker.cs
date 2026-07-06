// Net/ApplicationQuitTracker.cs
// Flag global "a aplicação está encerrando". Usado pelos NetworkBehaviours de MP para
// pular limpeza manual (desinscrições, etc.) durante o OnApplicationQuit — nesse momento
// o próprio NGO destrói NetworkObjects DontDestroyOnLoad em ordem não-determinística, e
// tentar fazer cleanup manual em cima disso pode disparar NullReferenceException DENTRO
// do pacote Netcode for GameObjects (NetworkObject.OnNetworkBehaviourDestroyed). Isso é
// cosmético (só polui o console ao fechar o Play Mode/app; não afeta o gameplay), mas
// evitamos contribuir para o problema.
using UnityEngine;

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
    }
}
