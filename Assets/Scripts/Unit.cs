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
        public int remainingAP;
        public int remainingBAP;

        [Header("AI Parameters (per-unit override)")]
        [Range(0f, 1f)] public float aiAggression = 0.7f;
        [Range(0f, 1f)] public float aiAttackPreference = 0.5f;
        [Range(0f, 1f)] public float aiIntelligence = 0.8f;
        [Range(0f, 1f)] public float aiSurvivalInstinct = 0.6f;
        public bool aiUseSpells = false;

        public bool hasPlannedMove   => plannedMoveCount   > 0;
        public bool hasPlannedAttack => plannedAttackCount > 0;
        public bool hasPlannedSpell  => plannedSpells.Count > 0;
        public int currentHP;
        public int currentMana;
        public int reservedMana;
        public int rolledInitiative;
        public int plannedConcentrations;
        public readonly List<PlannedSpell>   plannedSpells = new List<PlannedSpell>();
        public readonly List<StatusEffect>   statusEffects = new List<StatusEffect>();

        public int AvailableMana => currentMana - reservedMana;

        public Vector2Int FinalAnchor => hasPlannedBonus ? plannedBonusAnchor : plannedAnchor;

        public static event System.Action<Unit, int, bool> OnDamageTaken;

        private GridManager _grid;
        private SpriteRenderer _sr;
        private WeaponOverlay _weapon;
        private readonly Dictionary<string, Sprite> _frames = new Dictionary<string, Sprite>();
        private Color _baseTint = Color.white;
        private string _baseDir = "SE";  // NE ou SE
        private bool _flip;               // espelha p/ NW/SW
        private Coroutine _idleCoroutine;

        public bool IsDead => currentHP <= 0;
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
        public static Unit PickAtWorld(Vector2 world)
        {
            var hits = Physics2D.OverlapPointAll(world);
            Unit best = null;
            int bestOrder = int.MinValue;
            foreach (var h in hits)
            {
                var u = h.GetComponentInParent<Unit>();
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
            plannedAnchor      = startAnchor;
            plannedBonusAnchor = startAnchor;
            currentHP    = stats.MaxHP;
            currentMana  = stats.MaxMana;
            remainingAP  = stats.ActionPoints;
            remainingBAP = stats.BonusActionPoints;

            // Inimigos: tom escurecido para diferenciar (reuso de classe heroica).
            _baseTint = team == Team.Enemy ? Tuning.Get().enemyTint : Color.white;

            BuildVisual(resourcePath);
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

            var spriteGo = new GameObject("Sprite");
            spriteGo.transform.SetParent(transform, false);
            spriteGo.transform.localScale = new Vector3(spriteScale, spriteScale, 1f);
            // O footprint passou a ser centrado na célula (subiu 0.5*footprintScale vs. o layout
            // antigo); o personagem sobe a mesma medida para manter a relação aprovada.
            spriteGo.transform.localPosition =
                new Vector3(0f, T.spriteFootOffsetRatio * spriteScale + 0.5f * footprintScale, 0f);
            _sr = spriteGo.AddComponent<SpriteRenderer>();
            _sr.color = _baseTint;

            // Collider 2D no corpo (sprite) para seleção
            var bodyCollider = spriteGo.AddComponent<BoxCollider2D>();
            bodyCollider.size = T.bodyColliderSize;
            bodyCollider.offset = T.bodyColliderOffset;

            // Collider 2D achatado na base (footprint) para seleção
            var footprintCollider = footprintGo.AddComponent<BoxCollider2D>();
            footprintCollider.size = T.footprintColliderSize;
            footprintCollider.offset = T.footprintColliderOffset;

            // Equipar arma e aplicar dano/alcance
            EquipWeapon(spriteGo.transform);

            BuildHealthBars();

            ShowFrame("walking", 0);
        }

        // ------------------------------------------------------------------ BARRAS HP/MP (mundo)

        private Transform _barsRoot;
        private SpriteRenderer _hpFill, _mpFill;
        private float _hpFillY, _mpFillY;
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
            _hpFill = MakeBar(_barsRoot, "HpFill", _hpFillY, new Vector2(sz.x, sz.y * 0.8f), T.worldHpHighColor, sortBg + 1);
            MakeBar(_barsRoot, "MpBg", _mpFillY, sz, T.worldBarBgColor, sortBg);
            _mpFill = MakeBar(_barsRoot, "MpFill", _mpFillY, new Vector2(sz.x, sz.y * 0.8f), T.worldMpColor, sortBg + 1);

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
                _mpFill.color = T.worldMpColor;
            }
        }

        private void LateUpdate()
        {
            if (_barsRoot != null) UpdateBars();
            BobMarker(_activeMarker);
            BobMarker(_targetMarker);
        }

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
                stats.AttackRange = baseRange + w.range;
                stats.strScalesDamage = w.range <= (tuning != null ? tuning.strDamageMaxRange : 4);
            }
            else
            {
                // Sem arma: usar valores desarmados do GameTuning
                stats.WeaponDamage = tuning != null ? tuning.unarmedDamage : 1;
                stats.AttackRange = baseRange + (tuning != null ? tuning.unarmedRange : 0);
                stats.strScalesDamage = true; // unarmed sempre escala com STR
            }

            // Criar overlay apenas se houver arma
            if (w != null)
            {
                _weapon = gameObject.AddComponent<WeaponOverlay>();
                _weapon.Init(w.id, spriteGoTransform);
            }
        }

        private SpriteRenderer _footprint;

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
                bool inside = (dx + dy) <= 0.95f;
                tex.SetPixel(x, y, inside ? Color.white : Color.clear);
            }
            tex.Apply();
            _sharedFootprint = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 32);
            return _sharedFootprint;
        }

        // ------------------------------------------------------------------ FRAMES

        private void ShowFrame(string state, int frame)
        {
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

        // Define _baseDir (NE/SE) + _flip a partir do vetor de movimento em tela (XY iso).
        private void SetDirectionFromDelta(Vector3 delta)
        {
            if (Mathf.Abs(delta.x) < 0.0001f && Mathf.Abs(delta.y) < 0.0001f) return;
            bool right = delta.x >= 0f;
            bool up    = delta.y >= 0f;
            // NE = cima-direita, SE = baixo-direita, NW = cima-esq, SW = baixo-esq
            if (up)  { _baseDir = "NE"; _flip = !right; }
            else     { _baseDir = "SE"; _flip = !right; }
            if (_weapon != null) _weapon.RefreshDirection(_baseDir, _flip);
        }

        // ------------------------------------------------------------------ POSIÇÃO / SORTING

        public void SnapToAnchor()
        {
            transform.position = _grid.AnchorToWorldCenter(anchor, stats.Footprint);
            ApplySorting();
        }

        private void ApplySorting()
        {
            int order = _grid.SortingFor(anchor, stats.Footprint);
            if (_footprint != null) _footprint.sortingOrder = 10000 + order;
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
            AudioManager.I?.Play(AudioManager.I.sfxHit);
            OnDamageTaken?.Invoke(this, amount, isCritical);
            StartCoroutine(FlashHit());
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
            Vector3 start = transform.position;
            anchor = dest;
            Vector3 end = _grid.AnchorToWorldCenter(anchor, stats.Footprint);
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
                transform.position = Vector3.Lerp(start, end, Mathf.SmoothStep(0f, 1f, t / duration));
                if (t >= nextFrameTime)
                {
                    frameIdx = (frameIdx + 1) % n;
                    ShowFrame("walking", frameIdx);
                    nextFrameTime += frameInterval;
                }
                yield return null;
            }
            transform.position = end;
            ShowFrame("walking", 0);
        }

        public IEnumerator MoveBonusToPlanned(float duration = -1f)
        {
            if (duration <= 0f) duration = Tuning.Get().bonusMoveDuration;
            StopIdleAnim();
            Vector3 start = transform.position;
            anchor = plannedBonusAnchor;
            Vector3 end = _grid.AnchorToWorldCenter(anchor, stats.Footprint);
            SetDirectionFromDelta(end - start);
            ApplySorting();
            ShowFrame("walking", 0);
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                transform.position = Vector3.Lerp(start, end, Mathf.SmoothStep(0f, 1f, t / duration));
                yield return null;
            }
            transform.position = end;
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

        public IEnumerator PlayAttackAnim(Vector3 targetPos)
        {
            AudioManager.I?.Play(AudioManager.I.sfxAttack);
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

        // ------------------------------------------------------------------ MAGIA

        public IEnumerator PlayCastAnim(Vector3 targetPos)
        {
            StopIdleAnim();
            SetDirectionFromDelta(targetPos - transform.position);

            int nCharging = FrameCount("charging");
            if (nCharging > 0)
            {
                float dur = Tuning.Get().spellCastAnimDuration;
                float t = 0f;
                while (t < dur)
                {
                    float p = Mathf.Clamp01(t / dur);
                    int frame = Mathf.Min(nCharging - 1, Mathf.FloorToInt(p * nCharging));
                    ShowFrame("charging", frame);
                    t += Time.deltaTime;
                    yield return null;
                }
            }
            else
            {
                yield return PlayAttackAnim(targetPos);
            }

            StartIdleAnim();
        }

        public int ApplySpellDamage(int rawPotency, SpellElement element)
        {
            if (rawPotency <= 0) return 0;
            bool isPhysical = element == SpellElement.Physical;

            int dmg = rawPotency;
            if (isPhysical)
            {
                dmg = Mathf.Max(Tuning.Get().spellMinDamage,
                    rawPotency + StatusEffectSystem.ConsumeStrBuffOnHit(statusEffects) - stats.PhysicalDefense);
            }
            else
            {
                int resist = StatusEffectSystem.ElementResist(statusEffects, element);
                dmg = Mathf.Max(Tuning.Get().spellMinDamage, rawPotency - stats.MagicDefense - resist);
                dmg = StatusEffectSystem.AbsorbWithShield(statusEffects, dmg);
            }

            TakeDamage(dmg, false);
            return dmg;
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
            remainingAP  = stats.ActionPoints;
            remainingBAP = stats.BonusActionPoints;
        }

        public void RebuildSequenceFromLists()
        {
            actionSequence.Clear();
            for (int i = 0; i < plannedMoveCount; i++)
                actionSequence.Add(new ScheduledAction { Type = ActionType.Move, Index = i, IsBonus = false, BonusStep = Vector2Int.zero });
            for (int i = 0; i < plannedAttacks.Count; i++)
                actionSequence.Add(new ScheduledAction { Type = ActionType.Attack, Index = i, IsBonus = false, BonusStep = Vector2Int.zero });
            for (int i = 0; i < plannedSpells.Count; i++)
                actionSequence.Add(new ScheduledAction { Type = ActionType.Spell, Index = i, IsBonus = false, BonusStep = Vector2Int.zero });
            for (int c = 0; c < plannedConcentrations; c++)
                actionSequence.Add(new ScheduledAction { Type = ActionType.Concentrate, Index = 0, IsBonus = false, BonusStep = Vector2Int.zero });
        }
    }
}
