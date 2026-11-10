using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PangeaSkirmish
{
    public enum PlanMode { Idle, MovePicking, AttackUnitPicking, AttackTilePicking, QuickStepPicking }

    /// <summary>
    /// Controla o planejamento do jogador (fase de planning).
    /// Cada PA pode ser gasto em Mover (encadeado, com ghost por waypoint) ou Atacar.
    /// Ghosts brancos marcam cada waypoint; o cursor mostra o próximo destino possível.
    /// </summary>
    public class PlanningController : MonoBehaviour
    {
        private GridManager _grid;
        private Camera _cam;
        private List<Unit> _allUnits;
        private Unit _controlled;
        private BattleHUD _hud;

        private List<Vector2Int> _reachable        = new List<Vector2Int>();
        private List<Vector2Int> _attackable       = new List<Vector2Int>();
        private List<Unit>       _targetableEnemies = new List<Unit>();
        private GameObject _cursorGhost;
        private readonly List<GameObject> _pathGhosts = new List<GameObject>();
        private readonly List<BattleLabel> _ghostLabels = new List<BattleLabel>();
        private PlanMode _mode  = PlanMode.Idle;
        private bool     _active;
        private Unit     _lastTargetedUnit = null; // rastreia unidade em destaque p/ limpar após sair do hover

        // Range preview (alcance destacado no grid). _baseRange é o range da unidade
        // controlada (mostrado em Idle e nos pickings de unidade); ao passar o mouse
        // num inimigo mostramos o range DELE em vermelho e restauramos o base ao sair.
        private readonly List<Vector2Int> _baseRange = new List<Vector2Int>();
        private bool  _hasBaseRange;
        private Color _baseRangeColor;
        private Unit  _rangeHoverUnit;
        private readonly List<GameObject> _rangeGhosts = new List<GameObject>(); // overlay de range (não tinge o tile)

        /// <summary>True quando o clique do mouse está reservado para escolher destino/alvo.</summary>
        public bool IsPicking => _active && _mode != PlanMode.Idle;

        public void Setup(GridManager grid, Camera cam, List<Unit> allUnits)
        {
            _grid     = grid;
            _cam      = cam;
            _allUnits = allUnits;
        }

        public void SetHUD(BattleHUD hud) { _hud = hud; }

        // ── Modo Intenção (GDD-016 §6) ──
        private enum ArmedIntent { None, Charge, HitAndRun, Retreat }
        private ArmedIntent _armed = ArmedIntent.None;
        private Unit _previewUnit;            // alvo da prévia atual (segundo clique nele confirma)
        private Vector2Int? _previewCell;     // idem, para clique no chão
        /// <summary>Pedido de confirmação do plano (Enter, Espaço ou segundo clique no mesmo alvo).</summary>
        public System.Action OnConfirmRequested;

        private void OnEnable()  => GridManager.OnViewOrientationChanged += OnViewOrientationChanged;
        private void OnDisable() => GridManager.OnViewOrientationChanged -= OnViewOrientationChanged;

        /// <summary>
        /// Girar a vista reindexa o grid: os ghosts já se reposicionam sozinhos
        /// (GridAnchored), mas os rótulos numerados da sequência derivam da
        /// posição dos ghosts, então precisam ser recalculados depois deles.
        /// </summary>
        private void OnViewOrientationChanged(int _)
        {
            if (!_active) return;
            RefreshSequenceVisuals();
        }

        public void Begin(Unit controlled)
        {
            _controlled = controlled;
            _active     = controlled != null && !controlled.IsDead;
            if (!_active) return;

            _mode = PlanMode.Idle;
            _controlled.SetSelected(true);
            foreach (var u in _allUnits) u.SetAttackMarked(false); // limpa marcas de alvo do round anterior
            EnsureCursorGhost();
            _cursorGhost.SetActive(false);

            _hud.SetActionBarVisible(true, controlled);
            _hud.BindMove(OnMoveClick);
            _hud.BindAttackUnit(OnAttackUnitClick);
            _hud.BindAttackTile(OnAttackTileClick);
            _hud.BindPowerStrike(OnPowerStrikeClick);
            _hud.BindQuickStep(OnQuickStepClick);
            _hud.BindConcentrate(OnConcentrateClick);
            _hud.BindIncrement(OnIncrementClick);
            _hud.BindAim(OnAimClick);
            _hud.BindUndo(OnUndoClick);
            _hud.BindClear(OnClearClick);
            _hud.OnSeqChipToggled = OnSeqChipToggled;
            _armed = ArmedIntent.None; _previewUnit = null; _previewCell = null;
            ShowIntentHint();
            SyncButtonStates();
            RefreshSequenceVisuals();
            ShowSelfAttackRange(); // range visível assim que a unidade é selecionada
        }

        public void End()
        {
            _active = false;
            _mode   = PlanMode.Idle;
            if (_hud != null) _hud.HidePrompt();
            if (_controlled != null) _controlled.SetSelected(false);
            if (_lastTargetedUnit != null) _lastTargetedUnit.SetTargeted(false);
            _lastTargetedUnit = null;
            foreach (var u in _allUnits) u.SetAttackMarked(false); // limpa marcas de alvo confirmadas
            ClearBaseRange();
            _grid.ClearHighlight();
            HideCursorGhost();
            ClearPathGhosts();
            _hud.SetActionBarVisible(false);
            _hud.HideActionSequence();
        }

        // ------------------------------------------------------------------ BOTÕES

        private void OnMoveClick()
        {
            if (!_active || _controlled == null) return;

            if (_mode == PlanMode.MovePicking)
            {
                // cancela o pick atual sem consumir PA
                SetMode(PlanMode.Idle);
                HideCursorGhost();
                return;
            }

            if (_controlled.CanFit(ActionType.Move))
            {
                EnterMovePicking();
                return;
            }

            // sem PA restante: cancela todos os movimentos e devolve os PA
            if (_controlled.plannedMoveCount > 0)
                CancelAllMoves();
        }

        private void OnAttackUnitClick()
        {
            if (!_active || _controlled == null) return;

            if (_mode == PlanMode.AttackUnitPicking)
            {
                SetMode(PlanMode.Idle);
                return;
            }

            if (_controlled.CanFit(ActionType.Attack))
            {
                EnterAttackUnitPicking();
                return;
            }
        }

        private void OnAttackTileClick()
        {
            if (!_active || _controlled == null) return;

            if (_mode == PlanMode.AttackTilePicking)
            {
                SetMode(PlanMode.Idle);
                return;
            }

            if (_controlled.CanFit(ActionType.Attack))
            {
                EnterAttackTilePicking();
                return;
            }
        }

        private void OnPowerStrikeClick()
        {
            if (!_active || _controlled == null) return;

            // Verificar se há um ataque selecionado
            int idx = _hud.SelectedSeqIndex;
            if (idx < 0 || idx >= _controlled.actionSequence.Count)
            {
                Debug.Log("Selecione um ataque planejado para usar Golpe Poderoso");
                return;
            }

            var action = _controlled.actionSequence[idx];
            if (action.Type != ActionType.Attack)
            {
                Debug.Log("Golpe Poderoso só funciona em ataques");
                return;
            }

            // Toggle: aplicar ou remover bônus
            if (action.IsBonus)
            {
                // Remover bônus: devolve PAB
                _controlled.actionSequence[idx] = new ScheduledAction
                {
                    Type = action.Type,
                    Index = action.Index,
                    IsBonus = false,
                    IsAimed = action.IsAimed,
                    BonusStep = action.BonusStep
                };
            }
            else
            {
                // Aplicar bônus: gasta PAB
                if (_controlled.remainingBAP < Tuning.Get().instinctPowerStrike)
                {
                    _hud.LogAction($"<color=#888888>x</color> {_controlled.unitName}: Instinto insuficiente para Golpe Poderoso", _controlled);
                    return;
                }
                int extraTicks = TickRules.Duration(_controlled, ActionType.Attack, true)
                               - TickRules.Duration(_controlled, ActionType.Attack, false);
                if (extraTicks > _controlled.remainingAP)
                {
                    _hud.LogAction($"<color=#888888>x</color> {_controlled.unitName}: sem tempo na fila para Golpe Poderoso", _controlled);
                    return;
                }
                _controlled.actionSequence[idx] = new ScheduledAction
                {
                    Type = action.Type,
                    Index = action.Index,
                    IsBonus = true,
                    IsAimed = action.IsAimed,
                    BonusStep = action.BonusStep
                };
            }

            _controlled.RecomputeTickBudget(); _hud.UpdateAPDisplay(_controlled);
            RefreshSequenceVisuals();
            SyncButtonStates();
            _hud.ShowMainMenu(); // ação bônus concluída → volta ao menu inicial
        }

        private void OnQuickStepClick()
        {
            if (!_active || _controlled == null) return;

            // Verificar se há um movimento selecionado
            int idx = _hud.SelectedSeqIndex;
            if (idx < 0 || idx >= _controlled.actionSequence.Count)
            {
                Debug.Log("Selecione um movimento planejado para usar Passo Rápido");
                return;
            }

            var action = _controlled.actionSequence[idx];
            if (action.Type != ActionType.Move)
            {
                Debug.Log("Passo Rápido só funciona em movimentos");
                return;
            }

            // Toggle: em SP, o PLANEJAMENTO só reserva/devolve o PAB — o DESTINO do passo
            // extra é escolhido na FASE DE AÇÃO (RoundManager.DoBonusStep, clique ao vivo).
            // Em MP isso NÃO é mais possível: DoBonusStep sempre usa o destino PRÉ-decidido
            // (senão o clique ao vivo diverge entre as máquinas — ver commit c7b7f39), então
            // o destino precisa ser escolhido JÁ AQUI, no planejamento, antes de confirmar.
            if (action.IsBonus)
            {
                action.IsBonus = false;
                _controlled.actionSequence[idx] = action;
                _controlled.hasPlannedBonus = false;
                RefreshSequenceVisuals();
                SyncButtonStates();
                _controlled.RecomputeTickBudget(); _hud.UpdateAPDisplay(_controlled);
                return;
            }

            if (_controlled.remainingBAP <= 0)
            {
                Debug.Log("PAB insuficiente para Passo Rápido");
                return;
            }

            if (RuntimeMultiplayerSession.IsMultiplayer)
            {
                _quickStepActionIndex = idx;
                EnterQuickStepPicking();
                return;
            }

            action.IsBonus = true;
            _controlled.actionSequence[idx] = action;

            _controlled.RecomputeTickBudget(); _hud.UpdateAPDisplay(_controlled);
            RefreshSequenceVisuals();
            SyncButtonStates();
            _hud.ShowMainMenu(); // ação bônus concluída → volta ao menu inicial
        }

        // ---- Passo Rápido em MP: destino escolhido AGORA (não ao vivo na ação) --------
        private int _quickStepActionIndex = -1;
        private List<Vector2Int> _quickStepReachable = new List<Vector2Int>();

        private void EnterQuickStepPicking()
        {
            ClearRangeGhosts();
            var dest = _controlled.plannedAnchor; // destino final do movimento já planejado
            int fp = _controlled.stats.Footprint;
            var blockers = new List<Unit>();
            foreach (var u in _allUnits)
                if (u != _controlled && !u.IsDead) blockers.Add(u);

            _quickStepReachable = _grid.GetReachableAnchors(dest, 1, fp, blockers);
            _quickStepReachable.RemoveAll(a => a == dest);

            _grid.HighlightAnchors(_quickStepReachable, ColTileTarget);
            EnsureCursorGhost();
            SetCursorGhostColor(ColTileTarget);
            _hud.ShowPrompt("Passo extra: clique num tile ao lado do destino");
            SetMode(PlanMode.QuickStepPicking);
        }

        private void CommitQuickStep(Vector2Int anchor)
        {
            if (!_quickStepReachable.Contains(anchor)) return;
            if (_quickStepActionIndex < 0 || _quickStepActionIndex >= _controlled.actionSequence.Count) return;

            _controlled.plannedBonusAnchor = anchor;
            _controlled.hasPlannedBonus = true;

            var action = _controlled.actionSequence[_quickStepActionIndex];
            action.IsBonus = true;
            _controlled.actionSequence[_quickStepActionIndex] = action;
            _quickStepActionIndex = -1;

            HideCursorGhost();
            _hud.HidePrompt();
            SetMode(PlanMode.Idle);
            _controlled.RecomputeTickBudget(); _hud.UpdateAPDisplay(_controlled);
            RefreshSequenceVisuals();
            SyncButtonStates();
            _hud.ShowMainMenu();
        }

        private void OnConcentrateClick()
        {
            if (!_active || _controlled == null) return;

            // Concentração agora é um CONTADOR: cada clique adiciona uma (gasta 1 PAB),
            // até acabar o PAB ou a mana projetada encher. Cancela via Desfazer/Limpar.
            // Concentrar é da Mente: custa tempo, não Instinto.
            if (!_controlled.CanFit(ActionType.Concentrate))
            {
                _hud.LogAction($"<color=#888888>x</color> {_controlled.unitName}: sem tempo na fila para concentrar", _controlled);
                return;
            }
            int projectedMana = _controlled.currentMana + _controlled.plannedConcentrations * _controlled.stats.ManaRegen;
            if (projectedMana >= _controlled.stats.MaxMana)
            {
                _hud.LogAction($"{_controlled.unitName} mana ja cheia", _controlled);
                return;
            }
            _controlled.plannedConcentrations++;
            _controlled.actionSequence.Add(new ScheduledAction
            {
                Type = ActionType.Concentrate, Index = 0, IsBonus = false, BonusStep = Vector2Int.zero
            });
            _hud.LogAction($"{_controlled.unitName} planejou concentrar ({_controlled.plannedConcentrations})", _controlled);

            _controlled.RecomputeTickBudget(); _hud.UpdateAPDisplay(_controlled);
            SyncButtonStates();
            RefreshSequenceVisuals();
            _hud.ShowMainMenu(); // ação bônus concluída → volta ao menu inicial
        }

        private void OnIncrementClick()
        {
            if (!_active || _controlled == null) return;

            int idx = _hud.SelectedSeqIndex;
            if (idx < 0 || idx >= _controlled.actionSequence.Count)
            {
                Debug.Log("Selecione uma ação na sequência para incrementar");
                return;
            }

            var action = _controlled.actionSequence[idx];
            if (action.IsBonus)
            {
                // Remover bônus: devolve PAB
                action.IsBonus = false;
                _controlled.actionSequence[idx] = action;
            }
            else
            {
                // Aplicar bônus: gasta PAB
                var probe = action; probe.IsBonus = true;
                if (_controlled.remainingBAP < TickRules.InstinctCost(probe))
                {
                    _hud.LogAction($"<color=#888888>x</color> {_controlled.unitName}: Instinto insuficiente", _controlled);
                    return;
                }
                action.IsBonus = true;
                _controlled.actionSequence[idx] = action;
            }

            _controlled.RecomputeTickBudget(); _hud.UpdateAPDisplay(_controlled);
            RefreshSequenceVisuals();
            SyncButtonStates();
            _hud.ShowMainMenu(); // ação bônus concluída → volta ao menu inicial
        }

        private void OnAimClick()
        {
            if (!_active || _controlled == null) return;

            int idx = _hud.SelectedSeqIndex;
            if (idx < 0 || idx >= _controlled.actionSequence.Count)
            {
                Debug.Log("Selecione um ataque planejado para usar Mirar");
                return;
            }

            var action = _controlled.actionSequence[idx];
            if (action.Type != ActionType.Attack)
            {
                Debug.Log("Mirar só funciona em ataques");
                return;
            }

            if (action.IsAimed)
            {
                // Remover mira: devolve PAB
                _controlled.actionSequence[idx] = new ScheduledAction
                {
                    Type = action.Type,
                    Index = action.Index,
                    IsBonus = action.IsBonus,
                    IsAimed = false,
                    BonusStep = action.BonusStep
                };
            }
            else
            {
                // Aplicar mira: gasta PAB
                if (_controlled.remainingBAP <= 0)
                {
                    Debug.Log("PAB insuficiente para Mirar");
                    return;
                }
                _controlled.actionSequence[idx] = new ScheduledAction
                {
                    Type = action.Type,
                    Index = action.Index,
                    IsBonus = action.IsBonus,
                    IsAimed = true,
                    BonusStep = action.BonusStep
                };
            }

            _controlled.RecomputeTickBudget(); _hud.UpdateAPDisplay(_controlled);
            RefreshSequenceVisuals();
            SyncButtonStates();
            _hud.ShowMainMenu(); // ação bônus concluída → volta ao menu inicial
        }

        // ------------------------------------------------------------------ PICKING: MOVIMENTO

        // ------------------------------------------------------------------ RANGE PREVIEW

        /// <summary>Anchors dentro de 'range' (Chebyshev de footprint) a partir de 'from'.</summary>
        private List<Vector2Int> RangeAnchors(Vector2Int from, int fp, int range)
        {
            var list = new List<Vector2Int>();
            int budget = range + fp;
            for (int dx = -budget; dx <= budget; dx++)
            for (int dy = -budget; dy <= budget; dy++)
            {
                var a = new Vector2Int(from.x + dx, from.y + dy);
                if (a == from || !_grid.IsAnchorInBounds(a, fp)) continue;
                if (GridManager.FootprintGap(from, fp, a, fp) <= range)
                    list.Add(a);
            }
            return list;
        }

        private void SetBaseRange(List<Vector2Int> anchors, Color color)
        {
            _baseRange.Clear();
            _baseRange.AddRange(anchors);
            _baseRangeColor = color;
            _hasBaseRange   = true;
            _rangeHoverUnit = null;
            ShowRangeGhosts(_baseRange, color);
            Debug.Log($"[Range] base range: {_baseRange.Count} tiles");
        }

        private void ClearBaseRange()
        {
            _hasBaseRange = false;
            _baseRange.Clear();
            _rangeHoverUnit = null;
            ClearRangeGhosts();
        }

        private void RestoreBaseRange()
        {
            if (_hasBaseRange) ShowRangeGhosts(_baseRange, _baseRangeColor);
            else               ClearRangeGhosts();
        }

        // Range renderizado como OVERLAY de losangos translúcidos (não tinge/transforma o tile).
        private void ShowRangeGhosts(List<Vector2Int> anchors, Color color)
        {
            ClearRangeGhosts();
            foreach (var a in anchors)
            {
                var center = new Vector2Int(a.x + 1, a.y + 1); // mesmo tile central do HighlightAnchors
                if (!_grid.IsAnchorInBounds(center, 1)) continue;
                _rangeGhosts.Add(CreateCellGhost(center, color));
            }
        }

        private void ClearRangeGhosts()
        {
            foreach (var g in _rangeGhosts) if (g != null) Destroy(g);
            _rangeGhosts.Clear();
        }

        private GameObject CreateCellGhost(Vector2Int cell, Color color)
        {
            var g = new GameObject("RangeGhost");
            var sr = g.AddComponent<SpriteRenderer>();
            sr.sprite = GhostSprite();
            sr.color = color;
            sr.sortingOrder = 4000; // acima dos tiles, abaixo dos ghosts de waypoint (5000)
            g.transform.localScale = new Vector3(1f, World3D.GroundStretch, 1f); // 1 tile, deitado
            // Fix (2026-07-20): marcador de chão, deitado no XZ como os tiles
            // (câmera reto de cima) — sem isso ficava de canto/invisível.
            g.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            // Fix (2026-07-29): preso à célula LÓGICA — Attach já posiciona. Sem
            // isso o anel de alcance ficava desenhado no slot da orientação antiga
            // ao girar a vista com Q/E durante o planejamento.
            GridAnchored.Attach(g, cell, 1);
            return g;
        }

        /// <summary>Range de ataque da unidade controlada, a partir da posição planejada.</summary>
        private void ShowSelfAttackRange()
        {
            if (_controlled == null) return;
            int fp = _controlled.stats.Footprint;
            var ring = RangeAnchors(_controlled.plannedAnchor, fp, _controlled.stats.AttackRange);
            SetBaseRange(ring, Tuning.Get().attackRangeColor);
        }

        /// <summary>Nos modos Idle/mira-unidade: hover em inimigo mostra o range DELE em vermelho.</summary>
        private void UpdateEnemyRangeHover()
        {
            if (_controlled == null) return;
            var hovered = HitUnitWithCollider();
            bool isEnemy = hovered != null && !hovered.IsDead && hovered.team != _controlled.team;
            if (isEnemy)
            {
                if (_rangeHoverUnit != hovered)
                {
                    _rangeHoverUnit = hovered;
                    int fp = hovered.stats.Footprint;
                    var ring = RangeAnchors(hovered.anchor, fp, hovered.stats.AttackRange);
                    ShowRangeGhosts(ring, Tuning.Get().enemyRangeColor);
                    Debug.Log($"[Range] hover inimigo {hovered.unitName}: alcance {hovered.stats.AttackRange} ({ring.Count} tiles)");
                }
            }
            else if (_rangeHoverUnit != null)
            {
                _rangeHoverUnit = null;
                RestoreBaseRange();
            }
        }

        private void EnterMovePicking()
        {
            ClearRangeGhosts(); // esconde o overlay de range durante o picking de movimento
            var from     = _controlled.plannedAnchor;
            var blockers = BlockerUnits(_controlled);
            _reachable   = _grid.GetReachableAnchors(from, _controlled.stats.MoveStep,
                               _controlled.stats.Footprint, blockers);
            _reachable.Add(from);

            _grid.HighlightAnchors(_reachable);
            EnsureCursorGhost();
            SetMode(PlanMode.MovePicking);
        }

        private void CommitMove(Vector2Int dest)
        {
            _controlled.plannedPath.Add(dest);
            _controlled.plannedAnchor = dest;
            _controlled.actionSequence.Add(new ScheduledAction
                { Type = ActionType.Move, Index = _controlled.plannedMoveCount, IsBonus = false, BonusStep = Vector2Int.zero });
            _controlled.plannedMoveCount++;

            // ghost permanente no waypoint confirmado
            var pg = CreateGhostAt(dest, Tuning.Get().moveGhostColor);
            _pathGhosts.Add(pg);

            HideCursorGhost();
            SetMode(PlanMode.Idle);
            _controlled.RecomputeTickBudget(); _hud.UpdateAPDisplay(_controlled);
            SyncButtonStates();
            RefreshSequenceVisuals();
        }

        private void CancelAllMoves()
        {
            _controlled.plannedMoveCount  = 0;
            _controlled.plannedAnchor     = _controlled.anchor;
            _controlled.plannedPath.Clear();
            // Devolve PAB dos movimentos bônus (Passo Rápido)
            foreach (var a in _controlled.actionSequence)
                if (a.Type == ActionType.Move && a.IsBonus)
            _controlled.actionSequence.RemoveAll(a => a.Type == ActionType.Move);
            _controlled.hasPlannedBonus = false;

            ClearPathGhosts();
            HideCursorGhost();
            _grid.ClearHighlight();
            SyncButtonStates();
            _controlled.RecomputeTickBudget(); _hud.UpdateAPDisplay(_controlled);
            RefreshSequenceVisuals();
        }

        // ------------------------------------------------------------------ PICKING: ATAQUE UNIDADE

        private static Color ColUnitTarget => Tuning.Get().unitTargetColor;

        private void EnterAttackUnitPicking()
        {
            _targetableEnemies.Clear();
            foreach (var u in _allUnits)
                if (!u.IsDead && u.team != _controlled.team)
                    _targetableEnemies.Add(u);

            // A mira destaca o FOOTPRINT do alvo sob o cursor (SetTargeted). Além disso
            // mostramos o range de ataque no grid para o jogador saber o alcance.
            SetMode(PlanMode.AttackUnitPicking);
            ShowSelfAttackRange();
        }

        private void CommitAttackUnit(Unit target)
        {
            if (target == null || target.IsDead) return;

            var atk = new PlannedAttack { Mode = AttackMode.Unit, TargetUnit = target };
            _controlled.plannedAttacks.Add(atk);

            _controlled.actionSequence.Add(new ScheduledAction
            {
                Type = ActionType.Attack,
                Index = _controlled.plannedAttacks.Count - 1,
                IsBonus = false,
                BonusStep = Vector2Int.zero
            });


            // Marca a UNIDADE como alvo (footprint + sprite vermelhos), em vez de um losango no tile.
            // Um marcador invisível sobre a unidade ancora o número da ordem (mantém o 1:1 com a sequência).
            target.SetAttackMarked(true);
            var marker = new GameObject("UnitAttackMarker");
            marker.transform.position = target.HeadWorld;
            _pathGhosts.Add(marker);

            HideCursorGhost();
            SetMode(PlanMode.Idle);
            _controlled.RecomputeTickBudget(); _hud.UpdateAPDisplay(_controlled);
            SyncButtonStates();
            RefreshSequenceVisuals();
        }

        // ------------------------------------------------------------------ PICKING: ATAQUE TILE

        private static Color ColTileTarget => Tuning.Get().tileTargetColor; // ciano

        private void EnterAttackTilePicking()
        {
            ClearRangeGhosts();
            var from = _controlled.plannedAnchor;
            _attackable.Clear();

            int fp     = _controlled.stats.Footprint;
            int budget = _controlled.stats.AttackRange + fp;
            for (int dx = -budget; dx <= budget; dx++)
            for (int dy = -budget; dy <= budget; dy++)
            {
                var a = new Vector2Int(from.x + dx, from.y + dy);
                if (a == from || !_grid.IsAnchorInBounds(a, fp)) continue;
                if (GridManager.FootprintGap(from, fp, a, fp) <= _controlled.stats.AttackRange)
                    _attackable.Add(a);
            }

            _grid.HighlightAnchors(_attackable, ColTileTarget);
            EnsureCursorGhost();
            SetCursorGhostColor(ColTileTarget);
            SetMode(PlanMode.AttackTilePicking);
        }

        private void CommitAttackTile(Vector2Int anchor)
        {
            if (!_attackable.Contains(anchor)) return;

            var atk = new PlannedAttack { Mode = AttackMode.Tile, TargetTile = anchor };
            _controlled.plannedAttacks.Add(atk);

            _controlled.actionSequence.Add(new ScheduledAction
            {
                Type = ActionType.Attack,
                Index = _controlled.plannedAttacks.Count - 1,
                IsBonus = false,
                BonusStep = Vector2Int.zero
            });

            var ghostColor = Tuning.Get().tileTargetGhostColor; // ciano
            _pathGhosts.Add(CreateGhostAt(anchor, ghostColor));

            HideCursorGhost();
            SetMode(PlanMode.Idle);
            _controlled.RecomputeTickBudget(); _hud.UpdateAPDisplay(_controlled);
            SyncButtonStates();
            RefreshSequenceVisuals();
        }

        // Passo Rápido não tem picking no planejamento: o destino do passo é escolhido na
        // fase de ação (ver RoundManager.DoBonusStep). Aqui só reserva o PAB (OnQuickStepClick).

        // ------------------------------------------------------------------ CANCEL / UNDO

        private void OnUndoClick()
        {
            if (!_active || _controlled == null) return;
            if (_controlled.actionSequence.Count == 0) return;

            var last = _controlled.actionSequence[_controlled.actionSequence.Count - 1];

            if (last.Type == ActionType.Move)
            {
                _controlled.plannedPath.RemoveAt(_controlled.plannedPath.Count - 1);
                _controlled.plannedMoveCount--;
                _controlled.plannedAnchor = _controlled.plannedMoveCount > 0
                    ? _controlled.plannedPath[_controlled.plannedPath.Count - 1]
                    : _controlled.anchor;
                if (last.IsBonus)
                {
                    // Desfez o movimento com Passo Rápido — limpa o destino escolhido em
                    // EnterQuickStepPicking/CommitQuickStep (senão hasPlannedBonus ficaria
                    // true residual, vazando para o próximo round mesmo sem plano novo).
                    _controlled.hasPlannedBonus = false;
                }
            }
            else if (last.Type == ActionType.Attack)
            {
                _controlled.plannedAttacks.RemoveAt(_controlled.plannedAttacks.Count - 1);

                if (_pathGhosts.Count > 0)
                {
                    var lastGhost = _pathGhosts[_pathGhosts.Count - 1];
                    if (lastGhost != null) Destroy(lastGhost);
                    _pathGhosts.RemoveAt(_pathGhosts.Count - 1);
                }
            }
            else if (last.Type == ActionType.Concentrate)
            {
                if (_controlled.plannedConcentrations > 0) _controlled.plannedConcentrations--;
            }

            _controlled.actionSequence.RemoveAt(_controlled.actionSequence.Count - 1);

            if (_mode == PlanMode.MovePicking) SetMode(PlanMode.Idle);
            _controlled.RecomputeTickBudget(); _hud.UpdateAPDisplay(_controlled);
            SyncButtonStates();
            ClearGhostLabels();
            RefreshSequenceVisuals();
            if (_mode == PlanMode.Idle) ShowSelfAttackRange(); // range acompanha a posição planejada
        }

        private void OnClearClick()
        {
            if (!_active || _controlled == null) return;
            if (_controlled.plannedMoveCount == 0 && _controlled.plannedAttacks.Count == 0 && _controlled.plannedConcentrations == 0) return;

            _controlled.plannedMoveCount = 0;
            _controlled.plannedAnchor = _controlled.anchor;
            _controlled.plannedPath.Clear();
            _controlled.plannedAttacks.Clear();
            _controlled.reservedMana = 0;
            _controlled.plannedConcentrations = 0;
            _controlled.actionSequence.Clear();
            _controlled.hasPlannedBonus = false;
            _controlled.bonusDamageThisAttack = false;

            ClearPathGhosts();
            HideCursorGhost();
            _grid.ClearHighlight();
            if (_mode != PlanMode.Idle) SetMode(PlanMode.Idle);
            _controlled.RecomputeTickBudget(); _hud.UpdateAPDisplay(_controlled);
            SyncButtonStates();
            RefreshSequenceVisuals();
            ShowSelfAttackRange(); // recomputa o range para a posição resetada
        }

        /// <summary>Toggle BAP direto no chip da sequência.</summary>
        private void OnSeqChipToggled(int index)
        {
            if (!_active || _controlled == null) return;
            if (index < 0 || index >= _controlled.actionSequence.Count) return;

            var action = _controlled.actionSequence[index];
            _hud.SelectSeqIndex(index);
            _hud.OnSeqChipToggled = null; // evita loop

            if (action.Type == ActionType.Attack)
                OnPowerStrikeClick();
            else if (action.Type == ActionType.Move)
                OnQuickStepClick();

            _hud.OnSeqChipToggled = OnSeqChipToggled;
        }

        // ------------------------------------------------------------------ MODO INTENÇÃO

        private static string IntentName(ArmedIntent i) =>
            i == ArmedIntent.Charge ? "Investir" : i == ArmedIntent.HitAndRun ? "Golpear e recuar"
            : i == ArmedIntent.Retreat ? "Recuar" : "Automática";

        private void ShowIntentHint()
        {
            _hud.ShowPrompt($"Intenção: <b>{IntentName(_armed)}</b>  •  [1] Investir  [2] Golpear e recuar  [3] Recuar\n" +
                            "Clique no inimigo ou no chão  •  Enter / 2º clique confirma");
        }

        private void Arm(ArmedIntent intent)
        {
            _armed = _armed == intent ? ArmedIntent.None : intent; // apertar de novo desarma
            ShowIntentHint();
        }

        private bool PointerOverUI()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            return es != null && es.IsPointerOverGameObject();
        }

        /// <summary>Teclas e cliques do Modo Intenção. Devolve true se consumiu o input do frame.</summary>
        private bool HandleIntentInput()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame) { Arm(ArmedIntent.Charge); return true; }
                if (kb.digit2Key.wasPressedThisFrame) { Arm(ArmedIntent.HitAndRun); return true; }
                if (kb.digit3Key.wasPressedThisFrame)
                {
                    _armed = ArmedIntent.Retreat; ShowIntentHint();
                    ApplyIntent(() => IntentBuilder.Retreat(_controlled, _allUnits, _grid), null, null);
                    return true;
                }
                bool confirmKey = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame
                               || kb.spaceKey.wasPressedThisFrame;
                if (confirmKey && _controlled.actionSequence.Count > 0) { OnConfirmRequested?.Invoke(); return true; }
            }

            // Só o botão esquerdo: o direito já é o arrasto da câmera.
            bool left = Mouse.current.leftButton.wasPressedThisFrame;
            if (!left || PointerOverUI()) return false;

            bool shift = kb != null && kb.shiftKey.isPressed;
            bool ctrl  = kb != null && kb.ctrlKey.isPressed;

            var unit = HitUnitWithCollider();
            if (unit != null && !unit.IsDead && unit.IsHostileTo(_controlled))
            {
                // Segundo clique no mesmo alvo confirma a prévia.
                if (_previewUnit == unit && !shift && !ctrl && _controlled.actionSequence.Count > 0)
                { OnConfirmRequested?.Invoke(); return true; }

                if (ctrl || (_armed == ArmedIntent.HitAndRun && !shift))
                    ApplyIntent(() => IntentBuilder.HitAndRun(_controlled, unit, _allUnits, _grid), unit, null);
                else if (shift)
                    ApplyIntent(() => IntentBuilder.Charge(_controlled, unit, _allUnits, _grid, maxAttacks: 1), unit, null);
                else
                    ApplyIntent(() => IntentBuilder.Charge(_controlled, unit, _allUnits, _grid), unit, null);
                return true;
            }

            var cell = HitAnchor();
            if (cell.HasValue && _grid.IsAnchorInBounds(cell.Value, _controlled.stats.Footprint) && (unit == null || unit == _controlled))
            {
                var c = cell.Value;
                if (_previewCell == c && _controlled.actionSequence.Count > 0)
                { OnConfirmRequested?.Invoke(); return true; }

                if (_armed == ArmedIntent.Retreat)
                    ApplyIntent(() => IntentBuilder.Retreat(_controlled, _allUnits, _grid, c), null, c);
                else
                    ApplyIntent(() => IntentBuilder.MoveTo(_controlled, c, _allUnits, _grid), null, c);
                return true;
            }
            return false;
        }

        /// <summary>Roda o montador, guarda o alvo da prévia e redesenha fantasmas, marcas e barra de ticks.</summary>
        private void ApplyIntent(System.Func<bool> build, Unit targetUnit, Vector2Int? targetCell)
        {
            bool ok = build();
            _previewUnit = ok ? targetUnit : null;
            _previewCell = ok ? targetCell : null;
            if (!ok)
                _hud.LogAction($"<color=#888888>x</color> {_controlled.unitName}: nada a fazer com essa intenção daqui", _controlled);
            RebuildPlanVisuals();
        }

        /// <summary>Recria os fantasmas e marcas a partir do plano atual da unidade (1 por ação da fila).</summary>
        private void RebuildPlanVisuals()
        {
            ClearPathGhosts();
            foreach (var u in _allUnits) if (u != null) u.SetAttackMarked(false);
            foreach (var a in _controlled.actionSequence)
            {
                if (a.Type == ActionType.Move && a.Index < _controlled.plannedPath.Count)
                    _pathGhosts.Add(CreateGhostAt(_controlled.plannedPath[a.Index], Tuning.Get().moveGhostColor));
                else if (a.Type == ActionType.Attack && a.Index < _controlled.plannedAttacks.Count)
                {
                    var t = _controlled.plannedAttacks[a.Index].TargetUnit;
                    if (t != null) t.SetAttackMarked(true);
                    var marker = new GameObject("UnitAttackMarker");
                    marker.transform.position = t != null ? t.HeadWorld : _controlled.HeadWorld;
                    _pathGhosts.Add(marker);
                }
            }
            HideCursorGhost();
            _grid.ClearHighlight();
            _controlled.RecomputeTickBudget(); _hud.UpdateAPDisplay(_controlled);
            SyncButtonStates();
            RefreshSequenceVisuals();
            ShowSelfAttackRange();
        }

        /// <summary>Toggle BAP clicando no ghost tile (movimento) ou unidade marcada (ataque).</summary>
        private void HandleBapClickOnTile()
        {
            int clickedIdx = -1;
            var anchor = HitAnchor();

            if (anchor.HasValue)
            {
                // Ghosts de movimento: índice i em _pathGhosts / actionSequence = mesmo plannedPath
                int pathIdx = _controlled.plannedPath.IndexOf(anchor.Value);
                if (pathIdx >= 0)
                {
                    // Mapeia índice plannedPath → índice actionSequence (só Move)
                    for (int i = 0; i < _controlled.actionSequence.Count; i++)
                    {
                        if (_controlled.actionSequence[i].Type == ActionType.Move
                            && _controlled.actionSequence[i].Index == pathIdx)
                        { clickedIdx = i; break; }
                    }
                }
            }

            // Se não achou ghost de movimento, tenta unidade marcada (ataque ou magia)
            if (clickedIdx < 0)
            {
                var hoveredUnit = HitUnitWithCollider();
                if (hoveredUnit != null && hoveredUnit.team != _controlled.team
                    && hoveredUnit.IsAttackMarked)
                {
                    for (int i = 0; i < _controlled.actionSequence.Count; i++)
                    {
                        if (_controlled.actionSequence[i].Type == ActionType.Attack)
                        {
                            int atkIdx = _controlled.actionSequence[i].Index;
                            if (atkIdx < _controlled.plannedAttacks.Count
                                && _controlled.plannedAttacks[atkIdx].Mode == AttackMode.Unit
                                && _controlled.plannedAttacks[atkIdx].TargetUnit == hoveredUnit)
                            { clickedIdx = i; break; }
                        }
                    }
                }
            }

            if (clickedIdx >= 0)
                OnSeqChipToggled(clickedIdx);
        }

        // ------------------------------------------------------------------ MODE

        private void SetMode(PlanMode mode)
        {
            // Ao sair da mira de unidade (cancelar, confirmar ou trocar de modo),
            // limpa o realce vermelho da unidade que estava sob o cursor.
            if (mode != PlanMode.AttackUnitPicking && _lastTargetedUnit != null)
            {
                _lastTargetedUnit.SetTargeted(false);
                _lastTargetedUnit = null;
            }
            _mode = mode;
            if (mode == PlanMode.Idle)
            {
                _grid.ClearHighlight();  // limpa o tint de picking (movimento/tile)
                ShowSelfAttackRange();   // range como overlay de ghost (não tinge o tile)
                _hud.HidePrompt();
                _hud.ShowMainMenu();
            }
            SyncButtonStates();
        }

        private void Update()
        {
            if (!_active || Mouse.current == null) return;

            // ESC: voltar no menu cascata (quando não está em nenhum modo de picking)
            if (_mode == PlanMode.Idle && Keyboard.current != null
                && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                _hud.GoBack();
                return;
            }

            // Modo Intenção: um clique monta a fila inteira (GDD-016 §6).
            if (_mode == PlanMode.Idle && HandleIntentInput()) return;

            // Em idle, hover em inimigo revela o range dele (vermelho); sair restaura o meu.
            if (_mode == PlanMode.Idle)
                UpdateEnemyRangeHover();

            if (_mode == PlanMode.MovePicking)
            {
                var hovered = HitAnchor();
                bool valid = hovered.HasValue && _reachable.Contains(hovered.Value);
                if (valid) PlaceGhost(_cursorGhost, hovered.Value);
                else       HideCursorGhost();

                if (Mouse.current.leftButton.wasPressedThisFrame)
                {
                    var clicked = HitAnchor();
                    if (clicked.HasValue && _reachable.Contains(clicked.Value))
                        CommitMove(clicked.Value);
                }
            }
            else if (_mode == PlanMode.AttackUnitPicking)
            {
                UpdateEnemyRangeHover(); // range do inimigo sob o cursor em vermelho
                var hoveredUnit = HitUnitWithCollider();
                if (hoveredUnit != null && !hoveredUnit.IsDead && hoveredUnit.team != _controlled.team)
                {
                    if (_lastTargetedUnit != hoveredUnit)
                    {
                        if (_lastTargetedUnit != null) _lastTargetedUnit.SetTargeted(false);
                        hoveredUnit.SetTargeted(true);
                        _lastTargetedUnit = hoveredUnit;
                    }
                    if (Mouse.current.leftButton.wasPressedThisFrame)
                    {
                        CommitAttackUnit(hoveredUnit);
                    }
                }
                else
                {
                    if (_lastTargetedUnit != null) _lastTargetedUnit.SetTargeted(false);
                    _lastTargetedUnit = null;
                    HideCursorGhost();
                }
            }
            else if (_mode == PlanMode.AttackTilePicking)
            {
                var hovered = HitAnchor();
                bool valid = hovered.HasValue && _attackable.Contains(hovered.Value);
                if (valid)
                {
                    PlaceGhost(_cursorGhost, hovered.Value);
                    if (Mouse.current.leftButton.wasPressedThisFrame)
                    {
                        CommitAttackTile(hovered.Value);
                    }
                }
                else
                    HideCursorGhost();
            }
            else if (_mode == PlanMode.QuickStepPicking)
            {
                var hovered = HitAnchor();
                bool valid = hovered.HasValue && _quickStepReachable.Contains(hovered.Value);
                if (valid)
                {
                    PlaceGhost(_cursorGhost, hovered.Value);
                    if (Mouse.current.leftButton.wasPressedThisFrame)
                        CommitQuickStep(hovered.Value);
                }
                else
                    HideCursorGhost();
            }
        }

        // ------------------------------------------------------------------ SYNC UI

        private void SyncButtonStates()
        {
            if (_controlled == null) return;
            _controlled.RecomputeTickBudget();
            bool hasSequence = _controlled.actionSequence.Count > 0;
            _hud.SetMoveState(
                canAdd:    _controlled.CanFit(ActionType.Move),
                count:     _controlled.plannedMoveCount,
                isPicking: _mode == PlanMode.MovePicking);
            _hud.SetAttackUnitState(
                canAdd:    _controlled.CanFit(ActionType.Attack),
                count:     _controlled.plannedAttacks.Count,
                isPicking: _mode == PlanMode.AttackUnitPicking);
            _hud.SetAttackTileState(
                canAdd:    _controlled.CanFit(ActionType.Attack),
                count:     _controlled.plannedAttacks.Count,
                isPicking: _mode == PlanMode.AttackTilePicking);
            _hud.SetConcentrateState(
                canAdd:    _controlled.CanFit(ActionType.Concentrate)
                           && (_controlled.currentMana + _controlled.plannedConcentrations * _controlled.stats.ManaRegen) < _controlled.stats.MaxMana,
                isPicking: false);
            _hud.SetIncrementState(
                canAdd:    _controlled.remainingBAP > 0 && hasSequence,
                isPicking: false);
            _hud.SetAimState(
                canAdd:    _controlled.remainingBAP > 0 && hasSequence);
        }

        // ------------------------------------------------------------------ GHOSTS

        private void EnsureCursorGhost()
        {
            if (_cursorGhost != null) return;
            _cursorGhost = MakeGhostQuad("CursorGhost", Tuning.Get().cursorGhostColor);
        }

        private GameObject CreateGhostAt(Vector2Int anchor, Color color)
        {
            var g = MakeGhostQuad("PathGhost", color);
            PlaceGhost(g, anchor);
            return g;
        }

        private static Sprite _ghostSprite;
        private static Sprite GhostSprite()
        {
            if (_ghostSprite != null) return _ghostSprite;
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = Mathf.Abs(x - 32) / 32f;
                float dy = Mathf.Abs(y - 32) / 16f;
                tex.SetPixel(x, y, (dx + dy) <= 1f ? Color.white : Color.clear);
            }
            tex.Apply();
            _ghostSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 32);
            return _ghostSprite;
        }

        private GameObject MakeGhostQuad(string name, Color color)
        {
            var g = new GameObject(name);
            var sr = g.AddComponent<SpriteRenderer>();
            sr.sprite = GhostSprite();
            sr.color = color;
            sr.sortingOrder = 5000;
            int fp = _controlled != null ? _controlled.stats.Footprint : AttributeStats.DefaultFootprint;
            g.transform.localScale = new Vector3(fp, fp * World3D.GroundStretch, 1f);
            // Fix (2026-07-20): marcador de chão, deitado no XZ (ver CreateCellGhost).
            g.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            g.SetActive(false);
            return g;
        }

        private void PlaceGhost(GameObject g, Vector2Int anchor)
        {
            int fp = _controlled != null ? _controlled.stats.Footprint : AttributeStats.DefaultFootprint;
            // Fix (2026-07-29): Attach guarda a âncora lógica e já posiciona, para
            // o waypoint acompanhar a reindexação da rotação de vista.
            GridAnchored.Attach(g, anchor, fp);
            g.SetActive(true);
        }

        private void HideCursorGhost()
        {
            if (_cursorGhost != null) _cursorGhost.SetActive(false);
        }

        private void ClearPathGhosts()
        {
            foreach (var g in _pathGhosts) if (g != null) Destroy(g);
            _pathGhosts.Clear();
            ClearGhostLabels();
        }

        // ------------------------------------------------------------------ SEQUÊNCIA (ordem visível)

        private static Color SeqLabelMove => Tuning.Get().seqLabelMoveColor;
        private static Color SeqLabelAtk  => Tuning.Get().seqLabelAttackColor;


        private void ClearGhostLabels()
        {
            foreach (var l in _ghostLabels) if (l != null) l.Dismiss();
            _ghostLabels.Clear();
        }

        /// <summary>Renumera os rótulos sobre os ghosts e atualiza a faixa de ordem na HUD.</summary>
        private void RefreshSequenceVisuals()
        {
            // Sincroniza um rótulo numerado por ghost (ordem = ordem de criação = sequência)
            while (_ghostLabels.Count > _pathGhosts.Count)
            {
                var last = _ghostLabels[_ghostLabels.Count - 1];
                if (last != null) last.Dismiss();
                _ghostLabels.RemoveAt(_ghostLabels.Count - 1);
            }

            var seq = _controlled != null ? _controlled.actionSequence : null;

            // Empilhamento: rastreia quantos labels já foram colocados em cada posição base
            var usedSlots = new System.Collections.Generic.Dictionary<Vector2Int, int>();
            var _tuning = RuntimeTuning.Active;
            float seqStackSpacing = _tuning != null ? _tuning.seqStackSpacing : 1.2f;

            for (int i = 0; i < _pathGhosts.Count; i++)
            {
                bool isAtk = seq != null && i < seq.Count && seq[i].Type == ActionType.Attack;
                Vector3 ghostPos = _pathGhosts[i].transform.position;
                string num = (i + 1).ToString();
                Color col = isAtk ? SeqLabelAtk : SeqLabelMove;

                // Quantiza a posição do ghost pra agrupar tiles próximos
                var key = new Vector2Int(Mathf.RoundToInt(ghostPos.x * 10), Mathf.RoundToInt(ghostPos.z * 10));
                if (!usedSlots.TryGetValue(key, out int slot))
                    slot = 0;
                usedSlots[key] = slot + 1;
                Vector3 pos = ghostPos + Vector3.up * (Tuning.Get().seqLabelBaseHeight + slot * seqStackSpacing);

                if (i < _ghostLabels.Count)
                {
                    _ghostLabels[i].SetText(num);
                    _ghostLabels[i].SetColor(col);
                    _ghostLabels[i].transform.position = pos;
                }
                else
                {
                    var c32 = new Color32(
                        (byte)(col.r * 255), (byte)(col.g * 255), (byte)(col.b * 255), 255);
                    var lbl = BattleLabel.CreateSequence(_cam, pos, num, Color.white, c32);
                    _ghostLabels.Add(lbl);
                }
            }

            // Faixa na HUD reflete a sequência real de ações
            if (_hud != null)
            {
                if (seq == null || seq.Count == 0) _hud.HideActionSequence();
                else
                {
                    _hud.SetActionSequence(seq.ConvertAll(a => a.Type), seq);
                }
            }
        }

        // ------------------------------------------------------------------ HELPERS

        private Unit HitUnitWithCollider()
        {
            if (Mouse.current == null || _cam == null) return null;
            Vector2 screen = Mouse.current.position.ReadValue();
            // Migração XY→XZ: raycast 3D do mouse contra as unidades (colliders 3D).
            var u = Unit.PickAtWorld(_cam, screen);
            // No picking de ataque só inimigos vivos do time oposto são alvos.
            if (u != null && !u.IsDead && _controlled != null && u.team != _controlled.team)
                return u;
            return null;
        }

        private Vector2Int? HitCell()
        {
            if (_cam == null) return null;
            Vector2 screen = Mouse.current.position.ReadValue();
            // Migração XY→XZ (2026-07-20): raycast no plano y=0 (ScreenToGround),
            // NÃO ScreenToWorldPoint (2D, plano Z=0 que não existe mais).
            Vector3 world;
            if (!_grid.ScreenToGround(_cam, screen, out world)) return null;
            return _grid.WorldToCell(world);
        }

        private Vector2Int? HitAnchor()
        {
            var cell = HitCell();
            if (!cell.HasValue) return null;
            return new Vector2Int(cell.Value.x - 1, cell.Value.y - 1);
        }

        private List<Unit> BlockerUnits(Unit self)
        {
            var list = new List<Unit>();
            foreach (var u in _allUnits)
                if (u != self && !u.IsDead) list.Add(u);
            return list;
        }

        // Retorna inimigo cujo anchor == a, ou null.
        private Unit EnemyAtAnchor(Vector2Int anchor)
        {
            foreach (var u in _allUnits)
                if (!u.IsDead && u.team != _controlled.team && u.anchor == anchor) return u;
            return null;
        }

        // Retorna inimigo em qualquer posição do mapa cujo footprint cobre a célula clicada, ou null.
        private Unit EnemyAtCellAnywhere(Vector2Int cell)
        {
            foreach (var u in _targetableEnemies)
            {
                if (u.IsDead) continue;
                int fp = u.stats.Footprint;
                if (cell.x >= u.anchor.x && cell.x < u.anchor.x + fp &&
                    cell.y >= u.anchor.y && cell.y < u.anchor.y + fp)
                    return u;
            }
            return null;
        }

        private void SetCursorGhostColor(Color c)
        {
            if (_cursorGhost == null) return;
            var sr = _cursorGhost.GetComponent<SpriteRenderer>();
            if (sr != null) sr.color = c;
        }
    }
}
