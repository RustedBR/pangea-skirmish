using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace PangeaSkirmish
{
    public class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private GameTuning tuning;
        [SerializeField] private IsoConfig isoConfig;

        // Referências públicas para uso pelo PlacementSync e RoundManager em MP
        [HideInInspector] public GridManager MpGrid;
        [HideInInspector] public Canvas MpCanvas;
        [HideInInspector] public BattleHUD MpHUD;
        [HideInInspector] public PlanningController MpPlanner;
        [HideInInspector] public RoundManager MpRoundManager;

        private void Start()
        {
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

            var canvas = BuildCanvas();
            BuildEventSystem();
            var hud = gameObject.AddComponent<BattleHUD>();
            hud.Build(canvas);

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
            if (RuntimeMultiplayerSession.IsMultiplayer)
            {
                UnitRegistry.Clear();
                MpGrid          = grid;
                MpCanvas        = canvas;
                MpHUD           = hud;
                MpPlanner       = planner;
                MpRoundManager  = round;

                // RoundManager e PlanningController ficam prontos mas NÃO começam
                // PlacementSync vai spawnar unidades e chamar Begin() via StartBattleClientRpc
                planner.Setup(grid, cam, new System.Collections.Generic.List<Unit>());
                round.Setup(grid, planner, hud, canvas, cam, camCtrl,
                    new System.Collections.Generic.List<Unit>(), null, tileFx);

                // Mostrar HUD "Aguardando posicionamento"
                hud.ShowWaitingForPlacement();

                // Notificar PlacementSync de que o grid está pronto
                PlacementSync.Instance?.OnGridReady(grid, canvas, hud, cam, camCtrl, round, planner, tileFx, tuning);
                return;
            }

            // ---- Modo Single-Player (comportamento original) -----------------
            var units = new System.Collections.Generic.List<Unit>();
            Unit controlled = null;

            if (mapForBattle != null)
            {
                foreach (var p in mapForBattle.units)
                {
                    var team  = (Team)p.team;
                    var color = team == Team.Player ? tuning.playerTeamColor : tuning.enemyTeamColor;
                    bool isPlayerChar = team == Team.Player && controlled == null;
                    string resPath = !string.IsNullOrEmpty(p.spritePath) ? p.spritePath : CharacterSpriteCatalog.Default;
                    var weaponId = !string.IsNullOrEmpty(p.weaponId) ? p.weaponId : "";
                    var u = CreateUnit(p.displayName, team, new Vector2Int(p.x, p.y),
                                       color, resPath, grid, p.stats, isPlayerChar, weaponId);
                    units.Add(u);
                    if (isPlayerChar) controlled = u;
                }
                if (controlled == null && units.Count > 0) controlled = units[0];
            }
            else
            {
                const string FIGHTER = "Sprites/TinyTactics/Characters/fighter";
                const string MAGE    = "Sprites/TinyTactics/Characters/mage";

                Unit playerUnit;
                if (RuntimeSelectedCharacter.Active != null)
                {
                    var preset = RuntimeSelectedCharacter.Active;
                    string resPath = !string.IsNullOrEmpty(preset.spritePath) ? preset.spritePath : FIGHTER;
                    string wId = !string.IsNullOrEmpty(preset.weaponId) ? preset.weaponId : "Hatchet";
                    playerUnit = CreateUnit(preset.presetName, Team.Player, new Vector2Int(1, 1),
                        new Color(0.30f, 0.50f, 0.92f),
                        resPath, grid, preset.stats, isPlayerChar: true, weaponId: wId);
                }
                else
                {
                    playerUnit = CreateUnit("Guerreiro", Team.Player, new Vector2Int(1, 1),
                        new Color(0.30f, 0.50f, 0.92f),
                        FIGHTER, grid, tuning.guerreiro, isPlayerChar: true, weaponId: "Hatchet");
                }
                units.Add(playerUnit);

                var ladino = CreateUnit("Ladino", Team.Player, new Vector2Int(1, 8),
                    new Color(0.40f, 0.80f, 0.55f),
                    MAGE, grid, tuning.ladino, weaponId: "WoodenStaff");
                units.Add(ladino);

                var goblinA = CreateUnit("Goblin A", Team.Enemy, new Vector2Int(14, 3),
                    new Color(0.30f, 0.75f, 0.20f), FIGHTER, grid, tuning.goblin, weaponId: "Hatchet");
                units.Add(goblinA);

                var goblinB = CreateUnit("Goblin B", Team.Enemy, new Vector2Int(15, 12),
                    new Color(0.25f, 0.65f, 0.15f), FIGHTER, grid, tuning.goblin, weaponId: "Hatchet");
                units.Add(goblinB);

                var goblinC = CreateUnit("Goblin C", Team.Enemy, new Vector2Int(8, 16),
                    new Color(0.20f, 0.55f, 0.10f), FIGHTER, grid, tuning.goblin, weaponId: "Hatchet");
                units.Add(goblinC);
                controlled = playerUnit;
            }

            planner.Setup(grid, cam, units);
            round.Setup(grid, planner, hud, canvas, cam, camCtrl, units, controlled, tileFx);
            round.Begin();
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

        private Canvas BuildCanvas()
        {
            var go = new GameObject("Canvas");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            go.AddComponent<GraphicRaycaster>();
            return canvas;
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
