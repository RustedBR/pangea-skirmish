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

            var go = World3D.Spawn(_monster ? "Monster" : "Character", _root);
            if (go == null) { enabled = false; Destroy(_root.gameObject); return; }
            _model = go.transform;

            // Altura na tela parecida com a do sprite antigo; o monstro é mais baixo e comprido.
            _scale = spriteHeight * (_monster ? 0.62f : 0.80f) / CharacterHeight;

            if (_monster)
            {
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
                _armL = MakePivot("ArmL", 0f, 1f, 0f);
                _armR = MakePivot("ArmR", 0f, 1f, 0f);
                _legL = MakePivot("LegL", 0f, 1f, 0f);
                _legR = MakePivot("LegR", 0f, 1f, 0f);
                EquipWeaponModel(weapon);
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

        private void EquipWeaponModel(WeaponDef w)
        {
            if (w == null || _armR == null) return;
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
            // Mão: ponta de baixo do braço (o pivô fica no ombro, a ~0.32 acima da mão).
            go.transform.localPosition = new Vector3(0f, -0.29f, 0.07f);
            go.transform.localRotation = model == "Sword" ? Quaternion.Euler(65f, 0f, 0f) : Quaternion.identity;
        }

        /// <summary>Quadrante de tela (0=NE, 1=SE, 2=SW, 3=NW) → para onde o modelo olha.</summary>
        public void SetScreenQuadrant(int quadrant) => _targetYaw = 45f + 90f * quadrant;

        public void PlayAttack(float duration)
        {
            _attackDur = Mathf.Max(0.05f, duration);
            _attackT = 0f;
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

            // Golpe: 0→1, pico no meio.
            float atk = 0f;
            if (_attackT >= 0f)
            {
                _attackT += dt / _attackDur;
                if (_attackT >= 1f) _attackT = -1f; else atk = Mathf.Sin(_attackT * Mathf.PI);
            }

            float bob = Mathf.Abs(Mathf.Sin(_walkPhase)) * 0.03f * _walkBlend;
            _model.localPosition = new Vector3(0f, bob, atk * (_monster ? 0.30f : 0.14f));

            if (_monster)
            {
                Rot(_legFL, swing); Rot(_legBR, swing);
                Rot(_legFR, -swing); Rot(_legBL, -swing);
                Rot(_head, -atk * 30f + idle * 2.5f);
                if (_tail != null) _tail.localRotation = Quaternion.Euler(0f, idle * 14f, 0f);
            }
            else
            {
                Rot(_legL, swing); Rot(_legR, -swing);
                Rot(_armL, -swing * 0.8f);
                Rot(_armR, swing * 0.8f - atk * 115f);
                Rot(_head, idle * 2f + atk * 8f);
            }
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
