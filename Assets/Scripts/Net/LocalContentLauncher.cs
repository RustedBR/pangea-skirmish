// Net/LocalContentLauncher.cs
// Abre uma "sala" de conteúdo OFFLINE (loopback localhost) para o jogador criar
// mapa ou personagem sem precisar de uma partida multiplayer de verdade.
//
// Fluxo (atalho direto, sem RoomManager):
//   1. NetBootstrap.EnsureExists() + HostLoopback()  -> sobe host em 127.0.0.1:7777
//   2. marca RuntimeMultiplayerSession.IsLocalContentSession = true
//   3. carrega a cena Sandbox (que já respeita IsMultiplayer=true)
//   4. Criar Personagem => abre o overlay CharCreationHUD em modo local
//      (salva em CharacterStorage e volta ao menu)
//   5. ao concluir/voltar: FinishAndReturnToMenu() faz Shutdown + Reset + recarrega MainMenu
//
// Vantagem: reaproveita 100% a cena/UI de criação do multiplayer, então o que o
// jogador treina offline é EXATAMENTE o que ele fará online.

using UnityEngine;
using UnityEngine.SceneManagement;
using PangeaSkirmish.UI;

namespace PangeaSkirmish
{
    public class LocalContentLauncher : MonoBehaviour
    {
        public static LocalContentLauncher Instance { get; private set; }

        /// <summary>
        /// Cria a sala loopback solo e carrega a cena de edição.
        /// content = "map"  -> Sandbox (só terreno, salvar local)
        /// content = "char" -> Sandbox + overlay CharCreationHUD (salvar local)
        /// </summary>
        public static void Launch(string content)
        {
            var bootstrap = NetBootstrap.EnsureExists();
            if (bootstrap == null) { Debug.LogError("[LocalContent] NetBootstrap nulo."); return; }

            // Já existe uma sessão rodando? (ex.: usuário clicou 2x) — ignora.
            if (RuntimeMultiplayerSession.IsMultiplayer) return;

            bootstrap.HostLoopback();
            RuntimeMultiplayerSession.IsLocalContentSession = true;

            // Garante que a sessão tenha um config padrão válido (budget 30) —
            // o CharCreationHUD.Bind lê CurrentConfig.attributeBudget.
            if (RuntimeMultiplayerSession.CurrentConfig == null)
                RuntimeMultiplayerSession.CurrentConfig = new RoomConfigData();

            Debug.Log($"[LocalContent] Sala loopback solo iniciada (conteudo={content}).");

            // Singleton DontDestroyOnLoad para orquestrar o retorno ao menu.
            if (Instance == null)
            {
                var go = new GameObject("LocalContentLauncher");
                DontDestroyOnLoad(go);
                Instance = go.AddComponent<LocalContentLauncher>();
            }

            if (content == "char")
            {
                // Criar Personagem: NÃO carrega o Sandbox (evita o grid ao fundo).
                // Abre o overlay CharCreationHUD por cima da cena atual (menu), com
                // fundo opaco (cc-dimmer). O usuário confirma ou volta ao menu.
                var hud = PangeaScreen.Spawn<CharCreationHUD>("CharCreationHUD");
                var doc = hud.GetComponent<UnityEngine.UIElements.UIDocument>();
                if (doc != null) doc.sortingOrder = 1000;
                hud.LocalContentMode = true;
                hud.OnBackToMenu = FinishAndReturnToMenu;
            }
            else
            {
                // Criar Mapa: carrega a cena Sandbox (só terreno).
                SceneManager.sceneLoaded += Instance.OnSceneLoaded;
                Instance._pendingContent = content;
                SceneManager.LoadScene("Sandbox");
            }
        }

        private string _pendingContent;

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "Sandbox") return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            // "map": o SandboxController já entra em modo terreno sozinho (IsMultiplayer=true).
        }

        /// <summary>
        /// Chamado pelo CharCreationHUD (modo local) ou por um botão "Voltar" do Sandbox
        /// para encerrar a sala loopback e voltar ao menu principal.
        /// </summary>
        public static void FinishAndReturnToMenu()
        {
            if (NetBootstrap.Instance != null) NetBootstrap.Instance.Shutdown();
            RuntimeMultiplayerSession.Reset();

            if (Instance != null)
            {
                Destroy(Instance.gameObject);
                Instance = null;
            }
            SceneManager.LoadScene("MainMenu");
        }
    }
}
