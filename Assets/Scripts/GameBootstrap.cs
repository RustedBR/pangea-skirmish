using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using PangeaSkirmish.UI;

namespace PangeaSkirmish
{
    public class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private GameTuning tuning;
        [SerializeField] private IsoConfig isoConfig;

        // Referências públicas para uso pelo PlacementSync e RoundManager em MP
        [HideInInspector] public GridManager MpGrid;
        [HideInInspector] public BattleHUD MpHUD;
        [HideInInspector] public PlanningController MpPlanner;
        [HideInInspector] public RoundManager MpRoundManager;

        private void Start()
        {
            // Volume na metade por padrao (web/build) — ajustavel num menu de opcoes futuro.
            AudioListener.volume = 0.5f;

            if (AudioManager.I == null) new GameObject("AudioManager", typeof(AudioManager));
            AudioManager.I?.PlayMusic(AudioManager.I.bgmBattle);
            if (tuning == null) tuning = RuntimeTuning.Active;
            if (tuning == null) tuning = Resources.Load<GameTuning>("GameTuning");
            if (tuning == null) tuning = ScriptableObject.CreateInstance<GameTuning>();
            RuntimeTuning.Active = tuning;
            AttributeStats.Formulas = tuning.statFormulas;

            if (isoConfig == null)
                isoConfig = Resources.Load<IsoConfig>("IsoConfig");
            if (isoConfig == null)
                isoConfig = ScriptableObject.CreateInstance<IsoConfig>();

            int gridW = 20;
            int gridH = 20;
            float halfW = isoConfig.TileUnitsW * 0.5f;
            float halfH = isoConfig.TileUnitsH * 0.5f;
            // centro do grid em coordenadas de tela isométrica (XY, z=0)
            float centerX = ((gridW - 1) - (gridH - 1)) * 0.5f * halfW;
            float centerY = -((gridW - 1) + (gridH - 1)) * 0.5f * halfH;

            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
            }
            cam.orthographic = true;
            cam.orthographicSize = tuning.camInitialSize;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = tuning.battleBackgroundColor;

            var camCtrl = cam.gameObject.GetComponent<CameraController>();
            if (camCtrl == null) camCtrl = cam.gameObject.AddComponent<CameraController>();
            camCtrl.Configure(cam, new Vector3(centerX, centerY, 0f), cam.orthographicSize);
            camCtrl.followSpeed  = tuning.followSpeed;
            camCtrl.edgeMargin   = tuning.edgeMargin;
            camCtrl.edgePanSpeed = tuning.edgePanSpeed;
            camCtrl.dragSpeed    = tuning.dragSpeed;

            // Em MP: usar o mapa colaborativo se disponível
            var mapForBattle = RuntimeMap.Selected;
            if (RuntimeMultiplayerSession.IsMultiplayer && RuntimeMultiplayerSession.CollabMap != null)
                mapForBattle = RuntimeMultiplayerSession.CollabMap;

            var gridGo = new GameObject("GridManager");
            var grid = gridGo.AddComponent<GridManager>();
            grid.iso = isoConfig;
            if (mapForBattle != null)
            {
                grid.sourceMap = mapForBattle;
                gridW = mapForBattle.width;
                gridH = mapForBattle.height;
            }
            grid.width  = gridW;
            grid.height = gridH;
            grid.Build();

            float panRangeX = (grid.width + grid.height) * halfW;
            float panRangeY = (grid.width + grid.height) * halfH;
            camCtrl.SetPanBounds(new Vector2(centerX - panRangeX, centerY - panRangeY),
                                 new Vector2(centerX + panRangeX, centerY + panRangeY));

            BuildEventSystem();
            var hud = PangeaScreen.Spawn<BattleHUD>("BattleHUD");

            var planner = gameObject.AddComponent<PlanningController>();

            var round = gameObject.AddComponent<RoundManager>();
            round.planningTime    = RuntimeMultiplayerSession.IsMultiplayer && RuntimeMultiplayerSession.CurrentConfig != null
                ? RuntimeMultiplayerSession.CurrentConfig.planningTime : tuning.planningTime;
            round.zoomSize        = tuning.zoomSize;
            round.camMoveDuration = tuning.camMoveDuration;
            round.preActionPause  = tuning.preActionPause;
            round.postActionPause = tuning.postActionPause;
            round.initiativeHold  = tuning.initiativeHold;
            round.slotPause       = tuning.slotPause;
            round.bonusConfirmTime = tuning.bonusConfirmSeconds;
            round.bonusStepTime    = tuning.bonusStepSeconds;

            var tileFxGo = new GameObject("TileEffectManager");
            var tileFx = tileFxGo.AddComponent<TileEffectManager>();
            tileFx.Setup(grid, cam);

            // ---- Modo Multiplayer: terreno apenas, sem spawn de unidades ----
            // (O jogo é 100% multiplayer — o modo Single-Player foi removido em 2026-07-07.)
            UnitRegistry.Clear();
            MpGrid          = grid;
            MpHUD           = hud;
            MpPlanner       = planner;
            MpRoundManager  = round;

            // RoundManager e PlanningController ficam prontos mas NÃO começam
            // PlacementSync vai spawnar unidades e chamar Begin() via StartBattleClientRpc
            planner.Setup(grid, cam, new System.Collections.Generic.List<Unit>());
            round.Setup(grid, planner, hud, cam, camCtrl,
                new System.Collections.Generic.List<Unit>(), null, tileFx);

            hud.ShowWaitingForPlacement();

            // Notificar PlacementSync de que o grid está pronto
            if (PlacementSync.Instance != null)
                PlacementSync.Instance.OnGridReady(grid, hud, cam, camCtrl, round, planner, tileFx, tuning);
            else
                Debug.LogError("[GameBootstrap] MP: PlacementSync.Instance NULL na cena Battle — o objeto de rede nao sobreviveu a troca de cena; posicionamento nao inicia.");
        }

        private Unit CreateUnit(string name, Team team, Vector2Int anchor, Color teamColor,
            string resourcePath, GridManager grid, UnitStatBlock block,
            bool isPlayerChar = false, string weaponId = "")
        {
            var go = new GameObject(name);
            var u = go.AddComponent<Unit>();
            u.unitName = name;
            u.team = team;
            u.isPlayerCharacter = isPlayerChar;
            u.weaponId = weaponId; // Setar antes de Init
            u.stats = (block ?? new UnitStatBlock()).ToAttributeStats();
            u.Init(grid, anchor, teamColor, resourcePath);
            return u;
        }

        private void BuildEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }
    }
}
