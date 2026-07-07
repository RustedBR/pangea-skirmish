// Net/MpPhaseDirector.cs
// Singleton DontDestroyOnLoad que reage às mudanças de fase do RoomManager
// e cria os overlays MP corretos (CharCreationHUD, mensagem de espera de placement).
// É spawnado pelo NetBootstrap quando a sessão MP inicia (logo após StartHost/StartClient).

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using PangeaSkirmish.UI;

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

            // CharCreationHUD é PangeaScreen (UI Toolkit): Spawn cria o GO com UIDocument
            // e carrega o UXML automaticamente. O render é full-screen via PanelSettings
            // compartilhado — não precisa de canvas pai (o dimmer no UXML cobre a cena).
            // HideCharCreation (Destroy(go)) remove tudo, evitando vazamento p/ sandbox.
            _charCreationHUD = PangeaScreen.Spawn<CharCreationHUD>("CharCreationHUD");
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
