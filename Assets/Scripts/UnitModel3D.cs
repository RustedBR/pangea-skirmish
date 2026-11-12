using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Corpo 3D low-poly de uma unidade (substitui o sprite TinyTactics na tela).
    ///
    /// A Unit continua dona de toda a lógica e dos sprites: este componente só ESCONDE o
    /// sprite do corpo e espelha o estado dele no modelo — direção (quadrante de tela),
    /// tint (seleção, mira, dano), fade de morte — e anima por código: caminhada quando a
    /// unidade se desloca e golpe quando a Unit toca a animação de ataque.
    /// </summary>
    public class UnitModel3D : MonoBehaviour
    {
        private const float CharacterHeight = 0.93f;   // altura do modelo em unidades do OBJ
        private const float TurnSpeed = 720f;          // graus/s

        private Unit _unit;
        private SpriteRenderer _body, _footprint;
        private Transform _root, _model;
        private Transform _armL, _armR, _legL, _legR, _head, _tail;
        private Transform _legFL, _legFR, _legBL, _legBR;
        private bool _monster;
        private float _scale, _footprintScale;
        private float _yaw = 135f, _targetYaw = 135f;
        private Vector3 _lastPos;
        private float _walkPhase, _walkBlend;
        private float _attackT = -1f, _attackDur = 0.4f;

        /// <summary>Como a unidade golpeia: decide a coreografia do ataque.</summary>
        public enum AttackStyle { Slash, Chop, Thrust, Shoot, Bite }
        /// <summary>Estilo de golpe da arma equipada (feras sempre mordem).</summary>
        public AttackStyle Style { get; private set; } = AttackStyle.Slash;
        /// <summary>Animação de ação em curso ("" = nenhuma). Para testes e depuração.</summary>
        public string CurrentAction =>
            _deathT >= 0f ? "death" : _attackT >= 0f ? "attack" : _blockT >= 0f ? "block" : _dodgeT >= 0f ? "dodge"
          : _hitT >= 0f ? "hit" : _releaseT >= 0f ? "release" : _leapT >= 0f ? "leap" : _flexT >= 0f ? "flex" : IsChanneling ? "cast" : "";

        private bool _attackPower;
        private bool _shotFired;
        private Vector3 _attackTarget;
        private Transform _weaponT, _torso;
        private Quaternion _weaponRest = Quaternion.identity;
        private float _hitT = -1f, _blockT = -1f, _dodgeT = -1f, _deathT = -1f, _releaseT = -1f, _leapT = -1f, _flexT = -1f;
        private float _blockDur = 0.6f, _leapDur = 0.45f, _dodgeSide = 1f;
        private Vector3 _leapFrom;
        private float _castBlend;
        private GameObject _castOrb;
        private Color _castColor = Color.white;
        private bool _soulReleased;

        /// <summary>Conjurando magia Elemental (a fila está parada numa Spell não-Física)?</summary>
        public bool IsChanneling
        {
            get
            {
                if (_unit == null || _unit.IsDead || _unit.queue.Count == 0) return false;
                var q = _unit.queue[0];
                if (q.Type != ActionType.Spell) return false;
                var d = q.Spell.Def;
                return d != null && !d.IsPhysical;
            }
        }
        private Color _lastTint = new Color(-1, -1, -1, -1);
        private readonly Dictionary<string, Color> _colors = new Dictionary<string, Color>();
        private SpriteRenderer[] _hidden = new SpriteRenderer[0];
        private int _frame;

        public static UnitModel3D Attach(Unit unit, SpriteRenderer body, SpriteRenderer footprint,
                                         Color teamColor, string resourcePath, float spriteHeight, WeaponDef weapon)
        {
            var m = unit.gameObject.AddComponent<UnitModel3D>();
            m._unit = unit;
            m._body = body;
            m._footprint = footprint;
            m._footprintScale = footprint != null ? footprint.transform.localScale.x : 1f;
            m._monster = LooksLikeMonster(unit, resourcePath);
            m.Build(teamColor, spriteHeight, weapon);
            return m.enabled ? m : null;
        }

        private static bool LooksLikeMonster(Unit u, string resourcePath)
        {
            string s = ((resourcePath ?? "") + "|" + (u.definitionId ?? "") + "|" + (u.unitName ?? "")).ToLowerInvariant();
            foreach (var k in new[] { "monster", "monstro", "beast", "besta", "fera", "wolf", "lobo", "boar", "javali", "dragon" })
                if (s.Contains(k)) return true;
            return false;
        }

        private void Build(Color teamColor, float spriteHeight, WeaponDef weapon)
        {
            _root = new GameObject("Model3D").transform;
            _root.SetParent(transform, false);

            ModelName = _monster ? MonsterModelFor(_unit) : CharacterModelFor(_unit, weapon);
            var go = World3D.Spawn(ModelName, _root);
            if (go == null) { enabled = false; Destroy(_root.gameObject); return; }
            _model = go.transform;

            // Altura na tela parecida com a do sprite antigo; o monstro é mais baixo e comprido.
            _scale = spriteHeight * (_monster ? 0.62f : 0.80f) / CharacterHeight;

            if (_monster)
            {
                Style = AttackStyle.Bite;
                _head  = MakePivot("Head", 0f, 0.5f, -1f);
                _tail  = MakePivot("Tail", 0f, 1f, 1f);
                _legFL = MakePivot("LegFL", 0f, 1f, 0f);
                _legFR = MakePivot("LegFR", 0f, 1f, 0f);
                _legBL = MakePivot("LegBL", 0f, 1f, 0f);
                _legBR = MakePivot("LegBR", 0f, 1f, 0f);
            }
            else
            {
                _head = MakePivot("Head", 0f, -1f, 0f);
                _torso = World3D.FindPart(_model, "Torso");
                _armL = MakePivot("ArmL", 0f, 1f, 0f);
                _armR = MakePivot("ArmR", 0f, 1f, 0f);
                _legL = MakePivot("LegL", 0f, 1f, 0f);
                _legR = MakePivot("LegR", 0f, 1f, 0f);
                EquipWeaponModel(weapon);
                if (ModelName == "Tank") EquipShield();
            }

            if (teamColor.a > 0.01f && teamColor != Color.white)
                _colors["team"] = new Color(teamColor.r, teamColor.g, teamColor.b, 1f);
            else if (_footprint != null)
                _colors["team"] = new Color(_footprint.color.r, _footprint.color.g, _footprint.color.b, 1f);

            _lastPos = transform.position;
            LateUpdate();
        }

        /// <summary>
        /// Cria um pivô na borda da parte (fx/fy/fz ∈ [-1,1] sobre os bounds da malha) e
        /// re-parenta a parte nele, para girar o membro pela articulação e não pela origem.
        /// </summary>
        private Transform MakePivot(string partName, float fx, float fy, float fz)
        {
            var part = World3D.FindPart(_model, partName);
            if (part == null) return null;
            var mf = part.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return null;
            var b = mf.sharedMesh.bounds;
            var local = b.center + Vector3.Scale(b.extents, new Vector3(fx, fy, fz));

            var pivot = new GameObject(partName + "Pivot").transform;
            pivot.SetParent(part.parent, false);
            pivot.localPosition = part.localPosition + part.localRotation * Vector3.Scale(part.localScale, local);
            part.SetParent(pivot, true);
            return pivot;
        }

        /// <summary>Nome do modelo do corpo em uso (Warrior, Mage, MonsterAlpha...). Para testes e depuração.</summary>
        public string ModelName { get; private set; }
        /// <summary>Nome do modelo da arma na mão direita, ou null.</summary>
        public string WeaponModelName { get; private set; }

        /// <summary>
        /// Classe → modelo. A definição da unidade decide; sem definição (personagem criado pelo
        /// jogador), a arma decide: cajado/varinha = Mago, arco = Arqueiro, machado/espada = Guerreiro.
        /// </summary>
        public static string CharacterModelFor(Unit u, WeaponDef weapon)
        {
            // Aparência escolhida pelo jogador na criação tem prioridade sobre tudo.
            if (!string.IsNullOrEmpty(u.modelId) && World3D.Has(u.modelId)) return u.modelId;
            return CharacterModelFor(u.definitionId, u.unitName, weapon);
        }

        /// <summary>Mesma regra sem uma Unit (prévia da criação de personagem).</summary>
        public static string CharacterModelFor(string definitionId, string unitName, WeaponDef weapon)
        {
            string s = ((definitionId ?? "") + "|" + (unitName ?? "")).ToLowerInvariant();
            string pick = null;
            if (s.Contains("tank") || s.Contains("tanque")) pick = "Tank";
            else if (s.Contains("archer") || s.Contains("arqueir")) pick = "Archer";
            else if (s.Contains("mage") || s.Contains("mago") || s.Contains("maga")) pick = "Mage";
            else if (s.Contains("warrior") || s.Contains("guerreir")) pick = "Warrior";
            else if (weapon != null)
            {
                string id = (weapon.id ?? "").ToLowerInvariant();
                if (id.Contains("staff") || id.Contains("wand") || id.Contains("scepter")) pick = "Mage";
                else if (id.Contains("bow")) pick = "Archer";
                else if (id.Contains("axe") || id.Contains("hatchet") || id.Contains("sword")) pick = "Warrior";
            }
            return World3D.Has(pick) ? pick : "Character";
        }

        /// <summary>Variedade de feras: o nome decide (alfa, espreitador); senão o número da fera alterna os três tipos.</summary>
        public static string MonsterModelFor(Unit u)
        {
            string s = ((u.definitionId ?? "") + "|" + (u.unitName ?? "")).ToLowerInvariant();
            string pick = "Monster";
            if (s.Contains("alfa") || s.Contains("alpha")) pick = "MonsterAlpha";
            else if (s.Contains("espreit") || s.Contains("stalker") || s.Contains("lagart")) pick = "MonsterStalker";
            else
            {
                int n = 0;
                foreach (char c in u.unitName ?? "") if (char.IsDigit(c)) n = n * 10 + (c - '0');
                if (n > 0) pick = n % 3 == 0 ? "MonsterAlpha" : n % 3 == 2 ? "MonsterStalker" : "Monster";
            }
            return World3D.Has(pick) ? pick : "Monster";
        }

        private void EquipShield()
        {
            if (_armL == null || !World3D.Has("Shield")) return;
            var go = World3D.Spawn("Shield", _armL);
            if (go == null) return;
            go.transform.localPosition = new Vector3(-0.07f, -0.22f, 0.05f);
            go.transform.localRotation = Quaternion.Euler(0f, -70f, 0f);
            go.transform.localScale = Vector3.one * 0.85f;
        }

        private void EquipWeaponModel(WeaponDef w)
        {
            if (w == null || _armR == null) return;
            // Modelo próprio da arma (o nome do OBJ é o id do catálogo) antes das famílias genéricas.
            if (World3D.Has(w.id))
            {
                var own = World3D.Spawn(w.id, _armR);
                if (own != null)
                {
                    own.transform.localPosition = new Vector3(0f, -0.29f, 0.07f);
                    WeaponModelName = w.id;
                    SetWeapon(own.transform, StyleFor(w.id));
                    return;
                }
            }
            string id = (w.id ?? "").ToLowerInvariant();
            string model = "Sword";
            // O nome decide primeiro (cajados também têm alcance longo); alcance é só desempate.
            // (Afinidade elemental não serve de critério: toda arma do catálogo é conduíte.)
            if (id.Contains("staff") || id.Contains("cajado") || id.Contains("wand")) model = "Staff";
            else if (id.Contains("bow") || id.Contains("arco") || id.Contains("sling")) model = "Bow";
            else if (!id.Contains("sword") && !id.Contains("axe") && !id.Contains("spear")
                     && !id.Contains("dagger") && w.range >= 4) model = "Bow";

            var go = World3D.Spawn(model, _armR);
            if (go == null) return;
            WeaponModelName = model;
            // Mão: ponta de baixo do braço (o pivô fica no ombro, a ~0.32 acima da mão).
            go.transform.localPosition = new Vector3(0f, -0.29f, 0.07f);
            SetWeapon(go.transform, StyleFor(model));
        }

        public static AttackStyle StyleFor(string weaponOrModel)
        {
            string id = (weaponOrModel ?? "").ToLowerInvariant();
            if (id.Contains("bow")) return AttackStyle.Shoot;
            if (id.Contains("axe") || id.Contains("hatchet")) return AttackStyle.Chop;
            if (id.Contains("staff") || id.Contains("wand") || id.Contains("scepter")) return AttackStyle.Thrust;
            return AttackStyle.Slash;
        }

        /// <summary>
        /// Pose de descanso da arma na mão. Lâminas e machados ficam em guarda (ponta para cima,
        /// levemente à frente) — antes ficavam deitados para a frente como uma lança.
        /// </summary>
        private void SetWeapon(Transform weapon, AttackStyle style)
        {
            _weaponT = weapon; Style = style;
            _weaponRest = (style == AttackStyle.Slash || style == AttackStyle.Chop) ? Quaternion.Euler(18f, 0f, 0f) : Quaternion.identity;
            weapon.localRotation = _weaponRest;
        }

        /// <summary>Quadrante de tela (0=NE, 1=SE, 2=SW, 3=NW) → para onde o modelo olha.</summary>
        public void SetScreenQuadrant(int quadrant) => _targetYaw = 45f + 90f * quadrant;

        public void PlayAttack(float duration) => PlayAttack(duration, transform.position + _root.forward, false);

        /// <summary>Golpe conforme a arma. <paramref name="power"/> = Golpe poderoso/Fúria: preparo maior e investida mais longa.</summary>
        public void PlayAttack(float duration, Vector3 targetWorld, bool power)
        {
            _attackDur = Mathf.Max(0.05f, duration) * (power ? 1.25f : 1f);
            _attackT = 0f; _attackPower = power; _attackTarget = targetWorld; _shotFired = false;
        }

        /// <summary>Recuo ao levar dano.</summary>
        public void PlayHit() { if (_deathT < 0f) _hitT = 0f; }

        /// <summary>Bloquear: braço/escudo à frente, joelhos dobrados, por <paramref name="hold"/> segundos.</summary>
        public void PlayBlock(float hold = 0.7f) { if (_deathT < 0f) { _blockT = 0f; _blockDur = Mathf.Max(0.2f, hold); } }

        /// <summary>Esquiva: pulo lateral curto.</summary>
        public void PlayDodge()
        {
            if (_deathT >= 0f) return;
            _dodgeT = 0f; _dodgeSide = (transform.GetSiblingIndex() & 1) == 0 ? 1f : -1f;
        }

        /// <summary>Morte: o corpo tomba; a alma sai da cabeça e desce para dentro do planeta (GDD-013).</summary>
        public void PlayDeath() { if (_deathT < 0f) { _deathT = 0f; _attackT = _hitT = _blockT = _dodgeT = _releaseT = _leapT = _flexT = -1f; } }

        /// <summary>Solta a magia: braços para a frente. A cor é a do elemento.</summary>
        public void PlayCastRelease(Color elementColor) { if (_deathT < 0f) { _releaseT = 0f; _castColor = elementColor; } }

        /// <summary>Magia Física (Fúria, Pele de pedra, Olho certeiro): o corpo se retesa.</summary>
        public void PlayFlex() { if (_deathT < 0f) _flexT = 0f; }

        /// <summary>Salto: o modelo descreve um arco de onde estava até a posição atual da unidade.</summary>
        public void PlayLeap(Vector3 fromWorld, float duration = 0.45f)
        {
            if (_deathT >= 0f) return;
            _leapFrom = fromWorld; _leapDur = Mathf.Max(0.15f, duration); _leapT = 0f;
        }

        /// <summary>Ponto de onde saem flechas e magias (mão/ponta da arma, ou a boca da fera).</summary>
        public Vector3 MuzzleWorld
        {
            get
            {
                if (_weaponT != null) return _weaponT.TransformPoint(new Vector3(0f, 0.45f, 0f));
                if (_head != null) return _head.position + _root.forward * 0.2f * _scale;
                return transform.position + Vector3.up * 0.6f * _scale;
            }
        }

        /// <summary>Escala do modelo em mundo (para dimensionar efeitos).</summary>
        public float ModelScale => _scale;

        /// <summary>Cabeça em mundo: onde mora a alma.</summary>
        public Vector3 HeadWorld3D => _head != null ? _head.position + Vector3.up * 0.15f * _scale : transform.position + Vector3.up * 0.8f * _scale;

        private static float Ease(float t) => t * t * (3f - 2f * t);
        private static float Tick(ref float t, float dt, float dur)
        {
            if (t < 0f) return -1f;
            t += dt / Mathf.Max(0.01f, dur);
            if (t >= 1f) { t = -1f; return -1f; }
            return t;
        }

        private void LateUpdate()
        {
            if (_root == null || _unit == null) return;
            float dt = Time.deltaTime;

            HideSprites();

            // Footprint: losango deitado no chão (o pai é billboard, então fixa em mundo).
            if (_footprint != null)
            {
                var ft = _footprint.transform;
                ft.rotation = World3D.GroundRotation;
                ft.position = transform.position + new Vector3(0f, 0.02f, 0f);
                ft.localScale = new Vector3(_footprintScale, _footprintScale * World3D.GroundStretch, 1f);
            }

            // Tint e fade espelhados do sprite do corpo.
            Color tint = _body != null ? _body.color : Color.white;
            float alpha = Mathf.Clamp01(tint.a);
            if (tint != _lastTint)
            {
                _lastTint = tint;
                World3D.Paint(_root, _colors, new Color(tint.r, tint.g, tint.b, 1f));
            }

            // Direção: o pai gira com a câmera (BillboardFace), então fixa o yaw em mundo.
            _yaw = Mathf.MoveTowardsAngle(_yaw, _targetYaw, TurnSpeed * dt);
            _root.rotation = Quaternion.Euler(0f, _yaw, 0f);
            _root.position = transform.position;
            _root.localScale = Vector3.one * (_scale * Mathf.Lerp(0.2f, 1f, alpha));

            // Caminhada: liga quando a unidade se desloca no chão.
            Vector3 d = transform.position - _lastPos;
            _lastPos = transform.position;
            float speed = dt > 0f ? new Vector2(d.x, d.z).magnitude / dt : 0f;
            _walkBlend = Mathf.MoveTowards(_walkBlend, speed > 0.2f ? 1f : 0f, dt * 8f);
            if (_walkBlend > 0f) _walkPhase += dt * 11f;
            float swing = Mathf.Sin(_walkPhase) * 38f * _walkBlend;
            float idle  = Mathf.Sin(Time.time * 2.2f + transform.GetSiblingIndex() * 0.7f);

            // ---------------- timelines das ações (0→1; −1 = parada) ----------------
            float atkT  = Tick(ref _attackT, dt, _attackDur);
            float hitT  = Tick(ref _hitT, dt, 0.28f);
            float blkT  = Tick(ref _blockT, dt, _blockDur);
            float dgT   = Tick(ref _dodgeT, dt, 0.38f);
            float relT  = Tick(ref _releaseT, dt, 0.40f);
            float leapT = Tick(ref _leapT, dt, _leapDur);
            float flexT = Tick(ref _flexT, dt, 0.55f);
            if (_deathT >= 0f) _deathT = Mathf.Min(1f, _deathT + dt / 0.55f);
            bool channel = IsChanneling && _deathT < 0f;
            _castBlend = Mathf.MoveTowards(_castBlend, channel ? 1f : 0f, dt * 5f);
            UpdateCastOrb(channel);

            // Golpe em três tempos: preparo (0–0.35), golpe (0.35–0.55), volta (0.55–1).
            float wind = 0f, strike = 0f;
            if (atkT >= 0f)
            {
                wind   = atkT < 0.35f ? Ease(atkT / 0.35f) : atkT < 0.55f ? 1f - Ease((atkT - 0.35f) / 0.20f) : 0f;
                strike = atkT < 0.35f ? 0f : atkT < 0.55f ? Ease((atkT - 0.35f) / 0.20f) : 1f - Ease((atkT - 0.55f) / 0.45f);
                // Arco: a flecha sai quando a corda solta.
                if (Style == AttackStyle.Shoot && !_shotFired && atkT >= 0.55f)
                {
                    _shotFired = true;
                    ActionFx.Projectile("Arrow", MuzzleWorld, _attackTarget + Vector3.up * 0.5f * _scale, 0.16f, null, 0.15f, _scale);
                }
            }
            float pw = _attackPower ? 1.35f : 1f;

            float hit   = hitT  >= 0f ? Mathf.Sin(hitT * Mathf.PI) : 0f;
            float block = blkT  >= 0f ? Mathf.Clamp01(Mathf.Min(blkT / 0.15f, (1f - blkT) / 0.2f)) : 0f;
            float dodge = dgT   >= 0f ? Mathf.Sin(dgT * Mathf.PI) : 0f;
            float rel   = relT  >= 0f ? Mathf.Sin(relT * Mathf.PI) : 0f;
            float flex  = flexT >= 0f ? Mathf.Sin(flexT * Mathf.PI) : 0f;
            float cast  = _castBlend;
            float castSway = Mathf.Sin(Time.time * 5f);
            float death = _deathT >= 0f ? Ease(_deathT) : 0f;

            // ---------------- corpo inteiro: posição e inclinação ----------------
            float bob = Mathf.Abs(Mathf.Sin(_walkPhase)) * 0.03f * _walkBlend;
            float lunge = strike * (_monster ? 0.34f : Style == AttackStyle.Shoot ? -0.04f : 0.16f) * pw - wind * 0.05f
                        - hit * 0.10f + rel * 0.05f;
            float lift = bob + cast * (0.05f + castSway * 0.012f) - block * 0.05f + dodge * 0.10f + flex * 0.03f;
            float side = dodge * 0.30f * _dodgeSide;
            float pitch = 0f, roll = 0f, twist = 0f;     // graus
            pitch += -hit * 16f + block * 8f + flex * -6f;
            roll  += dodge * 14f * _dodgeSide;
            if (_monster)
            {
                pitch += wind * -8f + strike * 10f;
                roll += death * 88f;                         // tomba de lado
            }
            else
            {
                if (Style == AttackStyle.Slash) twist += wind * -32f * pw + strike * 30f * pw;
                if (Style == AttackStyle.Chop)  pitch += wind * -14f * pw + strike * 20f * pw;
                if (Style == AttackStyle.Thrust) pitch += strike * 10f;
                if (Style == AttackStyle.Shoot) twist += (wind + strike) * 35f;      // de lado para o alvo
                pitch += cast * -5f + rel * 12f;
                pitch += death * -86f;                       // cai de costas
            }
            lift -= death * (_monster ? 0.10f : 0.04f);

            // Salto: o modelo viaja de onde estava até a unidade, em arco.
            Vector3 leapOffset = Vector3.zero;
            if (leapT >= 0f)
            {
                leapOffset = Vector3.Lerp(_leapFrom - transform.position, Vector3.zero, Ease(leapT));
                leapOffset.y += Mathf.Sin(leapT * Mathf.PI) * 0.9f * _scale;
                pitch += Mathf.Cos(leapT * Mathf.PI) * -18f;
            }
            _root.position = transform.position + leapOffset;
            _model.localPosition = new Vector3(side, lift, lunge);
            _model.localRotation = Quaternion.Euler(pitch, twist, roll);

            // ---------------- membros ----------------
            if (_monster)
            {
                float still = 1f - death;
                Rot(_legFL, swing * still + death * 25f); Rot(_legBR, swing * still - death * 20f);
                Rot(_legFR, -swing * still - death * 15f); Rot(_legBL, -swing * still + death * 30f);
                // Mordida: ergue a cabeça no preparo e crava para baixo no golpe.
                Rot(_head, (wind * 22f - strike * 38f * pw + idle * 2.5f - hit * 14f) * still + death * 20f);
                if (_tail != null) _tail.localRotation = Quaternion.Euler(wind * -25f - death * 30f, idle * 14f * still, 0f);
            }
            else
            {
                float armR = swing * 0.8f, armL = -swing * 0.8f;        // caminhada
                float armRy = 0f, armLy = 0f, armRz = 0f, armLz = 0f;
                Quaternion weaponRot = _weaponRest;
                switch (Style)
                {
                    case AttackStyle.Slash:     // braço atrás → corte cruzado
                        armR += wind * 55f * pw - strike * 118f;
                        armRz += wind * -20f + strike * 25f;
                        armL += strike * 25f;
                        weaponRot = _weaponRest * Quaternion.Euler(strike * 55f - wind * 20f, 0f, 0f);
                        break;
                    case AttackStyle.Chop:      // acima da cabeça → desce reto
                        armR += -wind * 172f * Mathf.Min(1f, pw) - strike * 70f;
                        armL += -wind * 60f - strike * 30f;
                        weaponRot = _weaponRest * Quaternion.Euler(strike * 70f - wind * 25f, 0f, 0f);
                        break;
                    case AttackStyle.Thrust:    // estocada com a ponta do cajado
                        armR += wind * 30f - strike * 95f;
                        armL += -strike * 55f;
                        weaponRot = _weaponRest * Quaternion.Euler(strike * 80f, 0f, 0f);
                        break;
                    case AttackStyle.Shoot:     // ergue o arco, puxa a corda, solta
                        float hold = Mathf.Max(wind, strike);
                        armR += -hold * 88f;
                        armL += -hold * 80f + wind * 22f;      // mão da corda recua no preparo
                        armLy += wind * -35f;
                        break;
                }
                // Conjurar: braços erguidos e oscilando; soltar: os dois para a frente.
                armR = Mathf.Lerp(armR, -150f + castSway * 8f, cast);
                armL = Mathf.Lerp(armL, -120f - castSway * 8f, cast);
                armR = Mathf.Lerp(armR, -95f, rel); armL = Mathf.Lerp(armL, -95f, rel);
                // Bloquear: braço do escudo cruzado à frente, o outro recolhido.
                armL = Mathf.Lerp(armL, -85f, block); armLy = Mathf.Lerp(armLy, 55f, block);
                armR = Mathf.Lerp(armR, -35f, block);
                // Magia Física: punhos cerrados, braços abertos.
                armRz += flex * 35f; armLz -= flex * 35f; armR -= flex * 25f; armL -= flex * 25f;
                // Dano: braços jogados para trás. Morte: abertos.
                armR += hit * 35f; armL += hit * 35f;
                armRz += death * 60f; armLz -= death * 60f;

                float legs = 1f - death;
                Rot(_legL, swing * legs - block * 12f + dodge * 20f * _dodgeSide + death * 12f);
                Rot(_legR, -swing * legs + block * 14f - dodge * 20f * _dodgeSide - death * 8f);
                if (_armL != null) _armL.localRotation = Quaternion.Euler(armL, armLy, armLz);
                if (_armR != null) _armR.localRotation = Quaternion.Euler(armR, armRy, armRz);
                if (_weaponT != null) _weaponT.localRotation = weaponRot;
                Rot(_head, idle * 2f + strike * 8f - hit * 18f + cast * -12f + block * 10f + death * -15f);
            }

            // A alma deixa a cabeça quando o corpo termina de cair.
            if (_deathT >= 0.6f && !_soulReleased)
            {
                _soulReleased = true;
                ActionFx.SoulDescent(HeadWorld3D, _scale);
            }
        }

        /// <summary>Orbe do elemento crescendo acima da mão/arma enquanto a magia é conjurada.</summary>
        private void UpdateCastOrb(bool channel)
        {
            if (!channel)
            {
                if (_castOrb != null) { Destroy(_castOrb); _castOrb = null; }
                return;
            }
            var q = _unit.queue[0];
            var def = q.Spell.Def;
            if (_castOrb == null)
            {
                _castOrb = World3D.Spawn("SpellOrb", null);
                if (_castOrb == null) return;
                AudioManager.I?.PlayNamed("sfx_cast_start", null, 0.7f);
                _castColor = def != null ? ElementInfo.Color(def.PrimaryElement) : Color.white;
                ActionFx.Tint(_castOrb.transform, _castColor);
            }
            float progress = q.Duration > 0 ? Mathf.Clamp01((q.Elapsed + 0.5f) / q.Duration) : 1f;
            _castOrb.transform.position = MuzzleWorld + Vector3.up * 0.12f * _scale;
            _castOrb.transform.rotation = Quaternion.Euler(0f, Time.time * 220f, Mathf.Sin(Time.time * 3f) * 12f);
            _castOrb.transform.localScale = Vector3.one * _scale * Mathf.Lerp(0.35f, 1.1f, progress) * (1f + Mathf.Sin(Time.time * 9f) * 0.06f);
        }

        private void OnDisable()
        {
            if (_castOrb != null) { Destroy(_castOrb); _castOrb = null; }
        }

        private static void Rot(Transform t, float xDeg)
        {
            if (t != null) t.localRotation = Quaternion.Euler(xDeg, 0f, 0f);
        }

        /// <summary>O sprite do corpo (e a arma em overlay) seguem existindo para a lógica, mas não são desenhados.</summary>
        private void HideSprites()
        {
            if (_body == null) return;
            if (_frame++ % 30 == 0) _hidden = _body.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var sr in _hidden)
                if (sr != null && sr.enabled) sr.enabled = false;
        }
    }
}
