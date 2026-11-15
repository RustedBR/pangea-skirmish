using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Prévia 3D de um personagem (corpo + arma na mão + escudo do Tanque) renderizada numa
    /// RenderTexture, para telas de UI. Monta o modelo longe da cena, com uma câmera própria,
    /// e gira devagar. Usado na criação de personagem, que antes mostrava só o sprite 2D
    /// antigo, sem arma e sem relação com o corpo 3D que entra na batalha.
    /// </summary>
    public class CharacterPreview3D : MonoBehaviour
    {
        private static readonly Vector3 Stage = new Vector3(4000f, 0f, 4000f);

        public RenderTexture Texture { get; private set; }
        public string BodyModel { get; private set; }
        public string WeaponModel { get; private set; }
        public bool HasShield { get; private set; }

        private Camera _cam;
        private Transform _pivot;
        private GameObject _body;
        private float _spin;
        private readonly Dictionary<string, Color> _colors = new Dictionary<string, Color>();

        public static CharacterPreview3D Create(int size = 320)
        {
            var go = new GameObject("CharacterPreview3D");
            var p = go.AddComponent<CharacterPreview3D>();
            p.Build(size);
            return p;
        }

        private void Build(int size)
        {
            transform.position = Stage;
            Texture = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { name = "CharacterPreviewRT" };
            Texture.Create();

            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = 0.68f;
            _cam.nearClipPlane = 0.1f; _cam.farClipPlane = 20f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.07f, 0.09f, 0.16f, 1f);
            _cam.targetTexture = Texture;
            _cam.depth = -50;
            // Olha de frente, um pouco de cima, para o meio do corpo (~0,6 de altura).
            camGo.transform.localPosition = new Vector3(0f, 1.55f, -4f);
            camGo.transform.LookAt(Stage + new Vector3(0f, 0.60f, 0f));

            _pivot = new GameObject("Pivot").transform;
            _pivot.SetParent(transform, false);
        }

        /// <summary>Troca corpo e arma. <paramref name="bodyModel"/> vazio usa o corpo genérico.</summary>
        public void Show(string bodyModel, string weaponId, Color teamColor) => Show(bodyModel, weaponId, teamColor, null);

        /// <summary>Corpo, arma da mão direita e o que vai na esquerda (escudo ou arma de 1 mão, GDD-021).</summary>
        public void Show(string bodyModel, string weaponId, Color teamColor, string offhandId)
        {
            if (_body != null) Destroy(_body);
            BodyModel = World3D.Has(bodyModel) ? bodyModel : "Character";
            WeaponModel = null; HasShield = false;
            _body = World3D.Spawn(BodyModel, _pivot);
            if (_body == null) return;

            // Arma na mão direita. Sem os pivôs de animação do UnitModel3D: aqui a pose é fixa.
            var armR = World3D.FindPart(_body.transform, "ArmR");
            if (armR != null && !string.IsNullOrEmpty(weaponId))
            {
                string model = World3D.Has(weaponId) ? weaponId : FamilyModel(weaponId);
                var w = World3D.Has(model) ? World3D.Spawn(model, armR) : null;
                if (w != null)
                {
                    var b = Bounds(armR);
                    w.transform.localPosition = new Vector3(b.center.x, b.min.y + 0.03f, b.center.z + 0.07f);
                    var style = UnitModel3D.StyleFor(weaponId);
                    w.transform.localRotation = (style == UnitModel3D.AttackStyle.Slash || style == UnitModel3D.AttackStyle.Chop)
                        ? Quaternion.Euler(18f, 0f, 0f) : Quaternion.identity;
                    WeaponModel = model;
                }
            }
            var armL = World3D.FindPart(_body.transform, "ArmL");
            var offItem = ItemCatalog.Get(offhandId);
            var offWeapon = Equipment.OffhandWeapon(offhandId);
            if (armL != null && offWeapon != null)
            {
                string om = World3D.Has(offWeapon.id) ? offWeapon.id : FamilyModel(offWeapon.id);
                var ow = World3D.Has(om) ? World3D.Spawn(om, armL) : null;
                if (ow != null)
                {
                    var b = Bounds(armL);
                    ow.transform.localPosition = new Vector3(b.center.x, b.min.y + 0.03f, b.center.z + 0.07f);
                    ow.transform.localRotation = Quaternion.Euler(18f, 0f, 0f);
                }
            }
            if (offItem != null && offItem.isShield && armL != null && World3D.Has("Shield"))   // Tanque sem escudo fixo (2026-11-15)
            {
                var sh = World3D.Spawn("Shield", armL);
                if (sh != null)
                {
                    var b = Bounds(armL);
                    sh.transform.localPosition = new Vector3(b.center.x - 0.07f, b.min.y + 0.10f, b.center.z + 0.05f);
                    sh.transform.localRotation = Quaternion.Euler(0f, -70f, 0f);
                    sh.transform.localScale = Vector3.one * 0.85f;
                    HasShield = true;
                }
            }
            _colors.Clear();
            _colors["team"] = new Color(teamColor.r, teamColor.g, teamColor.b, 1f);
            World3D.Paint(_body.transform, _colors, Color.white);
        }

        private static string FamilyModel(string weaponId)
        {
            switch (UnitModel3D.StyleFor(weaponId))
            {
                case UnitModel3D.AttackStyle.Shoot:  return "Bow";
                case UnitModel3D.AttackStyle.Thrust: return "Staff";
                default:                             return "Sword";
            }
        }

        private static Bounds Bounds(Transform part)
        {
            var mf = part.GetComponent<MeshFilter>();
            return mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one * 0.1f);
        }

        /// <summary>
        /// Falso na batalha (2026-11-15): a câmera fica desligada e renderiza sob demanda, ~8× por segundo e só com o
        /// retrato na tela. Ligada, ela redesenhava a cena a cada quadro mesmo com a inspeção fechada.
        /// </summary>
        public bool Live { get => _live; set { _live = value; if (_cam != null) _cam.enabled = value; } }
        private bool _live = true;
        /// <summary>O retrato está na tela? (só vale com Live = false)</summary>
        public bool Visible { get; set; }
        private float _renderTimer;

        /// <summary>Renderiza agora (troca de unidade com a câmera desligada).</summary>
        public void RenderNow() { if (_cam != null && !_live) _cam.Render(); }

        private void Update()
        {
            if (_pivot == null) return;
            if (!_live)
            {
                if (!Visible) return;
                _renderTimer -= Time.unscaledDeltaTime;
                if (_renderTimer > 0f) return;
                _renderTimer = MobileHUD.Active ? 0.5f : 0.125f;   // celular: quase parado
            }
            // Balança de um lado ao outro mostrando frente e perfil (não gira de costas).
            _spin += Time.unscaledDeltaTime;
            _pivot.localRotation = Quaternion.Euler(0f, 180f + Mathf.Sin(_spin * 0.8f) * 55f, 0f);
            if (!_live && _cam != null) _cam.Render();
        }

        private void OnDestroy()
        {
            if (_cam != null) _cam.targetTexture = null;
            if (Texture != null) { Texture.Release(); Destroy(Texture); }
        }
    }
}
