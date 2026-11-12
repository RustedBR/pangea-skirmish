using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Unidade isométrica 2D (TinyTactics). SpriteRenderer com 4 direções (NE/SE + flip
    /// horizontal para NW/SW) e animações walking/attack + poses. A lógica de grid/turnos
    /// é independente; aqui só o render/movimento isométrico.
    /// </summary>
    public class Unit : MonoBehaviour
    {
        public string unitName = "Unit";
        public string definitionId = ""; // unitId da UnitDefinition (se spawnada via SpawnUnit)
        public Team team = Team.Player;
        public bool isPlayerCharacter = false;
        public string weaponId = ""; // id da arma equipada (setado antes de Init)

        // ---- Campos de identidade MP (Fase 5 — preenchidos no SpawnUnitClientRpc) ----
        // Não usados pela lógica SP. Fase 6 substituirá IsHostileTo usando estes campos.
        /// <summary>ClientId NGO do dono desta unidade (0 em SP).</summary>
        public ulong ownerId = 0;
        /// <summary>Time numérico (TDM: 0 ou 1; FFA: índice do slot; SP: irrelevante).</summary>
        public int teamId = 0;
        public AttributeStats stats = new AttributeStats();

        public Vector2Int anchor;
        /// <summary>Onde a unidade foi posicionada no início (volta para cá a cada camada do Coop).</summary>
        public Vector2Int spawnAnchor;
        public Vector2Int plannedAnchor;
        public Vector2Int plannedBonusAnchor;
        public int plannedMoveCount;
        public readonly List<Vector2Int>    plannedPath    = new List<Vector2Int>();
        public readonly List<PlannedAttack>   plannedAttacks  = new List<PlannedAttack>();
        public readonly List<ScheduledAction> actionSequence  = new List<ScheduledAction>();
        public int plannedAttackCount => plannedAttacks.Count;
        public bool bonusDamageThisAttack;
        public bool aimBonusThisAttack;
        public bool hasPlannedBonus;
        /// <summary>Ticks livres na fila deste ponto de decisão (GDD-014; antigo PA).</summary>
        public int remainingAP;
        /// <summary>Estâmina ainda livre NESTE plano (currentStamina − custo planejado). Antigo PAB.</summary>
        public int remainingBAP;
        /// <summary>Estâmina atual: reserva do Corpo, paga ações bônus e reações (GDD-016 §3).</summary>
        public int currentStamina;

        // ── Semi-turno por ticks (GDD-014) ──
        /// <summary>Fila comprometida. Não vazia = unidade ocupada (não decide neste ponto).</summary>
        public readonly List<QueuedAction> queue = new List<QueuedAction>();
        public bool IsBusy => queue.Count > 0;
        /// <summary>Inimigos em cujo alcance esta unidade ENTROU andando e ainda não atacou (gatilho de AoO ao sair).</summary>
        public readonly HashSet<Unit> enteredReach = new HashSet<Unit>();
        public int QueueTicksRemaining
        {
            get { int t = 0; foreach (var q in queue) t += q.Remaining; return t; }
        }

        /// <summary>Ticks que o plano atual (actionSequence) ocupa.</summary>
        public int PlannedTicks()
        {
            int t = 0;
            foreach (var a in actionSequence) t += TickRules.Duration(this, a);
            return t;
        }

        /// <summary>Recalcula remainingAP = maxQueueTicks − ticks planejados.</summary>
        public void RecomputeTickBudget()
        {
            remainingAP  = Tuning.Get().maxQueueTicks - PlannedTicks();
            remainingBAP = currentStamina - PlannedStaminaCost();
            reservedMana = PlannedManaCost();
        }

        /// <summary>Estâmina que o plano atual gasta (derivado do plano: igual em toda máquina).</summary>
        public int PlannedStaminaCost()
        {
            int c = 0;
            foreach (var a in actionSequence) c += TickRules.StaminaCost(this, a);
            return c;
        }

        /// <summary>Mana que o plano atual gasta (magias Elementais). Derivado do plano: igual em toda máquina.</summary>
        public int PlannedManaCost()
        {
            int c = 0;
            foreach (var a in actionSequence) c += TickRules.ManaCost(this, a);
            return c;
        }

        /// <summary>Cabe esta magia no orçamento de ticks e no recurso dela? (motivo em 'why' quando não cabe)</summary>
        public bool CanFitSpell(SpellDef s, out string why)
        {
            why = null;
            if (s == null) { why = "magia desconhecida"; return false; }
            int d = TickRules.Duration(this, ActionType.Spell, false, s);
            if (remainingAP < d) { why = $"sem tick livre ({d} tick{(d == 1 ? "" : "s")})"; return false; }
            if (s.IsPhysical)
            {
                if (remainingBAP < s.cost) { why = $"Estâmina insuficiente ({remainingBAP}/{s.cost})"; return false; }
            }
            else if (AvailableMana < s.cost) { why = $"Mana insuficiente ({AvailableMana}/{s.cost})"; return false; }
            return true;
        }

        /// <summary>Recupera Estâmina (regeneração por tick), até o máximo.</summary>
        public void GainStamina(int amount) =>
            currentStamina = Mathf.Clamp(currentStamina + amount, 0, stats.MaxStamina);

        /// <summary>Cabe uma ação de 'type' no orçamento restante?</summary>
        public bool CanFit(ActionType type, bool isBonus = false) =>
            remainingAP >= TickRules.Duration(this, type, isBonus);

        /// <summary>Converte o plano (actionSequence + listas) na fila de ticks. Plano vazio = Esperar.</summary>
        public void BuildQueueFromPlan()
        {
            queue.Clear();
            // Compromete a Estâmina do plano (ações bônus). Feito aqui, a partir do plano,
            // para valer igual em todas as máquinas do lockstep.
            currentStamina = Mathf.Max(0, currentStamina - PlannedStaminaCost());
            // Mana das magias Elementais também é comprometida aqui (GDD-019 §1: gasta mesmo se interrompida).
            currentMana = Mathf.Max(0, currentMana - PlannedManaCost());
            reservedMana = 0;
            foreach (var a in actionSequence)
            {
                var q = new QueuedAction { Type = a.Type, IsBonus = a.IsBonus, IsAimed = a.IsAimed,
                                           Duration = TickRules.Duration(this, a) };
                if (a.Type == ActionType.Spell)
                {
                    if (a.Index >= plannedSpells.Count) continue;
                    q.Spell = plannedSpells[a.Index];
                }
                if (a.Type == ActionType.Move)
                {
                    if (a.Index >= plannedPath.Count) continue;
                    q.Dest = plannedPath[a.Index];
                }
                else if (a.Type == ActionType.Attack)
                {
                    if (a.Index >= plannedAttacks.Count) continue;
                    q.Attack = plannedAttacks[a.Index];
                }
                queue.Add(q);
            }
            if (queue.Count == 0)
                queue.Add(new QueuedAction { Type = ActionType.Wait, Duration = TickRules.Duration(this, ActionType.Wait, false) });
        }

        /// <summary>Custo em Estâmina de um incremento: 0 = golpe poderoso, 1 = mirar.</summary>
        public static int IncrementCost(int kind) =>
            kind == 0 ? Tuning.Get().staminaPowerStrike : Tuning.Get().staminaAim;

        /// <summary>
        /// A próxima ação da fila (que ainda não começou) pode receber o incremento?
        /// Só ataques; golpe poderoso uma vez, mirar uma vez; exige Estâmina.
        /// </summary>
        public bool CanIncrementNext(int kind)
        {
            if (IsDead || queue.Count == 0) return false;
            var q = queue[0];
            if (q.Type != ActionType.Attack || q.Elapsed > 0) return false;
            if (kind == 0 && q.IsBonus) return false;
            if (kind == 1 && q.IsAimed) return false;
            return currentStamina >= IncrementCost(kind);
        }

        /// <summary>Aplica o incremento (golpe poderoso alonga o ataque em ticks). Determinístico.</summary>
        public bool TryIncrementNext(int kind)
        {
            if (!CanIncrementNext(kind)) return false;
            var q = queue[0];
            if (kind == 0) { q.IsBonus = true; q.Duration = TickRules.Duration(this, ActionType.Attack, true); }
            else q.IsAimed = true;
            currentStamina -= IncrementCost(kind);
            return true;
        }

        /// <summary>Insere uma espera no início da fila (custo de reação).</summary>
        public void DelayQueue(int ticks)
        {
            if (ticks <= 0) return;
            queue.Insert(0, new QueuedAction { Type = ActionType.Wait, Duration = ticks });
        }

        // ── Ações Bônus (rework): reações ──
        /// <summary>Reações disponíveis neste round (1/round, renova em StartRound).</summary>
        public int remainingReactions = 1;
        /// <summary>Bônus de dodge temporário desta reação (somado ao DodgeChance no RollHit).</summary>
        public float dodgeReactBonus;
        /// <summary>Redução de dano da reação de Bloqueio (0..1, aplicada no dano final).</summary>
        public int blockFlat;

        public bool CanReact() => currentStamina >= Tuning.Get().staminaReaction && remainingReactions > 0;
        public void ConsumeReaction()
        {
            currentStamina   = Mathf.Max(0, currentStamina - Tuning.Get().staminaReaction);
            remainingReactions = Mathf.Max(0, remainingReactions - 1);
        }
        public void ResetReactionsForRound() => remainingReactions = 1;

        public bool hasPlannedMove   => plannedMoveCount   > 0;
        public bool hasPlannedAttack => plannedAttackCount > 0;
        public int currentHP;
        public int currentMana;
        public int reservedMana;
        public int rolledInitiative;
        public int plannedConcentrations;
        /// <summary>Magias planejadas (GDD-019). ScheduledAction.Index aponta aqui quando Type == Spell.</summary>
        public readonly List<PlannedSpell>   plannedSpells = new List<PlannedSpell>();
        public readonly List<StatusEffect>   statusEffects = new List<StatusEffect>();

        public int AvailableMana => currentMana - reservedMana;

        // ---- Estado de magia (GDD-019). Por ticks, sobrevive entre pontos de decisão. ----
        [System.NonSerialized] public float furyReady;      // Fúria: multiplicador do próximo ataque (0 = nada)
        [System.NonSerialized] public float trueAimCrit;    // Olho certeiro: bônus de crítico do próximo ataque (0 = nada)
        [System.NonSerialized] public int   stoneSkinTicks; // Pele de pedra
        [System.NonSerialized] public float stoneSkinMult = 1f;
        [System.NonSerialized] public int   burningTicks, burningDamage; // Queimando
        [System.NonSerialized] public int   soakedTicks;    // Encharcado: −1 tile de andada
        [System.NonSerialized] public int   foresightTicks; // Visão arcana

        /// <summary>Andada com Lama (= 1) e Encharcado (−1). GDD-019 §3.</summary>
        public int EffectiveMoveStep
        {
            get
            {
                if (SpellField.Current != null && SpellField.Current.InZone(this, SpellEffect.ZoneMud)) return 1;
                return Mathf.Max(1, stats.MoveStep - (soakedTicks > 0 ? 1 : 0));
            }
        }

        /// <summary>Texto curto dos estados de magia ativos, para a ficha da HUD.</summary>
        public string SpellStateSummary()
        {
            var p = new System.Collections.Generic.List<string>();
            if (furyReady > 0f) p.Add("Fúria");
            if (trueAimCrit > 0f) p.Add("Olho certeiro");
            if (stoneSkinTicks > 0) p.Add($"Pele de pedra {stoneSkinTicks}t");
            if (burningTicks > 0) p.Add($"Queimando {burningTicks}t");
            if (soakedTicks > 0) p.Add($"Encharcado {soakedTicks}t");
            if (foresightTicks > 0) p.Add($"Visão arcana {foresightTicks}t");
            return string.Join(" · ", p);
        }

        /// <summary>Recupera Mana (regeneração por tick / Concentrar), até o máximo.</summary>
        public void GainMana(int amount) =>
            currentMana = Mathf.Clamp(currentMana + amount, 0, stats.MaxMana);

        /// <summary>
        /// Embotamento dos sentidos (GDD-013 §3, GDD-019 §4): com Mana abaixo de dullManaFraction do máximo,
        /// a mente — onde ficam os sentidos — perde alcance e acerto. Só afeta a unidade, não a tela.
        /// </summary>
        public bool IsDulled
        {
            get
            {
                int max = stats.MaxMana;
                if (max <= 0) return false;
                return currentMana < Mathf.CeilToInt(max * Tuning.Get().dullManaFraction);
            }
        }

        /// <summary>Alcance de ataque com o embotamento aplicado. Corpo a corpo (1) nunca cai.</summary>
        public int EffectiveAttackRange =>
            IsDulled ? Mathf.Max(1, stats.AttackRange - Tuning.Get().dullRangePenalty) : stats.AttackRange;

        /// <summary>Chance de acerto com o embotamento aplicado.</summary>
        public float EffectiveHitChance =>
            Mathf.Clamp01(stats.HitChance - (IsDulled ? Tuning.Get().dullHitPenalty : 0f)
                          - (SpellField.Current != null && SpellField.Current.InZone(this, SpellEffect.ZoneSteam) ? 0.20f : 0f));

        public Vector2Int FinalAnchor => hasPlannedBonus ? plannedBonusAnchor : plannedAnchor;

        /// <summary>
        /// Atributo efetivo = base + bônus de buffs ativos (AttrBuff).
        /// Atributo efetivo (base + buffs de status).
        /// </summary>
        public int EffectiveStat(Attr attr)
        {
            int baseVal;
            switch (attr)
            {
                case Attr.STR: baseVal = Mathf.RoundToInt(stats.STR); break;
                case Attr.VIT: baseVal = Mathf.RoundToInt(stats.VIT); break;
                case Attr.DEX: baseVal = Mathf.RoundToInt(stats.DEX); break;
                case Attr.AGI: baseVal = Mathf.RoundToInt(stats.AGI); break;
                case Attr.INT: baseVal = Mathf.RoundToInt(stats.INT); break;
                case Attr.WIS: baseVal = Mathf.RoundToInt(stats.WIS); break;
                default: baseVal = 0; break;
            }
            return baseVal + StatusEffectSystem.AttrBonus(statusEffects, attr);
        }

        public static event System.Action<Unit, int, bool> OnDamageTaken;

        private GridManager _grid;
        private SpriteRenderer _sr;
        private WeaponOverlay _weapon;
        private UnitModel3D _model;   // corpo 3D (mundo 3D); o sprite vira só estado
        /// <summary>Corpo 3D da unidade (null se o modelo não carregou).</summary>
        public UnitModel3D Model3D => _model;

        /// <summary>Animação de uma reação defensiva (GDD-017): Bloquear ergue o escudo, Esquiva e Passo desviam.</summary>
        public void PlayReactionAnim(ReactionKind kind)
        {
            if (_model == null) return;
            if (kind == ReactionKind.Block || kind == ReactionKind.Intercept) _model.PlayBlock();
            else if (kind == ReactionKind.Dodge || kind == ReactionKind.Step) _model.PlayDodge();
        }
        private readonly Dictionary<string, Sprite> _frames = new Dictionary<string, Sprite>();
        private Color _baseTint = Color.white;
        private string _baseDir = "SE";  // NE ou SE
        private bool _flip;               // espelha p/ NW/SW
        private string _currentState = "idle";
        private int    _currentFrame = 0;
        private Coroutine _idleCoroutine;

        public bool IsDead => currentHP <= 0;

        /// <summary>
        /// Retorna true se <paramref name="other"/> é inimigo desta unidade.
        /// SP: team != other.team  |  MP-TDM: teamId != other.teamId  |  MP-FFA: ownerId != other.ownerId
        /// </summary>
        public bool IsHostileTo(Unit other)
        {
            if (other == null) return false;
            if (!RuntimeMultiplayerSession.IsMultiplayer)
                return team != other.team;
            // Times (PvP Times e Coop) → por teamId; todos contra todos → por dono.
            if (GameModes.IsTeamBased)
                return teamId != other.teamId;
            return ownerId != other.ownerId;
        }
        public Vector2Int CenterCell => anchor;

        public Sprite CurrentSprite => _sr != null ? _sr.sprite : null;

        private float _spriteH = 1f;
        public Vector3 HeadWorld => transform.position + new Vector3(0f, _spriteH * Tuning.Get().headHeightRatio, 0f);

        // Escalas/cores de unidade agora vivem no GameTuning (seção Unidades: visual).
        private Color _footprintBaseColor = Color.white;
        private bool _attackMarked; // alvo de um ataque confirmado

        /// <summary>
        /// Unidade viva cujo collider 2D está sob o ponto do mundo (a da frente, por sortingOrder).
        /// Os colliders ficam em filhos (Sprite/Footprint), então o SpriteRenderer também — por isso
        /// GetComponentInChildren (GetComponent no raiz retornaria null).
        /// </summary>
        public static Unit PickAtWorld(Camera cam, Vector2 screen)
        {
            if (cam == null) return null;
            Ray ray = cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));
            RaycastHit[] hits = Physics.RaycastAll(ray);
            Unit best = null;
            int bestOrder = int.MinValue;
            foreach (var h in hits)
            {
                var u = h.collider.GetComponentInParent<Unit>();
                if (u == null || u.IsDead) continue;
                var sr = u.GetComponentInChildren<SpriteRenderer>();
                int order = sr != null ? sr.sortingOrder : 0;
                if (order > bestOrder) { best = u; bestOrder = order; }
            }
            return best;
        }

        // ------------------------------------------------------------------ INIT

        public void Init(GridManager grid, Vector2Int startAnchor, Color teamColor, string resourcePath)
        {
            _grid      = grid;
            anchor     = startAnchor;
            spawnAnchor = startAnchor;
            plannedAnchor      = startAnchor;
            plannedBonusAnchor = startAnchor;
            currentHP    = stats.MaxHP;
            currentMana  = stats.MaxMana;
            remainingAP  = Tuning.Get().maxQueueTicks;
            currentStamina = stats.MaxStamina;
            remainingBAP = currentStamina;

            // A unidade vira filha do _gridRig (organização/pivot; ele não gira
            // mais fisicamente — ver GridManager _viewOrientation).
            if (grid != null && grid.GridRig != null)
                transform.SetParent(grid.GridRig, true);

            // Fix (2026-07-20): BillboardFace nunca era de fato adicionado a
            // nenhum GameObject de unidade (só existia como classe, citada em
            // comentário) — por isso footprint/sprite/mira nunca apareciam com
            // a câmera reto de cima. Aplicado no objeto principal da Unit: como
            // footprint/sprite/barras são filhos com rotação local identity,
            // todos herdam o alinhamento com a câmera automaticamente.
            gameObject.AddComponent<BillboardFace>();

            // Fix (2026-07-29): rastreio independente do id de rede, para que a
            // rotação de vista alcance também as unidades do Sandbox/editor de mapa
            // (que nunca passam por PlacementSync e portanto não têm unitId).
            UnitRegistry.Track(this);

            // Inimigos: tom escurecido para diferenciar (reuso de classe heroica).
            _baseTint = team == Team.Enemy ? Tuning.Get().enemyTint : Color.white;

            BuildVisual(resourcePath);
            _model = UnitModel3D.Attach(this, _sr, _footprint, teamColor, resourcePath, _spriteH,
                                        WeaponCatalog.Get(weaponId));
            RefreshFacing();
            SnapToAnchor();
            StartIdleAnim();
        }

        private void BuildVisual(string resourcePath)
        {
            var loaded = Resources.LoadAll<Sprite>(resourcePath);
            foreach (var s in loaded)
                if (!_frames.ContainsKey(s.name)) _frames.Add(s.name, s);

            var T = Tuning.Get();
            float footprintScale = stats.Footprint * T.footprintScalePerFootprint;
            float spriteScale    = stats.Footprint * T.spriteScalePerFootprint;
            _spriteH = spriteScale;

            // Footprint (diamante BRANCO tingido pela cor do time — tem o tamanho do footprint;
            // a cor pode ser trocada por SetTargeted para a mira de ataque).
            var footprintGo = new GameObject("Footprint");
            footprintGo.transform.SetParent(transform, false);
            _footprint = footprintGo.AddComponent<SpriteRenderer>();
            _footprint.sprite = BuildFootprintSprite();
            _footprintBaseColor = team == Team.Player
                ? T.playerFootprintColor
                : T.enemyFootprintColor;
            // O slider footprintAlpha do GameTuning é a fonte de verdade do alpha do
            // losango (a cor do time define só o RGB). Antes o alpha vinha embutido na
            // cor e o slider não fazia nada.
            _footprintBaseColor.a = T.footprintAlpha;
            _footprint.color = _footprintBaseColor;
            footprintGo.transform.localScale = new Vector3(footprintScale, footprintScale, 1f);
            footprintGo.transform.localPosition = Vector3.zero;

            // Sombra escura sob a placa: separa a cor do time da grama e desenha a divisa entre
            // unidades coladas (as duas sombras se encostam, as placas não).
            var shadowGo = new GameObject("FootprintShadow");
            shadowGo.transform.SetParent(footprintGo.transform, false);
            shadowGo.transform.localScale = new Vector3(1.07f, 1.07f, 1f);
            _footprintShadow = shadowGo.AddComponent<SpriteRenderer>();
            _footprintShadow.sprite = BuildFootprintShadowSprite();
            _footprintShadow.color = new Color(0f, 0f, 0f, 0.38f);

            var spriteGo = new GameObject("Sprite");
            spriteGo.transform.SetParent(transform, false);
            spriteGo.transform.localScale = new Vector3(spriteScale, spriteScale, 1f);
            // O footprint passou a ser centrado na célula (subiu 0.5*footprintScale vs. o layout
            // antigo); o personagem sobe a mesma medida para manter a relação aprovada.
            spriteGo.transform.localPosition =
                new Vector3(0f, T.spriteFootOffsetRatio * spriteScale + 0.5f * footprintScale, 0f);
            _sr = spriteGo.AddComponent<SpriteRenderer>();
            _sr.color = _baseTint;

            // Collider 3D no corpo (sprite) para seleção (migração XY→XZ: mundo é 3D)
            var bodyCollider = spriteGo.AddComponent<BoxCollider>();
            bodyCollider.size = new Vector3(T.bodyColliderSize.x, T.bodyColliderSize.y, 0.5f);
            bodyCollider.center = new Vector3(T.bodyColliderOffset.x, T.bodyColliderOffset.y, 0f);

            // Collider 3D achatado na base (footprint) para seleção
            var footprintCollider = footprintGo.AddComponent<BoxCollider>();
            footprintCollider.size = new Vector3(T.footprintColliderSize.x, T.footprintColliderSize.y, 0.5f);
            footprintCollider.center = new Vector3(T.footprintColliderOffset.x, T.footprintColliderOffset.y, 0f);

            // Equipar arma e aplicar dano/alcance
            EquipWeapon(spriteGo.transform);

            BuildHealthBars();

            ShowFrame("walking", 0);
        }

        // ------------------------------------------------------------------ BARRAS HP/MP (mundo)

        private Transform _barsRoot;
        private SpriteRenderer _hpFill, _mpFill, _hpTrail, _castBg, _castFill;
        private float _hpFillY, _mpFillY, _pipY, _castY;
        private float _hpTrailRatio = 1f, _hpTrailHold;
        private readonly List<SpriteRenderer> _staminaPips = new List<SpriteRenderer>();
        private TextMesh _statusText;
        private string _statusShown = "";

        /// <summary>Quantos pips de Estâmina estão acesos acima da cabeça (testes).</summary>
        public int WorldStaminaPipsLit { get; private set; }
        /// <summary>A barra de conjuração acima da cabeça está visível? (testes)</summary>
        public bool WorldCastBarVisible => _castBg != null && _castBg.enabled;
        /// <summary>Texto de estados mostrado acima da cabeça (testes).</summary>
        public string WorldStatusText => _statusShown;
        private static Sprite _sharedWhite;

        private static Sprite WhiteSprite()
        {
            if (_sharedWhite != null) return _sharedWhite;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color[16];
            for (int i = 0; i < px.Length; i++) px[i] = Color.white;
            tex.SetPixels(px); tex.Apply();
            _sharedWhite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            return _sharedWhite;
        }

        private void BuildHealthBars()
        {
            var T = Tuning.Get();
            if (!T.worldBarsEnabled) return;

            var rootGo = new GameObject("WorldBars");
            rootGo.transform.SetParent(transform, false);
            float y = _spriteH * T.headHeightRatio + T.worldBarYOffset;
            rootGo.transform.localPosition = new Vector3(0f, y, 0f);
            _barsRoot = rootGo.transform;

            Vector2 sz = T.worldBarSize;
            int sortBg = T.worldBarSortingBase;

            _hpFillY = 0f;
            _mpFillY = -(sz.y + T.worldBarGap);

            MakeBar(_barsRoot, "HpBg", _hpFillY, sz, T.worldBarBgColor, sortBg);
            // Rastro do dano: fica um instante onde a vida estava e depois escorre até a barra real.
            _hpTrail = MakeBar(_barsRoot, "HpTrail", _hpFillY, new Vector2(sz.x, sz.y * 0.8f), new Color(1f, 0.95f, 0.80f, 0.95f), sortBg + 1);
            _hpFill = MakeBar(_barsRoot, "HpFill", _hpFillY, new Vector2(sz.x, sz.y * 0.8f), T.worldHpHighColor, sortBg + 2);
            _hpTrailRatio = stats.MaxHP > 0 ? Mathf.Clamp01((float)currentHP / stats.MaxHP) : 1f;
            MakeBar(_barsRoot, "MpBg", _mpFillY, sz, T.worldBarBgColor, sortBg);
            _mpFill = MakeBar(_barsRoot, "MpFill", _mpFillY, new Vector2(sz.x, sz.y * 0.8f), T.worldMpColor, sortBg + 1);

            // Estâmina: um pip por ponto, abaixo da Mana (dourado = tem, apagado = gasto).
            _pipY = _mpFillY - (sz.y + T.worldBarGap);
            int maxPips = Mathf.Clamp(stats.MaxStamina, 0, 10);
            float pipW = Mathf.Min(0.16f, (sz.x - 0.03f * (maxPips - 1)) / Mathf.Max(1, maxPips));
            float total = maxPips * pipW + (maxPips - 1) * 0.03f;
            for (int i = 0; i < maxPips; i++)
            {
                var go = new GameObject("Pip" + i);
                go.transform.SetParent(_barsRoot, false);
                go.transform.localPosition = new Vector3(-total * 0.5f + pipW * 0.5f + i * (pipW + 0.03f), _pipY, 0f);
                go.transform.localScale = new Vector3(pipW, sz.y * 0.7f, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = WhiteSprite(); sr.sortingOrder = sortBg + 1;
                _staminaPips.Add(sr);
            }

            // Conjuração: barra acima da vida, só enquanto conjura (enche até a magia sair).
            _castY = _hpFillY + sz.y + T.worldBarGap;
            _castBg = MakeBar(_barsRoot, "CastBg", _castY, sz, T.worldBarBgColor, sortBg);
            _castFill = MakeBar(_barsRoot, "CastFill", _castY, new Vector2(sz.x, sz.y * 0.8f), new Color(0.72f, 0.48f, 1f), sortBg + 1);
            _castBg.enabled = _castFill.enabled = false;

            // Estados (Queimando, Encharcado, Pele de pedra...) em uma linha curta sob os pips.
            var stGo = new GameObject("Status");
            stGo.transform.SetParent(_barsRoot, false);
            stGo.transform.localPosition = new Vector3(0f, _pipY - 0.16f, 0f);
            _statusText = stGo.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _statusText.font = font; _statusText.fontSize = 48; _statusText.characterSize = 0.05f;
            _statusText.anchor = TextAnchor.UpperCenter; _statusText.alignment = TextAlignment.Center;
            _statusText.color = new Color(0.88f, 0.84f, 1f);
            var stMr = stGo.GetComponent<MeshRenderer>();
            stMr.sharedMaterial = font.material; stMr.sortingOrder = sortBg + 2;

            UpdateBars();
        }

        /// <summary>Pílula 9-slice do BDragon1727 p/ barras de HP/MP; null se skin desligada/sem asset (usa fallback branco).</summary>
        private static Sprite BarPillSprite()
        {
            var T = Tuning.Get();
            if (!T.uiSkinEnabled) return null;
            return UiSkin.SliceSliced(T.uiSheetPath, T.worldBarPillRect, T.worldBarPillBorder);
        }

        private SpriteRenderer MakeBar(Transform parent, string name, float y, Vector2 size, Color color, int sorting)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            var pill = BarPillSprite();
            if (pill != null)
            {
                sr.sprite = pill;
                sr.drawMode = SpriteDrawMode.Sliced;
                sr.size = size;
            }
            else
            {
                sr.sprite = WhiteSprite();
                go.transform.localScale = new Vector3(size.x, size.y, 1f);
            }
            sr.color = color;
            sr.sortingOrder = sorting;
            return sr;
        }

        private void UpdateBars()
        {
            if (_barsRoot == null) return;
            var T = Tuning.Get();
            float w = T.worldBarSize.x;
            float h = T.worldBarSize.y * 0.8f;
            bool sliced = _hpFill != null && _hpFill.drawMode == SpriteDrawMode.Sliced;

            float hpRatio = stats.MaxHP   > 0 ? Mathf.Clamp01((float)currentHP   / stats.MaxHP)   : 0f;
            float mpRatio = stats.MaxMana > 0 ? Mathf.Clamp01((float)currentMana / stats.MaxMana) : 0f;

            if (_hpFill != null)
            {
                float fw = w * hpRatio;
                if (sliced) _hpFill.size = new Vector2(fw, h);
                else _hpFill.transform.localScale = new Vector3(fw, h, 1f);
                _hpFill.transform.localPosition = new Vector3(-w * 0.5f + fw * 0.5f, _hpFillY, 0f);
                _hpFill.color = Color.Lerp(T.worldHpLowColor, T.worldHpHighColor, hpRatio);
            }
            if (_mpFill != null)
            {
                float fw = w * mpRatio;
                if (sliced) _mpFill.size = new Vector2(fw, h);
                else _mpFill.transform.localScale = new Vector3(fw, h, 1f);
                _mpFill.transform.localPosition = new Vector3(-w * 0.5f + fw * 0.5f, _mpFillY, 0f);
                // Embotado (Mana < 25%): a barra de Mana fica roxa e pulsa.
                _mpFill.color = IsDulled ? Color.Lerp(new Color(0.75f, 0.45f, 1f), T.worldMpColor, 0.5f + 0.5f * Mathf.Sin(Time.time * 6f)) : T.worldMpColor;
            }

            // Rastro do dano: segura 0,35 s onde a vida estava e depois escorre.
            if (_hpTrail != null)
            {
                if (_hpTrailShownTarget >= 0f && hpRatio < _hpTrailShownTarget) _hpTrailHold = 0.35f;   // novo golpe: segura de novo
                _hpTrailShownTarget = hpRatio;
                if (hpRatio >= _hpTrailRatio) _hpTrailRatio = hpRatio;                // cura: acompanha na hora
                else if (_hpTrailHold > 0f) _hpTrailHold -= Time.deltaTime;
                else _hpTrailRatio = Mathf.MoveTowards(_hpTrailRatio, hpRatio, Time.deltaTime * 0.9f);
                float tw = w * _hpTrailRatio;
                if (sliced) _hpTrail.size = new Vector2(tw, h);
                else _hpTrail.transform.localScale = new Vector3(tw, h, 1f);
                _hpTrail.transform.localPosition = new Vector3(-w * 0.5f + tw * 0.5f, _hpFillY, 0f);
                _hpTrail.enabled = _hpTrailRatio > hpRatio + 0.001f;
            }

            // Estâmina em pips.
            int lit = 0;
            for (int i = 0; i < _staminaPips.Count; i++)
            {
                bool on = i < currentStamina;
                if (on) lit++;
                _staminaPips[i].color = on ? new Color(1f, 0.82f, 0.30f) : new Color(0.22f, 0.20f, 0.16f, 0.85f);
            }
            WorldStaminaPipsLit = lit;

            // Barra de conjuração (só magia Elemental: é o que pode ser interrompido).
            if (_castBg != null)
            {
                bool casting = false; float prog = 0f; Color cc = new Color(0.72f, 0.48f, 1f);
                if (!IsDead && queue.Count > 0 && queue[0].Type == ActionType.Spell)
                {
                    var d = queue[0].Spell.Def;
                    if (d != null && !d.IsPhysical)
                    {
                        casting = true;
                        prog = queue[0].Duration > 0 ? Mathf.Clamp01((float)queue[0].Elapsed / queue[0].Duration) : 1f;
                        cc = ElementInfo.Color(d.PrimaryElement);
                    }
                }
                _castBg.enabled = _castFill.enabled = casting;
                if (casting)
                {
                    float cw = Mathf.Max(0.04f, w * prog);
                    if (sliced) _castFill.size = new Vector2(cw, h);
                    else _castFill.transform.localScale = new Vector3(cw, h, 1f);
                    _castFill.transform.localPosition = new Vector3(-w * 0.5f + cw * 0.5f, _castY, 0f);
                    _castFill.color = cc;
                }
            }

            // Estados em texto curto.
            if (_statusText != null)
            {
                string st = IsDead ? "" : WorldStatusLine();
                if (st != _statusShown) { _statusShown = st; _statusText.text = st; }
            }
        }

        private float _hpTrailShownTarget = -1f;   // última razão de vida vista pela barra

        /// <summary>Estados visíveis acima da cabeça, curtos: "Queimando 3 · Encharcado 2".</summary>
        private string WorldStatusLine()
        {
            var p = new List<string>();
            if (burningTicks > 0) p.Add($"Queimando {burningTicks}");
            if (soakedTicks > 0) p.Add($"Encharcado {soakedTicks}");
            if (stoneSkinTicks > 0) p.Add($"Pele de pedra {stoneSkinTicks}");
            if (furyReady > 0f) p.Add("Fúria");
            if (trueAimCrit > 0f) p.Add("Olho certeiro");
            if (foresightTicks > 0) p.Add($"Visão {foresightTicks}");
            if (blockFlat > 0) p.Add($"Escudo −{blockFlat}");
            if (IsDulled) p.Add("Embotado");
            return string.Join(" · ", p);
        }

        private void LateUpdate()
        {
            if (_barsRoot != null) UpdateBars();
            BobMarker(_activeMarker);
            BobMarker(_targetMarker);
        }

        private void OnDestroy() => UnitRegistry.Untrack(this);

        // ------------------------------------------------------------------ MARCADORES NO MUNDO

        private GameObject _activeMarker, _targetMarker;

        private GameObject EnsureMarker(ref GameObject marker, string name, Color color)
        {
            if (marker != null) return marker;
            var T = Tuning.Get();
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            var s = UiSkin.Slice(T.uiSheetPath, T.markerArrowRect);
            sr.sprite = s != null ? s : UiSkin.FallbackDownArrow();
            sr.color = color;
            sr.sortingOrder = T.markerSortingOrder;
            float y = _spriteH * T.headHeightRatio + T.markerYOffset;
            go.transform.localPosition = new Vector3(0f, y, 0f);
            go.transform.localScale = Vector3.one * T.markerScale;
            marker = go;
            return go;
        }

        private void ShowMarker(ref GameObject marker, string name, bool on, Color color)
        {
            if (!Tuning.Get().worldIndicatorsEnabled || !on)
            {
                if (marker != null) marker.SetActive(false);
                return;
            }
            var m = EnsureMarker(ref marker, name, color);
            m.GetComponent<SpriteRenderer>().color = color;
            m.SetActive(true);
        }

        private void BobMarker(GameObject m)
        {
            if (m == null || !m.activeSelf) return;
            var T = Tuning.Get();
            float baseY = _spriteH * T.headHeightRatio + T.markerYOffset;
            var p = m.transform.localPosition;
            p.y = baseY + Mathf.Sin(Time.time * T.markerBobSpeed) * T.markerBobAmplitude;
            m.transform.localPosition = p;
        }

        private void EquipWeapon(Transform spriteGoTransform)
        {
            // Resolver a arma via WeaponCatalog
            var w = WeaponCatalog.Get(weaponId); // null = desarmado

            // Aplicar dano da arma aos stats (alcance é ADITIVO: base + arma)
            var tuning = RuntimeTuning.Active ?? Resources.Load<GameTuning>("GameTuning");
            int baseRange = tuning != null ? tuning.baseAttackRange : 1;
            if (w != null)
            {
                stats.WeaponDamage = w.damage;
                stats.AttackTicks = Mathf.Max(1, w.attackTicks);
                stats.AttackRange = baseRange + w.range;
                stats.strScalesDamage = w.range <= (tuning != null ? tuning.strDamageMaxRange : 4);
                stats.dexScalesDamage = !stats.strScalesDamage;   // à distância: DEX no lugar da STR
            }
            else
            {
                // Sem arma: usar valores desarmados do GameTuning
                stats.WeaponDamage = tuning != null ? tuning.unarmedDamage : 1;
                stats.AttackTicks = tuning != null ? Mathf.Max(1, tuning.ticksUnarmed) : 1;
                stats.AttackRange = baseRange + (tuning != null ? tuning.unarmedRange : 0);
                stats.strScalesDamage = true; // unarmed sempre escala com STR
                stats.dexScalesDamage = false;
            }

            // Criar overlay apenas se houver arma E o overlay estiver ligado no GameTuning.
            // Os sprites TinyTactics JÁ trazem a arma desenhada e animada nos frames de corpo;
            // com o overlay desligado (padrão), a arma do próprio corpo é usada e não fica desalinhada.
            if (w != null && (tuning == null || tuning.weaponOverlayEnabled))
            {
                _weapon = gameObject.AddComponent<WeaponOverlay>();
                _weapon.Init(w.id, spriteGoTransform);
            }
        }

        private SpriteRenderer _footprint;

        private SpriteRenderer _footprintShadow;
        private static Sprite _sharedFootprintShadow;
        /// <summary>Losango cheio (mesma proporção do footprint) usado como sombra/contorno escuro.</summary>
        private static Sprite BuildFootprintShadowSprite()
        {
            if (_sharedFootprintShadow != null) return _sharedFootprintShadow;
            const int W = 64, H = 32;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float d = Mathf.Abs(x - 32) / 32f + Mathf.Abs(y - 16) / 16f;
                tex.SetPixel(x, y, d <= 0.90f ? Color.white : Color.clear);
            }
            tex.Apply();
            _sharedFootprintShadow = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 32);
            return _sharedFootprintShadow;
        }

        private static Sprite _sharedFootprint;
        private static Sprite BuildFootprintSprite()
        {
            if (_sharedFootprint != null) return _sharedFootprint;

            // Textura 64x32 (proporção do losango isométrico) com o diamante CENTRADO e o pivot
            // no centro (0.5,0.5) → posicionado em AnchorToWorldCenter, cobre a célula exata.
            const int W = 64, H = 32;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float dx = Mathf.Abs(x - 32) / 32f;
                float dy = Mathf.Abs(y - 16) / 16f;
                // Contorno nítido + preenchimento leve: lê como "placa" no chão, não como mancha.
                float d = dx + dy;
                Color px = Color.clear;
                // Aro grosso e cheio + miolo leve. O aro termina em 0.90 (não 0.95): duas unidades
                // coladas ficam com uma faixa de chão visível entre as placas.
                if (d <= 0.90f) px = new Color(1f, 1f, 1f, d >= 0.72f ? 1f : 0.20f);
                tex.SetPixel(x, y, px);
            }
            tex.Apply();
            _sharedFootprint = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 32);
            return _sharedFootprint;
        }

        // ------------------------------------------------------------------ FRAMES

        private void ShowFrame(string state, int frame)
        {
            _currentState = state;
            _currentFrame = frame;
            if (_sr == null) return;
            string key = $"{state}{_baseDir}_{frame}";
            if (_frames.TryGetValue(key, out var sp)) _sr.sprite = sp;
            _sr.flipX = _flip;

            if (_weapon == null) return;
            if (state == "walking")
                _weapon.ShowCarry(_baseDir, _flip);
            else if (state == "damage" || state == "dead")
                _weapon.Hide();
            // "attack"/"charging"/"release": a arma é dirigida pelo Animator (PlayAttack).
        }

        private int FrameCount(string state)
        {
            int n = 0;
            while (_frames.ContainsKey($"{state}{_baseDir}_{n}")) n++;
            return n;
        }

        public void SetSelected(bool selected)
        {
            var T = Tuning.Get();
            if (_sr != null)
                _sr.color = selected ? Color.Lerp(_baseTint, T.selectedTintColor, T.selectedTintStrength) : _baseTint;
            ShowMarker(ref _activeMarker, "ActiveMarker", selected, T.activeMarkerColor);
        }

        // Mira de ataque (hover): destaca footprint + sprite. Ao sair, volta para a marca de
        // alvo confirmado (se houver) ou para o estado normal.
        public void SetTargeted(bool on)
        {
            var T = Tuning.Get();
            ApplyHighlightTint(on ? (Color?)T.targetHoverColor : (_attackMarked ? (Color?)T.attackMarkColor : null));
        }

        // Marca persistente: "esta unidade será atacada" (footprint + sprite em vermelho).
        public bool IsAttackMarked => _attackMarked;
        public void SetAttackMarked(bool on)
        {
            _attackMarked = on;
            ApplyHighlightTint(on ? (Color?)Tuning.Get().attackMarkColor : null);
            ShowMarker(ref _targetMarker, "TargetMarker", on, Tuning.Get().targetMarkerColor);
        }

        private void ApplyHighlightTint(Color? c)
        {
            if (_footprint != null) _footprint.color = c ?? _footprintBaseColor;
            if (_sr != null) _sr.color = c ?? _baseTint;
        }

        // ------------------------------------------------------------------ DIRECAO
        //
        // Fix (2026-07-30): o facing agora tem DUAS camadas.
        //
        // _facing é a direção LÓGICA — para onde a unidade encara no MUNDO. Não muda
        // quando o jogador gira a vista. _viewQuadrant guarda a orientação de vista
        // corrente. O facing de TELA (_baseDir + _flip, o que de fato é desenhado) é
        // derivado dos dois: telaDir = (_facing + _viewQuadrant) % 4.
        //
        // Antes só existia a camada de tela, e SetViewOrientation a escrevia com
        // valores absolutos assumindo que toda unidade encarava NE. Duas consequências
        // que o Marcus viu em jogo: (a) girar 360° NÃO devolvia o personagem à
        // direção inicial — ele nascia em SE (campo _baseDir) e terminava em NE;
        // (b) o facing conquistado por um movimento era descartado no Q/E seguinte.

        // Quadrantes em sentido horário na tela, na vista 0.
        private const int DirNE = 0, DirSE = 1, DirSW = 2, DirNW = 3;

        private int _facing = DirSE;    // direção lógica no mundo (default = o antigo _baseDir "SE")
        private int _viewQuadrant;      // orientação de vista atual, em passos de 90°

        /// <summary>Direção LÓGICA no mundo (0=NE, 1=SE, 2=SW, 3=NW). Invariante à vista.</summary>
        public int FacingQuadrant => _facing;

        /// <summary>
        /// Quadrante efetivamente DESENHADO na tela — facing lógico combinado com a
        /// orientação de vista. Exposto para diagnóstico e testes de orientação.
        /// </summary>
        public int ScreenFacingQuadrant => ((_facing + _viewQuadrant) % 4 + 4) % 4;

        /// <summary>
        /// Recalcula o facing de TELA a partir do facing lógico + orientação da vista.
        /// Com apenas 2 sprites base (NE/SE), os outros dois quadrantes saem por flipX.
        /// </summary>
        private void RefreshFacing()
        {
            int telaDir = ((_facing + _viewQuadrant) % 4 + 4) % 4;
            switch (telaDir)
            {
                case DirNE: _baseDir = "NE"; _flip = false; break;
                case DirSE: _baseDir = "SE"; _flip = false; break;
                case DirSW: _baseDir = "SE"; _flip = true;  break;
                default:    _baseDir = "NE"; _flip = true;  break; // DirNW
            }
            ShowFrame(_currentState, _currentFrame);
            if (_weapon != null) _weapon.RefreshDirection(_baseDir, _flip);
            if (_model != null) _model.SetScreenQuadrant(telaDir);
        }

        // Define o facing a partir do vetor de movimento.
        //
        // Fix (2026-07-29): antes da migração XY→XZ o chão era o plano XY e delta.y
        // era o eixo vertical DA TELA. Depois da migração o chão é XZ e delta.y
        // passou a ser a ALTURA do terreno — com a câmera reto de cima
        // (CameraController.Configure, Euler(90,0,0)) o vertical da tela é +Z.
        // O código continuou lendo delta.y, então em terreno plano (delta.y == 0)
        // "up" dava sempre true e TODA unidade encarava NE, qualquer que fosse o
        // rumo do passo; e descer um degrau virava a unidade para SE, fazendo o
        // facing depender da topografia em vez da direção do movimento.
        private void SetDirectionFromDelta(Vector3 delta)
        {
            if (Mathf.Abs(delta.x) < 0.0001f && Mathf.Abs(delta.z) < 0.0001f) return;
            bool right = delta.x >= 0f;
            bool up    = delta.z >= 0f;

            // O delta vem de posições FÍSICAS, então isto é a direção na TELA.
            int telaDir = up ? (right ? DirNE : DirNW)
                             : (right ? DirSE : DirSW);

            // Converte para direção LÓGICA descontando a vista, senão o facing
            // ganho num movimento seria perdido no próximo Q/E.
            _facing = ((telaDir - _viewQuadrant) % 4 + 4) % 4;
            RefreshFacing();
        }

        /// <summary>
        /// A vista girou (degrees = 0/90/180/270). Só atualiza o quadrante de vista —
        /// a direção lógica da unidade é preservada, então uma volta completa devolve
        /// o personagem exatamente à pose inicial.
        /// </summary>
        public void SetViewOrientation(int degrees)
        {
            _viewQuadrant = ((degrees / 90) % 4 + 4) % 4;
            RefreshFacing();
        }

        // ------------------------------------------------------------------ POSIÇÃO / SORTING

        public void SnapToAnchor()
        {
            transform.position = _grid.AnchorToWorldCenter(anchor, stats.Footprint);
            ApplySorting();
        }

        /// <summary>Teleporte (magia Physical-Tile): move o caster para o tile alvo.</summary>
        public void TeleportTo(Vector2Int cell)
        {
            anchor = cell;
            plannedAnchor = cell;
            plannedBonusAnchor = cell;
            SnapToAnchor();
        }

        private void ApplySorting()
        {
            int order = _grid.SortingFor(anchor, stats.Footprint);
            if (_footprint != null) _footprint.sortingOrder = 10000 + order;
            if (_footprintShadow != null) _footprintShadow.sortingOrder = 9999 + order;
            if (_sr != null) _sr.sortingOrder = 10001 + order;
            // Arma: frente/atrás do corpo é decidido por frame nos clips (WeaponSortDriver)
            if (_weapon != null) _weapon.SetSortingOrder(_sr.sortingOrder);
        }

        // ------------------------------------------------------------------ IDLE

        private void StartIdleAnim()
        {
            StopIdleAnim();
            if (IsDead) return;
            _idleCoroutine = StartCoroutine(IdleLoop());
        }

        private void StopIdleAnim()
        {
            if (_idleCoroutine != null) { StopCoroutine(_idleCoroutine); _idleCoroutine = null; }
        }

        private IEnumerator IdleLoop()
        {
            // Parado NÃO cicla a caminhada inteira: os frames de meio-de-passada (5-7)
            // são agachados/comprimidos (23-25px vs 27px) e só parecem naturais em
            // movimento — em câmera parada davam efeito de "esticar". O idle alterna
            // os dois frames de contato (0 e 4, ambos eretos) num balanço sutil.
            int n = FrameCount("walking");
            int alt = n > 4 ? 4 : 0;
            bool primeiro = true;
            while (true)
            {
                ShowFrame("walking", primeiro ? 0 : alt);
                primeiro = !primeiro;
                yield return new WaitForSeconds(Tuning.Get().idleFrameInterval);
            }
        }

        // ------------------------------------------------------------------ DANO

        public void TakeDamage(int amount, bool isCritical = false)
        {
            amount = StatusEffectSystem.ReduceIncomingDamage(statusEffects, amount);
            if (amount <= 0) return;
            currentHP = Mathf.Max(0, currentHP - amount);
            // Conjuração Elemental em andamento: acumula dano; passou do limite → interrompida (GDD-019 §1).
            if (queue.Count > 0 && queue[0].Type == ActionType.Spell)
            {
                var sd = queue[0].Spell.Def;
                if (sd != null && !sd.IsPhysical)
                {
                    queue[0].DamageTaken += amount;
                    int limit = SpellResolver.InterruptThreshold(this);
                    if (queue[0].DamageTaken >= limit) queue[0].Interrupted = true;
                    else if (!IsDead) BattleFeedback.KeptConcentration(this, queue[0].DamageTaken, limit);   // ensina a regra (GDD-019 §9.3)
                }
            }
            AudioManager.I?.Play(AudioManager.I.sfxHit);
            OnDamageTaken?.Invoke(this, amount, isCritical);
            StartCoroutine(FlashHit());
            if (_model != null) { if (IsDead) _model.PlayDeath(); else _model.PlayHit(); }
            if (IsDead)
            {
                StopIdleAnim();
                ShowFrame("dead", 0);
                if (_barsRoot != null) _barsRoot.gameObject.SetActive(false);
                if (_activeMarker != null) _activeMarker.SetActive(false);
                if (_targetMarker != null) _targetMarker.SetActive(false);
                StartCoroutine(FadeOut());
            }
        }

        /// <summary>
        /// Prepara a unidade para a próxima camada do Coop (GDD-015 §3.2): volta ao ponto de
        /// partida, limpa fila e plano, e recupera só PARTE da vida e da Mana — descer tem peso.
        /// Unidades caídas voltam de pé com <paramref name="reviveFraction"/> da vida.
        /// </summary>
        public void RestoreForNextLayer(float healFraction, float manaFraction, float reviveFraction)
        {
            bool wasDead = IsDead;
            StopAllCoroutines();
            gameObject.SetActive(true);

            int maxHP = stats.MaxHP;
            currentHP = wasDead
                ? Mathf.Max(1, Mathf.RoundToInt(maxHP * reviveFraction))
                : Mathf.Min(maxHP, currentHP + Mathf.RoundToInt(maxHP * healFraction));
            currentMana = Mathf.Min(stats.MaxMana, currentMana + Mathf.RoundToInt(stats.MaxMana * manaFraction));

            queue.Clear();
            enteredReach.Clear();
            statusEffects.Clear();
            ResetPlan();
            ResetReactionsForRound();
            _attackMarked = false;
            if (_sr != null) _sr.color = _baseTint;
            if (_footprint != null) _footprint.color = _footprintBaseColor;
            if (_barsRoot != null) _barsRoot.gameObject.SetActive(true);
            TeleportTo(spawnAnchor);
            StartIdleAnim();
        }

        private IEnumerator FlashHit()
        {
            if (_sr == null) yield break;
            var T = Tuning.Get();
            if (!IsDead) ShowFrame("damage", 0);
            _sr.color = T.hitFlashColor;
            yield return new WaitForSeconds(T.hitFlashDuration);
            if (!IsDead) _sr.color = _baseTint;
        }

        private IEnumerator FadeOut()
        {
            AudioManager.I?.Play(AudioManager.I.sfxDeath);
            if (_sr == null) yield break;
            // Com corpo 3D: primeiro ele tomba e a alma sai (UnitModel3D.PlayDeath); só depois some.
            if (_model != null) yield return new WaitForSeconds(0.75f);
            float dur = Tuning.Get().deathFadeDuration;
            float t = 0f;
            Color from   = _sr.color;
            Color fromFp = _footprint != null ? _footprint.color : Color.clear;
            while (t < dur)
            {
                t += Time.deltaTime;
                float a = 1f - t / dur;
                _sr.color = new Color(from.r, from.g, from.b, a);
                if (_footprint != null) _footprint.color = new Color(fromFp.r, fromFp.g, fromFp.b, fromFp.a * a);
                yield return null;
            }
            gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ MOVIMENTO

        public IEnumerator MoveAlongPath()
        {
            if (!hasPlannedMove) yield break;
            StopIdleAnim();
            var tuning = RuntimeTuning.Active ?? Resources.Load<GameTuning>("GameTuning");
            float moveBase = tuning != null ? tuning.moveDurationBase : 0.9f;
            float moveMin  = tuning != null ? tuning.moveDurationMin : 0.18f;
            float segDuration = Mathf.Max(moveMin, moveBase / Mathf.Max(1f, stats.AGI));
            foreach (var waypoint in plannedPath)
            {
                AudioManager.I?.Play(AudioManager.I.sfxStepGrass);
                yield return MoveSegment(waypoint, segDuration);
            }
            StartIdleAnim();
        }

        private IEnumerator MoveSegment(Vector2Int dest, float duration)
        {
            // Fix (2026-07-29): guardamos as CÉLULAS de origem e destino em vez das
            // posições de mundo. Girar a vista com Q/E no meio de um passo reindexa
            // o grid e dispara SnapToAnchor; com start/end congelados o frame
            // seguinte do lerp puxava a unidade de volta para a posição da
            // orientação antiga, e ela terminava o passo fora da malha. Recalculando
            // a partir das células, a rotação reposiciona o trajeto inteiro.
            Vector2Int fromCell = anchor;
            int fp = stats.Footprint;
            anchor = dest;
            Vector3 start = _grid.AnchorToWorldCenter(fromCell, fp);
            Vector3 end   = _grid.AnchorToWorldCenter(dest, fp);
            SetDirectionFromDelta(end - start);
            ApplySorting();

            int n = Mathf.Max(1, FrameCount("walking"));
            float fps = Mathf.Max(1f, Tuning.Get().walkAnimFps);
            float frameInterval = 1f / fps;
            float nextFrameTime = frameInterval;
            int frameIdx = 0;
            ShowFrame("walking", 0);

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                // Reavaliados a cada frame: se a vista girar no meio do passo, as
                // duas pontas do trajeto acompanham a reindexação.
                start = _grid.AnchorToWorldCenter(fromCell, fp);
                end   = _grid.AnchorToWorldCenter(dest, fp);
                transform.position = Vector3.Lerp(start, end, Mathf.SmoothStep(0f, 1f, t / duration));
                if (t >= nextFrameTime)
                {
                    frameIdx = (frameIdx + 1) % n;
                    ShowFrame("walking", frameIdx);
                    nextFrameTime += frameInterval;
                }
                yield return null;
            }
            transform.position = _grid.AnchorToWorldCenter(dest, fp);
            ShowFrame("walking", 0);
        }

        public IEnumerator MoveBonusToPlanned(float duration = -1f)
        {
            if (duration <= 0f) duration = Tuning.Get().bonusMoveDuration;
            StopIdleAnim();
            // Fix (2026-07-29): mesmas células-em-vez-de-mundo de MoveSegment.
            Vector2Int fromCell = anchor;
            Vector2Int dest = plannedBonusAnchor;
            int fp = stats.Footprint;
            anchor = dest;
            Vector3 start = _grid.AnchorToWorldCenter(fromCell, fp);
            Vector3 end   = _grid.AnchorToWorldCenter(dest, fp);
            SetDirectionFromDelta(end - start);
            ApplySorting();
            ShowFrame("walking", 0);
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                start = _grid.AnchorToWorldCenter(fromCell, fp);
                end   = _grid.AnchorToWorldCenter(dest, fp);
                transform.position = Vector3.Lerp(start, end, Mathf.SmoothStep(0f, 1f, t / duration));
                yield return null;
            }
            transform.position = _grid.AnchorToWorldCenter(dest, fp);
            StartIdleAnim();
        }

        public IEnumerator MoveToDestination(Vector2Int dest)
        {
            StopIdleAnim();
            var tuning = RuntimeTuning.Active ?? Resources.Load<GameTuning>("GameTuning");
            float moveBase = tuning != null ? tuning.moveDurationBase : 0.9f;
            float moveMin  = tuning != null ? tuning.moveDurationMin : 0.18f;
            float duration = Mathf.Max(moveMin, moveBase / Mathf.Max(1f, stats.AGI));
            yield return MoveSegment(dest, duration);
            StartIdleAnim();
        }

        // ------------------------------------------------------------------ ATAQUE

        /// <summary>Som do golpe conforme a arma (espada, machado, arco, cajado) ou a mordida da fera.</summary>
        public string AttackSfxName()
        {
            if (_model == null) return "sfx_attack";
            switch (_model.Style)
            {
                case UnitModel3D.AttackStyle.Chop:   return "sfx_chop";
                case UnitModel3D.AttackStyle.Shoot:  return "sfx_bow";
                case UnitModel3D.AttackStyle.Thrust: return "sfx_thrust";
                case UnitModel3D.AttackStyle.Bite:   return "sfx_bite";
                default:                             return "sfx_slash";
            }
        }

        public IEnumerator PlayAttackAnim(Vector3 targetPos, bool power = false)
        {
            if (AudioManager.I != null) AudioManager.I.PlayNamed(AttackSfxName(), AudioManager.I.sfxAttack);
            StopIdleAnim();
            SetDirectionFromDelta(targetPos - transform.position);

            // Sequência de golpe: attack x4 → release (5 frames).
            // O charging do kit é para magias — armas não o usam.
            var seq = new List<(string state, int frame)>();
            int nAtk = FrameCount("attack");
            for (int i = 0; i < nAtk; i++) seq.Add(("attack", i));
            if (_frames.ContainsKey($"release{_baseDir}_0")) seq.Add(("release", 0));
            if (seq.Count == 0) seq.Add(("walking", 0));

            float dur = Mathf.Max(0.05f, Tuning.Get().attackAnimDuration);
            if (_weapon != null) _weapon.PlayAttack(_baseDir, _flip, dur);
            if (_model != null) _model.PlayAttack(dur, targetPos, power || furyReady > 0f);

            float t = 0f;
            while (t < dur)
            {
                float p = Mathf.Clamp01(t / dur);
                var (st, fr) = seq[Mathf.Min(seq.Count - 1, Mathf.FloorToInt(p * seq.Count))];
                ShowFrame(st, fr);
                t += Time.deltaTime;
                yield return null;
            }

            StartIdleAnim();
        }

        // ------------------------------------------------------------------ PLANO

        public void ResetPlan()
        {
            plannedAnchor      = anchor;
            plannedBonusAnchor = anchor;
            plannedMoveCount = 0;
            plannedPath.Clear();
            plannedAttacks.Clear();
            plannedSpells.Clear();
            actionSequence.Clear();
            hasPlannedBonus       = false;
            bonusDamageThisAttack = false;
            aimBonusThisAttack    = false;
            plannedConcentrations = 0;
            reservedMana = 0;
            remainingAP  = Tuning.Get().maxQueueTicks;
            remainingBAP = currentStamina;
            dodgeReactBonus = 0f;
            blockFlat       = 0;
        }

        public void RebuildSequenceFromLists()
        {
            actionSequence.Clear();
            for (int i = 0; i < plannedMoveCount; i++)
                actionSequence.Add(new ScheduledAction { Type = ActionType.Move, Index = i, IsBonus = false, BonusStep = Vector2Int.zero });
            for (int i = 0; i < plannedAttacks.Count; i++)
                actionSequence.Add(new ScheduledAction { Type = ActionType.Attack, Index = i, IsBonus = false, BonusStep = Vector2Int.zero });
            for (int c = 0; c < plannedConcentrations; c++)
                actionSequence.Add(new ScheduledAction { Type = ActionType.Concentrate, Index = 0, IsBonus = false, BonusStep = Vector2Int.zero });
            for (int i = 0; i < plannedSpells.Count; i++)
                actionSequence.Add(new ScheduledAction { Type = ActionType.Spell, Index = i, IsBonus = false, BonusStep = Vector2Int.zero });
        }
    }
}
