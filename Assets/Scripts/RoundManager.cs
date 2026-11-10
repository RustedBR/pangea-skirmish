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
        public float planningTime = 15f;
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

        // ---- Lockstep MP (Fase 6) ----
        private bool _waitingLockstep; // true enquanto aguarda ExecuteRoundClientRpc

        private RoundPhase _phase = RoundPhase.Resolving;
        private float _timer;
        private int _round;
        private bool _gracePeriodUsed;
        /// <summary>Tick global da batalha (GDD-014). Status/tiles/reações renovam a cada ticksPerRound.</summary>
        private int _globalTick;

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

        public void Setup(GridManager grid, PlanningController planner, BattleHUD hud,
                          Camera cam, CameraController camCtrl, List<Unit> units, Unit playerUnit,
                          TileEffectManager tileFx = null)
        {
            _grid = grid;
            _planner = planner;
            _hud = hud;
            _cam = cam;
            _camCtrl = camCtrl;
            _units = units;
            _playerUnit = playerUnit;
            _tileFx = tileFx;
            _camBaseSize = cam.orthographicSize;
            _planner.SetHUD(hud);
            _hud.BindConfirm(ConfirmPlan);
            // Registro idempotente: evita handler duplicado se Setup rodar 2x
            // (causaria rótulo de dano em duplicata no MP).
            Unit.OnDamageTaken -= SpawnDamageLabel;
            Unit.OnDamageTaken += SpawnDamageLabel;
            _screenFlash = ScreenFlash.Create(hud.Document);

            // Fallback loopback: em modo local (criação de conteúdo offline), o
            // LockstepBattleSync não é spawnado pela rede (CheckAllPlaced só roda em MP
            // real). Sem isto, Instance fica null → timeout 8s → plano perdido → trava.
            // Criamos o prefab localmente (sem Spawn de rede) para o lockstep funcionar solo.
            if (RuntimeMultiplayerSession.IsLocalContentSession && LockstepBattleSync.Instance == null)
            {
                var prefab = Resources.Load<GameObject>("Net/LockstepBattleSyncNet");
                if (prefab != null)
                {
                    var go = Instantiate(prefab);
                    DontDestroyOnLoad(go);
                    var lbs = go.GetComponent<LockstepBattleSync>();
                    if (lbs != null) lbs.Init(this, units);
                    Debug.Log("[RoundManager] LockstepBattleSync criado localmente (fallback loopback).");
                }
                else Debug.LogError("[RoundManager] prefab Net/LockstepBattleSyncNet não encontrado para fallback loopback!");
            }

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
            _hud.LogRound(_round);
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
            var parts = new List<string>();
            foreach (var u in _units)
            {
                if (u.IsDead) continue;
                bool mine = RuntimeMultiplayerSession.IsMultiplayer ? u.ownerId == localId : u == _playerUnit;
                if (!u.IsBusy) { parts.Add($"{u.unitName}: <color=#60ff70>livre</color>"); continue; }
                if (!mine) { parts.Add($"{u.unitName}: ocupada"); continue; }
                var q = new List<string>();
                foreach (var a in u.queue) q.Add($"{ActionShort(a.Type)}{a.Remaining}");
                parts.Add($"{u.unitName}: [{string.Join(" ", q)}]");
            }
            _hud.Log($"<color=#aad4ff>⏱ Tick {_globalTick}</color>  " + string.Join("  •  ", parts));
        }

        private static string ActionShort(ActionType t) =>
            t == ActionType.Move ? "Mov" : t == ActionType.Attack ? "Atq" : t == ActionType.Concentrate ? "Con" : "Esp";

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
                    _hud.SetConfirmVisible(false);
                    _hud.LogAction($"<color=#888888>…</color> {controlled.unitName} ocupada ({controlled.QueueTicksRemaining} tick(s) na fila)", controlled);
                    _timer = planningTime;
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
            if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
            if (_planner != null && _planner.IsPicking) return; // o planner usa o clique
            if (_hud != null && _hud.IsPromptVisible) return;    // escolha de passo bônus
            if (PointerOverUI()) return;

            var u = UnitUnderMouse();
            if (u != null) _hud.ShowUnitInfo(u);
        }

        private bool PointerOverUI()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            return es != null && es.IsPointerOverGameObject();
        }

        private Unit UnitUnderMouse()
        {
            if (_cam == null || Mouse.current == null) return null;
            Vector2 screen = Mouse.current.position.ReadValue();
            var world = RaycastGround(screen);
            // Seleção por collider 3D (migração XY→XZ): raycast do mouse contra as unidades.
            return Unit.PickAtWorld(_cam, screen);
        }

        private IEnumerator AutoConfirmBusy()
        {
            yield return new WaitForSeconds(0.6f);
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

            // 1) Unidades livres comprometem o plano na fila (plano vazio = Esperar).
            //    Ordem por unitId: mesma em todas as máquinas (lockstep).
            //    As feras (Coop) planejam aqui, em toda máquina, com o mesmo estado (GDD-015 §4).
            foreach (var u in _units)
            {
                if (u.IsDead || u.IsBusy) continue;
                if (BeastAI.Controls(u)) BeastAI.Plan(u, _units, _grid);
                u.BuildQueueFromPlan();
            }

            // 2) Próximo ponto de decisão = menor fila restante entre os vivos.
            int segment = int.MaxValue;
            foreach (var u in _units)
                if (!u.IsDead) segment = Mathf.Min(segment, Mathf.Max(1, u.QueueTicksRemaining));
            if (segment == int.MaxValue) segment = 1;

            var burstMoves = new Dictionary<Unit, int>();

            for (int t = 0; t < segment; t++)
            {
                _hud.SetPhase($"Tick {_globalTick}");

                // Avança a ação da frente de cada fila; coleta as que TERMINAM neste tick.
                var movesThisSlot   = new List<(Unit u, Vector2Int dest, bool incr)>();
                var attacksThisSlot = new List<(Unit u, PlannedAttack atk, bool incr, bool aimed)>();
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
                            int regen = u.stats.ManaRegen;
                            u.currentMana = Mathf.Min(u.currentMana + regen, u.stats.MaxMana);
                            if (u.plannedConcentrations > 0) u.plannedConcentrations--;
                            _hud.LogAction($"✦ {u.unitName} concentrou +{regen} mana", u);
                            break;
                    }
                }

                // Um enquadramento por tick, cobrindo todos que agem.
                if (movesThisSlot.Count > 0 || attacksThisSlot.Count > 0)
                {
                    var actors = new List<Vector3>();
                    foreach (var m in movesThisSlot) { actors.Add(m.u.transform.position); actors.Add(_grid.AnchorToWorldCenter(m.dest, m.u.stats.Footprint)); }
                    foreach (var a in attacksThisSlot)
                    {
                        actors.Add(a.u.transform.position);
                        if (a.atk.TargetUnit != null && !a.atk.TargetUnit.IsDead) actors.Add(a.atk.TargetUnit.transform.position);
                    }
                    yield return FrameGroup(actors);
                }

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
                    int tiles = burstMoves[u];
                    _hud.LogAction($">> {u.unitName} avancou {tiles} tile{(tiles > 1 ? "s" : "")} -> {NearestUnitName(u)}", u);
                    burstMoves.Remove(u);
                }

                if (attacksThisSlot.Count > 0)
                    yield return ResolveAttackSlot(attacksThisSlot);

                yield return AdvanceGlobalTick();

                if (!CheckAnyFactionAlive()) break;

                if (movesThisSlot.Count > 0 || attacksThisSlot.Count > 0)
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
                    if (alive == 0)     { GameOver("Derrota! Ninguém saiu vivo da camada."); yield break; }
                    if (alive == 1)
                    {
                        GameOver(survivingTeam == BeastAI.TeamId
                            ? "Derrota! As feras venceram."
                            : "Vitória! A camada foi vencida.");
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

        /// <summary>Avança o relógio global. A cada ticksPerRound: status, efeitos de tile e reações.</summary>
        private IEnumerator AdvanceGlobalTick()
        {
            _globalTick++;
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
            if (_camCtrl == null || positions.Count == 0 || _camCtrl.Mode == CameraMode.Manual) yield break;
            Vector3 c = Vector3.zero;
            foreach (var p in positions) c += p;
            c /= positions.Count;
            _camCtrl.FocusOnArea(c, CalcZoomForPositions(positions.ToArray()));
            yield return _camCtrl.WaitUntilSettled(camMoveDuration);
        }

        /// <summary>Chamado pelo LockstepBattleSync quando TODOS os clientes confirmaram o
        /// hash do round (ou o host decidiu resync) — só então o próximo round começa.</summary>
        public void ProceedToNextRoundMp()
        {
            if (!RuntimeMultiplayerSession.IsMultiplayer) return;
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

            // ── TRIGGER DE REAÇÃO: Contra-ataque (AoO) ──
            // Nova regra (feedback do Marcus): o gatilho é MOVER DENTRO do alcance melee
            // de um inimigo e sair (ou passar por ele) sem atacá-lo — não "já estava
            // dentro e saiu". Dispara se a unidade 'u' passou por algum tile em alcance
            // melee de 'e' durante o movimento e não tem ataque planejado contra 'e'.
            foreach (var (u, _, _) in toMove)
            {
                if (u.IsDead) continue;
                int ufp = u.stats.Footprint;

                // Tiles percorridos NESTE tick (origem → destino). Com ticks, cada waypoint
                // é um passo próprio; usar o plannedPath inteiro re-dispararia a cada tick.
                var traversed = new List<Vector2Int>();
                if (startAnchors.TryGetValue(u, out var fromA)) traversed.Add(fromA);
                traversed.Add(u.anchor);

                foreach (var e in _units)
                {
                    if (e == u || e.IsDead || !e.IsHostileTo(u)) continue;
                    if (!e.CanReact()) continue;

                    // 'u' vai atacar 'e' neste round? Se sim, não há AoO (já é o alvo).
                    bool willAttackE = u.plannedAttacks.Exists(a => a.TargetUnit == e);
                    if (willAttackE) continue;

                    // Passou por dentro do alcance melee de 'e' em algum tile do trajeto?
                    bool enteredReach = traversed.Exists(t =>
                        GridManager.FootprintGap(t, ufp, e.anchor, e.stats.Footprint) <= e.stats.AttackRange);

                    if (enteredReach)
                    {
                        _hud.LogAction($"<color=#6aa9ff>↯</color> {e.unitName}: oportunidade contra {u.unitName}!", e);
                        yield return WaitReaction(e, new List<ReactionKind> { ReactionKind.CounterAttack }, u);
                    }
                }
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
                if (GridManager.FootprintGap(from, fp, candidate, fp) > u.stats.MoveBudget) break;

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
            _hud.LogAction(
                $"<color=#aad4ff>INI {context}:</color> " +
                string.Join(" vs ", contestants.ConvertAll(c => $"{c.u.unitName}({c.total})")) +
                $" -> <color=#60ff70>{winner.unitName}</color> vence",
                winner);
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
                StartCoroutine(RunThen(u.PlayAttackAnim(aimPos), () => animating--));
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
                    if (incr) u.remainingBAP++;
                    if (aimed) u.remainingBAP++;
                    _hud.RefreshUnitInfo();
                    continue;
                }

                if (incr && willHit) u.bonusDamageThisAttack = true;
                u.aimBonusThisAttack = aimed;

                Unit attackTarget = (atk.Mode == AttackMode.Unit && atk.TargetUnit != null && !atk.TargetUnit.IsDead)
                    ? atk.TargetUnit : null;

                if (incr && willHit)
                    AudioManager.I?.PlayAtPoint(AudioManager.I.sfxCritical, AttackAimPos(u, atk));

                // ── Reação rápida sim/não (alvo sob ataque) ──
                if (attackTarget != null && attackTarget.CanReact())
                    yield return WaitReaction(attackTarget,
                        new List<ReactionKind> { PreferredDefense(attackTarget) }, null);

                string log = AttackResolver.ResolveAttack(u, atk, _units);
                if (attackTarget != null) { attackTarget.dodgeReactBonus = 0f; attackTarget.blockReduction = 0f; }
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
            }
            yield return new WaitForSeconds(postActionPause);
        }

        private Vector3 AttackAimPos(Unit u, PlannedAttack atk)
        {
            if (atk.Mode == AttackMode.Tile)
                return _grid.AnchorToWorldCenter(atk.TargetTile);
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

        /// <summary>Defesa preferida da unidade (postura por atributo): AGI ≥ VIT → Esquiva; senão Bloqueio.</summary>
        private static ReactionKind PreferredDefense(Unit u) =>
            u.stats.AGI >= u.stats.VIT ? ReactionKind.Dodge : ReactionKind.Block;

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

            // Tile: precisa estar no alcance E ter um inimigo ocupando o tile
            int afp = u.stats.Footprint;
            if (GridManager.FootprintGap(u.anchor, afp, atk.TargetTile, afp) > u.stats.AttackRange)
                return false;
            foreach (var other in _units)
            {
                if (other == u || other.IsDead || !other.IsHostileTo(u)) continue;
                if (GridManager.FootprintsOverlap(other.anchor, other.stats.Footprint, atk.TargetTile, afp))
                    return true;
            }
            return false;
        }


        private Vector2Int? HitAnchorFromMouse()
        {
            if (Mouse.current == null) return null;
            Vector2 screen = Mouse.current.position.ReadValue();
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

        /// <summary>
        /// Pausa a fase de ação e espera a DECISÃO do jogador (ou RPC em MP) sobre uma reação.
        /// triggerTarget: unidade que causou o gatilho (ex.: quem se moveu, para AoO).
        /// </summary>
        private IEnumerator WaitReaction(Unit reactor, List<ReactionKind> options, Unit triggerTarget)
        {
            if (reactor == null || !reactor.CanReact() || options == null || options.Count == 0)
                yield break;

            _pendingReactionChoice = ReactionKind.None;
            _reactionResolved = false;
            bool submitted = false; // dono já enviou RPC (MP)

            // Em MP, só o cliente DONO da unidade reatora abre o menu interativo.
            // O outro cliente fica aguardando o RPC de decisão (aplica igual).
            bool isOwner = !RuntimeMultiplayerSession.IsMultiplayer
                || reactor.ownerId == RuntimeMultiplayerSession.LocalClientId;

            // IA decide automaticamente: inimigo em SP, ou fera do Coop (igual em toda máquina).
            if (BeastAI.Controls(reactor)
                || (!RuntimeMultiplayerSession.IsMultiplayer && reactor.team == Team.Enemy))
            {
                var aiChoice = AutoPickReaction(reactor, options);
                ApplyReaction(reactor, aiChoice, triggerTarget);
                yield break;
            }

            // SÓ o dono da unidade reatora abre o menu interativo. O outro cliente vê
            // apenas o indicador "Aguardando {dono} decidir..." (sem botões clicáveis),
            // evitando que qualquer jogador reaja a uma reação que não é sua.
            if (isOwner)
            {
                _hud.ShowReactionMenu(reactor, options, Tuning.Get().reactionChoiceTime,
                    choice =>
                    {
                        _pendingReactionChoice = choice;
                        _reactionResolved = true;
                        // Em MP, o dono manda a escolha pros outros clientes
                        if (RuntimeMultiplayerSession.IsMultiplayer)
                        {
                            submitted = true;
                            LockstepBattleSync.Instance?.SubmitReactionServerRpc(
                                UnitRegistry.GetId(reactor), (int)choice);
                        }
                    });
            }
            else if (RuntimeMultiplayerSession.IsMultiplayer)
            {
                // Não-dono: aguarda o RPC de decisão (ApplyReactionRemote seta _reactionResolved).
                // Mostra indicador "Aguardando {dono} decidir..." para o outro jogador saber quem está na decisão.
                string who = GetPlayerName(reactor.ownerId);
                _hud.SetWaitingText($"Aguardando {who} decidir...");
                _hud.ShowWaitingForPlacement();
            }
            float timer = Tuning.Get().reactionChoiceTime;
            while (!_reactionResolved && timer > 0f)
            {
                timer -= Time.deltaTime;
                _hud.UpdateBonusTimer(timer);
                yield return null;
            }

            if (RuntimeMultiplayerSession.IsMultiplayer && !isOwner)
                _hud.HideWaitingForPlacement();

            _hud.HideReactionMenu();
            _hud.UpdateBonusTimer(0f);

            var final = _reactionResolved ? _pendingReactionChoice : ReactionKind.None;

            // Se o dono em MP não clicou e o tempo esgotou, ainda assim avisa os outros (senão trava)
            if (RuntimeMultiplayerSession.IsMultiplayer && isOwner && !submitted)
                LockstepBattleSync.Instance?.SubmitReactionServerRpc(
                    UnitRegistry.GetId(reactor), (int)ReactionKind.None);

            // Em SP (ou dono em MP antes do echo), aplica localmente.
            // Em MP non-dono, o RPC (ApplyReactionRemote) já aplicou — não re-aplica (guard em ApplyReaction).
            if (!RuntimeMultiplayerSession.IsMultiplayer || isOwner)
            {
                if (final != ReactionKind.None)
                    ApplyReaction(reactor, final, triggerTarget);
                else
                    _hud.LogAction($"<color=#888888>~</color> {reactor.unitName}: reação não usada", reactor);
            }
        }

        /// <summary>Escolha automática da IA (inimigo SP ou fallback MP).</summary>
        private ReactionKind AutoPickReaction(Unit reactor, List<ReactionKind> options)
        {
            // AoO sempre prioritário se disponível
            if (options.Contains(ReactionKind.CounterAttack)) return ReactionKind.CounterAttack;
            // Esquiva se AGI > VIT, senão Bloqueio
            if (options.Contains(ReactionKind.Dodge) && options.Contains(ReactionKind.Block))
                return reactor.stats.AGI >= reactor.stats.VIT ? ReactionKind.Dodge : ReactionKind.Block;
            return options[0];
        }

        /// <summary>Aplica o efeito da reação escolhida e consome o recurso.</summary>
        private void ApplyReaction(Unit reactor, ReactionKind kind, Unit triggerTarget)
        {
            if (kind == ReactionKind.None) return;
            // Em MP o RPC pode chegar de volta ao dono (que já aplicou no clique) — ignora se já consumiu.
            if (reactor.remainingReactions <= 0) return;
            reactor.ConsumeReaction();
            // GDD-014: reagir custa tempo — empurra a fila do reator.
            reactor.DelayQueue(Tuning.Get().reactionTickCost);

            var T = Tuning.Get();
            switch (kind)
            {
                case ReactionKind.CounterAttack:
                    if (triggerTarget != null)
                    {
                        string log = AttackResolver.ResolveOpportunityStrike(reactor, triggerTarget, _units);
                        if (log != null) { _hud.LogAction(log, reactor); _hud.RefreshUnitInfo(); }
                    }
                    break;
                case ReactionKind.Dodge:
                    reactor.dodgeReactBonus = Mathf.Clamp01(reactor.stats.AGI * T.dodgeReactionPerAGI);
                    _hud.LogAction($"<color=#55ccff>↯</color> {reactor.unitName}: Esquiva! (+" +
                        Mathf.RoundToInt(reactor.dodgeReactBonus * 100) + "% dodge)", reactor);
                    break;
                case ReactionKind.Block:
                    reactor.blockReduction = Mathf.Min(T.maxBlockReduction,
                        reactor.stats.VIT * T.blockReductionPerVIT);
                    _hud.LogAction($"<color=#ffd700>🛡</color> {reactor.unitName}: Bloqueio! (-" +
                        Mathf.RoundToInt(reactor.blockReduction * 100) + "% dano)", reactor);
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
            ApplyReaction(reactor, (ReactionKind)kind, null);
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

                if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                {
                    var h = HitAnchorFromMouse();
                    if (h.HasValue && reachable.Contains(h.Value)) chosen = h.Value;
                }
                if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
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

            // MP: volta ao menu após delay
            // Decisão de design: sem lobby pós-batalha na v1 — reset limpo e volta ao MainMenu.
            // Host avança a fase; todos recebem o GameOver via ShowEndScreen + auto-retorno.
            if (RuntimeMultiplayerSession.IsMultiplayer)
                StartCoroutine(MpReturnToMenu());
        }

        private IEnumerator MpReturnToMenu()
        {
            yield return new WaitForSeconds(5f);
            RuntimeMultiplayerSession.Reset();
            if (Unity.Netcode.NetworkManager.Singleton != null)
                Unity.Netcode.NetworkManager.Singleton.Shutdown();
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
