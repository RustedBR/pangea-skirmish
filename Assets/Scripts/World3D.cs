using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Ponto único das regras do mundo 3D (modelos low-poly + sprites).
    ///
    /// O tabuleiro continua no plano XZ com a mesma malha em losango, mas agora o
    /// losango é um grid QUADRADO de verdade girado 45° (halfW == halfH) e a câmera
    /// ortográfica é inclinada de fato. Com pitch de 30° a projeção na tela é a mesma
    /// 2:1 de antes, só que a altura (Y) e os modelos 3D aparecem de lado.
    ///
    /// Regras para sprites:
    ///  - decalques de chão (grade, destaque, ghosts): deitados (GroundRotation) e com
    ///    o Y local esticado por GroundStretch, porque a arte deles é um losango 2:1;
    ///  - sprites em pé (rótulos, barras, marcadores): billboard com a rotação da câmera.
    /// </summary>
    public static class World3D
    {
        /// <summary>Inclinação da câmera em relação ao horizonte (30° = isométrico 2:1).</summary>
        public const float CamPitch = 30f;
        /// <summary>Losango 2:1 deitado no chão precisa do dobro de profundidade (1 / sin 30°).</summary>
        public const float GroundStretch = 2f;
        /// <summary>O topo dos blocos fica um fio abaixo dos decalques para não brigar no Z.</summary>
        public const float BlockSink = 0.03f;
        /// <summary>Malhas desenham antes de todos os sprites (escrevem profundidade).</summary>
        public const int MeshSortingOrder = -30000;

        public static readonly Quaternion CamRotation    = Quaternion.Euler(CamPitch, 0f, 0f);
        public static readonly Quaternion GroundRotation = Quaternion.Euler(90f, 0f, 0f);

        private static Shader _shader;
        private static readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();
        private static readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>();
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int TintId      = Shader.PropertyToID("_Tint");
        private static MaterialPropertyBlock _mpb;

        /// <summary>Cores por nome de material — espelho de COLORS em scripts/gen_models.py.</summary>
        private static readonly Dictionary<string, Color> Palette = new Dictionary<string, Color>
        {
            { "skin", new Color(0.80f, 0.58f, 0.42f) }, { "hair", new Color(0.20f, 0.13f, 0.09f) },
            { "team", new Color(0.75f, 0.22f, 0.18f) }, { "pants", new Color(0.36f, 0.27f, 0.18f) },
            { "leather", new Color(0.42f, 0.26f, 0.13f) }, { "eye", new Color(0.06f, 0.05f, 0.05f) },
            { "top", new Color(0.42f, 0.62f, 0.26f) }, { "side", new Color(0.52f, 0.38f, 0.24f) },
            { "side_dark", new Color(0.38f, 0.28f, 0.19f) },
            { "stone", new Color(0.62f, 0.63f, 0.66f) }, { "wood", new Color(0.48f, 0.33f, 0.18f) },
            { "bone", new Color(0.90f, 0.86f, 0.74f) }, { "string", new Color(0.85f, 0.80f, 0.65f) },
            { "conduit", new Color(0.35f, 0.85f, 0.95f) },
            { "fur", new Color(0.50f, 0.36f, 0.24f) }, { "fur_dark", new Color(0.30f, 0.21f, 0.15f) },
            { "belly", new Color(0.72f, 0.60f, 0.44f) }, { "eye_glow", new Color(0.95f, 0.75f, 0.15f) },
            // classes
            { "hood", new Color(0.27f, 0.35f, 0.22f) }, { "trim", new Color(0.88f, 0.74f, 0.34f) },
            { "hide", new Color(0.28f, 0.27f, 0.30f) },
            // armas de ferro
            { "iron", new Color(0.70f, 0.73f, 0.78f) }, { "iron_dark", new Color(0.40f, 0.42f, 0.47f) },
            // feras: alfa e espreitador
            { "hide_dark", new Color(0.16f, 0.15f, 0.18f) }, { "hide_belly", new Color(0.42f, 0.38f, 0.40f) },
            { "eye_red", new Color(0.98f, 0.22f, 0.14f) },
            { "scale", new Color(0.30f, 0.48f, 0.30f) }, { "scale_dark", new Color(0.17f, 0.30f, 0.20f) },
            { "scale_belly", new Color(0.74f, 0.76f, 0.48f) },
            // magias ("element" é pintado em runtime com a cor do elemento)
            { "element", new Color(0.85f, 0.85f, 0.90f) }, { "element_core", new Color(1f, 1f, 1f) },
            { "fire", new Color(0.98f, 0.45f, 0.10f) }, { "fire_core", new Color(1f, 0.88f, 0.35f) },
            { "water", new Color(0.25f, 0.58f, 0.95f) }, { "water_light", new Color(0.75f, 0.90f, 1f) },
            { "air", new Color(0.86f, 0.95f, 0.98f) },
            { "rock", new Color(0.50f, 0.44f, 0.38f) }, { "rock_dark", new Color(0.33f, 0.29f, 0.26f) },
            { "magic", new Color(0.72f, 0.48f, 1f) },
            { "soul", new Color(0.80f, 0.95f, 1f) }, { "soul_dark", new Color(0.20f, 0.35f, 0.50f) },
            { "leaf", new Color(0.36f, 0.72f, 0.30f) }, { "leaf_dark", new Color(0.22f, 0.46f, 0.20f) },
        };

        /// <summary>O modelo existe em Resources/Models/Base? (sem logar erro, para escolher com reserva)</summary>
        public static bool Has(string modelName)
        {
            if (string.IsNullOrEmpty(modelName)) return false;
            if (!_prefabs.TryGetValue(modelName, out var prefab) || prefab == null)
            {
                prefab = Resources.Load<GameObject>("Models/Base/" + modelName);
                _prefabs[modelName] = prefab;
            }
            return prefab != null;
        }

        private static Shader LowPolyShader
        {
            get
            {
                if (_shader == null) _shader = Resources.Load<Shader>("Shaders/PangeaLowPoly");
                if (_shader == null) _shader = Shader.Find("Pangea/LowPoly");
                return _shader;
            }
        }

        /// <summary>Material low-poly compartilhado para uma cor nomeada (ex.: "team", "top").</summary>
        private static Material MaterialFor(Material imported)
        {
            string key = imported != null ? imported.name : "default";
            if (_materials.TryGetValue(key, out var m) && m != null) return m;

            // A paleta é a fonte de verdade: o importador de OBJ da Unity altera o Kd
            // (conversão de espaço de cor), então a cor importada só serve de reserva.
            Color c = Color.white;
            if (!Palette.TryGetValue(key, out c))
            {
                c = Color.white;
                if (imported != null)
                {
                    if (imported.HasProperty(BaseColorId)) c = imported.GetColor(BaseColorId);
                    else if (imported.HasProperty("_Color")) c = imported.color;
                }
            }
            m = new Material(LowPolyShader) { name = key, enableInstancing = true };
            m.SetColor(BaseColorId, c);
            m.SetColor(TintId, Color.white);
            _materials[key] = m;
            return m;
        }

        /// <summary>
        /// Instancia um modelo de Resources/Models/Base como filho de <paramref name="parent"/>,
        /// trocando os materiais importados pelo shader low-poly. Null se o modelo não existir.
        /// </summary>
        public static GameObject Spawn(string modelName, Transform parent)
        {
            if (!_prefabs.TryGetValue(modelName, out var prefab) || prefab == null)
            {
                prefab = Resources.Load<GameObject>("Models/Base/" + modelName);
                _prefabs[modelName] = prefab;
            }
            if (prefab == null || LowPolyShader == null)
            {
                Debug.LogError($"[World3D] modelo '{modelName}' ou shader low-poly não encontrado em Resources.");
                return null;
            }

            var go = Object.Instantiate(prefab, parent, false);
            go.name = modelName;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                var src = r.sharedMaterials;
                var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++) dst[i] = MaterialFor(src[i]);
                r.sharedMaterials = dst;
                r.sortingOrder = MeshSortingOrder;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            return go;
        }

        /// <summary>Filho direto pelo nome, ou descendente (o OBJ importa cada "o Nome" como filho).</summary>
        public static Transform FindPart(Transform root, string partName)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == partName) return t;
            return null;
        }

        /// <summary>
        /// Pinta os sub-materiais cujo nome está em <paramref name="colors"/>; os demais só
        /// recebem o tint. Usa MaterialPropertyBlock (não cria material por instância).
        /// </summary>
        public static void Paint(Transform root, Dictionary<string, Color> colors, Color tint)
        {
            if (root == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    Color baseCol = mats[i].GetColor(BaseColorId);
                    if (colors != null && colors.TryGetValue(mats[i].name, out var c)) baseCol = c;
                    r.GetPropertyBlock(_mpb, i);
                    _mpb.SetColor(BaseColorId, baseCol);
                    _mpb.SetColor(TintId, tint);
                    r.SetPropertyBlock(_mpb, i);
                }
            }
        }

        // ------------------------------------------------------------------ terreno

        private static readonly Dictionary<string, Dictionary<string, Color>> _terrainCache =
            new Dictionary<string, Dictionary<string, Color>>();

        /// <summary>Cores do bloco (top / side / side_dark) a partir do nome do terreno do atlas.</summary>
        public static Dictionary<string, Color> TerrainColors(string terrainName)
        {
            string n = (terrainName ?? "").ToLowerInvariant();
            if (_terrainCache.TryGetValue(n, out var cached)) return cached;

            Color top  = new Color(0.42f, 0.62f, 0.26f);   // grama
            Color side = new Color(0.52f, 0.38f, 0.24f);   // terra
            if (n.Contains("water") || n.Contains("agua"))      { top = new Color(0.22f, 0.50f, 0.85f); side = new Color(0.16f, 0.34f, 0.60f); }
            else if (n.Contains("lava") || n.Contains("fire"))  { top = new Color(0.95f, 0.42f, 0.10f); side = new Color(0.35f, 0.16f, 0.12f); }
            else if (n.Contains("snow") || n.Contains("ice"))   { top = new Color(0.90f, 0.94f, 0.97f); side = new Color(0.60f, 0.68f, 0.76f); }
            else if (n.Contains("sand") || n.Contains("desert")){ top = new Color(0.86f, 0.76f, 0.50f); side = new Color(0.68f, 0.56f, 0.36f); }
            else if (n.Contains("stone") || n.Contains("rock") || n.Contains("brick") || n.Contains("wall"))
                                                                { top = new Color(0.58f, 0.59f, 0.62f); side = new Color(0.42f, 0.43f, 0.46f); }
            else if (n.Contains("dirt") || n.Contains("mud") || n.Contains("path") || n.Contains("road"))
                                                                { top = new Color(0.56f, 0.42f, 0.27f); }
            else if (n.Contains("wood") || n.Contains("plank")) { top = new Color(0.62f, 0.45f, 0.26f); side = new Color(0.44f, 0.31f, 0.18f); }

            var d = new Dictionary<string, Color>
            {
                { "top", top }, { "side", side }, { "side_dark", side * 0.74f },
            };
            _terrainCache[n] = d;
            return d;
        }
    }
}
