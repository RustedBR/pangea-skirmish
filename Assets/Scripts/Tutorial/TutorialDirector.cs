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

        // Aliado de treino (Cap. 4, Interpor): fora de cena e da lista de unidades, exceto durante a lição.
        public Unit Ally { get; private set; }
        private List<Unit> _unitList;
        private bool _allyRegistered;

        public void SetAlly(Unit ally, List<Unit> units) { Ally = ally; _unitList = units; }

        /// <summary>Coloca o aliado em cena (e na lista de unidades do round) no tile indicado.</summary>
        public void ActivateAlly(Vector2Int anchor, float hpFraction)
        {
            if (Ally == null || _unitList == null) return;
            if (!Ally.gameObject.activeSelf) Ally.gameObject.SetActive(true);
            Ally.TeleportTo(anchor);
            Ally.currentHP = Mathf.Max(1, Mathf.RoundToInt(Ally.stats.MaxHP * hpFraction));
            Ally.currentMana = Ally.stats.MaxMana; Ally.currentStamina = Ally.stats.MaxStamina;
            if (!_allyRegistered) { UnitRegistry.Register(3, Ally); _allyRegistered = true; }
            if (!_unitList.Contains(Ally)) _unitList.Add(Ally);
            Ally.BeginPlanning();
            Round?.TutorialReplan();   // refaz o planejamento já com o aliado (barra de ticks inclusa)
        }

        /// <summary>Tira o aliado de cena e da lista de unidades.</summary>
        public void DeactivateAlly()
        {
            if (Ally == null || _unitList == null || !_unitList.Contains(Ally)) return;
            _unitList.Remove(Ally);
            if (_allyRegistered) { UnitRegistry.Unregister(Ally); _allyRegistered = false; }
            Ally.queue.Clear();
            Ally.gameObject.SetActive(false);
        }

        private TutorialRunner _runner;
        private TutorialOverlay _overlay;

        /// <summary>Lição atual (debug/testes).</summary>
        public TutorialLesson CurrentLesson => _runner?.Current;
        public string Progress => _runner?.ProgressText() ?? "";
        public TutorialRunner.State RunnerState => _runner?.CurrentState ?? TutorialRunner.State.Idle;
        /// <summary>Motor exposto para debug/testes (dirigir lições via MCP).</summary>
        public TutorialRunner Runner => _runner;

        public void Init(Unit player, Unit fera, RoundManager round, PlanningController planner,
                         BattleHUD hud, CameraController camCtrl, GridManager grid)
        {
            PlayerUnit = player; Fera = fera; Round = round; Planner = planner;
            Hud = hud; CamCtrl = camCtrl; Grid = grid;
            GameLog.SetSuppressed(true);   // histórico de batalha não existe no tutorial

            var overlayGo = new GameObject("TutorialOverlay");
            _overlay = overlayGo.AddComponent<TutorialOverlay>();
            _overlay.OnSkip = () => _runner.Skip();
            // Repetir = reset de cena: recarrega a batalha e volta para esta mesma lição.
            _overlay.OnAction = () => _runner.Current?.action?.Invoke();
            _overlay.OnRepeat = () => { var cur = _runner.Current; if (cur != null) TutorialLauncher.RestartAtLesson(cur.id); else _runner.Repeat(); };
            _overlay.OnOpenMenu = () => OpenLessonMenu(true);
            _overlay.OnCloseMenu = () => { _overlay.HideMenu(true); TutorialHud.BlockPlanInput = false; };
            _overlay.OnPickLesson = i =>
            {
                _overlay.HideMenu(false); TutorialHud.BlockPlanInput = false;
                // Já começou? Escolher outra lição também reinicia a cena, para ela nascer limpa.
                if (_runner.Index >= 0 && i >= 0 && i < _lessonList.Count) TutorialLauncher.RestartAtLesson(_lessonList[i].id);
                else _runner.BeginAt(i);
            };
            _overlay.OnContinue = () => { if (_runner.CurrentState == TutorialRunner.State.WaitingDemo) _runner.RunDemo(); else _runner.Continue(); };
            _overlay.OnNext = () => { if (_runner.IsDone) TutorialLauncher.ReturnToMenu(); else _runner.Advance(); };

            var lessons = TutorialScenario.Build(this);
            _lessonList = lessons;
            _runner = new TutorialRunner(lessons);
            _runner.OnBeforeEnter = () =>
            {
                // Round em andamento (animação/resolução): para tudo e volta ao planejamento limpo.
                // IMPORTANTE: só nesse caso as unidades são zeradas. Em Planejamento o plano da lição anterior
                // tem de sobreviver (Esperar → Desfazer; Andar → Confirmar).
                if (Round != null && Round.Phase != RoundPhase.Planning)
                {
                    foreach (var u in new[] { PlayerUnit, Fera })
                    {
                        if (u == null) continue;
                        // A fila e os ticks "atravessando" o round interrompido não podem vazar para a lição nova.
                        u.queue.Clear(); u.carriedTicks = 0; u.enteredReach.Clear();
                        u.remainingReactions = 1; u.SetAttackMarked(false);
                    }
                    Round.TutorialReplan();   // EnterPlanning já faz BeginPlanning em todos
                }
                if (_runner.Current == null || !TutorialHeroEditor.IsEditorLesson(_runner.Current.id)) TutorialHeroEditor.Close();
                if (_runner.Current == null || _runner.Current.id != "intercept") DeactivateAlly();
                Hud?.HideReactionMenu();            // janela REAGIR/escolha aberta no meio do round
                Hud?.HidePrompt();
                _overlay?.SetHint(null);
                _hintShown = null; _stepsShown = -1;
                TutorialHud.BlockPlanInput = false;
            };
            _runner.OnChapterDone = ch => TutorialProgress.MarkChapterDone(ch);
            int chapters = 0; foreach (var x in lessons) chapters = Mathf.Max(chapters, x.chapter + 1);
            _runner.OnEnterLesson = l =>
            {
                l.hudFocus?.Invoke();
                TutorialHud.BeastIdle = l.id != "react" && l.id != "reaction" && l.id != "layer2" && l.id != "intercept";   // a fera só age nas lições de reação e na camada 2
                Planner?.RebuildActionBar();
                int at = 0, total = 0;
                for (int i = 0; i < lessons.Count; i++)
                    if (lessons[i].chapter == l.chapter) { total++; if (i <= _runner.Index) at++; }
                _overlay.ShowLesson(l, at, total, chapters);
                _stepsShown = -1;
            };
            _runner.OnFeedback = l =>
            {
                bool ok = l.result == TutorialResult.OK;
                string fb = null;
                if (ok && l.feedbackFn != null) { try { fb = l.feedbackFn(); } catch (System.Exception e) { Debug.LogWarning("[Tutorial] feedbackFn: " + e.Message); } }
                string txt = ok
                    ? (!string.IsNullOrEmpty(fb) ? fb : string.IsNullOrEmpty(l.feedback) ? "Mecânica executada corretamente." : l.feedback)
                    : l.detail;
                _overlay.ShowFeedback(l.title, txt, ok);
            };
            _runner.OnCompleted = () =>
            {
                TutorialHud.None();   // só o mapa de capítulos na tela; Clear() ao voltar ao menu
                Hud?.SetTutorialVisibility();
                GameLog.SetSuppressed(false);
                string report = TutorialTestLog.Report();
                Debug.Log("[Tutorial]\n" + report);
                try
                {
                    string path = System.IO.Path.Combine(Application.persistentDataPath, "tutorial-test-report.json");
                    System.IO.File.WriteAllText(path, TutorialTestLog.ToJson());
                    Debug.Log("[Tutorial] relatório JSON: " + path);
                }
                catch (System.Exception e) { Debug.LogWarning("[Tutorial] não gravou JSON: " + e.Message); }
                _overlay.ShowReport(lessons, report);
            };

            // Retomando depois de um reset de cena? Vai direto para a lição; senão abre o menu.
            string resume = TutorialLauncher.ConsumeResume();
            int resumeIdx = resume == null ? -1 : lessons.FindIndex(l => l.id == resume);
            if (resumeIdx >= 0) _runner.BeginAt(resumeIdx, keepLog: true);
            else OpenLessonMenu(false);   // antes de começar: escolher a lição (ou "Começar do início")
        }

        private List<TutorialLesson> _lessonList;

        /// <summary>Abre o menu de lições. Antes de começar só o mapa e o menu ficam na tela.</summary>
        private void OpenLessonMenu(bool midLesson)
        {
            TutorialHud.None();
            TutorialHud.BlockPlanInput = true;
            Hud?.SetTutorialVisibility();
            Planner?.RebuildActionBar();
            _overlay.ShowMenu(_lessonList, TutorialProgress.ChaptersDone, midLesson ? _runner.Index : -1, midLesson);
        }

        private int _stepsShown = -1;
        /// <summary>Rounds que já terminaram de resolver (= número do round − 1). Gates usam a diferença.</summary>
        public int RoundsResolved => Round != null ? Mathf.Max(0, Round.RoundNumber - 1) : 0;
        private string _hintShown;

        private void Update()
        {
            // Aliado de treino: nunca reage (senão o jogo o trata como jogador e abre uma janela REAGIR para ele).
            if (Ally != null && Ally.gameObject.activeSelf) Ally.remainingReactions = 0;
            // Fera passiva: sem Stamina nem reações ela não contra-ataca quando o herói anda por perto.
            if (TutorialHud.Active && TutorialHud.BeastIdle && Fera != null)
            { Fera.currentStamina = 0; Fera.remainingReactions = 0; }
            _runner?.Tick();
            var cur = _runner?.Current;
            if (cur != null && _runner.CurrentState != TutorialRunner.State.Feedback && _overlay != null)
            {
                int done = cur.stepsDone != null ? cur.stepsDone() : 0;
                if (cur.steps != null && done != _stepsShown) { _stepsShown = done; _overlay.SetSteps(cur.steps, done); }
                string hint = cur.onWrong?.Invoke();
                if (hint != _hintShown) { _hintShown = hint; _overlay.SetHint(hint); }
            }
            if (TutorialHud.Active && Hud != null) Hud.SetTutorialVisibility();
        }

        private void OnDestroy()
        {
            TutorialHeroEditor.Close(); GameLog.SetSuppressed(false);
            // Recarregando a cena dentro do tutorial (Repetir/escolher lição): o modo tutorial continua ligado.
            // Qualquer outra saída (voltar ao menu da batalha, fechar a cena...) desliga TUDO, senão a próxima partida
            // normal vira tutorial.
            if (TutorialLauncher.IsRestarting) TutorialHud.Clear();
            else TutorialLauncher.Deactivate();
        }
    }
}