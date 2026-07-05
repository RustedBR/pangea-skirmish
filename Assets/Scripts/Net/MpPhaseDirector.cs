// Net/MpPhaseDirector.cs
// Singleton DontDestroyOnLoad que reage às mudanças de fase do RoomManager
// e cria os overlays MP corretos (CharCreationHUD, mensagem de espera de placement).
// É spawnado pelo NetBootstrap quando a sessão MP inicia (logo após StartHost/StartClient).

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PangeaSkirmish
{
    public class MpPhaseDirector : MonoBehaviour
    {
        public static MpPhaseDirector Instance { get; private set; }

        private Canvas _overlayCanvas;
        private CharCreationHUD _charCreationHUD;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Quando a cena Sandbox carrega em MP, registrar CollabMapSync (já spawned)
            // e escutar mudanças de fase
            if (RoomManager.Instance != null)
            {
                RoomManager.Instance.OnPhaseChanged -= OnPhaseChanged;
                RoomManager.Instance.OnPhaseChanged += OnPhaseChanged;

                // Processar fase atual imediatamente (ex: late-joiner que entrou já em CharCreation)
                OnPhaseChanged(RoomManager.Instance.CurrentPhase);
            }
        }

        private bool _subscribed;

        private void Update()
        {
            // No CLIENTE o RoomManager é replicado alguns frames após conectar; como o fluxo
            // da sala fica no MainMenu (sem troca de cena), o OnSceneLoaded não dispara — então
            // assinamos aqui assim que RoomManager.Instance aparecer (senão o overlay de
            // criação de personagem nunca abre no cliente).
            if (_subscribed || RoomManager.Instance == null) return;
            RoomManager.Instance.OnPhaseChanged -= OnPhaseChanged;
            RoomManager.Instance.OnPhaseChanged += OnPhaseChanged;
            _subscribed = true;
            Debug.Log($"[MP] MpPhaseDirector inscrito no RoomManager; fase atual = {RoomManager.Instance.CurrentPhase}");
            OnPhaseChanged(RoomManager.Instance.CurrentPhase);
        }

        private void OnPhaseChanged(RoomPhase phase)
        {
            Debug.Log($"[MP] MpPhaseDirector.OnPhaseChanged -> {phase}");
            if (phase == RoomPhase.CharCreation)
                ShowCharCreation();
            else
                HideCharCreation();
        }

        // =========================================================================
        // CharCreation overlay
        // =========================================================================
        private void ShowCharCreation()
        {
            Debug.Log("[MP] Criacao de personagem: abrindo overlay CharCreationHUD");
            if (_charCreationHUD != null) return; // já aberto

            EnsureOverlayCanvas();
            var go = new GameObject("CharCreationHUD", typeof(RectTransform));
            go.transform.SetParent(_overlayCanvas.transform, false);
            // go ocupa a tela inteira e É o pai de TODA a UI do overlay (dimmer + painel),
            // para que HideCharCreation (Destroy(go)) remova tudo — senão o painel fica
            // órfão no canvas DontDestroyOnLoad e "vaza" para a cena seguinte (sandbox).
            var goRt = go.GetComponent<RectTransform>();
            goRt.anchorMin = Vector2.zero; goRt.anchorMax = Vector2.one;
            goRt.offsetMin = Vector2.zero; goRt.offsetMax = Vector2.zero;
            _charCreationHUD = go.AddComponent<CharCreationHUD>();
            _charCreationHUD.Build(go.transform);
        }

        private void HideCharCreation()
        {
            if (_charCreationHUD != null)
            {
                Destroy(_charCreationHUD.gameObject);
                _charCreationHUD = null;
            }
        }

        // =========================================================================
        // Canvas overlay DontDestroyOnLoad
        // =========================================================================
        private void EnsureOverlayCanvas()
        {
            if (_overlayCanvas != null) return;

            var go = new GameObject("MpOverlayCanvas");
            DontDestroyOnLoad(go);
            _overlayCanvas = go.AddComponent<Canvas>();
            _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _overlayCanvas.sortingOrder = 100; // acima de tudo
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        }

        // =========================================================================
        // Criação pelo NetBootstrap
        // =========================================================================
        public static MpPhaseDirector EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("MpPhaseDirector");
            return go.AddComponent<MpPhaseDirector>();
        }
    }
}
