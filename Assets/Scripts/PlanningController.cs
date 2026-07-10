using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PangeaSkirmish
{
    public enum PlanMode { Idle, MovePicking, AttackUnitPicking, AttackTilePicking, QuickStepPicking,
        SpellElementPicking, SpellTargetPicking, SpellManaPicking, SpellUnitPicking, SpellTilePicking }

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
        private SpellElement _spellElement = SpellElement.None;
        private SpellTargetKind _spellTargetKind = SpellTargetKind.Self;
        // Duas pools de mana SEPARADAS (ver SpellTypes.PlannedSpell):
        //  _spellManaRange = alcance (dano/tile) OU duração em rounds (Self). Só custa mana.
        //  _spellManaPower = potência (multiplica dano/atributo). Cada ponto = +1 PA + mana.
        private int _spellManaRange = 0;
        private int _spellManaPower = 1;

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
            _hud.BindMiraMagia(OnMiraMagiaClick);
            _hud.BindMagicElement(OnMagicElementChosen);
            _hud.BindSpellSelf(OnSpellTargetSelf);
            _hud.BindSpellUnit(OnSpellTargetUnit);
            _hud.BindSpellTile(OnSpellTargetTile);
            _hud.BindManaRangeMinus(OnManaRangeMinus);
            _hud.BindManaRangePlus(OnManaRangePlus);
            _hud.BindManaPowerMinus(OnManaPowerMinus);
            _hud.BindManaPowerPlus(OnManaPowerPlus);
            _hud.BindManaCast(OnManaCast);
            _hud.BindManaBack(OnManaBack);
            _hud.BindUndo(OnUndoClick);
            _hud.BindClear(OnClearClick);
            _hud.OnSeqChipToggled = OnSeqChipToggled;
            SyncButtonStates();
            RefreshSequenceVisuals();
            ShowSelfAttackRange(); // range visível assim que a unidade é selecionada
        }

        public void End()
        {
            _active = false;
            _mode   = PlanMode.Idle;
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

            if (_controlled.remainingAP > 0)
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

            if (_controlled.remainingAP > 0)
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

            if (_controlled.remainingAP > 0)
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
                    BonusStep = action.BonusStep
                };
                _controlled.remainingBAP++;
            }
            else
            {
                // Aplicar bônus: gasta PAB
                if (_controlled.remainingBAP <= 0)
                {
                    Debug.Log("PAB insuficiente para Golpe Poderoso");
                    return;
                }
                _controlled.actionSequence[idx] = new ScheduledAction
                {
                    Type = action.Type,
                    Index = action.Index,
                    IsBonus = true,
                    BonusStep = action.BonusStep
                };
                _controlled.remainingBAP--;
            }

            _hud.UpdateAPDisplay(_controlled);
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
                _controlled.remainingBAP++;
                _controlled.hasPlannedBonus = false;
                RefreshSequenceVisuals();
                SyncButtonStates();
                _hud.UpdateAPDisplay(_controlled);
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
            _controlled.remainingBAP--;

            _hud.UpdateAPDisplay(_controlled);
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
            _controlled.remainingBAP--;
            _quickStepActionIndex = -1;

            HideCursorGhost();
            _hud.HidePrompt();
            SetMode(PlanMode.Idle);
            _hud.UpdateAPDisplay(_controlled);
            RefreshSequenceVisuals();
            SyncButtonStates();
            _hud.ShowMainMenu();
        }

        // ------------------------------------------------------------------ MAGIA

        private void OnMagicClick()
        {
            if (!_active || _controlled == null) return;

            if (_controlled.remainingAP <= 0)
            {
                Debug.Log("PA insuficiente para conjurar");
                return;
            }

            if (_controlled.currentMana <= 0)
            {
                _hud.LogAction($"<color=#888888>x</color> {_controlled.unitName} sem mana", _controlled);
                return;
            }

            _hud.ShowSubMenu(_hud.MagicMenuPanel, "Magia");
        }

        private void OnConcentrateClick()
        {
            if (!_active || _controlled == null) return;

            // Concentração agora é um CONTADOR: cada clique adiciona uma (gasta 1 PAB),
            // até acabar o PAB ou a mana projetada encher. Cancela via Desfazer/Limpar.
            if (_controlled.remainingBAP <= 0) return;
            int projectedMana = _controlled.currentMana + _controlled.plannedConcentrations * _controlled.stats.ManaRegen;
            if (projectedMana >= _controlled.stats.MaxMana)
            {
                _hud.LogAction($"{_controlled.unitName} mana ja cheia", _controlled);
                return;
            }
            _controlled.plannedConcentrations++;
            _controlled.remainingBAP--;
            _controlled.actionSequence.Add(new ScheduledAction
            {
                Type = ActionType.Concentrate, Index = 0, IsBonus = false, BonusStep = Vector2Int.zero
            });
            _hud.LogAction($"{_controlled.unitName} planejou concentrar ({_controlled.plannedConcentrations})", _controlled);

            _hud.UpdateAPDisplay(_controlled);
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
                _controlled.remainingBAP++;
            }
            else
            {
                // Aplicar bônus: gasta PAB
                if (_controlled.remainingBAP <= 0)
                {
                    Debug.Log("PAB insuficiente para incremento");
                    return;
                }
                action.IsBonus = true;
                _controlled.actionSequence[idx] = action;
                _controlled.remainingBAP--;
            }

            _hud.UpdateAPDisplay(_controlled);
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
                _controlled.remainingBAP++;
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
                _controlled.remainingBAP--;
            }

            _hud.UpdateAPDisplay(_controlled);
            RefreshSequenceVisuals();
            SyncButtonStates();
            _hud.ShowMainMenu(); // ação bônus concluída → volta ao menu inicial
        }

        /// <summary>
        /// Mira Mágica: ação bônus de incremento que soma +INT ao bônus de potência da
        /// PRÓXIMA magia conjurada neste turno (espelho de Golpe Poderoso, mas para magia).
        /// Toggle: 1º clique gasta 1 PAB e adiciona +INT; 2º clique devolve o PAB e zera.
        /// </summary>
        private void OnMiraMagiaClick()
        {
            if (!_active || _controlled == null) return;

            int intVal = Mathf.RoundToInt(_controlled.stats.INT);
            if (intVal <= 0)
            {
                Debug.Log("INT insuficiente para Mira Mágica");
                return;
            }

            if (_controlled.plannedSpellBonusINT > 0)
            {
                // Toggle off: devolve o PAB e zera o bônus
                _controlled.plannedSpellBonusINT = 0;
                _controlled.remainingBAP++;
            }
            else
            {
                if (_controlled.remainingBAP <= 0)
                {
                    Debug.Log("PAB insuficiente para Mira Mágica");
                    return;
                }
                _controlled.plannedSpellBonusINT = intVal;
                _controlled.remainingBAP--;
            }

            _hud.UpdateAPDisplay(_controlled);
            SyncButtonStates();
            _hud.ShowMainMenu();
        }

        // Handler para clique nos botões de elemento no menu de magia
        private void OnMagicElementChosen(SpellElement element)
        {
            if (!_active || _controlled == null) return;

            if (_controlled.remainingAP <= 0)
            {
                Debug.Log("PA insuficiente para conjurar");
                return;
            }

            if (_controlled.currentMana <= 0)
            {
                _hud.LogAction($"<color=#888888>x</color> {_controlled.unitName} sem mana", _controlled);
                return;
            }

            _spellElement = element;
            Debug.Log($"[Magia] elemento escolhido: {SpellBook.ElementName(element)}");
            _hud.LogAction($"<b>Magia:</b> {SpellBook.ElementName(_spellElement)}", _controlled);
            // Abre o submenu de ALVO (botões clicáveis Self/Unidade/Tile); teclado S/U/T ainda funciona.
            _hud.ShowSubMenu(_hud.SpellTypeMenuPanel, "Magia > Alvo");
            SetMode(PlanMode.SpellTargetPicking);
        }

        // ── Alvo escolhido → abre o stepper de mana ──
        private void OnSpellTargetSelf() { ChooseSpellTarget(SpellTargetKind.Self); }
        private void OnSpellTargetUnit() { ChooseSpellTarget(SpellTargetKind.Unit); }
        private void OnSpellTargetTile() { ChooseSpellTarget(SpellTargetKind.Tile); }

        private void ChooseSpellTarget(SpellTargetKind kind)
        {
            if (!_active || _controlled == null) return;
            if (_mode != PlanMode.SpellTargetPicking) return;
            _spellTargetKind = kind;
            OpenManaStepper();
        }
        /// <summary>Abre o stepper de mana (2 pools: alcance/duração + potência) com preview ao vivo.</summary>
        private void OpenManaStepper()
        {
            if (_controlled.AvailableMana < 1)
            {
                _hud.LogAction($"<color=#888888>x</color> {_controlled.unitName} sem mana disponível", _controlled);
                return;
            }

            // Self: Range = duração em rounds (mín 1), Power = atributo (mín 1).
            // Unit/Tile: Range = alcance (mín 0), Power = potência (mín 1).
            int defPower = (_spellTargetKind == SpellTargetKind.Self)
                ? Mathf.Clamp(Tuning.Get().spellSelfManaCost, 1, _controlled.AvailableMana)
                : 1;
            int defRange = (_spellTargetKind == SpellTargetKind.Self)
                ? Mathf.Clamp(Tuning.Get().spellSelfManaCost, 1, _controlled.AvailableMana)
                : 0;

            _spellManaPower = Mathf.Clamp(defPower, 1, _controlled.AvailableMana);
            _spellManaRange = Mathf.Clamp(defRange, 0, _controlled.AvailableMana - _spellManaPower);

            _hud.ShowSubMenu(_hud.ManaStepperPanel, $"Magia > {SpellBook.ElementName(_spellElement)} > Mana");
            SetMode(PlanMode.SpellManaPicking);
            RefreshManaPreview();
            Debug.Log($"[Magia] stepper aberto: alvo={_spellTargetKind}, range={_spellManaRange}, power={_spellManaPower}/{_controlled.AvailableMana}");
        }

        private void RefreshManaPreview()
        {
            int range = SpellBook.SpellRange(_controlled, _spellManaRange);
            int pot = SpellBook.Potency(_controlled, _spellElement, _spellManaPower);
            _hud.SetManaPreview(_spellManaRange, _spellManaPower, _controlled.AvailableMana, pot, range);
        }

        private void OnManaRangeMinus()
        {
            if (_mode != PlanMode.SpellManaPicking) return;
            if (_spellManaRange <= 0) return;
            _spellManaRange--;
            RefreshManaPreview();
        }

        private void OnManaRangePlus()
        {
            if (_mode != PlanMode.SpellManaPicking) return;
            if (_spellManaRange + _spellManaPower >= _controlled.AvailableMana) return;
            _spellManaRange++;
            RefreshManaPreview();
        }

        private void OnManaPowerMinus()
        {
            if (_mode != PlanMode.SpellManaPicking) return;
            if (_spellManaPower <= 1) return;
            _spellManaPower--;
            RefreshManaPreview();
        }

        private void OnManaPowerPlus()
        {
            if (_mode != PlanMode.SpellManaPicking) return;
            if (_spellManaRange + _spellManaPower >= _controlled.AvailableMana) return;
            _spellManaPower++;
            RefreshManaPreview();
        }

        private void OnManaCast()
        {
            if (_mode != PlanMode.SpellManaPicking) return;
            Debug.Log($"[Magia] conjurar {SpellBook.ElementName(_spellElement)} alvo={_spellTargetKind} range={_spellManaRange} power={_spellManaPower} alcance={SpellBook.SpellRange(_controlled, _spellManaRange)} potência={SpellBook.Potency(_controlled, _spellElement, _spellManaPower)}");
            switch (_spellTargetKind)
            {
                case SpellTargetKind.Self: CommitSpellSelf(); break;
                case SpellTargetKind.Unit: EnterSpellUnitPicking(); break; // pick no grid usa range/power
                case SpellTargetKind.Tile: EnterSpellTilePicking(); break;
            }
        }

        private void OnManaBack()
        {
            if (_mode != PlanMode.SpellManaPicking) return;
            _hud.GoBack(); // volta ao submenu de alvo
            SetMode(PlanMode.SpellTargetPicking);
        }

        private void EnterSpellUnitPicking()
        {
            _targetableEnemies.Clear();
            foreach (var u in _allUnits)
                if (!u.IsDead && u.team != _controlled.team)
                    _targetableEnemies.Add(u);
            SetMode(PlanMode.SpellUnitPicking);
            // Range de magia (pode ser maior que o de ataque, via conduíte).
            int fp = _controlled.stats.Footprint;
            SetBaseRange(RangeAnchors(_controlled.plannedAnchor, fp, SpellBook.SpellRange(_controlled, _spellManaRange)),
                         Tuning.Get().spellTargetHighlightColor);
        }

        private void EnterSpellTilePicking()
        {
            ClearRangeGhosts();
            var from = _controlled.plannedAnchor;
            _attackable.Clear();
            int fp = _controlled.stats.Footprint;
            int range = SpellBook.SpellRange(_controlled, _spellManaRange);
            int budget = range + fp;
            for (int dx = -budget; dx <= budget; dx++)
            for (int dy = -budget; dy <= budget; dy++)
            {
                var a = new Vector2Int(from.x + dx, from.y + dy);
                if (a == from || !_grid.IsAnchorInBounds(a, fp)) continue;
                if (GridManager.FootprintGap(from, fp, a, fp) <= range)
                    _attackable.Add(a);
            }
            _grid.HighlightAnchors(_attackable, Tuning.Get().spellTargetHighlightColor);
            EnsureCursorGhost();
            SetCursorGhostColor(Tuning.Get().spellTargetHighlightColor);
            SetMode(PlanMode.SpellTilePicking);
        }

        private void CommitSpellSelf()
        {
            int manaRange = Mathf.Clamp(_spellManaRange, 1, _controlled.AvailableMana);
            int manaPower = Mathf.Clamp(_spellManaPower, 1, _controlled.AvailableMana - manaRange);
            if (manaRange + manaPower <= 0 || manaRange + manaPower > _controlled.AvailableMana) return;
            _controlled.reservedMana += manaRange + manaPower;

            var spell = new PlannedSpell
            {
                Element = _spellElement,
                Target = SpellTargetKind.Self,
                TargetUnit = null,
                TargetTile = Vector2Int.zero,
                ManaRange = manaRange,
                ManaPower = manaPower,
                Direction = Vector2Int.right,
            };
            _controlled.plannedSpells.Add(spell);
            _controlled.actionSequence.Add(new ScheduledAction
            {
                Type = ActionType.Spell,
                Index = _controlled.plannedSpells.Count - 1,
                IsBonus = false,
                BonusStep = Vector2Int.zero
            });
            // Self: Range = duração (só mana), Power = atributo (cada ponto = +1 PA).
            _controlled.remainingAP -= (1 + manaPower);

            var ghostColor = SpellBook.ElementColor(_spellElement);
            _pathGhosts.Add(CreateGhostAt(_controlled.anchor, ghostColor));

            _hud.HidePrompt();
            SetMode(PlanMode.Idle);
            _hud.UpdateAPDisplay(_controlled);
            SyncButtonStates();
            RefreshSequenceVisuals();
            _hud.ShowMainMenu(); // magia confirmada → volta ao menu (dá acesso a Desfazer/Limpar)
        }

        private void CommitSpellUnit(Unit target)
        {
            if (target == null || target.IsDead) return;

            int manaRange = Mathf.Clamp(_spellManaRange, 0, _controlled.AvailableMana);
            int manaPower = Mathf.Clamp(_spellManaPower, 1, _controlled.AvailableMana - manaRange);
            if (manaRange + manaPower <= 0 || manaRange + manaPower > _controlled.AvailableMana) return;
            _controlled.reservedMana += manaRange + manaPower;

            Vector2Int dir = target.anchor - _controlled.anchor;
            if (dir.x == 0 && dir.y == 0) dir = Vector2Int.right;

            var spell = new PlannedSpell
            {
                Element = _spellElement,
                Target = SpellTargetKind.Unit,
                TargetUnit = target,
                TargetTile = Vector2Int.zero,
                ManaRange = manaRange,
                ManaPower = manaPower,
                Direction = dir,
            };
            _controlled.plannedSpells.Add(spell);
            _controlled.actionSequence.Add(new ScheduledAction
            {
                Type = ActionType.Spell,
                Index = _controlled.plannedSpells.Count - 1,
                IsBonus = false,
                BonusStep = Vector2Int.zero
            });
            _controlled.remainingAP -= (1 + manaPower);

            target.SetAttackMarked(true);
            var marker = new GameObject("SpellMarker");
            marker.transform.position = target.HeadWorld;
            _pathGhosts.Add(marker);

            _hud.HidePrompt();
            SetMode(PlanMode.Idle);
            _hud.UpdateAPDisplay(_controlled);
            SyncButtonStates();
            RefreshSequenceVisuals();
            _hud.ShowMainMenu(); // magia confirmada → volta ao menu (dá acesso a Desfazer/Limpar)
        }

        private void CommitSpellTile(Vector2Int anchor)
        {
            if (!_attackable.Contains(anchor)) return;

            int manaRange = Mathf.Clamp(_spellManaRange, 0, _controlled.AvailableMana);
            int manaPower = Mathf.Clamp(_spellManaPower, 1, _controlled.AvailableMana - manaRange);
            if (manaRange + manaPower <= 0 || manaRange + manaPower > _controlled.AvailableMana) return;
            _controlled.reservedMana += manaRange + manaPower;

            Vector2Int dir = anchor - _controlled.anchor;
            if (dir.x == 0 && dir.y == 0) dir = Vector2Int.right;

            var spell = new PlannedSpell
            {
                Element = _spellElement,
                Target = SpellTargetKind.Tile,
                TargetUnit = null,
                TargetTile = anchor,
                ManaRange = manaRange,
                ManaPower = manaPower,
                Direction = dir,
            };
            _controlled.plannedSpells.Add(spell);
            _controlled.actionSequence.Add(new ScheduledAction
            {
                Type = ActionType.Spell,
                Index = _controlled.plannedSpells.Count - 1,
                IsBonus = false,
                BonusStep = Vector2Int.zero
            });
            _controlled.remainingAP -= (1 + manaPower);

            var ghostColor = SpellBook.ElementColor(_spellElement);
            _pathGhosts.Add(CreateGhostAt(anchor, ghostColor));

            _hud.HidePrompt();
            SetMode(PlanMode.Idle);
            _hud.UpdateAPDisplay(_controlled);
            SyncButtonStates();
            RefreshSequenceVisuals();
            _hud.ShowMainMenu(); // magia confirmada → volta ao menu (dá acesso a Desfazer/Limpar)
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
            g.transform.localScale = Vector3.one; // 1 tile
            g.transform.position = _grid.AnchorToWorldCenter(cell, 1);
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
            _reachable   = _grid.GetReachableAnchors(from, _controlled.stats.MoveBudget,
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
            _controlled.remainingAP--;

            // ghost permanente no waypoint confirmado
            var pg = CreateGhostAt(dest, Tuning.Get().moveGhostColor);
            _pathGhosts.Add(pg);

            HideCursorGhost();
            SetMode(PlanMode.Idle);
            _hud.UpdateAPDisplay(_controlled);
            SyncButtonStates();
            RefreshSequenceVisuals();
        }

        private void CancelAllMoves()
        {
            _controlled.remainingAP      += _controlled.plannedMoveCount;
            _controlled.plannedMoveCount  = 0;
            _controlled.plannedAnchor     = _controlled.anchor;
            _controlled.plannedPath.Clear();
            // Devolve PAB dos movimentos bônus (Passo Rápido)
            foreach (var a in _controlled.actionSequence)
                if (a.Type == ActionType.Move && a.IsBonus)
                    _controlled.remainingBAP++;
            _controlled.actionSequence.RemoveAll(a => a.Type == ActionType.Move);
            _controlled.hasPlannedBonus = false;

            ClearPathGhosts();
            HideCursorGhost();
            _grid.ClearHighlight();
            SyncButtonStates();
            _hud.UpdateAPDisplay(_controlled);
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

            _controlled.remainingAP--;

            // Marca a UNIDADE como alvo (footprint + sprite vermelhos), em vez de um losango no tile.
            // Um marcador invisível sobre a unidade ancora o número da ordem (mantém o 1:1 com a sequência).
            target.SetAttackMarked(true);
            var marker = new GameObject("UnitAttackMarker");
            marker.transform.position = target.HeadWorld;
            _pathGhosts.Add(marker);

            HideCursorGhost();
            SetMode(PlanMode.Idle);
            _hud.UpdateAPDisplay(_controlled);
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

            _controlled.remainingAP--;
            var ghostColor = Tuning.Get().tileTargetGhostColor; // ciano
            _pathGhosts.Add(CreateGhostAt(anchor, ghostColor));

            HideCursorGhost();
            SetMode(PlanMode.Idle);
            _hud.UpdateAPDisplay(_controlled);
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
                _controlled.remainingAP++;
                if (last.IsBonus)
                {
                    _controlled.remainingBAP++;
                    // Desfez o movimento com Passo Rápido — limpa o destino escolhido em
                    // EnterQuickStepPicking/CommitQuickStep (senão hasPlannedBonus ficaria
                    // true residual, vazando para o próximo round mesmo sem plano novo).
                    _controlled.hasPlannedBonus = false;
                }
            }
            else if (last.Type == ActionType.Attack)
            {
                _controlled.plannedAttacks.RemoveAt(_controlled.plannedAttacks.Count - 1);
                _controlled.remainingAP++;
                if (last.IsBonus) _controlled.remainingBAP++;
                if (last.IsAimed) _controlled.remainingBAP++;

                if (_pathGhosts.Count > 0)
                {
                    var lastGhost = _pathGhosts[_pathGhosts.Count - 1];
                    if (lastGhost != null) Destroy(lastGhost);
                    _pathGhosts.RemoveAt(_pathGhosts.Count - 1);
                }
            }
            else if (last.Type == ActionType.Spell)
            {
                int li = _controlled.plannedSpells.Count - 1;
                if (li >= 0)
                {
                    // Devolve a mana reservada (ManaRange + ManaPower) para a conjuração desfeita.
                    var undoneSpell = _controlled.plannedSpells[li];
                    _controlled.reservedMana = Mathf.Max(0, _controlled.reservedMana - (undoneSpell.ManaRange + undoneSpell.ManaPower));
                    if (undoneSpell.Target == SpellTargetKind.Unit && undoneSpell.TargetUnit != null)
                        undoneSpell.TargetUnit.SetAttackMarked(false);
                    _controlled.plannedSpells.RemoveAt(li);
                }
                _controlled.remainingAP++;

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
                _controlled.remainingBAP++;
            }

            _controlled.actionSequence.RemoveAt(_controlled.actionSequence.Count - 1);

            if (_mode == PlanMode.MovePicking) SetMode(PlanMode.Idle);
            _hud.UpdateAPDisplay(_controlled);
            SyncButtonStates();
            ClearGhostLabels();
            RefreshSequenceVisuals();
            if (_mode == PlanMode.Idle) ShowSelfAttackRange(); // range acompanha a posição planejada
        }

        private void OnClearClick()
        {
            if (!_active || _controlled == null) return;
            if (_controlled.plannedMoveCount == 0 && _controlled.plannedAttacks.Count == 0
                && _controlled.plannedSpells.Count == 0 && _controlled.plannedConcentrations == 0) return;

            _controlled.remainingAP = _controlled.stats.ActionPoints;
            _controlled.remainingBAP = _controlled.stats.BonusActionPoints;
            _controlled.plannedMoveCount = 0;
            _controlled.plannedAnchor = _controlled.anchor;
            _controlled.plannedPath.Clear();
            _controlled.plannedAttacks.Clear();
            _controlled.plannedSpells.Clear();
            _controlled.reservedMana = 0;
            _controlled.plannedConcentrations = 0;
            _controlled.actionSequence.Clear();
            _controlled.hasPlannedBonus = false;
            _controlled.bonusDamageThisAttack = false;

            ClearPathGhosts();
            HideCursorGhost();
            _grid.ClearHighlight();
            if (_mode != PlanMode.Idle) SetMode(PlanMode.Idle);
            _hud.UpdateAPDisplay(_controlled);
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
            // Spells não têm toggle de BAP por enquanto

            _hud.OnSeqChipToggled = OnSeqChipToggled;
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
                        if (_controlled.actionSequence[i].Type == ActionType.Spell)
                        {
                            int splIdx = _controlled.actionSequence[i].Index;
                            if (splIdx < _controlled.plannedSpells.Count
                                && _controlled.plannedSpells[splIdx].Target == SpellTargetKind.Unit
                                && _controlled.plannedSpells[splIdx].TargetUnit == hoveredUnit)
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
            if ((mode != PlanMode.AttackUnitPicking && mode != PlanMode.SpellUnitPicking) && _lastTargetedUnit != null)
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

            // Clique em idle: toggle BAP em ghost tile (movimento) ou unidade marcada (ataque)
            if (_mode == PlanMode.Idle && Mouse.current.leftButton.wasPressedThisFrame)
                HandleBapClickOnTile();

            // Em idle, hover em inimigo revela o range dele (vermelho); sair restaura o meu.
            if (_mode == PlanMode.Idle)
                UpdateEnemyRangeHover();

            // Teclas de elemento na mira de magia
            if (_mode == PlanMode.SpellElementPicking)
            {
                if (Keyboard.current != null)
                {
                    if (Keyboard.current.digit1Key.wasPressedThisFrame) { _spellElement = SpellElement.Physical; OnSpellElementChosen(); return; }
                    if (Keyboard.current.digit2Key.wasPressedThisFrame) { _spellElement = SpellElement.Magic; OnSpellElementChosen(); return; }
                    if (Keyboard.current.digit3Key.wasPressedThisFrame) { _spellElement = SpellElement.Fire; OnSpellElementChosen(); return; }
                    if (Keyboard.current.digit4Key.wasPressedThisFrame) { _spellElement = SpellElement.Water; OnSpellElementChosen(); return; }
                    if (Keyboard.current.digit5Key.wasPressedThisFrame) { _spellElement = SpellElement.Air; OnSpellElementChosen(); return; }
                    if (Keyboard.current.digit6Key.wasPressedThisFrame) { _spellElement = SpellElement.Earth; OnSpellElementChosen(); return; }
                    if (Keyboard.current.escapeKey.wasPressedThisFrame) { SetMode(PlanMode.Idle); _hud.HidePrompt(); }
                }
                return;
            }

            // Teclas de alvo na mira de magia (após escolher elemento) — atalho dos botões
            if (_mode == PlanMode.SpellTargetPicking)
            {
                if (Keyboard.current != null)
                {
                    if (Keyboard.current.sKey.wasPressedThisFrame) { OnSpellTargetSelf(); return; }
                    if (Keyboard.current.uKey.wasPressedThisFrame) { OnSpellTargetUnit(); return; }
                    if (Keyboard.current.tKey.wasPressedThisFrame) { OnSpellTargetTile(); return; }
                    if (Keyboard.current.escapeKey.wasPressedThisFrame) { SetMode(PlanMode.Idle); }
                }
                return;
            }

            // Stepper de mana: +/- ajustam, Enter conjura, Esc volta ao alvo
            if (_mode == PlanMode.SpellManaPicking)
            {
                if (Keyboard.current != null)
                {
                    var kb = Keyboard.current;
                    if (kb.numpadPlusKey.wasPressedThisFrame || kb.equalsKey.wasPressedThisFrame) { OnManaPowerPlus(); return; }
                    if (kb.numpadMinusKey.wasPressedThisFrame || kb.minusKey.wasPressedThisFrame) { OnManaPowerMinus(); return; }
                    if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) { OnManaCast(); return; }
                    if (kb.escapeKey.wasPressedThisFrame) { OnManaBack(); return; }
                }
                return;
            }

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
            else if (_mode == PlanMode.AttackUnitPicking || _mode == PlanMode.SpellUnitPicking)
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
                        if (_mode == PlanMode.SpellUnitPicking)
                            CommitSpellUnit(hoveredUnit);
                        else
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
            else if (_mode == PlanMode.AttackTilePicking || _mode == PlanMode.SpellTilePicking)
            {
                var hovered = HitAnchor();
                bool valid = hovered.HasValue && _attackable.Contains(hovered.Value);
                if (valid)
                {
                    PlaceGhost(_cursorGhost, hovered.Value);
                    if (Mouse.current.leftButton.wasPressedThisFrame)
                    {
                        if (_mode == PlanMode.SpellTilePicking)
                            CommitSpellTile(hovered.Value);
                        else
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

        private void OnSpellElementChosen()
        {
            _hud.HidePrompt();
            _hud.LogAction($"<b>Magia:</b> {SpellBook.ElementName(_spellElement)}", _controlled);
            _hud.ShowSubMenu(_hud.SpellTypeMenuPanel, "Magia > Alvo");
            SetMode(PlanMode.SpellTargetPicking);
        }

        // ------------------------------------------------------------------ SYNC UI

        private void SyncButtonStates()
        {
            if (_controlled == null) return;
            bool hasMana = _controlled.currentMana > 0;
            bool hasSequence = _controlled.actionSequence.Count > 0;
            _hud.SetMoveState(
                canAdd:    _controlled.remainingAP > 0,
                count:     _controlled.plannedMoveCount,
                isPicking: _mode == PlanMode.MovePicking);
            _hud.SetAttackUnitState(
                canAdd:    _controlled.remainingAP > 0,
                count:     _controlled.plannedAttacks.Count,
                isPicking: _mode == PlanMode.AttackUnitPicking);
            _hud.SetAttackTileState(
                canAdd:    _controlled.remainingAP > 0,
                count:     _controlled.plannedAttacks.Count,
                isPicking: _mode == PlanMode.AttackTilePicking);
            _hud.SetMagicState(
                canAdd:    _controlled.remainingAP > 0 && hasMana,
                isPicking: _mode == PlanMode.SpellElementPicking);
            _hud.SetConcentrateState(
                canAdd:    _controlled.remainingBAP > 0
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
            g.transform.localScale = new Vector3(fp, fp, 1f);
            g.SetActive(false);
            return g;
        }

        private void PlaceGhost(GameObject g, Vector2Int anchor)
        {
            int fp = _controlled != null ? _controlled.stats.Footprint : AttributeStats.DefaultFootprint;
            g.transform.position = _grid.AnchorToWorldCenter(anchor, fp);
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

        private static Color SeqLabelSpell => Tuning.Get().seqChipSpellColor;

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
                bool isSpell = seq != null && i < seq.Count && seq[i].Type == ActionType.Spell;
                Vector3 ghostPos = _pathGhosts[i].transform.position;
                string num = (i + 1).ToString();
                Color col = isSpell ? SeqLabelSpell : isAtk ? SeqLabelAtk : SeqLabelMove;

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
            Vector3 world = _cam.ScreenToWorldPoint(
                new Vector3(screen.x, screen.y, -_cam.transform.position.z));

            var u = Unit.PickAtWorld(new Vector2(world.x, world.y));
            // No picking de ataque só inimigos vivos do time oposto são alvos.
            if (u != null && !u.IsDead && _controlled != null && u.team != _controlled.team)
                return u;
            return null;
        }

        private Vector2Int? HitCell()
        {
            Vector2 screen = Mouse.current.position.ReadValue();
            Vector3 world = _cam.ScreenToWorldPoint(
                new Vector3(screen.x, screen.y, -_cam.transform.position.z));
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
