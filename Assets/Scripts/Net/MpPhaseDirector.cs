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

        // Quando a cena Sandbox carrega, o RoomManager pode já existir (ou ainda não ter
        // replicado) — delega para o mesmo ponto único de assinatura usado pelo Update.
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TryBindRoomManager();

        private RoomManager _boundRm; // instância REAL assinada (sala recriada = objeto novo)

        private void Update() => TryBindRoomManager();

        /// <summary>
        /// Único ponto de assinatura ao RoomManager.OnPhaseChanged. Antes, OnSceneLoaded e
        /// Update tinham cada um sua própria lógica de (re)assinatura — redundante e um
        /// convite a duplicar callbacks se algum dia divergissem. Rastreia a INSTÂNCIA (não
        /// uma flag booleana): se a sala for recriada, um novo RoomManager é spawnado e a
        /// assinatura antiga fica órfã — sem isso, o overlay de criação nunca reabriria.
        /// </summary>
        private void TryBindRoomManager()
        {
            var rm = RoomManager.Instance;
            if (rm == _boundRm) return;

            if (_boundRm != null) _boundRm.OnPhaseChanged -= OnPhaseChanged;
            _boundRm = rm;
            if (rm == null) return;

            rm.OnPhaseChanged += OnPhaseChanged;
            Debug.Log($"[MP] MpPhaseDirector (re)inscrito no RoomManager; fase atual = {rm.CurrentPhase}");
            OnPhaseChanged(rm.CurrentPhase);
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
