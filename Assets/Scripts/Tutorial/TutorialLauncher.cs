using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PangeaSkirmish
{
    /// <summary>
    /// Lança o tutorial (GDD-027): carrega a cena Battle em modo solo e, quando o GameBootstrap
    /// termina de montar o cenário, Attach() spawna jogador + fera, inicia o round e liga o
    /// TutorialDirector. Aditivo: o único toque no código existente é um if no fim do
    /// GameBootstrap.Start que chama Attach quando Active == true.
    /// </summary>
    public static class TutorialLauncher
    {
        public static bool Active { get; private set; }

        /// <summary>True entre RestartAtLesson e o Attach da cena nova: a cena está recarregando DENTRO do tutorial.</summary>
        public static bool IsRestarting { get; private set; }

        /// <summary>
        /// Sai do modo tutorial por completo (Launcher, sessão e HUD). Estado ESTÁTICO: se ficar ligado, a próxima
        /// partida normal monta o tutorial (GameBootstrap) e perde o relógio e a derrota (TutorialSession).
        /// Chamado ao sair da cena de tutorial por qualquer caminho e ao abrir o menu principal.
        /// </summary>
        public static void Deactivate()
        {
            Active = false; IsRestarting = false;
            _resumeLessonId = null;
            TutorialSession.End();
            TutorialHud.Clear();
        }

        /// <summary>Chamado pelo menu (botão Tutorial). Carrega a batalha em modo solo de tutorial.</summary>
        public static void Start()
        {
            Active = true;
            TutorialSession.Begin();   // sem relógio de decisão nem derrota (RoundManager lê a flag)
            RuntimeMultiplayerSession.IsMultiplayer = false;   // caminho single-player intacto
            SceneManager.LoadScene("Battle", LoadSceneMode.Single);
        }

        /// <summary>Lição a retomar depois de recarregar a cena (Repetir / escolher outra lição no meio).</summary>
        private static string _resumeLessonId;

        /// <summary>
        /// Reset de cena: recarrega a batalha de tutorial do zero (posições, vida, Stamina/Mana, status, zonas,
        /// terreno e contador de rounds) e retoma na lição indicada, sem passar pelo menu.
        /// </summary>
        public static void RestartAtLesson(string lessonId)
        {
            _resumeLessonId = lessonId;
            IsRestarting = true;
            TutorialHud.Clear();
            RuntimeMultiplayerSession.IsMultiplayer = false;
            SceneManager.LoadScene("Battle", LoadSceneMode.Single);
        }

        /// <summary>Devolve (e esquece) a lição a retomar; null = abrir o menu de lições.</summary>
        public static string ConsumeResume()
        {
            var id = _resumeLessonId; _resumeLessonId = null; return id;
        }

        /// <summary>Fim do tutorial (botão do mapa de capítulos): volta ao menu principal.</summary>
        public static void ReturnToMenu()
        {
            Deactivate();
            SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
        }

        /// <summary>
        /// Chamado pelo GameBootstrap.Start ao fim do setup (quando Active). Spawna as unidades do
        /// tutorial, inicia o round e anexa o director. Reusa os componentes que o bootstrap montou.
        /// </summary>
        public static void Attach(GameBootstrap gb)
        {
            IsRestarting = false;
            TutorialSession.Begin();   // também depois de um reset de cena (o OnDestroy da cena anterior a desligou)
            var grid = gb.MpGrid;
            var hud = gb.MpHUD;
            var planner = gb.MpPlanner;
            var round = gb.MpRoundManager;
            var cam = Camera.main;
            var camCtrl = Object.FindObjectOfType<CameraController>();
            var tileFx = Object.FindObjectOfType<TileEffectManager>();
            if (grid == null || hud == null || planner == null || round == null)
            {
                Debug.LogError("[Tutorial] Attach: componentes do bootstrap nulos — abortando.");
                Active = false;
                return;
            }

            var tuning = Tuning.Get();
            UnitRegistry.Clear();

            // Jogador: atributos equilibrados para mostrar bem cada mecânica.
            var player = CreateUnit("Herói do Tutorial", Team.Player, new Vector2Int(16, 15),
                tuning.playerTeamColor, grid,
                new UnitStatBlock { STR = 5, VIT = 5, DEX = 5, AGI = 5, INT = 5, WIS = 5, Footprint = 1, AttackRange = 1 },
                isPlayerChar: true, weaponId: "Hatchet", ownerId: 0);

            // Fera controlada: fraca, para o jogador praticar sem morrer.
            var fera = CreateUnit("Fera de Treino", Team.Enemy, new Vector2Int(20, 15),
                tuning.enemyTeamColor, grid,
                new UnitStatBlock { STR = 3, VIT = 8, DEX = 3, AGI = 3, INT = 1, WIS = 1, Footprint = 1, AttackRange = 1 },
                isPlayerChar: false, weaponId: "", ownerId: BeastAI.OwnerId);
            fera.definitionId = "monster";   // modelo 3D da fera

            UnitRegistry.Register(1, player);
            UnitRegistry.Register(2, fera);

            player.tutorialSafe = true;   // GDD-027 §5: a fera nunca mata o herói (vida mínima 1)

            // Aliado de treino (Cap. 4, Interpor): nasce escondido e FORA da lista de unidades; a lição o coloca em cena.
            var ally = CreateUnit("Aliado de Treino", Team.Player, new Vector2Int(2, 2),
                Color.Lerp(tuning.playerTeamColor, Color.white, 0.35f), grid,
                new UnitStatBlock { STR = 5, VIT = 5, DEX = 5, AGI = 5, INT = 5, WIS = 5, Footprint = 1, AttackRange = 1 },
                isPlayerChar: false, weaponId: "Hatchet", ownerId: 0);
            ally.tutorialSafe = true; ally.tutorialAlly = true;
            ally.gameObject.SetActive(false);

            var units = new List<Unit> { player, fera };
            planner.Setup(grid, cam, units);
            round.Setup(grid, planner, hud, cam, camCtrl, units, player, tileFx);

            var director = gb.gameObject.AddComponent<TutorialDirector>();
            director.SetAlly(ally, units);
            director.Init(player, fera, round, planner, hud, camCtrl, grid);

            round.Begin();
            Debug.Log("[Tutorial] batalha de tutorial iniciada (jogador + fera).");
        }

        private static Unit CreateUnit(string name, Team team, Vector2Int anchor, Color color,
            GridManager grid, UnitStatBlock block, bool isPlayerChar, string weaponId, ulong ownerId)
        {
            var go = new GameObject(name);
            var u = go.AddComponent<Unit>();
            u.unitName = name;
            u.team = team;
            u.ownerId = ownerId;
            u.isPlayerCharacter = isPlayerChar;
            u.weaponId = weaponId;
            u.stats = block.ToAttributeStats();
            u.Init(grid, anchor, color, CharacterSpriteCatalog.Default);
            return u;
        }
    }
}