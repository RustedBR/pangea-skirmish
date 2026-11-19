using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Director do tutorial (GDD-027 §3): MonoBehaviour que roda o TutorialRunner numa batalha real,
    /// liga os callbacks ao TutorialOverlay e expõe as refs do jogo para os gates das lições.
    /// Aditivo — não altera a lógica de RoundManager/PlanningController/BattleHUD.
    /// </summary>
    public class TutorialDirector : MonoBehaviour
    {
        // Refs do jogo (setadas pelo TutorialLauncher.Attach) — usadas pelos gates das lições.
        public Unit PlayerUnit;
        public Unit Fera;
        public RoundManager Round;
        public PlanningController Planner;
        public BattleHUD Hud;
        public CameraController CamCtrl;
        public GridManager Grid;

        private TutorialRunner _runner;
        private TutorialOverlay _overlay;

        /// <summary>Lição atual (debug/testes).</summary>
        public TutorialLesson CurrentLesson => _runner?.Current;
        public string Progress => _runner?.ProgressText() ?? "";
        public TutorialRunner.State RunnerState => _runner?.CurrentState ?? TutorialRunner.State.Idle;

        public void Init(Unit player, Unit fera, RoundManager round, PlanningController planner,
                         BattleHUD hud, CameraController camCtrl, GridManager grid)
        {
            PlayerUnit = player; Fera = fera; Round = round; Planner = planner;
            Hud = hud; CamCtrl = camCtrl; Grid = grid;

            var overlayGo = new GameObject("TutorialOverlay");
            _overlay = overlayGo.AddComponent<TutorialOverlay>();
            _overlay.OnSkip = () => _runner.Skip();
            _overlay.OnRepeat = () => _runner.Repeat();
            _overlay.OnNext = () => _runner.Advance();

            var lessons = TutorialScenario.Build(this);
            _runner = new TutorialRunner(lessons);
            _runner.OnEnterLesson = l => _overlay.ShowLesson(_runner.ProgressText(), l.title, l.instruction);
            _runner.OnFeedback = l =>
            {
                bool ok = l.result == TutorialResult.OK;
                string txt = ok
                    ? (string.IsNullOrEmpty(l.feedback) ? "Mecânica executada corretamente." : l.feedback)
                    : ("FALHOU: " + l.detail);
                _overlay.ShowFeedback(l.title, txt, ok);
            };
            _runner.OnCompleted = () =>
            {
                string report = TutorialTestLog.Report();
                Debug.Log("[Tutorial]\n" + report);
                try
                {
                    string path = System.IO.Path.Combine(Application.persistentDataPath, "tutorial-test-report.json");
                    System.IO.File.WriteAllText(path, TutorialTestLog.ToJson());
                    Debug.Log("[Tutorial] relatório JSON: " + path);
                }
                catch (System.Exception e) { Debug.LogWarning("[Tutorial] não gravou JSON: " + e.Message); }
                _overlay.ShowReport(report);
            };

            _runner.Begin();
        }

        private void Update()
        {
            _runner?.Tick();
        }
    }
}