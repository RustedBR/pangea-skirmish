using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PangeaSkirmish
{
    /// <summary>
    /// Semi-turno por TICKS (GDD-014). Cada "round" é um PONTO DE DECISÃO:
    /// só as unidades livres (fila vazia) planejam; depois a simulação avança tick a tick
    /// até a próxima unidade ficar livre. Ações resolvem no ÚLTIMO tick da sua duração;
    /// tudo que termina no mesmo tick resolve junto (movimento, depois ataques).
    /// Unidades ocupadas carregam a fila para o próximo ponto de decisão.
    /// </summary>
    public class RoundManager : MonoBehaviour
    {
        public float planningTime = 60f;
        public float zoomSize = 5.0f;      // tamanho ortográfico durante o "pequeno zoom"
        // Zoom do enquadramento automático e demais ritmos extras: GameTuning (Tuning.Get()).

        [Header("Ritmo das ações (pausas para acompanhar)")]
        public float camMoveDuration   = 0.45f; // duração-alvo dos movimentos de câmera
        public float preActionPause     = 0.35f; // antes de cada ação resolver
        public float postActionPause    = 0.55f; // depois de cada ação resolver
        public float initiativeHold      = 1.1f;  // tempo mostrando as rolagens de iniciativa
        public float slotPause           = 0.3f;  // entre slots de ação
        public float bonusConfirmTime    = 3f;    // tempo do prompt de confirmar incremento de ataque
        public float bonusStepTime       = 5f;    // tempo de escolher o passo extra (incr. de movimento)

        private GridManager _grid;
        private PlanningController _planner;
        private BattleHUD _hud;
        private Canvas _canvas;
        private Camera _cam;
        private CameraController _camCtrl;
        private List<Unit> _units;
        private Unit _playerUnit;
        private ScreenFlash _screenFlash;
        private TileEffectManager _tileFx;
        /// <summary>Zonas de magia da batalha (GDD-019). Exposto em SpellField.Current para o AttackResolver.</summary>
        private readonly SpellField _spellField = new SpellField();

        // ---- Lockstep MP (Fase 6) ----
        private bool _waitingLockstep; // true enquanto aguarda ExecuteRoundClientRpc

        private RoundPhase _phase = RoundPhase.Resolving;
        private float _timer;
        private int _round;
        private bool _gracePeriodUsed;
        private float _pauseUsed;   // segundos de relógio pausados neste ponto de decisão
        /// <summary>Tick global da batalha (GDD-014). Status/tiles/reações renovam a cada ticksPerRound.</summary>
        private int _globalTick;

        // ---- Coop: Descida ao Inferno (GDD-015 §3) ----
        /// <summary>Camada atual da descida (1 = superfície).</summary>
        public int Layer { get; private set; } = 1;
        private bool _awaitingDescent;   // camada vencida, esperando "descer ou recuar"
        public const string DeepestLayerKey = "pangea.coop.deepestLayer";

        // pose-base da câmera (alvo do overview entre rounds)
        private float _camBaseSize;

        /// <summary>
        /// Centro do mapa para o overview de fim de round.
        /// Fix (2026-07-29): era cacheado uma vez no Setup. Como a rotação de vista
        /// é reindexação lógica, a posição física do centro muda a cada Q/E e o
        /// valor cacheado ficava obsoleto — a câmera mirava um ponto deslocado.
        /// Calculado no uso, e sobre o centro REAL (width-1)/2, que é o ponto fixo
        /// da permutação, em vez de width/2 truncado.
        /// </summary>
        private Vector3 CamBaseCenter =>
            _grid != null ? _grid.WorldCenter : new Vector3(0f, 0f, 10f);


        public RoundPhase Phase => _phase;

        private void OnDestroy()
        {
            _spellField.Clear();
            if (SpellField.Current == _spellField) SpellField.Current = null;
        }

        public void Setup(GridManager grid, PlanningController planner, BattleHUD hud,
                          Camera cam, CameraController camCtrl, List<Unit> units, Unit playerUnit,
                          TileEffectManager tileFx = null)
        {
            _grid = grid;
            _spellField.Clear();
            SpellField.Current = _spellField;
            BattleFeedback.Cam = cam;
            // Reinício rápido a qualquer momento pelo painel de LOG (F8), só para o anfitrião.
            GameLog.RestartAction = CanRestart ? (System.Action)RequestRestart : null;
            GameLog.RestartLabel = "Reiniciar partida (anfitrião)";
            Debug.Log($"[RoundManager] Setup: {units?.Count ?? 0} unidade(s), multiplayer={RuntimeMultiplayerSession.IsMultiplayer}, host={RuntimeMultiplayerSession.IsHost}, modo={RuntimeMultiplayerSession.CurrentConfig?.gameMode}");
            _planner = planner;
            _hud = hud;
            _cam = cam;
            _camCtrl = camCtrl;
            _units = units;
            _playerUnit = playerUnit;
            _tileFx = tileFx;
            _camBaseSize = cam.orthographicSize;
            _planner.SetHUD(hud);
            _planner.OnConfirmRequested = ConfirmPlan;
            _hud.BindConfirm(ConfirmPlan);
            // Registro idempotente: evita handler duplicado se Setup rodar 2x
            // (causaria rótulo de dano em duplicata no MP).
            Unit.OnDamageTaken -= SpawnDamageLabel;
            Unit.OnDamageTaken += SpawnDamageLabel;
            _screenFlash = ScreenFlash.Create(hud.Document);
            DayNight.Reset();   // GDD-024: toda partida começa de dia

            // Indicador de modo câmera
            if (_camCtrl != null)
            {
                _camCtrl.OnModeChanged += mode => _hud.SetCameraMode(mode);
                _hud.SetCameraMode(_camCtrl.Mode);
            }

            // Clique numa linha do log: apenas inspeciona (não mexe na câmera), em qualquer fase.
            _hud.OnLogLineClicked = unit =>
            {
                if (unit != null && !unit.IsDead) _hud.ShowUnitInfo(unit);
            };
        }

        private void OnDisable()
        {
            Unit.OnDamageTaken -= SpawnDamageLabel;
        }

        private void SpawnDamageLabel(Unit unit, int damage, bool isCritical)
        {
            if (_cam == null || unit == null) return;
            BattleLabel.CreateDamage(_cam, unit.HeadWorld, damage, isCritical);
            // Label animates and self-destructs

            // Flash de tela quando o JOGADOR toma dano crítico
            if (isCritical && unit == _playerUnit)
            {
                var t = RuntimeTuning.Active ?? Resources.Load<GameTuning>("GameTuning");
                _screenFlash?.FlashRed(t.flashDurationCrit, t.flashIntensityCrit);
            }
        }

        public void Begin() => StartCoroutine(StartRound());

        private IEnumerator StartRound()
        {
            _round++;
            // Reações renovam por "round de ticks" (ver AdvanceGlobalTick), não por ponto de decisão.
            if (_round == 1) foreach (var u in _units) if (!u.IsDead) u.ResetReactionsForRound();
            AudioManager.I?.Play(AudioManager.I.sfxRound);
            _hud.SetPhase($"Tick {_globalTick} — Decisão");
            _hud.SetTimerVisible(false);
            _hud.SetConfirmVisible(false);
            LogTimeline();
            yield return new WaitForSeconds(Tuning.Get().roundBannerHold);
            EnterPlanning();
        }

        /// <summary>
        /// Linha do tempo no log (GDD-014 §7): suas unidades mostram a fila inteira;
        /// as dos outros só "livre" ou "ocupada" (informação incompleta).
        /// </summary>
        private void LogTimeline()
        {
            ulong localId = RuntimeMultiplayerSession.IsMultiplayer && Unity.Netcode.NetworkManager.Singleton != null
                ? Unity.Netcode.NetworkManager.Singleton.LocalClientId : 0;
            _hud.UpdateTimeline(_globalTick, _units, u =>
                RuntimeMultiplayerSession.IsMultiplayer ? u.ownerId == localId : u == _playerUnit);
        }

        /// <summary>Uma linha por unidade com a fila comprometida, recursos e posição. Só para o log.</summary>
        private void LogQueues()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"[Fila] tick {_globalTick} rodada {_round}:");
            foreach (var u in _units)
            {
                if (u == null) continue;
                sb.Append($"\n  {u.unitName}{(u.IsDead ? " (morto)" : "")} @({u.anchor.x},{u.anchor.y}) HP {u.currentHP}/{u.stats.MaxHP} Est {u.currentStamina} Mana {u.currentMana}: ");
                if (u.queue.Count == 0) { sb.Append("—"); continue; }
                for (int i = 0; i < u.queue.Count; i++)
                {
                    var q = u.queue[i];
                    if (i > 0) sb.Append(", ");
                    sb.Append(ActionShort(q.Type));
                    if (q.Type == ActionType.Spell) sb.Append($"[{q.Spell.SpellId}→({q.Spell.TargetTile.x},{q.Spell.TargetTile.y}){(q.Spell.TargetUnit != null ? " " + q.Spell.TargetUnit.unitName : "")}]");
                    else if (q.Type == ActionType.Move) sb.Append($"→({q.Dest.x},{q.Dest.y})");
                    else if (q.Type == ActionType.Attack && q.Attack.TargetUnit != null) sb.Append($"→{q.Attack.TargetUnit.unitName}");
                    sb.Append($" {q.Elapsed}/{q.Duration}");
                }
            }
            Debug.Log(sb.ToString());
        }

        private static string ActionShort(ActionType t) =>
            t == ActionType.Move ? "Mov" : t == ActionType.Attack ? "Atq" : t == ActionType.Concentrate ? "Con" : t == ActionType.Spell ? "Mag" : "Esp";

        /// <summary>Foco automático suave: pede o alvo ao CameraController e espera assentar.</summary>
        private IEnumerator FocusCamera(Vector3 center, float size, float duration)
        {
            if (_camCtrl != null)
            {
                var T = Tuning.Get();
                _camCtrl.FocusOn(center, size);
                yield return _camCtrl.WaitUntilSettled(Mathf.Max(duration, T.camSettleMinDuration) + T.camSettleExtra);
            }
            else yield return null;
        }

        /// <summary>Calcula o zoom necessário para mostrar todas as posições dadas.</summary>
        private float CalcZoomForPositions(Vector3[] positions, float padding = -1f)
        {
            if (padding < 0f) padding = Tuning.Get().autoFramePadding;
            if (positions == null || positions.Length == 0) return zoomSize;
            if (positions.Length == 1) return zoomSize;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            // Extensão na TELA: profundidade (Z) aparece pela metade e altura (Y) quase inteira.
            float sinP = Mathf.Sin(World3D.CamPitch * Mathf.Deg2Rad), cosP = Mathf.Cos(World3D.CamPitch * Mathf.Deg2Rad);
            foreach (var p in positions)
            {
                float sy = p.z * sinP + p.y * cosP;
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (sy < minY) minY = sy;
                if (sy > maxY) maxY = sy;
            }

            float width  = (maxX - minX) + padding * 2f;
            float height = (maxY - minY) + padding * 2f;
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);

            // Para câmera ortográfica: size controla metade da altura visível
            float requiredHeight = height;
            float requiredWidth  = width / aspect;
            float required = Mathf.Max(requiredHeight, requiredWidth) * 0.5f;

            var T = Tuning.Get();
            return Mathf.Clamp(required, T.autoFrameZoomMin, T.autoFrameZoomMax);
        }

        /// <summary>Foca na área de um ataque (atacante + alvo), respeitando modo Auto.</summary>
        private IEnumerator FocusOnAttackArea(Unit attacker, Unit target, float duration)
        {
            if (_camCtrl == null) yield break;
            if (_camCtrl.Mode == CameraMode.Manual) yield break;

            Vector3 atkPos = attacker.transform.position;
            Vector3 tgtPos = target != null && !target.IsDead ? target.transform.position : atkPos;
            Vector3 center = (atkPos + tgtPos) * 0.5f;
            float size = CalcZoomForPositions(new[] { atkPos, tgtPos });

            _camCtrl.FocusOnArea(center, size);
            yield return _camCtrl.WaitUntilSettled(duration + Tuning.Get().attackFocusSettleExtra);
        }

        // -------------------- FASE DE PLANEJAMENTO --------------------
        private void EnterPlanning()
        {
            _phase = RoundPhase.Planning;
            _gracePeriodUsed = false;
            _pauseUsed = 0f;
            _waitingLockstep = false;

            // Só unidades LIVRES recebem plano novo; ocupadas mantêm fila e listas (carry-over).
            foreach (var u in _units)
                if (!u.IsDead && !u.IsBusy) u.ResetPlan();
            float decisionTime = Tuning.Get().decisionTime;

            if (RuntimeMultiplayerSession.IsMultiplayer)
            {
                // MP: planningTime da config; ordenar unidades por unitId para determinismo
                _units.Sort((a, b) => UnitRegistry.GetId(a).CompareTo(UnitRegistry.GetId(b)));
                float cfgTime = RuntimeMultiplayerSession.CurrentConfig.planningTime;
                planningTime = cfgTime > 0f ? cfgTime : decisionTime;

                _hud.SetPhase($"Tick {_globalTick} — Decisão");

                // Habilita controle apenas da unidade do jogador local.
                // Usa o id direto do NGO (RuntimeMultiplayerSession pode ter sido capturado
                // cedo demais no cliente).
                ulong localId = Unity.Netcode.NetworkManager.Singleton != null
                    ? Unity.Netcode.NetworkManager.Singleton.LocalClientId
                    : RuntimeMultiplayerSession.LocalClientId;
                Unit controlled = null;
                foreach (var u in _units)
                    if (!u.IsDead && u.ownerId == localId)
                    { controlled = u; break; }
                Debug.Log($"[MP] Planejamento: localId={localId}, controlando '{(controlled != null ? controlled.unitName : "NENHUMA")}'");

                if (controlled != null && !controlled.IsDead && controlled.IsBusy)
                {
                    // Ocupada: não decide neste ponto — confirma sozinha (pequeno atraso para o
                    // host já ter aberto a janela de coleta).
                    _hud.SetTimerVisible(false);
                    _hud.SetConfirmVisible(false);                    _timer = planningTime;
                    StartCoroutine(AutoConfirmBusy());
                }
                else if (controlled != null && !controlled.IsDead)
                {
                    _timer = planningTime;
                    _hud.SetTimerVisible(true);
                    _hud.SetTimerWarning(false);
                    _planner.Begin(controlled);
                    _hud.ShowUnitInfo(controlled);
                    _hud.SetConfirmVisible(true);
                }
                else
                {
                    // Jogador local morto: auto-submit plano vazio
                    _hud.SetTimerVisible(false);
                    _hud.SetConfirmVisible(false);
                    _hud.LogAction("<color=#ff7a7a>Sua unidade foi eliminada — round automático.</color>");
                    _timer = planningTime; // ainda aguarda timer para sincronizar
                }

                // Host inicia coleta (mesma classe de bug: se Instance ainda for null aqui,
                // a coleta nunca começa e os planos submetidos são rejeitados o round todo).
                if (LockstepBattleSync.Instance != null && LockstepBattleSync.Instance.IsServer)
                {
                    BeginLockstepCollection();
                }
                else if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsServer)
                {
                    Debug.LogWarning("[MP] (host) LockstepBattleSync.Instance NULL ao entrar em Planning — aguardando antes de iniciar a coleta.");
                    StartCoroutine(BeginCollectionWhenReady());
                }
                return;
            }

            _hud.SetPhase($"Tick {_globalTick} — Decisão");
            planningTime = decisionTime;

            // Jogador morto ou ocupado: pula direto para a simulação de ticks
            if (_playerUnit != null && !_playerUnit.IsDead && _playerUnit.IsBusy)
            {
                _hud.SetTimerVisible(false);
                _hud.SetConfirmVisible(false);
                StartCoroutine(ActionRoutine());
                return;
            }
            if (_playerUnit == null || _playerUnit.IsDead)
            {
                _hud.SetTimerVisible(false);
                _hud.SetConfirmVisible(false);
                _hud.LogAction("<color=#ff7a7a>Guerreiro eliminado — rodada automática.</color>");
                StartCoroutine(ActionRoutine());
                return;
            }

            _timer = planningTime;
            _hud.SetTimerVisible(true);
            _hud.SetTimerWarning(false);
            _planner.Begin(_playerUnit);
            _hud.ShowUnitInfo(_playerUnit);
            _hud.SetConfirmVisible(true);
        }

        private void Update()
        {
            HandleInspectClick();

            if (_phase != RoundPhase.Planning) return;
            // Pausa o relógio enquanto o jogador monta a jogada, até o teto por ponto de decisão.
            float pauseBudget = Tuning.Get().planningPauseBudget;
            if (_planner != null && _planner.IsEngaged && _pauseUsed < pauseBudget && !_waitingLockstep)
            {
                _pauseUsed += Time.deltaTime;
                _hud.SetTimer(Mathf.Max(0f, _timer));
                _hud.SetTimerWarning(false);
                return;
            }
            _timer -= Time.deltaTime;
            _hud.SetTimer(Mathf.Max(0f, _timer));

            if (_timer <= 0f)
            {
                // Grace period só em SP (MP usa auto-submit via ConfirmPlan)
                if (!RuntimeMultiplayerSession.IsMultiplayer
                    && !_gracePeriodUsed
                    && _playerUnit != null && !_playerUnit.IsDead
                    && _playerUnit.remainingAP > 0)
                {
                    _gracePeriodUsed = true;
                    float grace = Tuning.Get().planningGraceSeconds;
                    _timer = grace;
                    _hud.SetTimerWarning(true);
                    AudioManager.I?.Play(AudioManager.I.sfxTimerWarning);
                    _hud.Log($"⚠ Tempo esgotado — {grace:0.#}s extras para gastar PA restantes!");
                }
                else
                {
                    _hud.SetTimerWarning(false);
                    ConfirmPlan();
                }
            }
        }

        /// <summary>
        /// Inspeção global: clicar numa unidade no mapa mostra sua info em qualquer fase,
        /// sem mexer na câmera. Não dispara durante picking de planejamento, prompts de
        /// bônus, ou quando o cursor está sobre a UI.
        /// </summary>
        private void HandleInspectClick()
        {
            if (!PointerInput.Available || !PointerInput.ClickThisFrame) return;
            if (_planner != null && _planner.IsPicking) return; // o planner usa o clique
            if (_hud != null && _hud.IsPromptVisible) return;    // escolha de passo bônus
            if (PointerOverUI()) return;

            var u = UnitUnderMouse();
            if (u != null) _hud.ShowUnitInfo(u);
            else if (_hud != null) _hud.HideUnitInfo();
        }

        private bool PointerOverUI()
        {
            // UI Toolkit não é enxergado pelo EventSystem UGUI (fix 2026-11-10).
            return _hud != null && _hud.IsPointerOverUI();
        }

        private Unit UnitUnderMouse()
        {
            if (_cam == null || !PointerInput.Available) return null;
            Vector2 screen = PointerInput.Position;
            var world = RaycastGround(screen);
            // Seleção por collider 3D (migração XY→XZ): raycast do mouse contra as unidades.
            return Unit.PickAtWorld(_cam, screen);
        }

        private IEnumerator AutoConfirmBusy()
        {
            yield return new WaitForSeconds(0.25f);
            if (_phase == RoundPhase.Planning) ConfirmPlan();
        }

        public void ConfirmPlan()
        {
            if (_phase != RoundPhase.Planning) return;
            if (_waitingLockstep) return; // já confirmou, aguardando host
            AudioManager.I?.Play(AudioManager.I.sfxUIConfirm);
            _planner.End();
            _hud.SetTimerVisible(false);
            _hud.SetConfirmVisible(false);

            if (RuntimeMultiplayerSession.IsMultiplayer)
            {
                _waitingLockstep = true;
                _hud.SetPhase($"Tick {_globalTick} — Aguardando jogadores...");
                if (LockstepBattleSync.Instance != null)
                {
                    LockstepBattleSync.Instance.OnPlanningComplete();
                }
                else
                {
                    // BUG conhecido de desync: se o objeto ainda não tiver replicado neste
                    // instante, o antigo "?." descartava a chamada em silêncio — o plano
                    // NUNCA era enviado, o host esperava o timeout e resolvia o round sem
                    // as ações deste jogador (dessincroniza o estado visível/HP/posição).
                    Debug.LogWarning("[MP] LockstepBattleSync.Instance NULL ao confirmar plano — aguardando aparecer antes de enviar.");
                    StartCoroutine(SendPlanWhenReady());
                }
                return;
            }

            StartCoroutine(ActionRoutine());
        }

        private void BeginLockstepCollection()
        {
            // Conta só jogadores AINDA VIVOS (com pelo menos uma unidade viva).
            // Jogadores já eliminados não devem travar a fase de planejamento.
            int players = LockstepBattleSync.Instance != null
                ? LockstepBattleSync.Instance.AlivePlayerCount()
                : (RoomManager.Instance != null
                    ? RoomManager.Instance.Slots.Count
                    : (Unity.Netcode.NetworkManager.Singleton != null ? Unity.Netcode.NetworkManager.Singleton.ConnectedClientsIds.Count : 1));
            LockstepBattleSync.Instance.BeginCollection(Mathf.Max(1, players));
        }

        private IEnumerator BeginCollectionWhenReady()
        {
            float t = 8f;
            while (LockstepBattleSync.Instance == null && t > 0f)
            {
                t -= Time.deltaTime;
                yield return null;
            }
            if (LockstepBattleSync.Instance != null)
                BeginLockstepCollection();
            else
                Debug.LogError("[MP] (host) LockstepBattleSync.Instance NUNCA apareceu (timeout 8s) — round travado, nenhum plano será coletado.");
        }

        /// <summary>Espera o LockstepBattleSync replicar no cliente antes de enviar o
        /// plano confirmado — evita que o "?." silencioso descarte a submissão.</summary>
        private IEnumerator SendPlanWhenReady()
        {
            float t = 8f;
            while (LockstepBattleSync.Instance == null && t > 0f)
            {
                t -= Time.deltaTime;
                yield return null;
            }
            if (LockstepBattleSync.Instance != null)
            {
                LockstepBattleSync.Instance.OnPlanningComplete();
            }
            else
            {
                Debug.LogError("[MP] LockstepBattleSync.Instance NUNCA apareceu (timeout 8s) — plano deste jogador foi PERDIDO neste round.");
            }
        }

        // -------------------- FASE DE AÇÃO (slot a slot) --------------------

        private IEnumerator ActionRoutine()
        {
            var T = Tuning.Get();
            // Sai de Planning já aqui: enquanto a fase ficava em Planning até o primeiro
            // movimento/ataque, o timer de decisão podia zerar e reenviar o plano.
            _phase = RoundPhase.Resolving;

            // 1) Unidades livres comprometem o plano na fila (plano vazio = Esperar).
            //    Ordem por unitId: mesma em todas as máquinas (lockstep).
            //    As feras (Coop) planejam aqui, em toda máquina, com o mesmo estado (GDD-015 §4).
            foreach (var u in _units)
            {
                if (u.IsDead || u.IsBusy) continue;
                if (BeastAI.Controls(u)) BeastAI.Plan(u, _units, _grid);
                u.BuildQueueFromPlan();
            }
            // Diagnóstico (vai para o painel de LOG): a fila de cada unidade neste ponto de decisão.
            LogQueues();

            // 2) Próximo ponto de decisão = menor fila restante entre os vivos.
            // Round fechado (2026-11-13): todo mundo planeja o round inteiro e ele termina junto,
            // sempre ticksPerRound ticks, sem parar no meio para decidir.
            int segment = Mathf.Max(1, T.ticksPerRound);

            var burstMoves = new Dictionary<Unit, int>();

            for (int t = 0; t < segment; t++)
            {
                _hud.SetPhase($"Tick {_globalTick}");

                // Avança a ação da frente de cada fila; coleta as que TERMINAM neste tick.
                var movesThisSlot   = new List<(Unit u, Vector2Int dest, bool incr)>();
                var attacksThisSlot = new List<(Unit u, PlannedAttack atk, bool incr, bool aimed)>();
                var spellsThisSlot  = new List<(Unit u, PlannedSpell sp)>();
                _provokedThisTick.Clear();
                // A magia paga Mana/Stamina no tick em que começa (2026-11-14): Concentrar/Respirar antes
                // dela já contam. Sem recurso na hora (ex.: drenado), a magia falha e sai da fila.
                foreach (var u in _units)
                {
                    if (u.IsDead || u.queue.Count == 0) continue;
                    var q = u.queue[0];
                    if (q.Type != ActionType.Spell || q.Elapsed > 0 || q.Paid) continue;
                    var d = q.Spell.Def;
                    if (d != null && (u.currentMana < d.ManaPart || u.currentStamina < d.StaminaPart))
                    {
                        _hud.LogAction($"<color=#ff7a6a>✕</color> {u.unitName}: sem recurso para <b>{d.displayName}</b> (Mana {u.currentMana}/{d.ManaPart}, Stamina {u.currentStamina}/{d.StaminaPart})", u);
                        u.queue.RemoveAt(0);
                        continue;
                    }
                    if (d != null) { u.currentMana -= d.ManaPart; u.currentStamina -= d.StaminaPart; }
                    q.Paid = true;
                }
                // Conjurar dentro do alcance de um inimigo provoca o golpe de oportunidade (GDD-020 §8).
                var castStarts = new List<Unit>();
                foreach (var u in _units)
                    if (!u.IsDead && u.queue.Count > 0 && u.queue[0].Type == ActionType.Spell && u.queue[0].Elapsed == 0)
                        castStarts.Add(u);
                foreach (var cu in castStarts)
                {
                    yield return Provoke(cu, "começou a conjurar", null);
                    if (cu.counterInterrupted && cu.queue.Count > 0 && cu.queue[0].Type == ActionType.Spell)
                    {
                        var q0 = cu.queue[0];
                        string nm = q0.Spell.Def != null ? q0.Spell.Def.displayName : q0.Spell.SpellId;
                        _hud.LogAction($"<color=#ff7a6a>✕</color> {cu.unitName}: <b>{nm}</b> cortada pelo golpe de oportunidade", cu);
                        cu.queue.RemoveAt(0);
                    }
                }
                foreach (var u in _units)
                {
                    if (u.IsDead || u.queue.Count == 0) continue;
                    var head = u.queue[0];
                    head.Elapsed++;
                    if (head.Elapsed < head.Duration) continue;
                    u.queue.RemoveAt(0);
                    switch (head.Type)
                    {
                        case ActionType.Move:   movesThisSlot.Add((u, head.Dest, head.IsBonus)); break;
                        case ActionType.Attack: attacksThisSlot.Add((u, head.Attack, head.IsBonus, head.IsAimed)); break;
                        case ActionType.Concentrate:
                            // Concentrar dá Mana imediata, além da regeneração por tick (GDD-019 §4).
                            int regen = u.stats.ConcentrateMana;
                            u.GainMana(regen);
                            if (u.plannedConcentrations > 0) u.plannedConcentrations--;
                            _hud.LogAction($"✦ {u.unitName} concentrou +{regen} Mana", u);
                            break;
                        case ActionType.Breathe:
                            // Respirar: espelho do Concentrar para o Corpo (GDD-020 §9).
                            int breath = Mathf.Max(0, Tuning.Get().breatheStamina);
                            u.GainStamina(breath);
                            BattleFeedback.Status(u, $"+{breath} Stamina", new Color(1f, 0.86f, 0.40f));
                            _hud.LogAction($"❂ {u.unitName} respirou +{breath} Stamina", u);
                            break;
                        case ActionType.Spell:
                            spellsThisSlot.Add((u, head.Spell));
                            break;
                    }
                }

                // Cabeçalho do tick no histórico: cada ação fica sob o tick em que realmente acontece.
                bool anyAction = movesThisSlot.Count > 0 || attacksThisSlot.Count > 0 || spellsThisSlot.Count > 0;
                if (anyAction) _hud.LogRound(_globalTick);

                // Muralha bloqueia movimento: quem terminaria dentro ou atravessaria fica parado (GDD-019 §3.2).
                if (_spellField.Zones.Count > 0 && movesThisSlot.Count > 0)
                    movesThisSlot.RemoveAll(m =>
                    {
                        bool blocked = false;
                        foreach (var z in _spellField.Zones)
                        {
                            if (z.Kind != SpellEffect.Barrier) continue;
                            foreach (var c in z.Cells)
                                if (GridManager.FootprintsOverlap(m.dest, m.u.stats.Footprint, c, 1)) { blocked = true; break; }
                            if (!blocked && _spellField.BarrierOnLine(m.u.anchor, m.dest) != null) blocked = true;
                            if (blocked) break;
                        }
                        if (blocked) _hud.LogAction($"<color=#888888>x</color> {m.u.unitName}: a Muralha bloqueia o caminho", m.u);
                        return blocked;
                    });

                // Um enquadramento por tick, cobrindo todos que agem.
                if (anyAction)
                {
                    var actors = new List<Vector3>();
                    foreach (var sp in spellsThisSlot) actors.Add(sp.u.transform.position);
                    foreach (var m in movesThisSlot) { actors.Add(m.u.transform.position); actors.Add(_grid.AnchorToWorldCenter(m.dest, m.u.stats.Footprint)); }
                    foreach (var a in attacksThisSlot)
                    {
                        actors.Add(a.u.transform.position);
                        if (a.atk.TargetUnit != null && !a.atk.TargetUnit.IsDead) actors.Add(a.atk.TargetUnit.transform.position);
                    }
                    yield return FrameGroup(actors);
                }

                foreach (var u in _units) { u.pendingMoveThisTick = false; u.moveCancelledThisTick = false; }
                foreach (var m in movesThisSlot) m.u.pendingMoveThisTick = true;
                // Magias que terminam neste tick (GDD-019) resolvem ANTES das andadas do mesmo tick
                // (2026-11-14): quem ia sair do lugar toma o dano onde estava, e o visual bate com o dano. Ordem por unitId, como o resto do lockstep.
                bool anySpellFx = false;
                foreach (var (su, sp) in spellsThisSlot)
                {
                    if (su.IsDead) continue;
                    // Visual primeiro (lê posições de antes do efeito); o Salto anima de onde saiu.
                    var castFrom = su.transform.position;
                    Debug.Log($"[Magia] {su.unitName} resolve {sp.SpellId} em ({sp.TargetTile.x},{sp.TargetTile.y}) alvo={(sp.TargetUnit != null ? sp.TargetUnit.unitName : "—")} de ({su.anchor.x},{su.anchor.y})");
                    // O visual nunca pode impedir a regra de resolver: se falhar, registra e segue.
                    try { ActionFx.MarkSpell(_grid, su, sp); ActionFx.PlaySpell(su, sp, _grid); }
                    catch (System.Exception ex) { Debug.LogError($"[Magia] falha no VISUAL de {sp.SpellId}: {ex}"); }
                    // Esquivar também vale contra magia (Bloquear não, GDD-020 §8).
                    List<Vector2Int> spellCells = null;
                    if (sp.Def != null && sp.Def.area > 0)
                    {
                        spellCells = SpellResolver.PreviewCells(su, sp);   // mesmo centro da resolução
                    }
                    foreach (var tgt in SpellResolver.DamageTargets(su, sp, _units))
                        if (tgt.CanReact(ReactionKind.Dodge))
                        {
                            _reactionAttacker = su;
                            yield return React(tgt, new List<ReactionKind> { ReactionKind.Dodge }, su, spellCells);
                        }
                    List<string> spellLines;
                    try { spellLines = SpellResolver.Resolve(su, sp, _units, _grid, _spellField); }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[Magia] falha ao RESOLVER {sp.SpellId} de {su.unitName}: {ex}");
                        spellLines = new List<string> { $"<color=#ff7a6a>✕</color> {su.unitName}: {sp.SpellId} falhou (ver LOG)" };
                    }
                    foreach (var line in spellLines)
                        _hud.LogAction(line, su);
                    yield return PlayPendingDodges();   // o dano da magia entrou: agora quem esquivou anda
                    var sdef = sp.Def;
                    if (sdef != null && (sdef.effect == SpellEffect.Leap || sdef.effect == SpellEffect.Teleport) && su.Model3D != null
                        && su.transform.position != castFrom && sdef.effect == SpellEffect.Leap)
                        su.Model3D.PlayLeap(castFrom, 0.5f);
                    anySpellFx = true;
                }
                if (anySpellFx) yield return new WaitForSeconds(Tuning.Get().spellFxPause);

                // Ataques também resolvem ANTES das andadas do mesmo tick (2026-11-14), como magias e reações:
                // ordem do tick = magias → ataques → andadas. Quem ia sair toma o golpe onde estava.
                if (attacksThisSlot.Count > 0)
                    yield return ResolveAttackSlot(attacksThisSlot);

                movesThisSlot.RemoveAll(m => m.u.moveCancelledThisTick || m.u.IsDead);   // Lasca de pedra / morreu na magia ou no golpe
                var beforeAnchor = new Dictionary<Unit, Vector2Int>();
                if (movesThisSlot.Count > 0)
                {
                    foreach (var (u, _, _) in movesThisSlot)
                        beforeAnchor[u] = u.anchor;
                    yield return ResolveMovementSlot(movesThisSlot);
                }

                foreach (var (u, _, _) in movesThisSlot)
                {
                    if (u.IsDead) continue;
                    if (!beforeAnchor.TryGetValue(u, out var before)) continue;
                    int moved = Mathf.Abs(u.anchor.x - before.x) + Mathf.Abs(u.anchor.y - before.y);
                    if (moved <= 0) continue;
                    if (!burstMoves.ContainsKey(u)) burstMoves[u] = 0;
                    burstMoves[u] += moved;
                }
                // Fecha o log de avanço quando a próxima ação da fila não é movimento.
                foreach (var u in new List<Unit>(burstMoves.Keys))
                {
                    bool willMoveNext = u.queue.Count > 0 && u.queue[0].Type == ActionType.Move;
                    if (willMoveNext && t < segment - 1) continue;
                    int tiles = burstMoves[u];                    burstMoves.Remove(u);
                }



                yield return PlayPendingDodges();   // garantia: nenhuma esquiva fica sem andar na tela
                yield return AdvanceGlobalTick();
                foreach (var line in SpellResolver.TickEffects(_units, _spellField)) _hud.LogAction(line, null);

                // Interrupção (GDD-019 §9.3): a conjuração Elemental sai da fila com o resto dela,
                // a Mana já foi gasta, e a unidade decide no tick seguinte (fecha o segmento).
                bool interrupted = false;
                foreach (var u in _units)
                {
                    // Golpe de oportunidade que acertou ou bloqueio que absorveu tudo: perde o resto da fila.
                    if (u.counterInterrupted || u.staggered)
                    {
                        if (!u.IsDead && u.queue.Count > 0)
                        {
                            _hud.LogAction($"<color=#ff7a6a>✕</color> {u.unitName}: {(u.staggered ? "desequilibrado pelo bloqueio" : "interrompido pelo golpe de oportunidade")} — perde o resto da fila", u);
                            BattleFeedback.Status(u, u.staggered ? "Desequilibrado" : "Interrompido", new Color(1f, 0.48f, 0.42f));
                            u.queue.Clear();
                            interrupted = true;
                        }
                        u.counterInterrupted = false; u.staggered = false;
                        continue;
                    }
                    if (u.IsDead || u.queue.Count == 0 || !u.queue[0].Interrupted) continue;
                    var q = u.queue[0];
                    string nm = q.Spell.Def != null ? q.Spell.Def.displayName : q.Spell.SpellId;
                    _hud.LogAction($"<color=#ff7a6a>✕</color> {u.unitName}: <b>{nm}</b> interrompida ({q.DamageTaken} ≥ {SpellResolver.InterruptThreshold(u)}). Mana gasta.", u);
                    BattleFeedback.Interrupted(u, q.DamageTaken, SpellResolver.InterruptThreshold(u));
                    u.queue.Clear();
                    interrupted = true;
                }
                LogTimeline();
                // Interrompido só perde a fila; o round segue até o fim para todos.
                if (t < segment - 1) yield return IncrementWindow();

                if (!CheckAnyFactionAlive()) break;

                if (anyAction)
                    yield return new WaitForSeconds(slotPause);
            }

            // Passo bônus da IA (SP) / QUALQUER unidade (MP): valida e executa o destino
            // pré-planejado contra posições atuais. Em MP nunca pula por "u == _playerUnit"
            // — essa comparação é local a cada máquina (mesmo motivo do DoBonusStep acima).
            foreach (var u in _units)
            {
                bool skipAsLocalPlayer = !RuntimeMultiplayerSession.IsMultiplayer && u == _playerUnit;
                if (u.IsDead || !u.hasPlannedBonus || skipAsLocalPlayer) continue;
                int fp = u.stats.Footprint;
                var blockers = new List<Unit>();
                foreach (var other in _units)
                    if (other != u && !other.IsDead) blockers.Add(other);
                var reachable = _grid.GetReachableAnchors(u.anchor, 1, fp, blockers);
                reachable.RemoveAll(a => a == u.anchor);
                if (reachable.Contains(u.plannedBonusAnchor))
                    yield return u.MoveToDestination(u.plannedBonusAnchor);
                else
                    _hud.LogAction($"<color=#888888>~</color> {u.unitName}: passo extra bloqueado", u);
                u.hasPlannedBonus = false;
            }

            // ---- Win condition (SP / MP-TDM / MP-FFA) ----
            if (RuntimeMultiplayerSession.IsMultiplayer)
            {
                if (GameModes.IsCoop)
                {
                    int alive = CountAliveTeams(out int survivingTeam);
                    if (alive == 0)     { AwardCoop(cleared: false); GameOver($"Derrota! Ninguém saiu vivo da camada {Layer}."); yield break; }
                    if (alive == 1)
                    {
                        if (survivingTeam == BeastAI.TeamId)
                            { AwardCoop(cleared: false); GameOver($"Derrota! As feras venceram na camada {Layer}."); }
                        else
                            LayerCleared();
                        yield break;
                    }
                }
                else if (GameModes.IsTeamBased) // PvP Times
                {
                    int alive = CountAliveTeams(out int survivingTeam);
                    if (alive == 0)     { GameOver("Empate! Todos eliminados."); yield break; }
                    if (alive == 1)     { GameOver($"Time {survivingTeam} venceu!"); yield break; }
                }
                else // FFA
                {
                    int alive = CountAliveOwners(out ulong survivingOwner);
                    if (alive == 0)     { GameOver("Empate! Todos eliminados."); yield break; }
                    if (alive == 1)     { GameOver($"Vitória! {GetPlayerName(survivingOwner)} venceu!"); yield break; }
                }
            }
            else
            {
                if (!AnyAlive(Team.Player)) { GameOver("Derrota! Inimigos venceram."); yield break; }
                if (!AnyAlive(Team.Enemy))  { GameOver("Vitória! Todos os inimigos eliminados."); yield break; }
            }

            if (RuntimeMultiplayerSession.IsMultiplayer)
            {
                // BARREIRA DE SINCRONIZAÇÃO — antes, ActionRoutine() chamava StartRound() do
                // PRÓXIMO round aqui mesmo, ANTES de ActionRoutineMp sequer calcular/reportar
                // o hash do round ATUAL. Cada cliente avançava pro planejamento seguinte assim
                // que terminava sua própria execução local, SEM esperar os demais — se um lado
                // processasse mais rápido (ex.: sem passo bônus) e o jogador confirmasse
                // rapidamente o plano seguinte, a submissão chegava ao host antes do outro
                // jogador sequer ter entrado naquele round (exatamente o "host confirma e o
                // jogo ignora que ainda tem gente planejando" relatado). Em MP, quem decide
                // quando avançar é ActionRoutineMp -> LockstepBattleSync, DEPOIS que TODOS os
                // clientes reportarem hash (ver LockstepBattleSync.ReportHashServerRpc).
                yield break;
            }

            yield return new WaitForSeconds(Tuning.Get().roundEndPause);
            yield return StartRound();
        }

        // -------------------- COOP: DESCIDA --------------------

        /// <summary>Camada vencida: registra a marca de profundidade e pede a decisão ao anfitrião.</summary>
        private void LayerCleared()
        {
            _phase = RoundPhase.Resolving;
            _awaitingDescent = true;
            _hud.SetTimerVisible(false);
            _hud.SetConfirmVisible(false);
            AudioManager.I?.Play(AudioManager.I.sfxVictory);

            if (Layer > PlayerPrefs.GetInt(DeepestLayerKey, 0))
            {
                PlayerPrefs.SetInt(DeepestLayerKey, Layer);
                PlayerPrefs.Save();
            }
            _hud.SetPhase($"Camada {Layer} vencida");
            AwardCoop(cleared: true);
            _hud.Log($"<color=#60ff70>✦ Camada {Layer} vencida.</color> Descer tem peso; recuar não tem nenhum.");

            // Loot (GDD-024 etapa B): o host gera drops + baú e manda para todos.
            if (RuntimeMultiplayerSession.IsHost && PlacementSync.Instance != null) PlacementSync.Instance.HostGenerateLoot(Layer);

            // Cartas (GDD-021 §7): primeiro cada um escolhe 1 de 3; depois os itens do grupo; depois descer.
            Unit mine = null;
            foreach (var u in _units) if (!BeastAI.Controls(u) && IsLocalUnit(u)) { mine = u; break; }
            if (mine != null && PlacementSync.Instance != null)
            {
                int layer = Layer; uint id = UnitRegistry.GetId(mine);
                var cards = Equipment.DrawCards(layer, id, mine);
                var opts = new List<(string, System.Action)>();
                for (int i = 0; i < cards.Count; i++)
                {
                    int idx = i;
                    opts.Add((Equipment.CardText(cards[i]), () => { PlacementSync.Instance.PickCardServerRpc(layer, id, idx); ShowLootMenu(); }));
                }
                _hud.ShowChoiceTitled("RECOMPENSA", $"Camada {Layer} vencida! {mine.unitName}, escolha 1:", opts.ToArray());
                return;
            }
            ShowDescentChoice();
        }

        // ── Progressão (GDD-024): XP e dinheiro vão para o personagem do perfil desta máquina ──
        public const int XpPerBeast = 5, XpPerLayer = 15, GoldPerBeast = 3;

        /// <summary>Recompensa da camada: cada fera abatida dá XP e dinheiro × camada; vencer dá XP × camada.
        /// Linear na camada, como o custo do nível (PlayerProfile.XpToNext).</summary>
        public static (int xp, int gold) CoopReward(int layer, int beastsKilled, bool cleared)
        {
            int l = Mathf.Max(1, layer);
            return (beastsKilled * XpPerBeast * l + (cleared ? XpPerLayer * l : 0), beastsKilled * GoldPerBeast * l);
        }

        private void AwardCoop(bool cleared)
        {
            if (!GameModes.IsCoop) return;
            int killed = 0;
            foreach (var u in _units) if (BeastAI.Controls(u) && u.IsDead) killed++;
            var (xp, gold) = CoopReward(Layer, killed, cleared);
            var p = PlayerProfile.Selected;
            if (p == null || (xp <= 0 && gold <= 0)) return;
            int ups = PlayerProfile.AddXp(p, xp);
            PlayerProfile.AddGold(gold);
            PlayerProfile.Save();
            _hud.Log($"<color=#ffd76a>★</color> {p.presetName}: +{xp} XP, +{gold} moedas ({killed} fera{(killed == 1 ? "" : "s")})" +
                     (ups > 0 ? $" — <b>subiu para o nível {p.level}!</b> +{ups * PlayerProfile.PointsPerLevel} pontos para distribuir no perfil" : ""));
        }

        /// <summary>Uma carta foi aplicada (aqui ou em outra máquina): avisa no histórico e atualiza a ficha.</summary>
        public void OnCardApplied(Unit u, string what)
        {
            _hud.LogAction($"<color=#ffd76a>★</color> {u.unitName} pegou <b>{what}</b>", u);
            _hud.RefreshUnitInfo();
        }

        // ── Itens do grupo (GDD-024 etapa B): pegar (troca com o que usava), vender, deixar ──
        private enum LootView { Closed, List, Item, Mine }
        private LootView _lootView = LootView.Closed;
        private int _lootIndex;

        private Unit LocalPlayerUnit()
        {
            foreach (var u in _units) if (!BeastAI.Controls(u) && IsLocalUnit(u)) return u;
            return null;
        }

        /// <summary>Baú mudou (aqui ou em outra máquina): registra e redesenha o menu aberto.</summary>
        public void OnLootChanged(Unit who, string what)
        {
            if (who != null) _hud.LogAction($"<color=#ffd76a>⬢</color> {who.unitName} {what}", who);
            else _hud.Log($"<color=#ffd76a>⬢</color> {what}");
            _hud.RefreshUnitInfo();
            if (_lootView == LootView.Item) _lootView = LootView.List;   // o item pode ter saído do baú
            if (_lootView != LootView.Closed) ShowLootMenu(_lootView);
        }

        private void ShowLootMenu() => ShowLootMenu(LootView.List);

        private void ShowLootMenu(LootView view)
        {
            var ps = PlacementSync.Instance;
            var mine = LocalPlayerUnit();
            if (ps == null || mine == null || !_awaitingDescent) { _lootView = LootView.Closed; ShowDescentChoice(); return; }
            _lootView = view;
            uint id = UnitRegistry.GetId(mine);
            var opts = new List<(string, System.Action)>();
            string head = $"{mine.unitName} · <color=#ffd76a>{PlayerProfile.Gold} moedas</color>";

            if (view == LootView.Item && _lootIndex < ps.Stash.Count)
            {
                int idx = _lootIndex; string itemId = ps.Stash[idx];
                var it = ItemCatalog.Get(itemId);
                var cur = ItemCatalog.Get(Loot.Current(mine, it.slot));
                if (Loot.CanEquip(mine, it, out string why))
                    opts.Add(($"Equipar{(cur != null ? $"  <size=12>(o seu {cur.displayName} vai para o grupo)</size>" : "")}",
                              () => ps.LootTakeServerRpc(id, idx, itemId)));
                else opts.Add(($"<color=#9fb3d9>Não dá para equipar: {why}</color>", () => ShowLootMenu(LootView.Item)));
                opts.Add(($"Vender  <size=12>+{Loot.SellPrice(it)} moedas</size>", () => ps.LootSellServerRpc(id, idx, itemId)));
                opts.Add(("← Voltar", () => ShowLootMenu(LootView.List)));
                _hud.ShowChoiceTitled("ITENS DO GRUPO", $"{head}\n{Loot.Describe(it)}", opts.ToArray());
                return;
            }
            if (view == LootView.Mine)
            {
                foreach (var s in new[] { ItemSlot.Head, ItemSlot.Torso, ItemSlot.Legs, ItemSlot.LeftHand })
                {
                    var it = ItemCatalog.Get(Loot.Current(mine, s));
                    if (it == null) continue;
                    var slot = s;
                    opts.Add(($"Deixar {Loot.Describe(it)}", () => ps.LootLeaveServerRpc(id, (int)slot)));
                }
                opts.Add(("← Voltar", () => ShowLootMenu(LootView.List)));
                _hud.ShowChoiceTitled("DEIXAR PARA O GRUPO", $"{head}\nO item sai de você e fica no baú para outro pegar.", opts.ToArray());
                return;
            }

            for (int i = 0; i < ps.Stash.Count; i++)
            {
                int idx = i;
                opts.Add((Loot.Describe(ItemCatalog.Get(ps.Stash[i])), () => { _lootIndex = idx; ShowLootMenu(LootView.Item); }));
            }
            opts.Add(("Deixar um item meu para o grupo…", () => ShowLootMenu(LootView.Mine)));
            opts.Add(("<b>Pronto</b>", () => { _lootView = LootView.Closed; ShowDescentChoice(); }));
            _hud.ShowChoiceTitled("ITENS DO GRUPO",
                $"{head}\n{(ps.Stash.Count == 0 ? "O baú está vazio." : "Toque num item para equipar (troca) ou vender.")}", opts.ToArray());
        }

        // ── Descanso (2026-11-14): cada um soma os pontos de nível na ficha, na unidade e no perfil ──
        private bool _restedThisLayer;

        /// <summary>Todas as máquinas: o grupo descansou. Abre a ficha DESCANSO de quem joga aqui.</summary>
        public void OnRested(int layer)
        {
            _restedThisLayer = true;
            _lootView = LootView.Closed;
            _hud.Log($"<color=#8fd18f>☾ Descanso.</color> Vida e Mana cheias, caídos de pé. Agora é {DayNight.Icon(DayNight.Current)} {DayNight.Name(DayNight.Current).ToLower()}.");
            _hud.RefreshUnitInfo();
            ShowRestMenu();
        }

        private static float StatOf(UnitStatBlock s, Attr a) => a switch
        {
            Attr.STR => s.STR, Attr.VIT => s.VIT, Attr.DEX => s.DEX, Attr.AGI => s.AGI, Attr.INT => s.INT, _ => s.WIS
        };

        private static void AddStat(UnitStatBlock s, Attr a)
        {
            switch (a)
            {
                case Attr.STR: s.STR++; break; case Attr.VIT: s.VIT++; break; case Attr.DEX: s.DEX++; break;
                case Attr.AGI: s.AGI++; break; case Attr.INT: s.INT++; break; default: s.WIS++; break;
            }
        }

        private void ShowRestMenu()
        {
            var mine = LocalPlayerUnit();
            var p = PlayerProfile.Selected;
            var ps = PlacementSync.Instance;
            if (mine == null || p == null || ps == null) { ShowDescentChoice(); return; }
            int free = PlayerProfile.BudgetFor(p) - PlayerProfile.Points(p);
            var opts = new List<(string, System.Action)>();
            if (free > 0)
                foreach (Attr a in System.Enum.GetValues(typeof(Attr)))
                {
                    var attr = a;
                    if (StatOf(p.stats, attr) >= CharacterConfig.AttrMax) continue;
                    opts.Add(($"+1 {attr}  <size=12>(agora {StatOf(p.stats, attr):0})</size>", () =>
                    {
                        AddStat(p.stats, attr);       // perfil: o ponto fica no personagem
                        PlayerProfile.Save();
                        ps.RestAttrServerRpc(UnitRegistry.GetId(mine), (int)attr);   // unidade: vale já na próxima camada
                        ShowRestMenu();
                    }));
                }
            opts.Add(("<b>Pronto</b>", () => ShowDescentChoice()));
            _hud.ShowChoiceTitled("DESCANSO",
                $"{p.presetName} · nível {PlayerProfile.Level(p)} · XP {p.xp}/{PlayerProfile.XpToNext(PlayerProfile.Level(p))}\n" +
                (free > 0 ? $"<color=#ffd76a>{free} ponto{(free == 1 ? "" : "s")} para somar</color>" : "Sem pontos novos para somar."),
                opts.ToArray());
        }

        private void ShowDescentChoice()
        {
            int t = DayNight.Current, next = (t + 1) % 3;
            string text = $"Camada {Layer} vencida — {DayNight.Icon(t)} {DayNight.Name(t)}. Descer para a camada {Layer + 1}?";
            if (RuntimeMultiplayerSession.IsHost && PlacementSync.Instance != null)
            {
                var opts = new List<(string, System.Action)>
                {
                    ($"Descer  <size=12>({(_restedThisLayer ? "descansados" : "cura 30%")})</size>", () => PlacementSync.Instance.HostDescend(Layer + 1)),
                };
                if (!_restedThisLayer)
                    opts.Add(($"Descansar  <size=12>(cura tudo e somar pontos de nível; vira {DayNight.Name(next).ToLower()}{(next == DayNight.Night ? ": feras +30%" : next == DayNight.Dusk ? ": feras +15%" : "")})</size>",
                              () => PlacementSync.Instance.HostRest(Layer)));
                opts.Add(("Recuar", () => PlacementSync.Instance.HostRetreat(Layer)));
                _hud.ShowChoice(text, opts.ToArray());
            }
            else
                _hud.ShowChoice(text + "\nAguardando o anfitrião decidir...");
        }

        /// <summary>Todas as máquinas: a próxima camada começa (unidades já preparadas e feras criadas).</summary>
        public void BeginNextLayer(int layer)
        {
            Layer = layer;
            _awaitingDescent = false;
            _lootView = LootView.Closed;
            _restedThisLayer = false;
            _hud.HideReactionMenu();
            _hud.Log($"<color=#ff9a5a>▼ Camada {Layer}</color> — o Inferno está mais perto. {DayNight.Icon(DayNight.Current)} {DayNight.Name(DayNight.Current)}.");
            StartCoroutine(StartRound());
        }

        /// <summary>Todas as máquinas: o grupo recuou; a descida termina em vitória.</summary>
        public void EndDescent(int layersCleared)
        {
            _awaitingDescent = false;
            _hud.HideReactionMenu();
            GameOver($"Vitória! O grupo recuou depois de vencer {layersCleared} camada{(layersCleared > 1 ? "s" : "")}.");
        }

        // -------------------- JANELA DE INCREMENTO ENTRE TICKS --------------------
        private int _windowSeq;                 // janelas que ESTA máquina já abriu
        private int _windowClosedSeq;           // maior janela que o host já fechou
        private readonly List<(int seq, uint unit, int kind)> _pendingIncrements = new List<(int, uint, int)>();

        /// <summary>Há alguma unidade de jogador com a próxima ação elegível? (estado igual em toda máquina)</summary>
        private bool AnyIncrementEligible()
        {
            foreach (var u in _units)
            {
                if (u.IsDead || BeastAI.Controls(u)) continue;
                if (u.CanIncrementNext(0) || u.CanIncrementNext(1)) return true;
            }
            return false;
        }

        public void BufferIncrement(int seq, uint unitId, int kind) => _pendingIncrements.Add((seq, unitId, kind));

        public void OnIncrementWindowClosed(int seq) { if (seq > _windowClosedSeq) _windowClosedSeq = seq; }

        private bool IsLocalUnit(Unit u)
        {
            if (!RuntimeMultiplayerSession.IsMultiplayer) return u == _playerUnit;
            ulong localId = Unity.Netcode.NetworkManager.Singleton != null
                ? Unity.Netcode.NetworkManager.Singleton.LocalClientId : RuntimeMultiplayerSession.LocalClientId;
            return u.ownerId == localId;
        }

        /// <summary>
        /// Entre dois ticks, o dono pode incrementar a PRÓXIMA ação (1 = golpe poderoso, 2 = mirar).
        /// O host fecha a janela para todos; os incrementos chegam em buffer e só são aplicados, na
        /// mesma ordem, quando a janela fecha — assim nenhuma máquina muda a fila no meio de um tick.
        /// </summary>
        private IEnumerator IncrementWindow()
        {
            var sync = LockstepBattleSync.Instance;
            if (sync == null || !AnyIncrementEligible()) yield break;

            int seq = ++_windowSeq;
            float seconds = Mathf.Max(0.5f, Tuning.Get().reactionChoiceTime);   // mesmo tempo da reação (2026-11-13)
            if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsServer)
            {
                var eligible = new List<uint>();
                foreach (var u in _units)
                    if (!u.IsDead && !BeastAI.Controls(u) && (u.CanIncrementNext(0) || u.CanIncrementNext(1))) eligible.Add(UnitRegistry.GetId(u));
                sync.HostOpenIncrementWindow(seq, seconds, eligible);
            }

            // Quem tem unidade elegível vê o aviso.
            Unit mine = null;
            foreach (var u in _units) if (!u.IsDead && IsLocalUnit(u) && (u.CanIncrementNext(0) || u.CanIncrementNext(1))) { mine = u; break; }
            float shown = 0f;
            // Janela no estilo da reação (2026-11-13): mostra quanto o golpe dá agora e com cada
            // incremento; um clique escolhe e fecha. Antes era clicar no bloco e apertar 1/2.
            bool picked = false;
            void Submit(params int[] kinds)
            {
                if (picked) return; picked = true;
                foreach (var k in kinds)
                    if (mine.CanIncrementNext(k))
                    {
                        sync.SubmitIncrementServerRpc(UnitRegistry.GetId(mine), k);
                        _hud.LogAction($"◆ {mine.unitName}: {(k == 0 ? "golpe poderoso" : "mira")} pedido", mine);
                    }
                _hud.HideReactionMenu();
                sync.IncrementDoneServerRpc(UnitRegistry.GetId(mine));
            }
            if (mine != null)
            {
                var q = mine.queue[0];
                var T = Tuning.Get();
                int aimBonus = Mathf.RoundToInt(mine.stats.DEX * T.aimDexMultiplier);
                int Dmg(bool power, bool aim) => AttackResolver.PowerDamage(mine, power, aim ? aimBonus : 0);
                int now = Dmg(q.IsBonus, q.IsAimed);
                string tgt = q.Attack.TargetUnit != null ? $" em {q.Attack.TargetUnit.unitName}" : "";
                var opts = new List<(string, System.Action)>();
                bool canP = mine.CanIncrementNext(0), canA = mine.CanIncrementNext(1);
                string Cost(int st, bool tick) => $"<size=12><color=#9fb3d9>{st} Stamina{(tick ? " · +1 tick" : "")}</color></size>";
                if (canP) opts.Add(($"<b>Golpe poderoso</b>   ~{now} → <b>~{Dmg(true, q.IsAimed)}</b> de dano\n{Cost(Unit.IncrementCost(0), true)}", () => Submit(0)));
                if (canA) opts.Add(($"<b>Mirar</b>   ~{now} → <b>~{Dmg(q.IsBonus, true)}</b> de dano, mais acerto\n{Cost(Unit.IncrementCost(1), false)}", () => Submit(1)));
                if (canP && canA && mine.currentStamina >= Unit.IncrementCost(0) + Unit.IncrementCost(1))
                    opts.Add(($"<b>Os dois</b>   ~{now} → <b>~{Dmg(true, true)}</b> de dano\n{Cost(Unit.IncrementCost(0) + Unit.IncrementCost(1), true)}", () => Submit(0, 1)));
                opts.Add(("Não incrementar", () => Submit()));
                _hud.ShowChoiceTitled("INCREMENTAR", $"{mine.unitName} vai atacar{tgt}: ~{now} de dano.\n<size=12><color=#9fb3d9>Stamina {mine.currentStamina}</color></size>", opts.ToArray());
            }

            float guard = seconds + 6f;     // trava de segurança caso o fechamento nunca chegue
            while (_windowClosedSeq < seq && guard > 0f)
            {
                guard -= Time.deltaTime; shown += Time.deltaTime;
                if (mine != null && !picked) _hud.UpdateBonusTimer(Mathf.Max(0f, seconds - shown));
                yield return null;
            }
            if (mine != null && !picked) _hud.HideReactionMenu();
            _hud.UpdateBonusTimer(0f);

            // Aplica, na ordem de chegada (igual em todas as máquinas), o que valeu para esta janela.
            for (int i = 0; i < _pendingIncrements.Count; i++)
            {
                var p = _pendingIncrements[i];
                if (p.seq != seq) continue;
                var u = UnitRegistry.Get(p.unit);
                if (u != null && u.TryIncrementNext(p.kind))
                    _hud.LogAction($"◆ {u.unitName}: {(p.kind == 0 ? "golpe poderoso" : "mira")}", u);
            }
            _pendingIncrements.RemoveAll(p => p.seq <= seq);
            LogTimeline();
        }

        /// <summary>Avança o relógio global. A cada ticksPerRound: status, efeitos de tile e reações.</summary>
        private IEnumerator AdvanceGlobalTick()
        {
            _globalTick++;
            // Stamina volta aos poucos (GDD-016 §3).
            foreach (var u in _units)
                if (!u.IsDead && _globalTick % u.stats.StaminaRegenTicks == 0) u.GainStamina(1);
            // Mana volta por tick; a WIS acelera (GDD-019 §4).
            foreach (var u in _units)
                if (!u.IsDead && u.stats.MaxMana > 0 && _globalTick % u.stats.ManaRegenTicks == 0) u.GainMana(1);
            int per = Mathf.Max(1, Tuning.Get().ticksPerRound);
            if (_globalTick % per != 0) yield break;
            foreach (var u in _units)
                if (!u.IsDead)
                {
                    StatusEffectSystem.TickEndOfRound(u.statusEffects);
                    u.ResetReactionsForRound();
                }
            if (_tileFx != null) yield return _tileFx.EndOfRoundTick(_units, null);
        }

        /// <summary>Enquadra um grupo de posições (respeita câmera Manual).</summary>
        private IEnumerator FrameGroup(List<Vector3> positions)
        {
            if (_camCtrl == null || _camCtrl.Mode == CameraMode.Manual) yield break;
            // Enquadra TODAS as unidades vivas (mais os destinos): a câmera fica estável de um
            // tick para o outro e nada que acontece no combate sai de quadro. Antes enquadrava
            // só quem agia no tick, com zoom apertado, e perdia o resto do grupo.
            var all = new List<Vector3>(positions);
            foreach (var u in _units) if (!u.IsDead) all.Add(u.transform.position);
            if (all.Count == 0) yield break;
            Vector3 c = Vector3.zero;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var p in all)
            {
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
            }
            c = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f);
            float size = Mathf.Max(CalcZoomForPositions(all.ToArray(), 3.5f), Tuning.Get().tickFrameMinSize);
            _camCtrl.FocusOnArea(c, size);
            yield return _camCtrl.WaitUntilSettled(camMoveDuration);
        }

        /// <summary>Chamado pelo LockstepBattleSync quando TODOS os clientes confirmaram o
        /// hash do round (ou o host decidiu resync) — só então o próximo round começa.</summary>
        public void ProceedToNextRoundMp()
        {
            if (!RuntimeMultiplayerSession.IsMultiplayer) return;
            // Batalha encerrada ou camada vencida: quem decide o próximo passo é a descida.
            if (_phase == RoundPhase.GameOver || _awaitingDescent) return;
            StartCoroutine(NextRoundAfterPause());
        }

        private IEnumerator NextRoundAfterPause()
        {
            yield return new WaitForSeconds(Tuning.Get().roundEndPause);
            yield return StartRound();
        }

        /// <summary>Resolve um slot de movimento: sem colisão = simultâneo; com colisão = iniciativa decide.
        /// Após cada movimento com IsBonus, executa o passo extra se válido.</summary>
        private IEnumerator ResolveMovementSlot(List<(Unit u, Vector2Int dest, bool incr)> moves)
        {
            _phase = RoundPhase.ActionMovement;
            _hud.SetPhase($"Tick {_globalTick} — Movimento");

            var startAnchors = new Dictionary<Unit, Vector2Int>();
            foreach (var (u, _, _) in moves)
                startAnchors[u] = u.anchor;

            // Posições já ocupadas por quem NÃO se move neste slot
            var occupied = new List<(Vector2Int anchor, int fp)>();
            foreach (var u in _units)
            {
                if (u.IsDead) continue;
                if (!moves.Exists(m => m.u == u)) occupied.Add((u.anchor, u.stats.Footprint));
            }

            // Detecta colisão entre os que se movem
            bool hasCollision = false;
            foreach (var (u, dest, _) in moves)
            {
                foreach (var (oAnchor, ofp) in occupied)
                {
                    if (GridManager.FootprintsOverlap(dest, u.stats.Footprint, oAnchor, ofp))
                    { hasCollision = true; break; }
                }
                if (hasCollision) break;
            }
            if (!hasCollision)
            {
                for (int i = 0; i < moves.Count && !hasCollision; i++)
                for (int j = i + 1; j < moves.Count; j++)
                {
                    if (GridManager.FootprintsOverlap(moves[i].dest, moves[i].u.stats.Footprint,
                                                      moves[j].dest, moves[j].u.stats.Footprint))
                    { hasCollision = true; break; }
                }
            }

            var toMove = new List<(Unit u, Vector2Int dest, bool hasBonus)>();
            foreach (var (u, dest, incr) in moves)
                toMove.Add((u, dest, incr));

            if (hasCollision)
            {
                // Rola iniciativa fresca para cada um (decomposta) — guardada para exibir e ordenar
                var rolls  = new Dictionary<Unit, int>();
                var decomp = new Dictionary<Unit, (int d20, int agi, int dex, int total)>();
                foreach (var (u, _, _) in moves)
                {
                    var ini = RollInitiative(u);
                    rolls[u]  = ini.total;
                    decomp[u] = (ini.d20, ini.agiPart, ini.dexPart, ini.total);
                }
                // Determina vencedor e mostra disputa visual
                Unit moveWinner = null;
                int  topMoveRoll = int.MinValue;
                foreach (var kv in rolls) if (kv.Value > topMoveRoll) { topMoveRoll = kv.Value; moveWinner = kv.Key; }
                var moveContestants = moves.ConvertAll(m =>
                    (m.u, decomp[m.u].d20, decomp[m.u].agi, decomp[m.u].dex, decomp[m.u].total));
                yield return ShowInitiativeContest(moveContestants, moveWinner, "Colisão de movimento");

                toMove.Sort((a, b) => rolls[b.u].CompareTo(rolls[a.u]));

                var finalMoves = new List<(Unit u, Vector2Int dest, bool hasBonus)>();
                foreach (var (u, dest, hasBonus) in toMove)
                {
                    bool conflict = false;
                    foreach (var (oAnchor, ofp) in occupied)
                    {
                        if (GridManager.FootprintsOverlap(dest, u.stats.Footprint, oAnchor, ofp))
                        { conflict = true; break; }
                    }

                    if (!conflict)
                    {
                        occupied.Add((dest, u.stats.Footprint));
                        finalMoves.Add((u, dest, hasBonus));
                    }
                    else
                    {
                        var partial = FindPartialDestination(u, dest, occupied);
                        if (partial != u.anchor)
                        {
                            occupied.Add((partial, u.stats.Footprint));
                            finalMoves.Add((u, partial, hasBonus));
                            _hud.LogAction($"! {u.unitName} avanca parcialmente", u);
                        }
                        else
                        {
                            _hud.LogAction($"x {u.unitName} bloqueado", u);
                        }
                    }
                }
                toMove = finalMoves;
            }

            // 1) Movimentos normais, simultâneos
            int moving = 0;
            foreach (var (u, dest, _) in toMove)
            {
                moving++;
                StartCoroutine(MoveOneStep(u, dest, () => moving--));
            }
            while (moving > 0) yield return null;

            // ── Golpe de oportunidade (GDD-020 §8): andar DENTRO do alcance de um inimigo provoca. ──
            // Começar o passo já no alcance de alguém é o gatilho (aproximar-se de fora não é).
            // Um golpe por provocador por tick; só o inimigo de maior DEX golpeia; se acertar, a fila para.
            foreach (var (u, _, _) in toMove)
            {
                if (u.IsDead || !startAnchors.TryGetValue(u, out var fromA) || fromA == u.anchor) continue;
                yield return Provoke(u, "andou no alcance", fromA);
            }

            // Tile effects: unidades atravessaram tiles (fogo, orbe, vento)
            foreach (var (u, _, _) in toMove)
                if (!u.IsDead && startAnchors.TryGetValue(u, out var start) && _tileFx != null)
                    yield return _tileFx.OnUnitMoved(u, start, null);

            // 2) Passo Rápido: o jogador escolhe o destino do passo AGORA (fase de ação),
            //    depois do movimento. Sequencial (um de cada vez).
            foreach (var (u, _, hasBonus) in toMove)
                if (hasBonus && !u.IsDead)
                {
                    yield return DoBonusStep(u);
                    if (_tileFx != null)
                        yield return _tileFx.OnUnitMoved(u, startAnchors.TryGetValue(u, out var s) ? s : u.anchor, null);
                }
        }

        /// <summary>
        /// Encontra o tile mais próximo do destino no caminho de <c>u.anchor</c> → <c>dest</c>
        /// que não colida com nenhuma posição já ocupada. Retorna <c>u.anchor</c> se não há nenhum tile livre.
        /// </summary>
        private Vector2Int FindPartialDestination(Unit u, Vector2Int dest,
            List<(Vector2Int anchor, int fp)> occupied)
        {
            var from = u.anchor;
            int fp   = u.stats.Footprint;
            if (from == dest) return from;

            int dx    = dest.x - from.x;
            int dy    = dest.y - from.y;
            int steps = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));

            Vector2Int best = from;
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                var candidate = new Vector2Int(
                    from.x + Mathf.RoundToInt(dx * t),
                    from.y + Mathf.RoundToInt(dy * t));

                if (!_grid.IsAnchorInBounds(candidate, fp)) break;
                if (GridManager.FootprintGap(from, fp, candidate, fp) > u.EffectiveMoveStep) break;

                bool free = true;
                foreach (var (oAnchor, ofp) in occupied)
                {
                    if (GridManager.FootprintsOverlap(candidate, fp, oAnchor, ofp))
                    { free = false; break; }
                }

                if (!free) break;
                best = candidate;
            }

            return best;
        }

        /// <summary>Decompõe a rolagem de iniciativa em d20 + AGI + DEX.</summary>
        private (int d20, int agiPart, int dexPart, int total) RollInitiative(Unit u)
        {
            int d20 = BattleRng.Next(1, 21);
            int agiPart = Mathf.RoundToInt(u.stats.AGI * AttributeStats.Formulas.iniPerAGI);
            int dexPart = Mathf.RoundToInt(u.stats.DEX * AttributeStats.Formulas.iniPerDEX);
            int total = d20 + agiPart + dexPart;
            // Desempate determinístico por unitId em MP; em SP unitId = 0 então é neutro
            // (unitId é tiebreaker apenas quando totais são iguais, resolvido na ordenação)
            AudioManager.I?.Play(AudioManager.I.sfxDice);
            return (d20, agiPart, dexPart, total);
        }

        /// <summary>
        /// Zoom para enquadrar os disputantes, mostra a rolagem decomposta acima de cada um,
        /// colapsando no resultado final.
        /// </summary>
        private IEnumerator ShowInitiativeContest(
            List<(Unit u, int d20, int agi, int dex, int total)> contestants, Unit winner, string context)
        {
            // GDD-014: só o resultado, sem animação de disputa nem pausas.
            if (contestants.Count < 2 || winner == null) yield break;
        }

        /// <summary>Resolve um slot de ataque: rola iniciativa fresca, executa em ordem desc.</summary>
        private IEnumerator ResolveAttackSlot(List<(Unit u, PlannedAttack atk, bool incr, bool aimed)> attacks)
        {
            _phase = RoundPhase.ActionAttack;
            _hud.SetPhase($"Tick {_globalTick} — Ataque");

            var rolls  = new Dictionary<Unit, int>();
            var decomp = new Dictionary<Unit, (int d20, int agi, int dex, int total)>();
            foreach (var (u, _, _, _) in attacks)
            {
                var ini = RollInitiative(u);
                rolls[u]  = ini.total;
                decomp[u] = (ini.d20, ini.agiPart, ini.dexPart, ini.total);
            }

            if (attacks.Count > 1)
            {
                Unit atkWinner = null;
                int  topAtkRoll = int.MinValue;
                foreach (var kv in rolls) if (kv.Value > topAtkRoll) { topAtkRoll = kv.Value; atkWinner = kv.Key; }
                var atkContestants = attacks.ConvertAll(a =>
                    (a.u, decomp[a.u].d20, decomp[a.u].agi, decomp[a.u].dex, decomp[a.u].total));
                yield return ShowInitiativeContest(atkContestants, atkWinner, "Ataques simultâneos");
            }

            var sorted = new List<(Unit u, PlannedAttack atk, bool incr, bool aimed)>(attacks);
            // Empate desfeito por unitId: List.Sort é instável e o lockstep precisa da mesma ordem.
            sorted.Sort((a, b) =>
            {
                int c = rolls[b.u].CompareTo(rolls[a.u]);
                return c != 0 ? c : UnitRegistry.GetId(a.u).CompareTo(UnitRegistry.GetId(b.u));
            });

            // GDD-014: todas as animações do tick tocam JUNTAS; o dano resolve em seguida,
            // em ordem de iniciativa (determinístico), com a reação sim/não de cada alvo.
            var willHitMap = new Dictionary<Unit, bool>();
            int animating = 0;
            foreach (var (u, atk, incr, aimed) in sorted)
            {
                if (u.IsDead) continue;
                bool willHit = WillAttackHit(u, atk);
                willHitMap[u] = willHit;
                if (atk.Mode == AttackMode.Unit && !willHit) continue;
                Vector3 aimPos = AttackAimPos(u, atk);
                BattleLabel.CreateAttack(_cam, u.HeadWorld, u.unitName);
                animating++;
                ActionFx.MarkAttack(_grid, u, atk);      // onde na grade o golpe acontece
                StartCoroutine(RunThen(u.PlayAttackAnim(aimPos, incr), () => animating--));
            }
            while (animating > 0) yield return null;

            foreach (var (u, atk, incr, aimed) in sorted)
            {
                if (u.IsDead || !willHitMap.TryGetValue(u, out bool willHit)) continue;

                // Ataque a UNIDADE sem alcance: perdido. O PAB do Golpe Poderoso/Mirar volta.
                if (atk.Mode == AttackMode.Unit && !willHit)
                {
                    string alvo = atk.TargetUnit != null ? atk.TargetUnit.unitName : "alvo";
                    AudioManager.I?.PlayAtPoint(AudioManager.I.sfxMiss, u.transform.position);
                    BattleLabel.CreateMiss(_cam, u.HeadWorld);
                    _hud.LogAction($"<color=#888888>x</color> {u.unitName}: {alvo} fora de alcance — ataque perdido", u);
                    // Ataque perdido devolve a Stamina do golpe poderoso / mirar.
                    if (incr) u.GainStamina(Tuning.Get().staminaPowerStrike);
                    if (aimed) u.GainStamina(Tuning.Get().staminaAim);
                    _hud.RefreshUnitInfo();
                    continue;
                }

                if (incr && willHit) u.bonusDamageThisAttack = true;
                u.aimBonusThisAttack = aimed;

                Unit attackTarget = (atk.Mode == AttackMode.Unit && atk.TargetUnit != null && !atk.TargetUnit.IsDead)
                    ? atk.TargetUnit : null;

                if (incr && willHit)
                    AudioManager.I?.PlayAtPoint(AudioManager.I.sfxCritical, AttackAimPos(u, atk));

                // ── Reações de quem está na forma do golpe (GDD-020 §8) ──
                var atkEff = atk;
                _reactionAttacker = u;
                Vector2Int aimCell;
                if (atk.Mode == AttackMode.Tile) aimCell = atk.TargetTile;
                else
                {
                    var aimUnit = attackTarget ?? (atk.Mode == AttackMode.Auto ? AttackResolver.FindTargetInRange(u, _units) : null);
                    aimCell = aimUnit != null ? AttackShape.Center(aimUnit.anchor, aimUnit.stats.Footprint)
                                              : AttackShape.Center(u.anchor, u.stats.Footprint);
                }
                var inShape = AttackResolver.Preview(u, u.anchor, aimCell, atk.Mode == AttackMode.Tile, _units);
                var shapeCells = AttackResolver.CellsFor(u, aimCell);

                // Tiro à distância perto de um inimigo corpo a corpo provoca o contra-ataque; se acertar, cancela o tiro.
                bool rangedShot = false;
                foreach (var (hitU, _, _) in inShape)
                    if (GridManager.FootprintGap(u.anchor, u.stats.Footprint, hitU.anchor, hitU.stats.Footprint) > 1) { rangedShot = true; break; }
                if (rangedShot)
                {
                    yield return Provoke(u, "atirou", null);
                    if (u.counterInterrupted || u.IsDead)
                    {
                        _hud.LogAction($"<color=#ff7a6a>✕</color> {u.unitName}: o tiro foi cortado pelo contra-ataque", u);
                        continue;
                    }
                }

                // Interpor (2026-11-13): qualquer aliado que chegue colado ao atingido com uma andada.
                Unit guardUnit = null, guarded = null;
                foreach (var (hitU, _, _) in inShape)
                {
                    var gs = FindInterceptors(hitU, u);
                    if (gs.Count == 0) continue;
                    _interceptedBy = null;
                    var gl = new List<(Unit, List<ReactionKind>)>();
                    foreach (var g in gs) gl.Add((g, new List<ReactionKind> { ReactionKind.Intercept }));
                    yield return AskReaction(gl, u);
                    var who = _lastReactor;
                    if (_lastApplied == ReactionKind.Intercept && who != null)
                    {
                        yield return MoveToGuard(who, hitU);
                        yield return AfterReaction(who, ReactionKind.Intercept, u, null);
                    }
                    if (_interceptedBy != null)
                    {
                        guardUnit = _interceptedBy; guarded = hitU; _interceptedBy = null;
                        _hud.LogAction($"<color=#ffd700>🛡</color> {guardUnit.unitName}: interpôs-se por {hitU.unitName}!", guardUnit);
                        break;
                    }
                }
                foreach (var (hitU, _, _) in inShape)
                    if (!hitU.IsDead && hitU.CanReact())
                        yield return React(hitU, DefenseOptions(hitU, u), u, shapeCells);
                if (guardUnit != null && !guardUnit.IsDead && guardUnit.CanReact())
                    yield return React(guardUnit, DefenseOptions(guardUnit, u), u, shapeCells);

                // O golpe fica nos tiles de onde foi mirado: quem esquivou para fora da forma escapa.
                AttackResolver.AimLock = aimCell;
                string log = AttackResolver.ResolveAttack(u, atkEff, _units, guardUnit, guarded);
                AttackResolver.AimLock = null;
                if (AttackResolver.Last.AbsorbedAll && !u.IsDead) u.staggered = true;

                // Reação Olho certeiro (2026-11-13): o SEU golpe à distância ia errar → pode acertar.
                var missed = AttackResolver.Last.Missed;
                if (AttackResolver.Last.Ranged && missed != null && missed.Count > 0 && !u.IsDead && u.CanReact(ReactionKind.TrueAim))
                {
                    var miss = missed[0];
                    if (log != null) { _hud.LogAction(log, u); log = null; }
                    _reactionAttacker = miss;
                    yield return React(u, new List<ReactionKind> { ReactionKind.TrueAim }, miss);
                    if (_lastApplied == ReactionKind.TrueAim && !miss.IsDead)
                        log = AttackResolver.ResolveForcedHit(u, atkEff, _units, miss);
                }
                if (log != null)
                {
                    _hud.LogAction(log, u);
                    _hud.RefreshUnitInfo();
                    var Tfx = Tuning.Get();
                    if (incr && willHit)
                        _camCtrl?.Shake(Tfx.shakeDurationCrit, Tfx.shakeMagnitudeCrit);
                    else if (willHit)
                        _camCtrl?.Shake(Tfx.shakeDurationNormal, Tfx.shakeMagnitudeNormal);
                }
                yield return PlayPendingDodges();   // o dano entrou: agora quem esquivou anda
            }
            yield return new WaitForSeconds(postActionPause);
        }

        private Vector3 AttackAimPos(Unit u, PlannedAttack atk)
        {
            if (atk.Mode == AttackMode.Tile)
                return _grid.CellToWorld(atk.TargetTile);
            if (atk.TargetUnit != null && !atk.TargetUnit.IsDead)
                return atk.TargetUnit.transform.position;
            var nearest = AttackResolver.FindTargetInRange(u, _units);
            return nearest != null ? nearest.transform.position : u.transform.position;
        }

        private static IEnumerator RunThen(IEnumerator routine, System.Action onDone)
        {
            yield return routine;
            onDone();
        }

        /// <summary>Defesa preferida das feras: AGI ≥ VIT → Esquivar; senão Bloquear.</summary>
        private ReactionKind PickDefense(Unit u, Unit attacker) =>
            u.stats.AGI >= u.stats.VIT ? ReactionKind.Dodge : ReactionKind.Block;
        private Unit _reactionAttacker;
        private Unit _interceptedBy;

        /// <summary>Opções de defesa contra arma: Esquivar e Bloquear, as que a Stamina paga.</summary>
        private List<ReactionKind> DefenseOptions(Unit u, Unit attacker)
        {
            var list = new List<ReactionKind>();
            if (u.CanReact(ReactionKind.Dodge)) list.Add(ReactionKind.Dodge);
            if (u.CanReact(ReactionKind.Block)) list.Add(ReactionKind.Block);
            return list;
        }

        // ── Golpe de oportunidade (GDD-020 §8) ──
        private readonly HashSet<Unit> _provokedThisTick = new HashSet<Unit>();
        private bool _lastCounterHit;

        /// <summary>
        /// <paramref name="provoker"/> andou, reagiu ou começou a conjurar no alcance de inimigos.
        /// Um golpe por provocador por tick; só o inimigo de maior DEX (empate: menor id) golpeia.
        /// Se acertar, o provocador fica marcado como interrompido (o passo/magia/reação não segue).
        /// <paramref name="fromAnchor"/>: posição antes do passo (andar), ou a atual (null).
        /// </summary>
        private IEnumerator Provoke(Unit provoker, string why, Vector2Int? fromAnchor, Unit exclude = null)
        {
            if (provoker == null || provoker.IsDead || _provokedThisTick.Contains(provoker)) yield break;
            var at = fromAnchor ?? provoker.anchor;
            // Contra-atacar (2026-11-13): só arma corpo a corpo (alcance ≤ counterMaxRange); todos os que
            // podem recebem a janela e vale o primeiro que confirmar. Ordem: maior DEX, empate menor id.
            var cands = new List<Unit>();
            foreach (var e in _units)
            {
                if (e == provoker || e == exclude || e.IsDead || !e.IsHostileTo(provoker) || !e.CanReact(ReactionKind.CounterAttack)) continue;
                if (!CanCounterWith(e)) continue;
                if (GridManager.FootprintGap(at, provoker.stats.Footprint, e.anchor, e.stats.Footprint) > e.EffectiveAttackRange) continue;
                cands.Add(e);
            }
            if (cands.Count == 0) yield break;
            cands.Sort((a, b) => a.stats.DEX != b.stats.DEX ? b.stats.DEX.CompareTo(a.stats.DEX) : UnitRegistry.GetId(a).CompareTo(UnitRegistry.GetId(b)));
            _provokedThisTick.Add(provoker);
            _hud.LogAction($"<color=#6aa9ff>↯</color> {provoker.unitName} {why} perto de {cands[0].unitName}{(cands.Count > 1 ? $" e mais {cands.Count - 1}" : "")}: oportunidade!", cands[0]);
            _lastCounterHit = false;
            var savedAttacker = _reactionAttacker;
            _reactionAttacker = provoker;
            var list = new List<(Unit, List<ReactionKind>)>();
            foreach (var c in cands) list.Add((c, new List<ReactionKind> { ReactionKind.CounterAttack }));
            yield return AskReaction(list, provoker);
            _reactionAttacker = savedAttacker;
            if (_lastCounterHit && !provoker.IsDead) provoker.counterInterrupted = true;
        }

        /// <summary>Contra-atacar só com arma corpo a corpo (alcance da arma ≤ counterMaxRange).</summary>
        public static bool CanCounterWith(Unit u) => u != null && u.stats.AttackRange <= Tuning.Get().counterMaxRange;

        /// <summary>Reação + o golpe de oportunidade que reagir no alcance de um inimigo provoca.</summary>
        private IEnumerator React(Unit reactor, List<ReactionKind> options, Unit trigger, List<Vector2Int> dodgeCells = null)
        {
            yield return AskReaction(new List<(Unit, List<ReactionKind>)> { (reactor, options) }, trigger);
            yield return AfterReaction(_lastReactor, _lastApplied, trigger, dodgeCells);
        }

        /// <summary>O que vem depois de reagir: golpe de oportunidade de quem está perto, e a esquiva anda.</summary>
        private IEnumerator AfterReaction(Unit reactor, ReactionKind applied, Unit trigger, List<Vector2Int> dodgeCells)
        {
            if (reactor == null || applied == ReactionKind.None || applied == ReactionKind.CounterAttack || applied == ReactionKind.TrueAim) yield break;
            // Quem provocou a reação (o atacante) já está golpeando: não pune a própria vítima no mesmo tick.
            yield return Provoke(reactor, applied == ReactionKind.Dodge ? "esquivou" : "reagiu", null, trigger);
            if (reactor.counterInterrupted)
            {
                // O golpe cortou a reação: ela não acontece (a Stamina já foi).
                reactor.counterInterrupted = false;
                reactor.dodgeReactTiles = 0; reactor.blockFlat = 0;
                if (applied == ReactionKind.Intercept) _interceptedBy = null;
                _hud.LogAction($"<color=#ff7a6a>✕</color> {reactor.unitName}: a reação foi cortada", reactor);
                yield break;
            }
            if (applied == ReactionKind.Dodge && !reactor.IsDead)
                yield return DodgeMove(reactor, dodgeCells, trigger);
        }

        /// <summary>
        /// Esquivar (2026-11-13): sai andando 1 + AGI/5 tiles para o tile livre que deixa menos do corpo
        /// dentro da forma do golpe (empate: o mais perto). Sem forma (magia de alvo único), se afasta de
        /// quem lançou e a esquiva tira tiles pela AGI, como antes.
        /// </summary>
        private IEnumerator DodgeMove(Unit u, List<Vector2Int> cells, Unit from)
        {
            int n = DodgeMoveTiles(u);
            int fp = u.stats.Footprint;
            var blockers = new List<Unit>();
            foreach (var o in _units) if (o != u && !o.IsDead) blockers.Add(o);
            var reach = _grid.GetReachableAnchors(u.anchor, n, fp, blockers);
            if (!reach.Contains(u.anchor)) reach.Add(u.anchor);
            Vector2Int best = u.anchor; int bestIn = int.MaxValue, bestD = int.MaxValue, bestFar = int.MinValue;
            foreach (var a in reach)
            {
                int inside = cells != null ? AttackShape.TilesOn(cells, a, fp) : 0;
                int d = Mathf.Abs(a.x - u.anchor.x) + Mathf.Abs(a.y - u.anchor.y);
                int far = from != null ? GridManager.FootprintGap(a, fp, from.anchor, from.stats.Footprint) : 0;
                bool better = cells != null
                    ? (inside < bestIn || (inside == bestIn && (d < bestD || (d == bestD && (a.y < best.y || (a.y == best.y && a.x < best.x))))))
                    : (far > bestFar || (far == bestFar && (d < bestD || (d == bestD && (a.y < best.y || (a.y == best.y && a.x < best.x))))));
                if (better) { best = a; bestIn = inside; bestD = d; bestFar = far; }
            }
            if (cells != null) u.dodgeReactTiles = 0;    // contra a forma, quem decide é onde ele parou
            if (best == u.anchor) yield break;
            _hud.LogAction($"<color=#55ccff>↯</color> {u.unitName} esquiva para ({best.x},{best.y})", u);
            // Dano primeiro (2026-11-14): a posição lógica muda já (o golpe é calculado de lá), mas o corpo
            // fica parado; depois do dano, PlayPendingDodges anima o passo 2× mais rápido.
            _pendingDodges.Add((u, u.anchor));
            u.anchor = best;
        }

        /// <summary>Esquivas que já valeram no dano e ainda não andaram na tela.</summary>
        private readonly List<(Unit u, Vector2Int from)> _pendingDodges = new List<(Unit, Vector2Int)>();
        public const float DodgeAnimSpeed = 2f;

        /// <summary>Depois do dano: quem esquivou anda até o tile novo, todos juntos, 2× mais rápido.</summary>
        private IEnumerator PlayPendingDodges()
        {
            if (_pendingDodges.Count == 0) yield break;
            var list = new List<(Unit u, Vector2Int from)>(_pendingDodges);
            _pendingDodges.Clear();
            var running = new List<Coroutine>();
            foreach (var (u, from) in list)
                if (u != null && !u.IsDead) running.Add(u.StartCoroutine(u.AnimateMoveFrom(from, DodgeAnimSpeed)));
            foreach (var c in running) yield return c;
        }

        public static int DodgeMoveTiles(Unit u) =>
            Mathf.Max(1, 1 + Mathf.FloorToInt(u.stats.AGI / Mathf.Max(1f, Tuning.Get().dodgeAgiPerMoveTile))
                         - u.stats.OverWeight + (Equipment.HasEffect(u, ItemEffect.LightFeet) ? 1 : 0));

        private ReactionKind _lastApplied;
        private bool _reactionApplied;

        /// <summary>Prevê se o ataque vai causar dano (há alvo válido dentro do alcance).</summary>
        private bool WillAttackHit(Unit u, PlannedAttack atk)
        {
            if (atk.Mode == AttackMode.Auto)
                return AttackResolver.FindTargetInRange(u, _units) != null;

            if (atk.Mode == AttackMode.Unit)
            {
                var t = atk.TargetUnit;
                if (t == null || t.IsDead) return false;
                return GridManager.FootprintGap(u.anchor, u.stats.Footprint,
                                                t.anchor, t.stats.Footprint) <= u.stats.AttackRange;
            }

            // Mira livre: no alcance e com algum inimigo dentro da forma.
            if (GridManager.FootprintGap(u.anchor, u.stats.Footprint, atk.TargetTile, 1) > u.EffectiveAttackRange)
                return false;
            return AttackResolver.Preview(u, u.anchor, atk.TargetTile, true, _units).Count > 0;
        }


        private Vector2Int? HitAnchorFromMouse()
        {
            if (!PointerInput.Available) return null;
            Vector2 screen = PointerInput.Position;
            var world = RaycastGround(screen);
            var cell = _grid.WorldToCell(world);
            return new Vector2Int(cell.x - 1, cell.y - 1);
        }

        private Vector3 RaycastGround(Vector2 screenPos)
        {
            // Migração XY→XZ (2026-07-20): raycast no plano y=0 (ScreenToGround),
            // NÃO ScreenToWorldPoint (2D, plano Z=0 que não existe mais).
            Vector3 world;
            if (!_grid.ScreenToGround(_cam, screenPos, out world)) world = Vector3.zero;
            return world;
        }

        private IEnumerator MoveOneStep(Unit u, Vector2Int dest, System.Action onDone)
        {
            yield return u.MoveToDestination(dest);
            onDone();
        }

        // ── REAÇÕES (Ações Bônus rework) ──
        // Estado da reação em espera (preenchido por WaitReaction, consumido pelo HUD/RPC).
        private ReactionKind _pendingReactionChoice = ReactionKind.None;
        private bool _reactionResolved;

        // ── Janela de reação sincronizada (2026-11-13) ──
        private int _reactSeq, _reactClosedSeq;
        private readonly HashSet<int> _reactTimedOut = new HashSet<int>();
        private readonly List<(int seq, uint unit, int kind)> _reactChoices = new List<(int, uint, int)>();
        private Unit _lastReactor;

        public void BufferReaction(int seq, uint unitId, int kind) => _reactChoices.Add((seq, unitId, kind));
        public void OnReactionWindowClosed(int seq, bool timedOut)
        {
            _reactClosedSeq = Mathf.Max(_reactClosedSeq, seq);
            if (timedOut) _reactTimedOut.Add(seq);
        }

        /// <summary>
        /// Pergunta a reação a quem pode reagir (uma ou várias unidades). Feras decidem sozinhas; no solo
        /// a janela é local; em rede o anfitrião abre e fecha a janela para todos e só vale a escolha que
        /// ele repassar — a primeira que chegar (contra-atacar/interpor com vários candidatos). Se o tempo
        /// acabar, vale a reação padrão marcada no plano. Resultado em _lastReactor/_lastApplied.
        /// </summary>
        private IEnumerator AskReaction(List<(Unit u, List<ReactionKind> opts)> cands, Unit trigger)
        {
            _lastApplied = ReactionKind.None; _lastReactor = null; _reactionApplied = false;
            var list = new List<(Unit u, List<ReactionKind> opts)>();
            foreach (var (u, o) in cands)
            {
                if (u == null || u.IsDead || o == null) continue;
                var ok = o.FindAll(k => u.CanReact(k));          // só o que a Stamina e o limite do round pagam
                if (ok.Count > 0) list.Add((u, ok));
            }
            if (list.Count == 0) yield break;

            // IA (fera do Coop, ou inimigo no solo): decide igual em toda máquina.
            foreach (var (u, o) in list)
                if (BeastAI.Controls(u) || (!RuntimeMultiplayerSession.IsMultiplayer && u.team == Team.Enemy))
                { DoApply(u, AutoPickReaction(u, o), trigger); yield break; }

            float time = Mathf.Max(1f, Tuning.Get().reactionChoiceTime);
            var sync = LockstepBattleSync.Instance;
            bool mp = RuntimeMultiplayerSession.IsMultiplayer && sync != null;

            if (RuntimeMultiplayerSession.IsMultiplayer && sync == null)
            {
                // Rede sem a sincronização (testes, queda): sem janela; vale a reação padrão.
                foreach (var (u, o) in list) if (o.Contains(u.defaultReaction)) { DoApply(u, u.defaultReaction, trigger); break; }
                yield break;
            }
            if (!mp)
            {
                // Solo: janela local para a primeira unidade do jogador.
                var (me, myOpts) = list[0];
                ReactionKind pick = ReactionKind.None; bool done = false;
                _hud.ShowReactionMenu(me, myOpts, time, k => { pick = k; done = true; });
                float t = time;
                while (!done && t > 0f) { t -= Time.deltaTime; _hud.UpdateBonusTimer(t); yield return null; }
                _hud.HideReactionMenu(); _hud.UpdateBonusTimer(0f);
                if (!done && myOpts.Contains(me.defaultReaction)) pick = me.defaultReaction;
                if (pick != ReactionKind.None) DoApply(me, pick, trigger);
                yield break;
            }

            int seq = ++_reactSeq;
            if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsServer)
            {
                var ids = new List<uint>();
                foreach (var (u, _) in list) ids.Add(UnitRegistry.GetId(u));
                sync.HostOpenReactionWindow(seq, time, ids);
            }
            Unit mine = null; List<ReactionKind> mineOpts = null;
            foreach (var (u, o) in list) if (IsLocalUnit(u)) { mine = u; mineOpts = o; break; }
            bool picked = false;
            if (mine != null)
                _hud.ShowReactionMenu(mine, mineOpts, time, k =>
                {
                    if (picked) return; picked = true;
                    sync.SubmitReactionChoiceServerRpc(seq, UnitRegistry.GetId(mine), (int)k);
                    _hud.HideReactionMenu();
                });
            else
            {
                _hud.SetWaitingText($"Aguardando {GetPlayerName(list[0].u.ownerId)} decidir a reação...");
                _hud.ShowWaitingForPlacement();
            }
            float left = time, guard = time + 8f;
            while (_reactClosedSeq < seq && guard > 0f)
            {
                left -= Time.deltaTime; guard -= Time.deltaTime;
                if (mine != null && !picked) _hud.UpdateBonusTimer(Mathf.Max(0f, left));
                yield return null;
            }
            if (mine != null) _hud.HideReactionMenu(); else _hud.HideWaitingForPlacement();
            _hud.UpdateBonusTimer(0f);

            // Aplica o que o anfitrião repassou (igual em todas as máquinas).
            int ci = _reactChoices.FindIndex(c => c.seq == seq);
            if (ci >= 0)
            {
                var c = _reactChoices[ci];
                var ru = UnitRegistry.Get(c.unit);
                foreach (var (u, o) in list)
                    if (u == ru && o.Contains((ReactionKind)c.kind)) { DoApply(u, (ReactionKind)c.kind, trigger); break; }
            }
            else if (_reactTimedOut.Contains(seq))
            {
                foreach (var (u, o) in list)
                    if (o.Contains(u.defaultReaction)) { DoApply(u, u.defaultReaction, trigger); break; }
            }
            _reactChoices.RemoveAll(c => c.seq <= seq);
            _reactTimedOut.Remove(seq);
        }

        private void DoApply(Unit u, ReactionKind k, Unit trigger)
        {
            if (k == ReactionKind.None) return;
            ApplyReaction(u, k, trigger);
            _lastReactor = u;
        }

        /// <summary>Compatibilidade: uma unidade só.</summary>
        private IEnumerator WaitReaction(Unit reactor, List<ReactionKind> options, Unit triggerTarget)
        {
            yield return AskReaction(new List<(Unit, List<ReactionKind>)> { (reactor, options) }, triggerTarget);
        }

        /// <summary>Escolha automática da IA (inimigo SP ou fallback MP).</summary>
        /// <summary>Bloquear (GDD-020 §8): soma à defesa fixa a dureza da arma/escudo + metade da VIT (blockFlatPerVIT).</summary>
        public static int BlockAmount(Unit u)
        {
            var w = WeaponCatalog.Get(u.weaponId);
            var shield = ItemCatalog.Get(u.offhandId);
            int hard = (w != null ? w.blockHardness : 0) + (shield != null && shield.isShield ? shield.shieldHardness : 0);   // GDD-021 §2.3
            return Mathf.Max(1, Mathf.RoundToInt(u.stats.VIT * Tuning.Get().blockFlatPerVIT) + hard);
        }

        /// <summary>
        /// Interpor (2026-11-13): aliados que chegam colados ao atingido com UMA andada (alcance de
        /// movimento). Ordem: mais vida primeiro, empate pelo menor id.
        /// </summary>
        private List<Unit> FindInterceptors(Unit target, Unit attacker)
        {
            var res = new List<Unit>();
            if (target == null || attacker == null) return res;
            foreach (var a in _units)
            {
                if (a == target || a == attacker || a.IsDead || a.IsHostileTo(target) || !a.CanReact(ReactionKind.Intercept)) continue;
                if (GuardSpot(a, target) == null) continue;
                res.Add(a);
            }
            res.Sort((x, y) => x.currentHP != y.currentHP ? y.currentHP.CompareTo(x.currentHP) : UnitRegistry.GetId(x).CompareTo(UnitRegistry.GetId(y)));
            return res;
        }

        /// <summary>Tile colado ao protegido que o protetor alcança com uma andada (o mais perto), ou null.</summary>
        private Vector2Int? GuardSpot(Unit g, Unit target)
        {
            int fp = g.stats.Footprint, tfp = target.stats.Footprint;
            if (GridManager.FootprintGap(g.anchor, fp, target.anchor, tfp) <= 1) return g.anchor;
            var blockers = new List<Unit>();
            foreach (var o in _units) if (o != g && !o.IsDead) blockers.Add(o);
            Vector2Int? best = null; int bestD = int.MaxValue;
            foreach (var a in _grid.GetReachableAnchors(g.anchor, g.EffectiveMoveStep, fp, blockers))
            {
                if (GridManager.FootprintGap(a, fp, target.anchor, tfp) > 1) continue;
                int d = Mathf.Abs(a.x - g.anchor.x) + Mathf.Abs(a.y - g.anchor.y);
                if (d < bestD || (d == bestD && best.HasValue && (a.y < best.Value.y || (a.y == best.Value.y && a.x < best.Value.x)))) { best = a; bestD = d; }
            }
            return best;
        }

        private IEnumerator MoveToGuard(Unit g, Unit target)
        {
            var spot = GuardSpot(g, target);
            if (spot.HasValue && spot.Value != g.anchor) yield return g.MoveToDestination(spot.Value);
        }

        private ReactionKind AutoPickReaction(Unit reactor, List<ReactionKind> options)
        {
            // AoO sempre prioritário se disponível
            if (options.Contains(ReactionKind.CounterAttack)) return ReactionKind.CounterAttack;
            if (options.Contains(ReactionKind.Intercept)) return ReactionKind.Intercept;
            // Esquiva se AGI > VIT, senão Bloqueio
            if (options.Contains(ReactionKind.Dodge) && options.Contains(ReactionKind.Block))
                return reactor.stats.AGI >= reactor.stats.VIT ? ReactionKind.Dodge : ReactionKind.Block;
            return options[0];
        }

        /// <summary>Aplica o efeito da reação escolhida e consome o recurso.</summary>
        private void ApplyReaction(Unit reactor, ReactionKind kind, Unit triggerTarget)
        {
            if (kind == ReactionKind.None) return;
            // Em MP o RPC pode chegar de volta ao dono (que já aplicou no clique) — uma vez por espera.
            if (_reactionApplied) return;
            _reactionApplied = true;
            _lastApplied = kind;
            reactor.ConsumeReaction(kind);
            // Reagir custa 1 tick da fila (2026-11-13): a reação acontece na hora, no tick do gatilho;
            // a ação em andamento recomeça do zero, ou a fila anda 1 tick se nada estava em andamento.
            reactor.ReactionPushQueue();

            var T = Tuning.Get();
            switch (kind)
            {
                case ReactionKind.CounterAttack:
                    if (triggerTarget != null)
                    {
                        string log = AttackResolver.ResolveOpportunityStrike(reactor, triggerTarget, _units);
                        _lastCounterHit = AttackResolver.Last.AnyHit;
                        if (log != null) { _hud.LogAction(log, reactor); _hud.RefreshUnitInfo(); }
                    }
                    break;
                case ReactionKind.Dodge:
                    reactor.dodgeReactTiles = 1;   // marca a reação; quantos tiles sai é decidido no golpe (fração da AGI)
                    reactor.PlayReactionAnim(ReactionKind.Dodge);
                    BattleFeedback.Status(reactor, $"Esquiva: −{Mathf.RoundToInt(AttackResolver.DodgeFrac(reactor) * 100)}%", new Color(0.55f, 0.85f, 1f));
                    _hud.LogAction($"<color=#55ccff>↯</color> {reactor.unitName}: Esquivar! tira {Mathf.RoundToInt(AttackResolver.DodgeFrac(reactor) * 100)}% dos tiles do golpe (−{Unit.ReactionCost(kind)} Stamina)", reactor);
                    break;
                case ReactionKind.Block:
                    reactor.blockFlat = BlockAmount(reactor);
                    reactor.PlayReactionAnim(ReactionKind.Block);
                    BattleFeedback.Status(reactor, $"Escudo erguido (−{reactor.blockFlat})", new Color(1f, 0.86f, 0.40f));
                    _hud.LogAction($"<color=#ffd700>🛡</color> {reactor.unitName}: Bloquear! −{reactor.blockFlat} de dano; se absorver tudo, desequilibra (−{Unit.ReactionCost(kind)} Stamina)", reactor);
                    break;
                case ReactionKind.TrueAim:
                    BattleFeedback.Status(reactor, "Olho certeiro!", new Color(0.33f, 0.80f, 1f));
                    _hud.LogAction($"<color=#55ccff>◎</color> {reactor.unitName}: Olho certeiro! o tiro acerta (−{Unit.ReactionCost(kind)} Stamina)", reactor);
                    break;
                case ReactionKind.Intercept:
                    _interceptedBy = reactor;    // o golpe divide os tiles do protegido com quem interpôs
                    reactor.PlayReactionAnim(ReactionKind.Intercept);
                    BattleFeedback.Status(reactor, "Interpôs!", new Color(1f, 0.86f, 0.40f));
                    break;
            }
        }

        /// <summary>Chamado via RPC em MP pelo cliente remoto (aplica a escolha do dono).</summary>
        public void ApplyReactionRemote(uint reactorId, int kind)
        {
            var reactor = UnitRegistry.Get(reactorId);
            if (reactor == null) return;
            _pendingReactionChoice = (ReactionKind)kind;
            _reactionResolved = true;
            ApplyReaction(reactor, (ReactionKind)kind, _reactionAttacker);
        }

        /// <summary>
        /// Passo Rápido na FASE DE AÇÃO: depois do movimento, o jogador escolhe um tile a 1 de
        /// distância do footprint (em volta) para onde dar o passo extra. Esc / tempo esgotado pula.
        /// Para IA, valida o destino pré-planejado (plannedBonusAnchor) contra posições atuais.
        /// </summary>
        private IEnumerator DoBonusStep(Unit u)
        {
            int fp = u.stats.Footprint;
            var blockers = new List<Unit>();
            foreach (var other in _units)
                if (other != u && !other.IsDead) blockers.Add(other);

            var reachable = _grid.GetReachableAnchors(u.anchor, 1, fp, blockers);
            reachable.RemoveAll(a => a == u.anchor);

            // IA (SP) OU QUALQUER unidade em MP: valida o passo bônus pré-planejado e executa
            // se ainda válido. Em MP, "u == _playerUnit" (linha abaixo) é FALSO/VERDADEIRO de
            // forma DIFERENTE em cada cliente (cada um só "é" _playerUnit na própria máquina)
            // — se não fosse pego aqui, a MESMA unidade seria tratada como pré-planejada numa
            // tela e como "espera clique ao vivo" na outra, causando: (a) timing divergente
            // entre as telas (uma espera input real, a outra não), (b) hasPlannedBonus NUNCA
            // resetado no ramo de clique ao vivo (mais abaixo) — vazando pros próximos rounds
            // e executando de novo sem o jogador ter planejado nada, e (c) posição final
            // diferente entre os dois lados (uma usa plannedBonusAnchor, a outra o clique ao
            // vivo, que pode divergir ou nunca ocorrer) — a causa raiz do desync de posição
            // reportado (ataque acerta em uma tela e erra na outra, HP diverge, etc).
            if (RuntimeMultiplayerSession.IsMultiplayer || u != _playerUnit)
            {
                if (!u.hasPlannedBonus) yield break;
                if (reachable.Contains(u.plannedBonusAnchor))
                    yield return u.MoveToDestination(u.plannedBonusAnchor);
                else
                    _hud.LogAction($"<color=#888888>~</color> {u.unitName}: passo extra bloqueado", u);
                u.hasPlannedBonus = false;
                yield break;
            }

            if (reachable.Count == 0)
            {
                _hud.LogAction($"<color=#888888>~</color> {u.unitName}: sem espaço para o passo extra", u);
                yield break;
            }

            yield return FocusCamera(u.transform.position, zoomSize, camMoveDuration);

            // Destaque dos tiles alcançáveis (mesma cor da seleção de movimento) + cursor losango.
            _grid.HighlightAnchors(reachable, _grid.highlightColor);
            var ghost = BuildStepGhost(fp);
            _hud.ShowPrompt("Passo extra: clique num tile  •  Esc = pular");

            float timer = bonusStepTime;
            Vector2Int? chosen = null;
            bool skip = false;
            while (timer > 0f && chosen == null && !skip)
            {
                timer -= Time.deltaTime;
                _hud.UpdateBonusTimer(timer);

                var hover = HitAnchorFromMouse();
                if (hover.HasValue && reachable.Contains(hover.Value))
                {
                    ghost.transform.position = _grid.AnchorToWorldCenter(hover.Value, fp);
                    ghost.SetActive(true);
                }
                else ghost.SetActive(false);

                if (PointerInput.Available && PointerInput.ClickThisFrame)
                {
                    var h = HitAnchorFromMouse();
                    if (h.HasValue && reachable.Contains(h.Value)) chosen = h.Value;
                }
                if (PointerInput.BackThisFrame)
                    skip = true;

                yield return null;
            }

            _grid.ClearHighlight();
            if (ghost != null) Destroy(ghost);
            _hud.HidePrompt();
            _hud.UpdateBonusTimer(0f);

            if (chosen.HasValue)
            {
                AudioManager.I?.PlayAtPoint(AudioManager.I.sfxDash, u.transform.position);
                yield return u.MoveToDestination(chosen.Value);
                _hud.LogAction($"<color=#ffd700>*</color> {u.unitName}: passo extra", u);
            }
            else
            {
                _hud.LogAction($"<color=#888888>~</color> {u.unitName}: passo extra pulado", u);
            }
            // Faltava aqui (só o ramo "IA" acima resetava) — hasPlannedBonus ficava true
            // indefinidamente, vazando e re-executando em rounds seguintes.
            u.hasPlannedBonus = false;
        }

        private GameObject BuildStepGhost(int fp)
        {
            var ghost = new GameObject("StepGhost");
            var sr = ghost.AddComponent<SpriteRenderer>();
            sr.sprite = StepGhostSprite();
            sr.sortingOrder = 9000;
            float gs = fp * Tuning.Get().stepGhostScale; // mesma escala do footprint da unidade (alinha com o tile)
            ghost.transform.localScale = new Vector3(gs, gs * World3D.GroundStretch, 1f);
            // Fix (2026-07-20): marcador de chão, deitado no XZ (câmera reto de cima).
            ghost.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            ghost.SetActive(false);
            return ghost;
        }

        private static Sprite _stepGhostSprite;
        // Losango isométrico amarelo — MESMA forma/pivot do footprint da unidade, p/ alinhar ao tile.
        private static Sprite StepGhostSprite()
        {
            if (_stepGhostSprite != null) return _stepGhostSprite;
            const int W = 64, H = 32;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var col = Tuning.Get().stepGhostColor;
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float dx = Mathf.Abs(x - 32) / 32f;
                float dy = Mathf.Abs(y - 16) / 16f;
                bool inside = (dx + dy) <= 0.95f;
                tex.SetPixel(x, y, inside ? col : Color.clear);
            }
            tex.Apply();
            _stepGhostSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 32);
            return _stepGhostSprite;
        }

        private string NearestUnitName(Unit self)
        {
            Unit best = null;
            int bestGap = int.MaxValue;
            foreach (var u in _units)
            {
                if (u == self || u.IsDead) continue;
                int gap = GridManager.FootprintGap(self.anchor, self.stats.Footprint,
                                                   u.anchor, u.stats.Footprint);
                if (gap < bestGap) { bestGap = gap; best = u; }
            }
            return best != null ? best.unitName : "---";
        }

        private Unit NearestEnemy(Unit self)
        {
            Unit best    = null;
            int  bestGap = int.MaxValue;
            foreach (var u in _units)
            {
                if (u == self || u.IsDead || !u.IsHostileTo(self)) continue;
                int gap = GridManager.FootprintGap(self.anchor, self.stats.Footprint,
                                                   u.anchor,   u.stats.Footprint);
                if (gap < bestGap) { bestGap = gap; best = u; }
            }
            return best;
        }


        private void GameOver(string message)
        {
            _phase = RoundPhase.GameOver;
            bool victory = message.StartsWith("Vitória") || message.Contains("venceu");
            AudioManager.I?.Play(victory ? AudioManager.I.sfxVictory : AudioManager.I.sfxDefeat);
            _hud.SetPhase("Fim de jogo");
            _hud.ShowEndScreen(message);

            // Antes: todos eram derrubados para o menu depois de 5 s e precisavam recriar a sala,
            // refazer personagens e mapa. Agora a sala continua: o anfitrião escolhe "Jogar de
            // novo" (mesma sala, de volta ao posicionamento) e qualquer um pode sair para o menu.
            bool canRestart = CanRestart;
            _hud.SetEndActions(canRestart ? (System.Action)RequestRestart : null, LeaveToMenu,
                RuntimeMultiplayerSession.IsMultiplayer && !canRestart ? "Aguardando o anfitrião reiniciar…" : null);
        }

        /// <summary>Só o anfitrião reinicia (ele é o servidor que recarrega a cena para todos).</summary>
        private static bool CanRestart
        {
            get
            {
                var nm = Unity.Netcode.NetworkManager.Singleton;
                return nm != null && nm.IsServer;
            }
        }

        /// <summary>Reinício rápido da partida: mesma sala, de volta ao posicionamento.</summary>
        public void RequestRestart()
        {
            Debug.Log($"[RoundManager] RequestRestart (fase {_phase}, tick {_globalTick}, rodada {_round})");
            if (RoomManager.Instance != null) { RoomManager.Instance.RestartBattleServerRpc(); return; }
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (nm != null && nm.IsServer && nm.SceneManager != null)
                nm.SceneManager.LoadScene("Battle", UnityEngine.SceneManagement.LoadSceneMode.Single);
            else
                UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }

        /// <summary>Sai da sala direito (encerra a rede) e volta ao menu.</summary>
        public void LeaveToMenu()
        {
            Debug.Log("[RoundManager] LeaveToMenu");
            RuntimeMultiplayerSession.Reset();
            if (Unity.Netcode.NetworkManager.Singleton != null)
                Unity.Netcode.NetworkManager.Singleton.Shutdown();
            _ = LobbyService.LeaveAsync();
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }

        private static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = BattleRng.Next(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // ---- MP Lockstep API -----------------------------------------------

        /// <summary>
        /// Chamado pelo LockstepBattleSync após receber ExecuteRoundClientRpc.
        /// Semeia o RNG e executa a fase de ação já com planos injetados por PlanWire.
        /// </summary>
        public void RunActionPhaseMp(int seed)
        {
            if (!RuntimeMultiplayerSession.IsMultiplayer) return;
            _waitingLockstep = false;
            BattleRng.Seed(seed);
            StartCoroutine(ActionRoutineMp());
        }

        private IEnumerator ActionRoutineMp()
        {
            yield return ActionRoutine();
            // Após a fase de ação, calcula e reporta hash
            if (LockstepBattleSync.Instance != null)
            {
                ulong hash = LockstepBattleSync.ComputeStateHash(_units);
                LockstepBattleSync.Instance.ReportRoundHash(hash);
            }
        }

        // ---- Win condition helpers (usadas pelo bloco 6.6) --------------------

        /// <summary>
        /// Retorna true se ainda existem 2+ facções vivas (a batalha continua).
        /// SP: 2 teams; MP-TDM: 2+ teamIds; MP-FFA: 2+ ownerIds.
        /// </summary>
        private bool CheckAnyFactionAlive()
        {
            if (RuntimeMultiplayerSession.IsMultiplayer)
            {
                if (GameModes.IsTeamBased)
                    return CountAliveTeams(out _) >= 2;
                return CountAliveOwners(out _) >= 2;
            }
            return AnyAlive(Team.Player) && AnyAlive(Team.Enemy);
        }

        /// <summary>Retorna true se há ao menos uma unidade viva com o team dado (SP).</summary>
        private bool AnyAlive(Team team)
        {
            foreach (var u in _units) if (u.team == team && !u.IsDead) return true;
            return false;
        }

        /// <summary>MP-TDM: retorna o número de teamIds com ao menos uma unidade viva.</summary>
        private int CountAliveTeams(out int survivingTeamId)
        {
            var alive = new HashSet<int>();
            survivingTeamId = -1;
            foreach (var u in _units)
                if (!u.IsDead) alive.Add(u.teamId);
            foreach (var id in alive) survivingTeamId = id;
            return alive.Count;
        }

        /// <summary>MP-FFA: retorna o número de ownerIds com ao menos uma unidade viva.</summary>
        private int CountAliveOwners(out ulong survivingOwnerId)
        {
            var alive = new HashSet<ulong>();
            survivingOwnerId = 0;
            foreach (var u in _units)
                if (!u.IsDead) alive.Add(u.ownerId);
            foreach (var id in alive) survivingOwnerId = id;
            return alive.Count;
        }

        private string GetPlayerName(ulong ownerId)
        {
            if (RoomManager.Instance == null) return $"Jogador({ownerId})";
            var slots = RoomManager.Instance.Slots;
            for (int i = 0; i < slots.Count; i++)
                if (slots[i].ClientId == ownerId) return slots[i].PlayerName.ToString();
            return $"Jogador({ownerId})";
        }
    }
}
