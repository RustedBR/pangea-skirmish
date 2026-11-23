using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PangeaSkirmish
{
    /// <summary>
    /// Lições do tutorial (GDD-027 §4), capítulos 0–9. DUPLO PROPÓSITO: ensinar o jogador E
    /// testar CADA mecânica de verdade. Toda lição tem um gate que lê o estado REAL do jogo e uma
    /// asserção que verifica o resultado — completá-la exercita e valida a mecânica (banco de testes).
    /// Lições de ação do jogador (câmera/mover/atacar/conjurar) ele executa; lições de sistema
    /// (status/zonas/cura/reação/progressão/coop) o tutorial resolve a mecânica real e verifica o
    /// efeito, enquanto explica. Baselines capturados no setup de cada lição.
    /// </summary>
    public static class TutorialScenario
    {
        private static List<Unit> Units(TutorialDirector d) => new List<Unit> { d.PlayerUnit, d.Fera };

        /// <summary>Resolve uma magia de verdade (SpellResolver) para demonstrar + testar a mecânica.</summary>
        private static void Cast(TutorialDirector d, string id, Unit target, Vector2Int tile, int boosts = 0)
        {
            var ps = new PlannedSpell { SpellId = id, TargetUnit = target, TargetTile = tile, Boosts = boosts };
            SpellResolver.Resolve(d.PlayerUnit, ps, Units(d), d.Grid, SpellField.Current);
        }

        private static int Cheb(Vector2Int a, Vector2Int b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

        /// <summary>Esvazia o plano do herói (lições que começam do zero).</summary>
        private static void ClearPlan(TutorialDirector d)
        {
            var u = d.PlayerUnit; if (u == null) return;
            u.ResetPlan();   // fila, destinos, ataques e magias: o plano inteiro (não só a sequência)
            d.Planner?.End(); d.Planner?.Begin(u);   // sai de qualquer modo de mira/andada armado
            d.Planner?.RebuildActionBar();
        }

        private static void KeepFeraAlive(TutorialDirector d)
        {
            if (d.Fera != null && d.Fera.currentHP < d.Fera.stats.MaxHP / 2) d.Fera.currentHP = d.Fera.stats.MaxHP;
        }

        public static List<TutorialLesson> Build(TutorialDirector d)
        {
            var L = new List<TutorialLesson>();

            // ── Capítulo 0: Orientação (anexo §3 Cap. 0) ────────────────────────
            float camT0 = 0f;
            var cam = new TutorialLesson("cam", 0, "Ache a fera", "Câmera (pan)",
                "Arraste o mapa até achar a <b>Fera de Treino</b>. No mouse, segure o <b>botão direito</b> e arraste (ou leve o cursor à borda da tela). No toque, arraste com <b>1 dedo</b>.");
            cam.setup = () => camT0 = Time.unscaledTime;
            cam.gate = () => CameraController.LastManualMoveTime > camT0 + 0.3f;
            cam.feedbackFn = () => d.Fera != null && d.PlayerUnit != null
                ? $"Você achou a fera: ela está a <b>{Cheb(d.PlayerUnit.anchor, d.Fera.anchor)} células</b> de você. Solte a câmera e, depois de um tempo, ela volta ao modo Auto."
                : "Câmera respondendo ao input.";
            L.Add(cam);

            float zoom0 = 0f; bool zoomIn = false, zoomOut = false;
            var zoom = new TutorialLesson("zoom", 0, "Zoom", "Câmera (zoom)",
                "Aproxime e depois afaste a câmera com a <b>roda do mouse</b> (no toque, <b>pinça</b> com 2 dedos). Perto para mirar, longe para ver o campo.");
            zoom.steps = new[] { "Aproximar", "Afastar" };
            zoom.setup = () => { zoom0 = d.CamCtrl != null ? d.CamCtrl.TargetSize : 0f; zoomIn = zoomOut = false; };
            zoom.stepsDone = () =>
            {
                if (d.CamCtrl == null) return 0;
                float z = d.CamCtrl.TargetSize;
                if (!zoomIn && z < zoom0 - 1f) zoomIn = true;
                if (zoomIn && !zoomOut && z > zoom0 - 0.5f) zoomOut = true;
                return (zoomIn ? 1 : 0) + (zoomOut ? 1 : 0);
            };
            zoom.gate = () => zoom.stepsDone() >= 2;
            zoom.feedbackFn = () => "Você aproximou e voltou. Gire a vista com <b>Q</b> e <b>E</b> (ou tocando na bússola) quando quiser — ninguém sai do lugar.";
            L.Add(zoom);

            var inspect = new TutorialLesson("inspect", 0, "Inspecione a fera", "Inspecionar unidade",
                "Abra a ficha da <b>Fera de Treino</b>: <b>clique</b> nela, ou <b>toque duas vezes</b> nela no celular.");
            inspect.setup = () => d.Hud?.HideUnitInfo();
            inspect.gate = () => d.Hud != null && d.Fera != null && d.Hud.InspectedUnit == d.Fera;
            inspect.onWrong = () => d.Hud != null && d.Hud.InspectedUnit != null && d.Hud.InspectedUnit == d.PlayerUnit
                ? "Esse é você. Procure a Fera de Treino." : null;
            inspect.assertion = () => d.Fera != null && d.Fera.currentHP > 0 ? null : "fera sem vida";
            inspect.feedbackFn = () => d.Fera != null ? $"Fera de Treino: <b>{d.Fera.currentHP}/{d.Fera.stats.MaxHP}</b> de vida. A ficha também mostra Mana e Stamina." : null;
            L.Add(inspect);

            var bar = new TutorialLesson("bar", 0, "A barra de ticks", "Barra de ticks (UI)",
                "Esta é a <b>barra de ticks</b>, o relógio da luta, embaixo da tela. Cada coluna é um tick; a linha vertical fecha o <b>round</b> de 4 ticks. Todos decidem juntos e as ações resolvem tick a tick.",
                "", "ticks-bar");
            bar.kind = TutorialLessonKind.Info;
            bar.assertion = () =>
            {
                if (d.Hud == null || d.Hud.Document == null) return "sem HUD";
                var tb = UQueryExtensions.Q<VisualElement>(d.Hud.Document.rootVisualElement, "ticks-bar");
                return tb != null ? null : "barra de ticks (ticks-bar) não existe na HUD";
            };
            bar.feedbackFn = () => $"Certo: o round tem <b>{Tuning.Get().ticksPerRound} ticks</b> e fecha na linha. Você e a fera planejam ao mesmo tempo, sem ver o plano do outro.";
            L.Add(bar);

            // ── Capítulo 1: Planejar ────────────────────────────────────────────
            var tick = new TutorialLesson("tick", 1, "Cada ação ocupa ticks", "Agendamento de ação",
                "Cada ação ocupa ticks na barra. Use <b>Esperar</b> (tecla 7 ou toque) e veja o bloco ocupar 1 tick.", "", "abtn-wait");
            tick.setup = () => ClearPlan(d);
            tick.gate = () => d.PlayerUnit != null && d.PlayerUnit.actionSequence.Exists(a => a.Type == ActionType.Wait);
            tick.feedbackFn = () => d.PlayerUnit != null
                ? $"Esperar ocupou <b>1 tick</b>. Seu plano: <b>{d.PlayerUnit.PlannedTicks()} de {Tuning.Get().maxQueueTicks}</b> ticks, sobram <b>{d.PlayerUnit.remainingAP}</b> neste round."
                : null;
            L.Add(tick);

            int undoCount0 = 0;
            var undo = new TutorialLesson("undo", 1, "Desfaça o último bloco", "Desfazer",
                "Mudou de ideia? Use <b>Desfazer</b> (Backspace ou toque) para tirar o último bloco. Só funciona antes de confirmar.", "", "btn-undo");
            bool undoArmed = false;
            undo.setup = () =>
            {
                undoArmed = false; undoCount0 = 0;
                if (d.PlayerUnit != null && d.PlayerUnit.actionSequence.Count > 0)
                { undoCount0 = d.PlayerUnit.actionSequence.Count; undoArmed = true; }   // o bloco veio da lição anterior
            };
            undo.gate = () =>
            {
                var u = d.PlayerUnit; if (u == null) return false;
                if (!undoArmed)
                {
                    // Entrada pelo menu: o plano está vazio e o planejador pode ainda não estar ativo. Cria o bloco
                    // assim que der (a lição é consultada a cada quadro) e só então passa a valer.
                    if (u.actionSequence.Count == 0) d.Planner?.TutorialAddWait();
                    if (u.actionSequence.Count > 0) { undoCount0 = u.actionSequence.Count; undoArmed = true; }
                    return false;
                }
                return u.actionSequence.Count < undoCount0;
            };
            undo.feedbackFn = () => d.PlayerUnit != null
                ? $"Bloco removido: o plano voltou a <b>{d.PlayerUnit.PlannedTicks()} de {Tuning.Get().maxQueueTicks}</b> ticks. <b>Desfazer</b> tira só o último bloco; <b>Limpar</b> tira o plano inteiro. Depois de confirmar, nenhum dos dois funciona."
                : null;
            L.Add(undo);

            var plan = new TutorialLesson("plan", 1, "Enfileire uma andada", "Planejar movimento",
                "Use <b>Andar</b> (tecla 1 ou toque) e depois clique num tile da área verde. A andada vira 1 bloco na barra.", "", "abtn-move");
            plan.steps = new[] { "Escolher Andar", "Escolher o destino" };
            plan.stepsDone = () => d.PlayerUnit != null && d.PlayerUnit.plannedMoveCount > 0 ? 2 : 0;
            plan.gate = () => d.PlayerUnit != null && d.PlayerUnit.plannedMoveCount > 0;
            plan.feedbackFn = () => d.PlayerUnit != null
                ? $"Andar ocupou <b>1 tick</b> e cobre até <b>{d.PlayerUnit.EffectiveMoveStep} tiles</b>. Plano: <b>{d.PlayerUnit.PlannedTicks()} de {Tuning.Get().maxQueueTicks}</b> ticks. Nada acontece até você confirmar."
                : null;
            L.Add(plan);

            int rounds0 = 0;
            var confirm = new TutorialLesson("confirm", 1, "Confirme o round", "Confirmar round",
                "Use <b>Confirmar</b> (Enter ou toque) para o tempo andar. Depois disso o plano não muda.", "", "btn-confirm");
            bool confirmArmed = false;
            confirm.setup = () =>
            {
                rounds0 = d.RoundsResolved;
                confirmArmed = d.PlayerUnit != null && d.PlayerUnit.plannedMoveCount > 0;   // a andada veio da lição anterior
            };
            confirm.gate = () =>
            {
                var h = d.PlayerUnit;
                if (!confirmArmed && h != null)
                {
                    // Só Confirmar fica liberado: sem andada na fila (entrada pelo menu), cria uma assim que o planejador estiver ativo.
                    if (h.plannedMoveCount == 0) d.Planner?.TutorialAddMove(h.anchor + new Vector2Int(-2, 0));
                    confirmArmed = h.plannedMoveCount > 0;
                }
                return d.RoundsResolved > rounds0;
            };
            confirm.assertion = () => d.PlayerUnit != null && d.PlayerUnit.anchor != d.PlayerUnit.spawnAnchor ? null : "herói não andou no round";
            confirm.feedbackFn = () => $"Round resolvido em <b>{Tuning.Get().ticksPerRound} ticks</b>. Sua andada aconteceu e você está a <b>{(d.PlayerUnit != null && d.Fera != null ? Cheb(d.PlayerUnit.anchor, d.Fera.anchor) : 0)} células</b> da fera.";
            L.Add(confirm);

            // ── Capítulo 2: Movimento ───────────────────────────────────────────
            var moved = new TutorialLesson("moved", 2, "Sua unidade andou", "Movimento",
                "Observe: sua unidade saiu do lugar e andou até a célula que você escolheu. O alcance da andada depende da AGI (MoveStep).",
                "Unidade moveu no grid.");
            moved.gate = () => d.PlayerUnit != null && d.PlayerUnit.anchor != d.PlayerUnit.spawnAnchor;
            moved.kind = TutorialLessonKind.Info;
            moved.feedbackFn = () => d.PlayerUnit != null ? $"Uma andada cobre até <b>{d.PlayerUnit.EffectiveMoveStep} tiles</b>: vem da sua AGI. Mais longe que isso, faça duas andadas no mesmo round." : null;
            L.Add(moved);

            // 2.3: andadas encadeadas — duas andadas no mesmo round cobrem mais que uma.
            int chainRounds0 = 0, chainMoves = 0; Vector2Int chainStart = Vector2Int.zero;
            var chain = new TutorialLesson("chain", 2, "Vá mais longe", "Andadas encadeadas",
                "Uma andada tem alcance limitado. Faça <b>duas andadas seguidas</b> no mesmo round: <b>Andar</b> (tecla 1 ou toque) duas vezes e <b>Confirme</b> (Enter).", "", "abtn-move");
            chain.setup = () =>
            {
                ClearPlan(d); chainMoves = 0; chainRounds0 = d.RoundsResolved;
                if (d.PlayerUnit != null) chainStart = d.PlayerUnit.anchor;
            };
            chain.steps = new[] { "Primeira andada", "Segunda andada", "Confirmar o round" };
            chain.stepsDone = () =>
            {
                if (d.PlayerUnit != null) chainMoves = Mathf.Max(chainMoves, d.PlayerUnit.plannedMoveCount);
                if (d.RoundsResolved > chainRounds0) return 3;
                return Mathf.Min(2, d.PlayerUnit != null ? d.PlayerUnit.plannedMoveCount : 0);
            };
            chain.gate = () => { chain.stepsDone(); return d.RoundsResolved > chainRounds0 && chainMoves >= 2; };
            chain.onWrong = () => d.RoundsResolved > chainRounds0 && chainMoves < 2 ? "Foi só 1 andada. Use <b>Repetir</b> e ponha duas na fila antes de confirmar." : null;
            chain.assertion = () => chainMoves >= 2 && d.PlayerUnit != null && d.PlayerUnit.anchor != chainStart ? null : "as duas andadas não foram executadas";
            chain.feedbackFn = () => d.PlayerUnit != null
                ? $"Você andou <b>{Cheb(chainStart, d.PlayerUnit.anchor)} tiles</b> com <b>{chainMoves} andadas</b>, gastando <b>{chainMoves} de {Tuning.Get().ticksPerRound}</b> ticks do round."
                : null;
            L.Add(chain);

            // 2.4/2.5: terreno real — cada passo sobe no máximo 1 de altura (maxStepClimb). O alcance (BFS) só
            // inclui casas com um caminho de passos legais. As células voltam ao normal ao sair de cada lição.
            Vector2Int climbStart = Vector2Int.zero, climbStep = Vector2Int.zero, climbTop = Vector2Int.zero;
            (string terrain, int h, string obj) climbStepOrig = ("", 0, ""), climbTopOrig = ("", 0, "");
            bool climbBuilt = false;
            System.Action climbRestore = () =>
            {
                if (!climbBuilt || d.Grid == null) return;
                d.Grid.SetCell(climbStep.x, climbStep.y, climbStepOrig.terrain, climbStepOrig.h, climbStepOrig.obj);
                d.Grid.SetCell(climbTop.x, climbTop.y, climbTopOrig.terrain, climbTopOrig.h, climbTopOrig.obj);
                climbBuilt = false;
            };
            // Monta o mirante (altura 2) e, se withStep, o degrau (altura 1) colado no herói.
            System.Action<bool> climbBuild = withStep =>
            {
                climbRestore();
                if (d.PlayerUnit == null || d.Grid == null) return;
                climbStart = d.PlayerUnit.anchor;
                climbStep = climbStart + new Vector2Int(0, 1);
                climbTop = climbStart + new Vector2Int(0, 2);
                climbStepOrig = (d.Grid.GetTerrainName(climbStep.x, climbStep.y), d.Grid.GetHeight(climbStep.x, climbStep.y), d.Grid.GetObjectName(climbStep.x, climbStep.y));
                climbTopOrig = (d.Grid.GetTerrainName(climbTop.x, climbTop.y), d.Grid.GetHeight(climbTop.x, climbTop.y), d.Grid.GetObjectName(climbTop.x, climbTop.y));
                if (withStep) d.Grid.SetCell(climbStep.x, climbStep.y, climbStepOrig.terrain, 1, climbStepOrig.obj);
                d.Grid.SetCell(climbTop.x, climbTop.y, climbTopOrig.terrain, 2, climbTopOrig.obj);
                climbBuilt = true;
            };
            System.Func<bool> topInReach = () =>
            {
                if (d.PlayerUnit == null || d.Grid == null) return false;
                var reach = d.Grid.GetReachableAnchors(climbStart, d.PlayerUnit.EffectiveMoveStep, d.PlayerUnit.stats.Footprint, new List<Unit>());
                return reach.Contains(climbTop);
            };

            // 2.4: penhasco — um mirante de altura 2 sem degrau fica FORA da área verde.
            var cliff = new TutorialLesson("cliff", 2, "Altura do terreno", "Terreno: altura máxima por passo",
                "Aperte <b>Andar</b> (tecla 1 ou toque) e olhe a área verde. O bloco alto ao lado tem <b>2</b> de altura, e cada passo sobe no máximo <b>1</b>.", "", "abtn-move");
            cliff.setup = () => { ClearPlan(d); climbBuild(false); };
            cliff.onExit = () => climbRestore();
            cliff.gate = () => d.Planner != null && d.Planner.Mode == PlanMode.MovePicking;
            cliff.assertion = () => topInReach() ? "o bloco de altura 2 apareceu no alcance sem degrau (GetReachableAnchors não respeitou maxStepClimb)" : null;
            cliff.feedbackFn = () => d.PlayerUnit != null
                ? $"O bloco de altura <b>2</b> ficou <b>fora</b> da área verde: do chão (altura 0) o passo máximo é 1. Sua andada alcança até <b>{d.PlayerUnit.EffectiveMoveStep}</b> passos, mas só por caminhos que sobem de 1 em 1."
                : null;
            L.Add(cliff);

            // 2.5: degrau — agora existe um caminho de passos legais, e o mirante entra no alcance.
            int climbRounds0 = 0;
            var climb = new TutorialLesson("climb", 2, "Suba pelo degrau", "Terreno: degrau (maxStepClimb)",
                "Agora há um <b>degrau</b> (altura 1) antes do mirante. Use <b>Andar</b> (tecla 1 ou toque), clique no mirante e <b>Confirme</b> (Enter): você sobe pelo degrau.", "", "abtn-move");
            climb.setup = () => { ClearPlan(d); climbBuild(true); climbRounds0 = d.RoundsResolved; };
            climb.onExit = () => climbRestore();
            climb.steps = new[] { "Andar até o mirante", "Confirmar o round" };
            climb.stepsDone = () => d.RoundsResolved > climbRounds0 ? 2 : (d.PlayerUnit != null && d.PlayerUnit.plannedMoveCount > 0 ? 1 : 0);
            climb.gate = () => d.RoundsResolved > climbRounds0 && d.PlayerUnit != null && d.PlayerUnit.anchor == climbTop;
            climb.onWrong = () => d.PlayerUnit != null && d.RoundsResolved > climbRounds0 && d.PlayerUnit.anchor != climbTop
                ? "Você não chegou ao mirante. Use <b>Repetir</b> e clique no bloco alto." : null;
            climb.assertion = () =>
            {
                if (d.PlayerUnit == null || d.Grid == null) return "sem grade";
                if (d.PlayerUnit.anchor != climbTop) return "o herói não chegou ao mirante";
                if (d.Grid.CanStepBetween(climbStart, climbTop)) return "o passo direto do chão ao mirante não foi bloqueado";
                return null;
            };
            climb.feedbackFn = () => "Do chão ao mirante a diferença é <b>2</b>, mas pelo degrau cada passo sobe <b>1</b>: o caminho existe e o mirante entrou na área verde. Descer segue a mesma regra.";
            L.Add(climb);

            // ── Capítulo 3: Ataque ──────────────────────────────────────────────
            int feraHp0 = 0;
            var attack = new TutorialLesson("attack", 3, "Ataque a fera", "Ataque corpo a corpo",
                "Mova até a Fera de Treino, use Atacar (tecla 2) clicando nela e confirme o round (Enter). O dano corpo a corpo escala com STR.",
                "Ataque causou dano na fera.");
            int attackRounds0 = 0;
            attack.setup = () =>
            {
                ClearPlan(d);
                attackRounds0 = d.RoundsResolved;
                if (d.Fera != null) { d.Fera.currentHP = d.Fera.stats.MaxHP; feraHp0 = d.Fera.currentHP; }
                if (d.PlayerUnit != null) { d.PlayerUnit.currentHP = d.PlayerUnit.stats.MaxHP; d.PlayerUnit.GainStamina(10); }
            };
            attack.stepsDone = () =>
            {
                var u = d.PlayerUnit; if (u == null) return 0;
                if (d.RoundsResolved > attackRounds0) return 3;
                if (u.plannedAttacks.Count > 0) return 2;
                return u.plannedMoveCount > 0 ? 1 : 0;
            };
            attack.gate = () => d.Fera != null && d.Fera.currentHP < feraHp0;
            attack.highlightTarget = "abtn-attack";
            attack.steps = new[] { "Andar até a fera", "Atacar a fera", "Confirmar o round" };
            attack.feedbackFn = () => d.Fera != null ? $"Seu golpe tirou <b>{feraHp0 - d.Fera.currentHP} de vida</b> da fera ({d.Fera.currentHP}/{d.Fera.stats.MaxHP}). O dano corpo a corpo cresce com a STR." : null;
            attack.assertion = () => d.Fera != null && d.Fera.currentHP < feraHp0 ? null : "fera não sofreu dano";
            L.Add(attack);

            int stam0 = 0;
            var breathe = new TutorialLesson("breathe", 3, "Stamina: o fôlego do corpo", "Respirar (Stamina)",
                "A <b>Stamina</b> paga golpes fortes e reações. Ela volta sozinha com o tempo, ou mais rápido com <b>Respirar</b> (tecla 6 ou toque). Ponha um Respirar na fila e <b>Confirme</b> (Enter).", "", "abtn-breathe");
            int breatheRounds0 = 0;
            breathe.setup = () => { ClearPlan(d); if (d.PlayerUnit != null) { d.PlayerUnit.currentStamina = 1; stam0 = 1; } breatheRounds0 = d.RoundsResolved; };
            breathe.steps = new[] { "Respirar na fila", "Confirmar o round" };
            breathe.stepsDone = () => d.RoundsResolved > breatheRounds0 ? 2 : d.PlayerUnit != null && d.PlayerUnit.actionSequence.Exists(a => a.Type == ActionType.Breathe) ? 1 : 0;
            breathe.gate = () => d.RoundsResolved > breatheRounds0 && d.PlayerUnit != null && d.PlayerUnit.currentStamina > stam0;
            breathe.assertion = () => d.PlayerUnit != null && d.PlayerUnit.currentStamina > stam0 ? null : "Stamina não subiu com Respirar";
            breathe.feedbackFn = () => d.PlayerUnit != null ? $"Sua Stamina foi de <b>{stam0}</b> para <b>{d.PlayerUnit.currentStamina}</b>. Respirar troca 1 tick parado por fôlego." : null;
            L.Add(breathe);

            int aimRounds0 = 0;
            var aim = new TutorialLesson("aim", 3, "Mirar livre", "Mirar livre",
                "<b>Mirar livre</b> (tecla 3 ou toque) deixa você escolher os tiles do golpe. Escolha o tile da fera e <b>Confirme</b> (Enter). Dá mais dano e mais acerto, mas só pega inimigos.", "", "abtn-aim");
            aim.steps = new[] { "Escolher Mirar livre e o tile da fera", "Confirmar o round" };
            aim.stepsDone = () => d.RoundsResolved > aimRounds0 ? 2 : d.PlayerUnit != null && d.PlayerUnit.plannedAttacks.Exists(a => a.Mode == AttackMode.Tile) ? 1 : 0;
            bool aimedTile = false;
            aim.setup = () =>
            {
                ClearPlan(d); KeepFeraAlive(d); aimRounds0 = d.RoundsResolved; aimedTile = false;
                // Fera colada no herói: a forma da arma só alcança quem está perto.
                if (d.PlayerUnit != null && d.Fera != null) d.Fera.TeleportTo(d.PlayerUnit.anchor + new Vector2Int(2, 0));
            };
            aim.gate = () =>
            {
                if (d.PlayerUnit != null && d.PlayerUnit.plannedAttacks.Exists(a => a.Mode == AttackMode.Tile)) aimedTile = true;
                return aimedTile && d.RoundsResolved > aimRounds0;
            };
            aim.onWrong = () => d.PlayerUnit != null && d.PlayerUnit.plannedAttacks.Exists(a => a.Mode == AttackMode.Unit)
                ? "Isso foi um Atacar comum. Use <b>Mirar livre</b> (tecla 3) e escolha o tile." : null;
            aim.assertion = () => aimedTile ? null : "nenhum ataque de Mirar livre foi planejado";
            aim.feedbackFn = () => $"Mirar livre custa o mesmo tick de um ataque comum. Quando a fera estiver fora do tile escolhido, o golpe erra: por isso o jogador posiciona a forma.";
            L.Add(aim);

            // ── Capítulo 4: Reações ─────────────────────────────────────────────
            // A fera VOLTA a atacar (BeastIdle só vale até o Cap. 3): a janela REAGIR abre de verdade.
            int react0 = 0, reactRound0 = 0, reactHp0 = 0, reactStam0 = 0; bool reactAsked = false;
            // 4.1: Contra-atacar — a fera ANDA até o seu alcance; a janela REAGIR oferece o golpe de oportunidade.
            // A lição só fecha se você usar Contra-atacar (a única defesa ativa dessa situação).
            int reactFeraHp0 = 0;
            var react = new TutorialLesson("react", 4, "Contra-ataque", "Reação Contra-atacar (golpe de oportunidade)",
                "A fera vai <b>se aproximar</b> de você. Quando abrir a janela <b>REAGIR</b>, escolha <b>Contra-atacar</b> (clique, toque ou tecla 1): você bate primeiro e, se acertar, interrompe a ação dela. Custa Stamina e só vale <b>1 por round</b>. Depois <b>Confirme</b> (Enter) para o round andar.", "", "reaction-menu");
            react.setup = () =>
            {
                ClearPlan(d); KeepFeraAlive(d);
                reactAsked = false;
                react0 = d.Round != null ? d.Round.ReactionsApplied : 0;
                reactRound0 = d.RoundsResolved;
                if (d.PlayerUnit != null)
                {
                    d.PlayerUnit.GainStamina(10); d.PlayerUnit.currentHP = d.PlayerUnit.stats.MaxHP;
                    d.PlayerUnit.remainingReactions = 1;
                    if (d.Fera != null) { d.Fera.currentStamina = 10; d.Fera.remainingReactions = 1; reactFeraHp0 = d.Fera.currentHP; }
                    reactHp0 = d.PlayerUnit.currentHP; reactStam0 = d.PlayerUnit.currentStamina;
                    if (d.Fera != null) d.Fera.TeleportTo(d.PlayerUnit.anchor + new Vector2Int(2, 0));
                }
            };
            System.Func<bool> countered = () => d.Round != null && d.Round.ReactionsApplied > react0 && d.Round.LastReaction == ReactionKind.CounterAttack;
            react.steps = new[] { "Confirmar o round", "Contra-atacar na janela REAGIR" };
            react.stepsDone = () => countered() ? 2 : (d.RoundsResolved > reactRound0 || (d.Round != null && d.Round.Phase != RoundPhase.Planning) ? 1 : 0);
            react.gate = () => countered() && d.RoundsResolved > reactRound0;
            react.onWrong = () =>
            {
                if (d.Round == null) return null;
                if (d.Round.ReactionsApplied > react0 && !countered())
                    return $"Você usou {PlanningController.ReactionName(d.Round.LastReaction)}. Aqui o objetivo é <b>Contra-atacar</b>: use <b>Repetir</b> e escolha essa opção.";
                return d.RoundsResolved > reactRound0 && d.Round.ReactionsApplied == react0
                    ? "O round passou sem você reagir. Use <b>Repetir</b> e escolha <b>Contra-atacar</b> quando a janela abrir." : null;
            };
            react.assertion = () =>
            {
                if (d.Round == null || d.PlayerUnit == null) return "sem round";
                if (!countered()) return "o Contra-atacar não foi aplicado";
                if (d.PlayerUnit.currentStamina >= reactStam0 + 1) return "Stamina não foi gasta pelo Contra-atacar";
                return null;
            };
            react.feedbackFn = () =>
            {
                if (d.Round == null || d.Fera == null) return null;
                int dealt = reactFeraHp0 - d.Fera.currentHP;
                string result = dealt > 0
                    ? $"O golpe acertou e tirou <b>{dealt} de vida</b> da fera, interrompendo a ação dela."
                    : "O golpe errou: Contra-atacar não garante o acerto.";
                return $"Você usou <b>Contra-atacar</b> por <b>{Unit.ReactionCost(ReactionKind.CounterAttack)} de Stamina</b>. {result} Só 1 reação por round: ela já foi usada.";
            };
            L.Add(react);

            // 4.2: defesa de verdade — a fera colada ataca, a janela REAGIR abre e você Bloqueia ou Esquiva.
            int defReact0 = 0, defRound0 = 0, defHp0 = 0, defStam0 = 0;
            var reaction = new TutorialLesson("reaction", 4, "Reação de defesa", "Reação defensiva (Bloquear/Esquivar)",
                "A fera está colada em você e vai <b>atacar</b>. Confirme o round (Enter); quando abrir a janela <b>REAGIR</b>, escolha <b>Bloquear</b> ou <b>Esquivar</b>. Reagir custa Stamina e só vale <b>1 por round</b>.", "", "reaction-menu");
            reaction.setup = () =>
            {
                ClearPlan(d); KeepFeraAlive(d);
                defReact0 = d.Round != null ? d.Round.ReactionsApplied : 0;
                defRound0 = d.RoundsResolved;
                if (d.PlayerUnit != null)
                {
                    d.PlayerUnit.GainStamina(10); d.PlayerUnit.currentHP = d.PlayerUnit.stats.MaxHP;
                    d.PlayerUnit.remainingReactions = 1;
                    defHp0 = d.PlayerUnit.currentHP; defStam0 = d.PlayerUnit.currentStamina;
                    // Colada: ela só ataca (andar para dentro do alcance abriria uma janela de Contra-atacar).
                    if (d.Fera != null) { d.Fera.TeleportTo(d.PlayerUnit.anchor + new Vector2Int(1, 0)); d.Fera.currentStamina = 10; d.Fera.remainingReactions = 1; }
                }
            };
            reaction.steps = new[] { "Confirmar o round", "Bloquear ou Esquivar na janela REAGIR" };
            System.Func<bool> defended = () => d.Round != null && d.Round.ReactionsApplied > defReact0
                && (d.Round.LastReaction == ReactionKind.Block || d.Round.LastReaction == ReactionKind.Dodge);
            reaction.stepsDone = () => defended() ? 2 : d.RoundsResolved > defRound0 || (d.Round != null && d.Round.Phase != RoundPhase.Planning) ? 1 : 0;
            reaction.gate = () => defended() && d.RoundsResolved > defRound0;
            reaction.onWrong = () =>
            {
                if (d.Round == null) return null;
                if (d.Round.ReactionsApplied > defReact0 && !defended())
                    return $"Você usou {PlanningController.ReactionName(d.Round.LastReaction)}. Aqui o objetivo é defender: use <b>Repetir</b> e escolha <b>Bloquear</b> ou <b>Esquivar</b>.";
                return d.RoundsResolved > defRound0 && d.Round.ReactionsApplied == defReact0
                    ? "O round passou sem você reagir. Use <b>Repetir</b> e escolha uma defesa quando a janela abrir." : null;
            };
            reaction.assertion = () =>
            {
                if (d.Round == null || d.PlayerUnit == null) return "sem round";
                if (!defended()) return "nenhuma defesa (Bloquear/Esquivar) foi aplicada";
                return null;
            };
            reaction.feedbackFn = () => d.Round != null && d.PlayerUnit != null
                ? $"Você usou <b>{PlanningController.ReactionName(d.Round.LastReaction)}</b> por <b>{Unit.ReactionCost(d.Round.LastReaction)} de Stamina</b> e ficou com <b>{d.PlayerUnit.currentHP}/{d.PlayerUnit.stats.MaxHP}</b> de vida. Só 1 reação por round: ela já foi usada."
                : null;
            L.Add(reaction);

            var posture = new TutorialLesson("posture", 4, "Postura", "Postura",
                "A <b>Postura</b> é a sua defesa planejada para o round inteiro. Troque a postura (tecla 8 ou toque; Shift volta) e veja o efeito na dica do botão.", "", "abtn-posture");
            posture.setup = () => { if (d.PlayerUnit != null) d.PlayerUnit.plannedPosture = Posture.Neutral; d.Planner?.RebuildActionBar(); };
            posture.gate = () => d.PlayerUnit != null && d.PlayerUnit.plannedPosture != Posture.Neutral;
            posture.feedbackFn = () => d.PlayerUnit != null ? $"Postura <b>{PostureRules.Name(d.PlayerUnit.plannedPosture)}</b> escolhida. Ela vale a partir do próximo round que você confirmar." : null;
            L.Add(posture);

            var defReact = new TutorialLesson("defreaction", 4, "Reação padrão", "Reação padrão",
                "Quando alguém age contra você, abre a janela de reação. Se o tempo acabar, vale a <b>Reação padrão</b>. Escolha uma (tecla 9 ou toque).", "", "abtn-reaction");
            defReact.setup = () => { if (d.PlayerUnit != null) d.PlayerUnit.defaultReaction = ReactionKind.None; d.Planner?.RebuildActionBar(); };
            defReact.gate = () => d.PlayerUnit != null && d.PlayerUnit.defaultReaction != ReactionKind.None;
            defReact.feedbackFn = () => d.PlayerUnit != null ? $"Reação padrão: <b>{PlanningController.ReactionName(d.PlayerUnit.defaultReaction)}</b>, custa <b>{Unit.ReactionCost(d.PlayerUnit.defaultReaction)} de Stamina</b>. Só 1 reação por round." : null;
            L.Add(defReact);

            // 4.3: Interpor — a fera ataca o ALIADO ferido; você chega colado e recebe o golpe no lugar dele.
            // Regra do jogo (RoundManager.FindInterceptors): qualquer aliado que alcance, com uma andada, um tile colado ao
            // atingido pode interpor-se. O aliado de treino entra em cena só nesta lição (TutorialDirector.ActivateAlly).
            int icReact0 = 0, icRound0 = 0, icHeroHp0 = 0, icAllyHp0 = 0, icCount0 = 0;
            var intercept = new TutorialLesson("intercept", 4, "Interpor", "Reação Interpor",
                "A fera vai atacar o seu <b>aliado ferido</b>. Ponha <b>Esperar</b> (tecla 7 ou toque) na fila e <b>Confirme</b> (Enter). Quando a janela <b>REAGIR</b> abrir, escolha <b>Interpor</b>: você recebe o golpe no lugar dele, com as <b>suas</b> defesas.", "", "reaction-menu");
            intercept.setup = () =>
            {
                var h = d.PlayerUnit; var f = d.Fera; if (h == null || f == null) return;
                // Aliado colado no herói; a fera colada no aliado (mais perto dela que o herói, então é o alvo).
                d.ActivateAlly(h.anchor + new Vector2Int(1, 0), 0.35f);
                f.TeleportTo(h.anchor + new Vector2Int(2, 0));
                ClearPlan(d); KeepFeraAlive(d);
                icReact0 = d.Round != null ? d.Round.ReactionsApplied : 0;
                icCount0 = d.Round != null ? d.Round.InterceptsApplied : 0;
                icRound0 = d.RoundsResolved;
                if (d.Ally != null) d.Ally.remainingReactions = 0;
                h.GainStamina(10); h.currentHP = h.stats.MaxHP; h.remainingReactions = 1;
                f.currentStamina = 10; f.remainingReactions = 1;
                icHeroHp0 = h.currentHP; icAllyHp0 = d.Ally != null ? d.Ally.currentHP : 0;
            };
            intercept.onExit = () => d.DeactivateAlly();
            System.Func<bool> interposed = () => d.Round != null && d.Round.InterceptsApplied > icCount0;
            intercept.steps = new[] { "Esperar e Confirmar o round", "Interpor na janela REAGIR" };
            intercept.stepsDone = () => interposed() ? 2 : (d.PlayerUnit != null && d.PlayerUnit.actionSequence.Count > 0 ? 1 : 0);
            intercept.gate = () => interposed() && d.RoundsResolved > icRound0;
            intercept.onWrong = () =>
            {
                if (d.Round == null) return null;
                if (d.Round.ReactionsApplied > icReact0 && !interposed())
                    return "Você usou outra reação. Aqui o objetivo é <b>Interpor</b>: use <b>Repetir</b> e escolha essa opção.";
                return d.RoundsResolved > icRound0 && d.Round.ReactionsApplied == icReact0
                    ? "O round passou sem você interpor. Use <b>Repetir</b> e escolha <b>Interpor</b> quando a janela abrir." : null;
            };
            intercept.assertion = () =>
            {
                if (d.Round == null || d.PlayerUnit == null || d.Ally == null) return "sem round, herói ou aliado";
                if (!interposed()) return "o Interpor não foi aplicado";
                // Quem interpôs recebe o golpe; o aliado protegido não pode ter perdido vida.
                if (d.Ally.currentHP < icAllyHp0) return "o aliado protegido tomou o golpe, mesmo com o Interpor";
                return null;
            };
            intercept.feedbackFn = () =>
            {
                if (d.PlayerUnit == null || d.Ally == null) return null;
                int heroLost = icHeroHp0 - d.PlayerUnit.currentHP;
                string hit = heroLost > 0
                    ? $"O golpe atingiu <b>você</b> (−{heroLost} de vida) e o aliado ficou com <b>{d.Ally.currentHP}</b>, intacto."
                    : $"O golpe errou, e o aliado continua com <b>{d.Ally.currentHP}</b> de vida.";
                return $"Você usou <b>Interpor</b> por <b>{Unit.ReactionCost(ReactionKind.Intercept)} de Stamina</b> e se pôs na frente do aliado. {hit} Interpor vale para qualquer aliado que você alcance com uma andada.";
            };
            L.Add(intercept);

            // ── Capítulo 5: Magia — básico ──────────────────────────────────────
            int mana0 = 0;
            var cast = new TutorialLesson("cast", 5, "Conjure Brasa (Fogo)", "Conjurar magia elemental",
                "Abra a aba Magia, escolha Brasa (Fogo), mire na fera e confirme o round. Magia Elemental gasta Mana. Veja a Mana cair.",
                "Magia conjurada — Mana gasta.");
            cast.setup = () =>
            {
                if (d.PlayerUnit != null) { d.PlayerUnit.currentMana = 20; mana0 = d.PlayerUnit.currentMana; }
                KeepFeraAlive(d);
            };
            cast.gate = () => d.PlayerUnit != null && d.PlayerUnit.currentMana < mana0;
            cast.highlightTarget = "abtn-spell";
            cast.steps = new[] { "Abrir Magia", "Escolher Brasa e a fera", "Confirmar o round" };
            cast.feedbackFn = () => d.PlayerUnit != null ? $"Brasa gastou <b>{mana0 - d.PlayerUnit.currentMana} de Mana</b> (sobra {d.PlayerUnit.currentMana}). Magia Elemental só gasta Mana; sua Stamina fica intacta." : null;
            L.Add(cast);

            int conc0 = 0, concRounds0 = 0;
            var concentrate = new TutorialLesson("concentrate", 5, "Concentrar", "Concentrar (Mana)",
                "Sem Mana não há magia. <b>Concentrar</b> (tecla 5 ou toque) recupera Mana parado. Ponha um Concentrar na fila e <b>Confirme</b> (Enter).", "", "abtn-concentrate");
            concentrate.setup = () => { ClearPlan(d); if (d.PlayerUnit != null) { d.PlayerUnit.currentMana = 0; conc0 = 0; } concRounds0 = d.RoundsResolved; };
            concentrate.steps = new[] { "Concentrar na fila", "Confirmar o round" };
            concentrate.stepsDone = () => d.RoundsResolved > concRounds0 ? 2 : d.PlayerUnit != null && d.PlayerUnit.actionSequence.Exists(a => a.Type == ActionType.Concentrate) ? 1 : 0;
            concentrate.gate = () => d.RoundsResolved > concRounds0 && d.PlayerUnit != null && d.PlayerUnit.currentMana > conc0;
            concentrate.assertion = () => d.PlayerUnit != null && d.PlayerUnit.currentMana > conc0 ? null : "Mana não subiu com Concentrar";
            concentrate.feedbackFn = () => d.PlayerUnit != null ? $"Mana de <b>0</b> para <b>{d.PlayerUnit.currentMana}</b>. Levar dano enquanto conjura pode interromper a magia." : null;
            L.Add(concentrate);

            // ── Capítulo 6: Magia — avançado (cada efeito resolvido + verificado) ──
            var status = new TutorialLesson("status", 6, "Status: Queimando", "Status Queimando",
                "Brasa aplica Queimando: dano por tick na fera. Veja a magia resolver e a fera queimar.",
                "Queimando aplicado (dano por tick).");
            status.kind = TutorialLessonKind.Demo;
            status.demo = () => { KeepFeraAlive(d); if (d.Fera != null) { d.Fera.burningTicks = 0; Cast(d, "Ember", d.Fera, d.Fera.anchor); } };
            status.gate = () => d.Fera != null && d.Fera.burningTicks > 0;
            status.assertion = () => d.Fera != null && d.Fera.burningTicks > 0 ? null : "Queimando não aplicado";
            L.Add(status);

            var soaked = new TutorialLesson("soaked", 6, "Status: Encharcado", "Status Encharcado",
                "Jato d'água aplica Encharcado: −1 tile de andada, e apaga Queimando. Veja a fera ficar encharcada.",
                "Encharcado aplicado (−1 andada).");
            soaked.kind = TutorialLessonKind.Demo;
            soaked.demo = () => { KeepFeraAlive(d); if (d.Fera != null) { d.Fera.soakedTicks = 0; Cast(d, "WaterJet", d.Fera, d.Fera.anchor); } };
            soaked.gate = () => d.Fera != null && d.Fera.soakedTicks > 0;
            soaked.assertion = () => d.Fera != null && d.Fera.soakedTicks > 0 ? null : "Encharcado não aplicado";
            L.Add(soaked);

            var zone = new TutorialLesson("zone", 6, "Zona: Magma (lava)", "Zona de lava",
                "Magma cria uma zona de lava: dano por tick em quem pisar. Veja a zona aparecer no grid.",
                "Zona de lava criada no SpellField.");
            zone.kind = TutorialLessonKind.Demo;
            zone.demo = () => { KeepFeraAlive(d); if (d.Fera != null) Cast(d, "Magma", d.Fera, d.Fera.anchor); };
            zone.gate = () => SpellField.Current != null && SpellField.Current.Zones.Exists(z => z.Kind == SpellEffect.ZoneLava);
            zone.assertion = () => zone.gate() ? null : "zona de lava não criada";
            L.Add(zone);

            var barrier = new TutorialLesson("barrier", 6, "Barreira: Muralha", "Barreira",
                "Muralha cria uma barreira que bloqueia movimento e projéteis, com HP. Veja a muralha aparecer.",
                "Barreira criada no SpellField.");
            barrier.kind = TutorialLessonKind.Demo;
            barrier.demo = () => { if (d.PlayerUnit != null) Cast(d, "Rampart", null, d.PlayerUnit.anchor + new Vector2Int(2, 0)); };
            barrier.gate = () => SpellField.Current != null && SpellField.Current.Zones.Exists(z => z.Kind == SpellEffect.Barrier);
            barrier.assertion = () => barrier.gate() ? null : "barreira não criada";
            L.Add(barrier);

            int areaHp0 = 0;
            var area = new TutorialLesson("area", 6, "Área: Explosão", "Dano em área",
                "Explosão causa dano em área (raio 1) e empurra. Veja o dano atingir a fera na área.",
                "Dano em área aplicado.");
            area.kind = TutorialLessonKind.Demo;
            area.demo = () => { KeepFeraAlive(d); if (d.Fera != null) { areaHp0 = d.Fera.currentHP; Cast(d, "Explosion", d.Fera, d.Fera.anchor); } };
            area.gate = () => d.Fera != null && d.Fera.currentHP < areaHp0;
            area.assertion = () => d.Fera != null && d.Fera.currentHP < areaHp0 ? null : "dano em área não aplicado";
            L.Add(area);

            int healHp0 = 0;
            var heal = new TutorialLesson("heal", 6, "Cura: Bálsamo", "Cura em área",
                "Bálsamo cura aliados na área (único caso em que área toca aliado) e remove Queimando/Encharcado. Veja sua vida subir.",
                "Cura aplicada ao aliado.");
            heal.kind = TutorialLessonKind.Demo;
            heal.demo = () => { if (d.PlayerUnit != null) { d.PlayerUnit.currentHP = 20; healHp0 = 20; Cast(d, "Balm", d.PlayerUnit, d.PlayerUnit.anchor); } };
            heal.gate = () => d.PlayerUnit != null && d.PlayerUnit.currentHP > healHp0;
            heal.assertion = () => d.PlayerUnit != null && d.PlayerUnit.currentHP > healHp0 ? null : "cura não aplicada";
            L.Add(heal);

            float incBase = 0, incBoosted = 0;
            var increment = new TutorialLesson("increment", 6, "Incremento: Potência", "Incremento de magia",
                "Incrementos melhoram a magia por mais Mana. Potência aumenta o dano ×1.5 (e +1 tick). Veja o valor subir.",
                "Incremento Potência aumenta o valor.");
            increment.kind = TutorialLessonKind.Info;
            increment.setup = () =>
            {
                var b = SpellCatalog.Get("Ember");
                incBase = b != null ? b.baseAmount : 0;
                var boosted = SpellBoosts.Apply(b, (int)SpellBoost.Power);
                incBoosted = boosted != null ? boosted.baseAmount : 0;
            };
            increment.gate = () => incBoosted > incBase;
            increment.assertion = () => incBoosted > incBase ? null : "incremento não aumentou o valor";
            L.Add(increment);

            var mud = new TutorialLesson("mud", 6, "Zona: Lama", "Zona de lama",
                "Lama cria uma zona que prende quem entra: a andada cai para <b>1 tile</b>. Veja a zona aparecer no grid.", "Zona de lama criada.");
            mud.kind = TutorialLessonKind.Demo;
            mud.demo = () => { KeepFeraAlive(d); if (d.Fera != null) Cast(d, "Mud", d.Fera, d.Fera.anchor); };
            mud.gate = () => SpellField.Current != null && SpellField.Current.Zones.Exists(z => z.Kind == SpellEffect.ZoneMud);
            mud.assertion = () => mud.gate() ? null : "zona de lama não criada";
            mud.feedbackFn = () => d.Fera != null ? $"Dentro da lama a andada cai de <b>{d.Fera.stats.MoveStep}</b> para <b>1 tile</b>. É controle: segura quem você quer perto." : null;
            L.Add(mud);

            Vector2Int windStart = Vector2Int.zero;
            var wind = new TutorialLesson("windstep", 6, "Teleporte: Passo do vento", "Teleporte",
                "Passo do vento <b>teleporta quem conjura</b> para um tile livre até 5 células, sem provocar Contra-atacar. Veja você aparecer em outro lugar.", "Teleporte moveu a unidade.");
            wind.kind = TutorialLessonKind.Demo;
            wind.demo = () => { if (d.PlayerUnit != null) { windStart = d.PlayerUnit.anchor; Cast(d, "WindStep", null, d.PlayerUnit.anchor + new Vector2Int(0, -4)); } };
            wind.gate = () => d.PlayerUnit != null && d.PlayerUnit.anchor != windStart;
            wind.assertion = () => wind.gate() ? null : "teleporte não moveu";
            wind.feedbackFn = () => d.PlayerUnit != null ? $"Você foi de <b>{windStart.x},{windStart.y}</b> para <b>{d.PlayerUnit.anchor.x},{d.PlayerUnit.anchor.y}</b> em 1 tick, ignorando o caminho entre os dois pontos." : null;
            L.Add(wind);

            // ── Capítulo 7: Magia Física (preparos resolvidos + verificados) ─────
            var fury = new TutorialLesson("fury", 7, "Física: Fúria", "Magia física Fúria",
                "Fúria prepara seu próximo ataque para causar dano ×2.5. Gasta metade Stamina. Veja o preparo ativar.",
                "Fúria pronta (próximo ataque ×2.5).");
            fury.kind = TutorialLessonKind.Demo;
            fury.demo = () => { if (d.PlayerUnit != null) { d.PlayerUnit.furyReady = 0; Cast(d, "Fury", null, d.PlayerUnit.anchor); } };
            fury.gate = () => d.PlayerUnit != null && d.PlayerUnit.furyReady > 1f;
            fury.assertion = () => d.PlayerUnit != null && d.PlayerUnit.furyReady > 1f ? null : "Fúria não ativou";
            L.Add(fury);

            var stoneskin = new TutorialLesson("stoneskin", 7, "Física: Pele de pedra", "Magia física Pele de pedra",
                "Pele de pedra reduz o dano físico recebido (×5) por 1 round. Veja a proteção ativar.",
                "Pele de pedra ativa (redução de dano).");
            stoneskin.kind = TutorialLessonKind.Demo;
            stoneskin.demo = () => { if (d.PlayerUnit != null) { d.PlayerUnit.stoneSkinTicks = 0; Cast(d, "StoneSkin", null, d.PlayerUnit.anchor); } };
            stoneskin.gate = () => d.PlayerUnit != null && d.PlayerUnit.stoneSkinTicks > 0;
            stoneskin.assertion = () => d.PlayerUnit != null && d.PlayerUnit.stoneSkinTicks > 0 ? null : "Pele de pedra não ativou";
            L.Add(stoneskin);

            var curve = new TutorialLesson("curve", 7, "Física: Curva", "Magia física Curva",
                "Curva faz seu próximo ataque à distância seguir o alvo e contornar obstáculos. Veja o preparo ativar.",
                "Curva pronta.");
            curve.kind = TutorialLessonKind.Demo;
            curve.demo = () => { if (d.PlayerUnit != null) { d.PlayerUnit.curveReady = false; Cast(d, "Curve", null, d.PlayerUnit.anchor); } };
            curve.gate = () => d.PlayerUnit != null && d.PlayerUnit.curveReady;
            curve.assertion = () => d.PlayerUnit != null && d.PlayerUnit.curveReady ? null : "Curva não ativou";
            L.Add(curve);

            Vector2Int leapStart = Vector2Int.zero;
            var leap = new TutorialLesson("leap", 7, "Física: Salto", "Magia física Salto",
                "Salto move por cima de obstáculos e ameaças, sem provocar ataque de oportunidade. Veja a unidade saltar.",
                "Salto moveu a unidade.");
            leap.kind = TutorialLessonKind.Demo;
            leap.demo = () => { if (d.PlayerUnit != null) { leapStart = d.PlayerUnit.anchor; Cast(d, "Leap", null, d.PlayerUnit.anchor + new Vector2Int(0, 3)); } };
            leap.gate = () => d.PlayerUnit != null && d.PlayerUnit.anchor != leapStart;
            leap.assertion = () => d.PlayerUnit != null && d.PlayerUnit.anchor != leapStart ? null : "Salto não moveu";
            L.Add(leap);

            // ── Capítulo 8: Progressão (XP → nível → pontos, via API real) ───────
            // 8.1: a ficha do herói — os 6 atributos ficam na ficha expandida.
            var sheet = new TutorialLesson("sheet", 8, "A ficha do herói", "Ficha de atributos",
                "Selecione o seu <b>herói</b> (clique, ou toque duas vezes). No painel dele, abra a <b>ficha</b> (clique ou toque): ali estão os <b>6 atributos</b>.", "", "inspect-toggle");
            sheet.setup = () => { d.Hud?.SetInspectExpanded(false); d.Hud?.HideUnitInfo(); };
            sheet.onExit = () => d.Hud?.SetInspectExpanded(false);
            sheet.steps = new[] { "Selecionar o herói", "Abrir a ficha" };
            sheet.stepsDone = () =>
            {
                if (d.Hud == null || d.PlayerUnit == null) return 0;
                if (d.Hud.InspectedUnit != d.PlayerUnit) return 0;
                return d.Hud.InspectExpanded ? 2 : 1;
            };
            sheet.gate = () => sheet.stepsDone() >= 2;
            sheet.onWrong = () => d.Hud != null && d.Fera != null && d.Hud.InspectedUnit == d.Fera
                ? "Essa é a Fera. Selecione o herói (o azul)." : null;
            sheet.assertion = () => d.PlayerUnit != null && d.PlayerUnit.stats.MaxHP > 0 ? null : "herói sem atributos";
            sheet.feedbackFn = () =>
            {
                var s2 = d.PlayerUnit != null ? d.PlayerUnit.stats : null;
                if (s2 == null) return null;
                float sum = s2.STR + s2.VIT + s2.DEX + s2.AGI + s2.INT + s2.WIS;
                return $"Ficha aberta: <b>STR {s2.STR:0}, VIT {s2.VIT:0}, DEX {s2.DEX:0}, AGI {s2.AGI:0}, INT {s2.INT:0} e WIS {s2.WIS:0}</b> — <b>{sum:0}</b> pontos no total. Cada atributo muda um número da ficha.";
            };
            L.Add(sheet);

            // 8.2: Corpo e Mente — tocar num atributo de cada grupo mostra de onde vêm Stamina e Mana.
            bool bmBody = false, bmMind = false; System.Action<string> bmHandler = null;
            var bodymind = new TutorialLesson("bodymind", 8, "Corpo e Mente", "Atributos: Corpo e Mente",
                "Toque ou passe o mouse num atributo do <b>Corpo</b> (STR, VIT, DEX, AGI) e depois num da <b>Mente</b> (INT, WIS). A dica aparece embaixo da ficha.", "");
            bodymind.setup = () =>
            {
                bmBody = bmMind = false;
                if (bmHandler != null) BattleHUD.InspectChipTouched -= bmHandler;
                bmHandler = key =>
                {
                    if (key == "STR" || key == "VIT" || key == "DEX" || key == "AGI") bmBody = true;
                    else if (key == "INT" || key == "WIS") bmMind = true;
                };
                BattleHUD.InspectChipTouched += bmHandler;
                d.Hud?.SetInspectExpanded(true);
                if (d.PlayerUnit != null) d.Hud?.ShowUnitInfo(d.PlayerUnit);
            };
            bodymind.onExit = () =>
            {
                if (bmHandler != null) { BattleHUD.InspectChipTouched -= bmHandler; bmHandler = null; }
                d.Hud?.SetInspectExpanded(false);
            };
            bodymind.steps = new[] { "Um atributo do Corpo", "Um atributo da Mente" };
            bodymind.stepsDone = () => (bmBody ? 1 : 0) + (bmMind ? 1 : 0);
            bodymind.gate = () => bmBody && bmMind;
            bodymind.assertion = () => d.PlayerUnit != null && d.PlayerUnit.stats.MaxStamina > 0 && d.PlayerUnit.stats.MaxMana > 0 ? null : "Stamina ou Mana máxima zerada";
            bodymind.feedbackFn = () =>
            {
                var st = d.PlayerUnit != null ? d.PlayerUnit.stats : null; if (st == null) return null;
                return $"<b>Stamina máxima {st.MaxStamina}</b> vem do Corpo (STR {st.STR:0}, VIT {st.VIT:0}, DEX {st.DEX:0}). <b>Mana máxima {st.MaxMana}</b> vem da Mente (WIS {st.WIS:0}).";
            };
            L.Add(bodymind);

            CharacterPreset xpPreset = null; int xpLevel0 = 1, xpBudget0 = 15;
            var xp = new TutorialLesson("xp", 8, "Progressão: XP e nível", "Progressão XP/nível/pontos",
                "Vitórias dão XP. Ao juntar XP suficiente, o personagem sobe de nível e ganha mais pontos para distribuir (orçamento 15 + 5×(nível−1)). Veja um nível subir.",
                "XP subiu o nível e aumentou o orçamento.");
            xp.kind = TutorialLessonKind.Demo;
            xp.demo = () =>
            {
                xpPreset = new CharacterPreset { level = 1, xp = 0 };
                xpLevel0 = PlayerProfile.Level(xpPreset);
                xpBudget0 = PlayerProfile.BudgetFor(xpPreset);
                PlayerProfile.AddXp(xpPreset, 1000);
            };
            xp.gate = () => xpPreset != null && PlayerProfile.Level(xpPreset) > xpLevel0;
            xp.assertion = () =>
            {
                if (xpPreset == null) return "sem preset";
                if (PlayerProfile.Level(xpPreset) <= xpLevel0) return "nível não subiu";
                if (PlayerProfile.BudgetFor(xpPreset) <= xpBudget0) return "orçamento não aumentou";
                return null;
            };
            L.Add(xp);

            // 8.3: nível dá pontos — abre o editor REAL (CharCreationHUD) por cima da batalha.
            System.Func<VisualElement> editorEl(string name) => () =>
                TutorialHeroEditor.Editor != null && TutorialHeroEditor.Editor.Document != null
                    ? UQueryExtensions.Q<VisualElement>(TutorialHeroEditor.Editor.Document.rootVisualElement, name) : null;
            var budget = new TutorialLesson("budget", 8, "Nível dá pontos", "Orçamento de pontos por nível",
                "O XP vem de vencer camadas no Coop. Seu herói está no <b>nível 5</b>. Abra <b>Editar personagem</b> (clique ou toque) e veja os <b>Pontos</b> no topo.", "");
            budget.actionLabel = "Editar personagem";
            budget.setup = () => TutorialHeroEditor.ResetHero(d.PlayerUnit);
            budget.action = () => TutorialHeroEditor.Open(d);
            budget.gate = () => TutorialHeroEditor.Editor != null;
            budget.assertion = () =>
            {
                var h = TutorialHeroEditor.Hero;
                int want = PlayerProfile.AttributeBudget + PlayerProfile.PointsPerLevel * (TutorialHeroEditor.SandboxLevel - 1);
                if (h == null) return "sem herói-sandbox";
                if (PlayerProfile.BudgetFor(h) != want) return $"orçamento {PlayerProfile.BudgetFor(h)} != {want}";
                if (!PlayerProfile.IsLocked(h)) return "o nível 5 deveria travar tudo menos somar pontos";
                return null;
            };
            budget.feedbackFn = () =>
            {
                var h = TutorialHeroEditor.Hero; if (h == null) return null;
                int lvl = PlayerProfile.Level(h), bud = PlayerProfile.BudgetFor(h), used = TutorialHeroEditor.Sum(h);
                return $"Nível {lvl} = {PlayerProfile.AttributeBudget} + {PlayerProfile.PointsPerLevel} × {lvl - 1} = <b>{bud} pontos</b>. Você usa <b>{used}</b>: sobram <b>{bud - used}</b>. No nível 5 só dá para somar pontos: arma, nome e itens ficam travados.";
            };
            L.Add(budget);

            // 8.4: um ponto, um número — o ponto em VIT muda a Vida na hora.
            int ptSum0 = 0; float[] ptVals0 = new float[6]; (int hp, int stam, int mana, int def) ptBefore = (0, 0, 0, 0);
            var point = new TutorialLesson("point", 8, "Um ponto, um número", "Gastar um ponto de atributo",
                "Toque em <b>+</b> ao lado de <b>VIT</b> (clique ou toque) e veja a <b>Vida</b> mudar no painel. Cada ponto vira um número real da luta.", "");
            point.actionLabel = "Abrir o editor";
            point.action = () => TutorialHeroEditor.Open(d);
            point.highlightResolver = editorEl("attr-plus-1");
            point.setup = () =>
            {
                TutorialHeroEditor.Open(d);
                var e = TutorialHeroEditor.Editor; if (e == null) return;
                var v = e.Editing.stats; ptSum0 = TutorialHeroEditor.Sum(e.Editing);
                ptVals0 = new[] { v.STR, v.VIT, v.DEX, v.AGI, v.INT, v.WIS };
                var cs = e.CurrentStats(); ptBefore = (cs.MaxHP, cs.MaxStamina, cs.MaxMana, cs.PhysicalDefense);
            };
            point.stepsDone = () => TutorialHeroEditor.Editor != null && TutorialHeroEditor.Sum(TutorialHeroEditor.Editor.Editing) > ptSum0 ? 1 : 0;
            point.gate = () => point.stepsDone() >= 1;
            point.onWrong = () => TutorialHeroEditor.Editor == null ? "O editor foi fechado. Use o botão <b>Abrir o editor</b>." : null;
            point.assertion = () =>
            {
                var e = TutorialHeroEditor.Editor; if (e == null) return "editor fechado";
                var cs = e.CurrentStats();
                // Um ponto em qualquer atributo tem de mudar algum número derivado (ou ao menos o próprio atributo).
                bool changed = cs.MaxHP != ptBefore.hp || cs.MaxStamina != ptBefore.stam || cs.MaxMana != ptBefore.mana || cs.PhysicalDefense != ptBefore.def;
                return TutorialHeroEditor.Sum(e.Editing) == ptSum0 + 1 || changed ? null : "o ponto não mudou nada";
            };
            point.feedbackFn = () =>
            {
                var e = TutorialHeroEditor.Editor; if (e == null) return null;
                string[] names = { "STR", "VIT", "DEX", "AGI", "INT", "WIS" };
                var v = e.Editing.stats; float[] now = { v.STR, v.VIT, v.DEX, v.AGI, v.INT, v.WIS };
                string attr = ""; for (int i = 0; i < 6; i++) if (now[i] != ptVals0[i]) attr += $"{names[i]} {ptVals0[i]:0} para {now[i]:0}  ";
                var cs = e.CurrentStats(); var parts = new List<string>();
                if (cs.MaxHP != ptBefore.hp) parts.Add($"Vida {ptBefore.hp} para {cs.MaxHP}");
                if (cs.MaxStamina != ptBefore.stam) parts.Add($"Stamina {ptBefore.stam} para {cs.MaxStamina}");
                if (cs.MaxMana != ptBefore.mana) parts.Add($"Mana {ptBefore.mana} para {cs.MaxMana}");
                if (cs.PhysicalDefense != ptBefore.def) parts.Add($"Defesa {ptBefore.def} para {cs.PhysicalDefense}");
                int bud = PlayerProfile.BudgetFor(e.Editing);
                return $"<b>{attr.Trim()}</b>" + (parts.Count > 0 ? ": " + string.Join(", ", parts) : "") + $". Pontos {TutorialHeroEditor.Sum(e.Editing)} de {bud}.";
            };
            L.Add(point);

            // 8.5: gaste tudo e salve — os atributos novos valem para o herói da luta (não vão ao perfil).
            var save = new TutorialLesson("save", 8, "Gaste tudo e salve", "Salvar personagem (orçamento exato)",
                "Some os pontos que sobraram onde quiser e toque em <b>Salvar personagem</b> (clique ou toque). O <b>−</b> não tira pontos já gastos.", "");
            save.actionLabel = "Abrir o editor";
            save.action = () => TutorialHeroEditor.Open(d);
            save.highlightResolver = editorEl("confirm-btn");
            save.setup = () => { TutorialHeroEditor.ClearConfirmed(); TutorialHeroEditor.Open(d); };
            save.onExit = () => TutorialHeroEditor.Close();
            save.steps = new[] { "Gastar todos os pontos", "Salvar o personagem" };
            save.stepsDone = () =>
            {
                if (TutorialHeroEditor.Confirmed) return 2;
                var e = TutorialHeroEditor.Editor;
                return e != null && TutorialHeroEditor.Sum(e.Editing) == PlayerProfile.BudgetFor(e.Editing) ? 1 : 0;
            };
            save.gate = () => TutorialHeroEditor.Confirmed;
            save.onWrong = () => TutorialHeroEditor.Editor == null && !TutorialHeroEditor.Confirmed ? "O editor foi fechado. Use o botão <b>Abrir o editor</b>." : null;
            save.assertion = () =>
            {
                var sv = TutorialHeroEditor.Saved; var u = d.PlayerUnit;
                if (sv == null || u == null) return "nada foi salvo";
                if (TutorialHeroEditor.Sum(sv) != PlayerProfile.BudgetFor(sv)) return "o orçamento não foi gasto por inteiro";
                if (u.stats.VIT != sv.stats.VIT || u.stats.STR != sv.stats.STR) return "os atributos salvos não chegaram ao herói da luta";
                return null;
            };
            save.feedbackFn = () =>
            {
                var sv = TutorialHeroEditor.Saved; var st = d.PlayerUnit != null ? d.PlayerUnit.stats : null;
                if (sv == null || st == null) return null;
                return $"<b>{TutorialHeroEditor.Sum(sv)} de {PlayerProfile.BudgetFor(sv)}</b>: herói salvo no nível {PlayerProfile.Level(sv)}. Na luta: <b>Vida {st.MaxHP} · Stamina {st.MaxStamina} · Mana {st.MaxMana}</b>. Vale também no Capítulo 9.";
            };
            L.Add(save);

            var rest = new TutorialLesson("rest", 9, "Descanso", "Descanso do grupo",
                "Entre camadas o grupo pode <b>descansar</b>: Vida e Mana voltam cheias e os caídos levantam. Descansar é grátis, mas só uma vez por camada. Veja o descanso acontecer.", "Descanso aplicado.");
            rest.kind = TutorialLessonKind.Demo;
            rest.demo = () => { if (d.PlayerUnit != null) { d.PlayerUnit.currentHP = Mathf.Max(1, d.PlayerUnit.stats.MaxHP / 3); d.PlayerUnit.currentMana = 0; } d.Round?.OnRested(1); };
            rest.gate = () => d.Round != null && d.Round.RestedThisLayer;
            rest.assertion = () => d.Round != null && d.Round.RestedThisLayer ? null : "descanso não aplicado";
            rest.feedbackFn = () => d.PlayerUnit != null ? $"Descanso aplicado: vida <b>{d.PlayerUnit.currentHP}/{d.PlayerUnit.stats.MaxHP}</b> e Mana <b>{d.PlayerUnit.currentMana}/{d.PlayerUnit.stats.MaxMana}</b>. Só uma vez por camada." : null;
            L.Add(rest);

            // ── Capítulo 9: Coop (descida de camada, via API real) ───────────────
            // 9.x: o baú do grupo — itens do Loot real, janela de escolha da HUD, sem rede nem perfil.
            var chestStash = new List<string>();
            string chestMsg = ""; bool chestViewed = false, chestActed = false, chestEquipped = false; string chestItemId = "";
            (string head, string torso, string legs, string off) chestOrig = ("", "", "", "");
            int chestGold = 0, chestCountAfter = 0, chestOrigCount = 0;
            System.Action chestRestore = () =>
            {
                var u = d.PlayerUnit; if (u == null) return;
                u.headId = chestOrig.head; u.torsoId = chestOrig.torso; u.legsId = chestOrig.legs; u.offhandId = chestOrig.off;
                u.ReapplyEquipment();
            };
            System.Action<int> showChestItem = null;
            System.Action showChestList = null;
            showChestList = () =>
            {
                var opts = new List<(string, System.Action)>();
                for (int i = 0; i < chestStash.Count; i++)
                {
                    int idx = i;
                    opts.Add((Loot.Describe(ItemCatalog.Get(chestStash[i])), () => showChestItem(idx)));
                }
                opts.Add(("<b>Pronto</b>", () => d.Hud?.HideReactionMenu()));
                d.Hud?.ShowChoiceTitled("ITENS DO GRUPO",
                    $"{(d.PlayerUnit != null ? d.PlayerUnit.unitName : "")} · <color=#F2B544>{chestGold} moedas</color>\n{(chestStash.Count == 0 ? "O baú está vazio." : "Escolha um item para equipar ou vender.")}", opts.ToArray());
            };
            showChestItem = idx =>
            {
                if (idx < 0 || idx >= chestStash.Count || d.PlayerUnit == null) { showChestList(); return; }
                chestViewed = true;
                var it = ItemCatalog.Get(chestStash[idx]);
                var cur = ItemCatalog.Get(Loot.Current(d.PlayerUnit, it.slot));
                var opts = new List<(string, System.Action)>();
                if (Loot.CanEquip(d.PlayerUnit, it, out string why))
                    opts.Add(($"Equipar{(cur != null ? $"  <size=12>(o seu {cur.displayName} vai para o grupo)</size>" : "")}", () =>
                    {
                        string details = Loot.Details(it, cur);
                        if (Loot.Take(d.PlayerUnit, chestStash, idx))
                        { chestActed = true; chestEquipped = true; chestItemId = it.id; chestMsg = $"Você equipou <b>{it.displayName}</b>. {details}"; }
                        d.Hud?.HideReactionMenu();
                    }));
                else opts.Add(($"<color=#B9B09C>Não dá para equipar: {why}</color>", () => showChestItem(idx)));
                opts.Add(($"Vender  <size=12>+{Loot.SellPrice(it)} moedas</size>", () =>
                {
                    int gain = Loot.Sell(chestStash, idx); chestGold += gain;
                    chestActed = true; chestEquipped = false; chestItemId = it.id; chestCountAfter = chestStash.Count;
                    chestMsg = $"Você vendeu <b>{it.displayName}</b> por <b>{gain} moedas</b>. O baú ficou com {chestStash.Count} itens.";
                    d.Hud?.HideReactionMenu();
                }));
                opts.Add(("Voltar", () => showChestList()));
                d.Hud?.ShowChoiceTitled("ITENS DO GRUPO", $"{Loot.Describe(it)}\n{Loot.Details(it, cur)}", opts.ToArray());
            };
            var chest = new TutorialLesson("chest", 9, "O baú do grupo", "Baú de itens (Loot)",
                "Vencer uma camada abre o <b>baú do grupo</b>. Escolha um item e <b>equipe</b> ou <b>venda</b> (clique, toque ou teclas 1..n). Trocar devolve o seu item antigo ao baú.", "");
            chest.setup = () =>
            {
                chestRestore();
                var u = d.PlayerUnit; if (u == null) return;
                chestOrig = (u.headId, u.torsoId, u.legsId, u.offhandId);
                chestStash.Clear(); chestStash.AddRange(Loot.Generate(1, 0, 1, 3));   // sempre os mesmos: Cota de Malha (rara) e Elmo de Couro
                chestOrigCount = chestStash.Count;
                chestMsg = ""; chestViewed = chestActed = chestEquipped = false; chestItemId = ""; chestGold = 0;
                showChestList();
            };
            chest.onExit = () => { d.Hud?.HideReactionMenu(); chestRestore(); };
            chest.steps = new[] { "Abrir um item do baú", "Equipar ou vender" };
            chest.stepsDone = () => chestActed ? 2 : chestViewed ? 1 : 0;
            chest.gate = () => chestActed;
            chest.assertion = () =>
            {
                if (!chestActed) return "nenhuma ação no baú";
                if (chestEquipped)
                {
                    var it = ItemCatalog.Get(chestItemId);
                    return it != null && Loot.Current(d.PlayerUnit, it.slot) == chestItemId ? null : "o item não ficou equipado";
                }
                // Compara a QUANTIDADE (o baú pode ter dois itens iguais): vender tira exatamente um.
                return chestCountAfter == chestOrigCount - 1 && chestGold > 0 ? null : "vender não tirou o item do baú";
            };
            chest.feedbackFn = () => chestMsg;
            L.Add(chest);

            var layer = new TutorialLesson("layer", 9, "Coop: descer camada", "Coop descida de camada",
                "No Coop, o grupo desce camadas rumo ao Inferno. Cada camada é mais difícil; recuar é livre. Veja a descida para a camada 2.",
                "Camada avançada (descida).");
            layer.kind = TutorialLessonKind.Demo;
            layer.demo = () => { if (d.Round != null) d.Round.BeginNextLayer(2); };
            layer.gate = () => d.Round != null && d.Round.Layer >= 2;
            layer.assertion = () => d.Round != null && d.Round.Layer >= 2 ? null : "camada não avançou";
            L.Add(layer);

            // 9.y: camada 2 jogável — a fera fica mais forte (orçamento +0,1 da fração por camada) e revida.
            UnitStatBlock feraBase = null; int layer2Hp0 = 0;
            System.Func<int, float> beastRatio = ly =>
            {
                float frac = Tuning.Get().beastBudgetFrac;
                return (frac + 0.10f * (ly - 1)) / frac;
            };
            var layer2 = new TutorialLesson("layer2", 9, "Camada 2: a fera mais forte", "Coop: camada 2 jogável",
                "Você desceu para a <b>camada 2</b>. A fera está colada em você, mais forte, e vai revidar. <b>Ataque</b> (tecla 2 ou toque) nela e <b>Confirme</b> (Enter). Quando a janela REAGIR abrir, reaja ou escolha <b>Não reagir</b>.", "", "abtn-attack");
            layer2.setup = () =>
            {
                var f = d.Fera; var h = d.PlayerUnit; if (f == null || h == null || d.Round == null) return;
                if (feraBase == null)
                    feraBase = new UnitStatBlock { STR = f.stats.STR, VIT = f.stats.VIT, DEX = f.stats.DEX, AGI = f.stats.AGI, INT = f.stats.INT, WIS = f.stats.WIS };
                float k = beastRatio(2);
                f.stats.STR = Mathf.Round(feraBase.STR * k); f.stats.VIT = Mathf.Round(feraBase.VIT * k);
                f.stats.DEX = Mathf.Round(feraBase.DEX * k); f.stats.AGI = Mathf.Round(feraBase.AGI * k);
                f.currentHP = f.stats.MaxHP;
                h.RestoreForNextLayer(0.30f, 0.50f, 0.25f);          // a descida cura só uma parte
                h.currentHP = Mathf.Max(h.currentHP, h.stats.MaxHP / 2);
                if (d.Round.Layer < 2) d.Round.BeginNextLayer(2);
                f.TeleportTo(h.anchor + new Vector2Int(1, 0));   // colada: ela só revida, e o golpe sai no tick 0
                layer2Hp0 = f.currentHP;
                ClearPlan(d);
            };
            layer2.steps = new[] { "Atacar a fera", "Confirmar e acertar" };
            layer2.stepsDone = () =>
            {
                var u = d.PlayerUnit; if (u == null || d.Fera == null) return 0;
                if (d.Fera.currentHP < layer2Hp0) return 2;
                return u.plannedAttacks.Count > 0 ? 1 : 0;
            };
            layer2.gate = () => d.Fera != null && d.Round != null && d.Round.Layer >= 2 && d.Fera.currentHP < layer2Hp0;
            layer2.assertion = () => d.Round != null && d.Round.Layer >= 2 && d.Fera != null && d.Fera.currentHP < layer2Hp0 ? null : "a camada 2 não foi jogada (sem dano na fera)";
            layer2.feedbackFn = () =>
            {
                if (d.Fera == null || feraBase == null) return null;
                return $"Camada 2: a fera foi de STR <b>{feraBase.STR:0}</b> e VIT <b>{feraBase.VIT:0}</b> para STR <b>{d.Fera.stats.STR:0}</b> e VIT <b>{d.Fera.stats.VIT:0}</b> (orçamento ×{beastRatio(2):0.##}). Seu golpe tirou <b>{layer2Hp0 - d.Fera.currentHP}</b> de vida: {d.Fera.currentHP}/{d.Fera.stats.MaxHP}. Cada camada fica mais difícil.";
            };
            L.Add(layer2);

            // Foco de HUD por lição (GDD-027): revela só o que é relevante para a etapa.
            foreach (var l in L)
            {
                // Só a UI da lição fica visível e funcional (tudo o mais some).
                string[] C = { "confirm" };
                switch (l.id)
                {
                    case "cam": case "zoom": l.hudFocus = () => TutorialHud.None(); break;
                    case "inspect": l.hudFocus = () => { TutorialHud.None(); TutorialHud.UnitInfo = true; }; break;
                    case "bar": l.hudFocus = () => TutorialHud.Ticks(); break;
                    case "tick": l.hudFocus = () => TutorialHud.Show(new[] { "wait" }); break;
                    case "undo": l.hudFocus = () => TutorialHud.Show(new string[0], new[] { "undo" }); break;
                    case "plan": l.hudFocus = () => TutorialHud.Show(new[] { "move" }); break;
                    case "confirm": l.hudFocus = () => TutorialHud.Show(new string[0], C); break;
                    case "moved": l.hudFocus = () => TutorialHud.Ticks(); break;
                    case "chain": case "climb": l.hudFocus = () => TutorialHud.Show(new[] { "move" }, C); break;
                    case "chest": l.hudFocus = () => TutorialHud.None(); break;
                    case "layer2": l.hudFocus = () => TutorialHud.Show(new[] { "move", "attack", "reaction" }, C); break;
                    case "bodymind": l.hudFocus = () => { TutorialHud.None(); TutorialHud.UnitInfo = true; }; break;
                    case "budget": case "point": case "save": l.hudFocus = () => TutorialHud.None(); break;
                    case "sheet": l.hudFocus = () => { TutorialHud.None(); TutorialHud.UnitInfo = true; }; break;
                    case "cliff": l.hudFocus = () => TutorialHud.Show(new[] { "move" }); break;
                    case "attack": l.hudFocus = () => TutorialHud.Show(new[] { "move", "attack" }, C); break;
                    case "breathe": l.hudFocus = () => TutorialHud.Show(new[] { "breathe" }, C); break;
                    case "posture": l.hudFocus = () => TutorialHud.Show(new[] { "posture" }); break;
                    case "defreaction": l.hudFocus = () => TutorialHud.Show(new[] { "reaction" }); break;
                    case "cast": l.hudFocus = () => TutorialHud.Show(new[] { "spell" }, C); break;
                    case "react": case "reaction": l.hudFocus = () => TutorialHud.Show(new[] { "reaction" }, C); break;
                    case "intercept": l.hudFocus = () => TutorialHud.Show(new[] { "wait" }, C); break;
                    case "aim": l.hudFocus = () => TutorialHud.Show(new[] { "aim" }, C); break;
                    case "concentrate": l.hudFocus = () => TutorialHud.Show(new[] { "concentrate" }, C); break;
                    // Demonstrações e lições de leitura: só o mapa e o card.
                    default: l.hudFocus = () => TutorialHud.None(); break;
                }
            }

            return L;
        }
    }
}