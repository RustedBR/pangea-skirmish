using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Categorias de tile/pincel do atlas TinyTactics (Sprite Mode: Multiple).
    /// </summary>
    public enum TileKind
    {
        Terrain,   // chão empilhável (grama full/half, água, etc.)
        Ramp,      // rampa direcional "ponte de altura" (liga 2 níveis)
        Object,    // objeto 1-por-célula (árvore, pedra, arbusto)
    }

    /// <summary>
    /// Metadados derivados do nome do sprite no atlas.
    /// </summary>
    public class TileDef
    {
        public string name;
        public TileKind kind;
        public int height;     // full=+2, half=+1, água=-2/-1, objeto=0 (não empilha terreno)
        public bool isWall;     // bloqueia a célula inteira (objeto sólido)
        public string dir;      // direção da rampa (NO/NE/SO/SE), vazio p/ demais
    }

    /// <summary>
    /// Carrega sprites individuais do atlas TinyTactics indexados PELO NOME
    /// (Sprite Mode: Multiple — Marcus nomeou cada sprite no Editor).
    ///
    /// Mantém GetTile(int) como legado (não usado pela paleta nova, mas evita
    /// quebra de compilação enquanto a migração de PaintOp/MapStorage acontece).
    /// </summary>
    public class TileDatabase : MonoBehaviour
    {
        private const string TILESET_PATH = "Sprites/TinyTactics/Tiles/tileset";

        // Dimensões do atlas (verificado no .meta)
        private const int ATLAS_HEIGHT = 416;
        private const int ATLAS_COLS = 8;
        private const int SPRITE_SIZE = 64;
        private const int BLOCK_SIZE = 32;
        public const float BLOCK_PPU = 16f;
        private static readonly Vector2 ISO_PIVOT = new Vector2(0.5f, 0.75f);

        private Texture2D _atlasTexture;
        private readonly Dictionary<int, Sprite> _cache = new();
        private readonly Dictionary<string, Sprite> _byName = new();
        private readonly Dictionary<string, TileDef> _defs = new();
        private bool _loaded;

        private void Awake() => EnsureLoaded();

        private void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            _atlasTexture = Resources.Load<Texture2D>(TILESET_PATH);
            if (_atlasTexture == null)
                Debug.LogError($"[TileDatabase] Textura não encontrada: Resources/{TILESET_PATH}");

            var sprites = Resources.LoadAll<Sprite>(TILESET_PATH);
            foreach (var sp in sprites)
            {
                if (sp == null) continue;
                // Recria o sprite com pivot na BASE (0.5, 0.75) e PPU 16 (BLOCK_PPU),
                // IGUAL ao código antigo que funcionava. O atlas nativo tem PPU 32,
                // mas o grid iso espera tiles de 2 unidades de mundo (halfW=1.0),
                // por isso travamos em 16 aqui.
                var rebuilt = Sprite.Create(
                    sp.texture,
                    sp.rect,
                    ISO_PIVOT,
                    BLOCK_PPU,
                    1,
                    SpriteMeshType.Tight,
                    new Vector4(0, 0, 0, 0),
                    true);
                rebuilt.name = sp.name;
                _byName[rebuilt.name] = rebuilt;
                _defs[rebuilt.name] = ParseDef(rebuilt.name);
            }
            Debug.Log($"[TileDatabase] Carregados {_byName.Count} sprites por nome");
        }

        /// <summary>Retorna o sprite pelo NOME (caminho novo da paleta).</summary>
        public Sprite GetTile(string name)
        {
            EnsureLoaded();
            return _byName.TryGetValue(name, out var sp) ? sp : null;
        }

        /// <summary>Metadados de gameplay derivados do nome.</summary>
        public TileDef GetDef(string name)
        {
            EnsureLoaded();
            return _defs.TryGetValue(name, out var d) ? d : null;
        }

        // Cache de sprites de "face lateral" (metade inferior do tile, usada nas
        // paredes de tiles empilhados — efeito de cubo com textura real).
        private readonly Dictionary<string, Sprite> _sideFaces = new();

        /// <summary>
        /// Retorna a "face lateral" do tile: recorte da METADE INFERIOR do sprite
        /// (a projeção iso que vira a parede do cubo), com pivot na base (0.5, 0)
        /// e PPU 16, para poder ser empilhada N vezes conforme a altura.
        /// </summary>
        public Sprite GetSideFace(string name)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(name)) return null;
            if (_sideFaces.TryGetValue(name, out var cached)) return cached;

            var sp = GetTile(name);
            if (sp == null) return null;

            var r = sp.rect;
            // Metade inferior do sprite na textura (a "parede" do cubo iso)
            var sideRect = new Rect(r.x, r.y, r.width, r.height * 0.5f);
            var side = Sprite.Create(
                _atlasTexture,
                sideRect,
                new Vector2(0.5f, 0f),   // pivot na base → empilha de baixo p/ cima
                BLOCK_PPU,
                1,
                SpriteMeshType.FullRect,  // retângulo (não segue contorno do losango)
                new Vector4(0, 0, 0, 0),
                true);
            side.name = name + "_side";
            _sideFaces[name] = side;
            return side;
        }

        public bool HasName(string name)
        {
            EnsureLoaded();
            return _byName.ContainsKey(name);
        }

        /// <summary>
        /// Parseia o nome do sprite (ex.: grass_tile_full, water_half, tree,
        /// grass_tile_ramp_NO) em TileDef (kind/height/isWall/dir).
        /// </summary>
        private static TileDef ParseDef(string name)
        {
            var d = new TileDef { name = name, kind = TileKind.Terrain, height = 0, isWall = false, dir = "" };
            string n = name.ToLowerInvariant();

            // Objetos (1 por célula)
            if (n.StartsWith("tree") || n.StartsWith("stone"))
            {
                d.kind = TileKind.Object;
                d.isWall = true;       // árvore/pedra = parede (regra b)
                d.height = 0;
                return d;
            }
            if (n.StartsWith("bush"))
            {
                d.kind = TileKind.Object;
                d.isWall = false;      // arbusto = decorativo (não-parede)
                d.height = 0;
                return d;
            }

            // Rampas direcionais
            if (n.Contains("ramp"))
            {
                d.kind = TileKind.Ramp;
                // direção: NO/NE/SO/SE
                foreach (var dir in new[] { "NO", "NE", "SO", "SE" })
                    if (n.Contains(dir.ToLowerInvariant())) { d.dir = dir; break; }
                return d;
            }
            // Escadas (rampa de 2 níveis) — tratadas como rampa alta
            if (n.Contains("stairs"))
            {
                d.kind = TileKind.Ramp;
                foreach (var dir in new[] { "NE", "NO" })
                    if (n.Contains(dir.ToLowerInvariant())) { d.dir = dir; break; }
                return d;
            }

            // Terreno — altura por tipo
            if (n.Contains("water"))
            {
                d.height = n.Contains("half") ? -1 : -2;   // água negativa
                return d;
            }
            if (n.Contains("half"))
            {
                d.height = 1;   // grama half = +1
                return d;
            }
            if (n.Contains("full"))
            {
                d.height = 2;   // grama full = +2
                return d;
            }
            // cliffs/slides/tileset_* antigos: terreno plano (height 0)
            return d;
        }

        // ── Legado (mantido p/ não quebrar compilação durante a migração) ──
        public Sprite GetTile(int index)
        {
            if (_cache.TryGetValue(index, out var cached)) return cached;
            var sprite = LoadBlockAt(index);
            if (sprite != null) _cache[index] = sprite;
            return sprite;
        }

        private Sprite LoadBlockAt(int index)
        {
            if (_atlasTexture == null) return null;
            int col = index % ATLAS_COLS;
            int row = index / ATLAS_COLS;
            int unityY_base = ATLAS_HEIGHT - SPRITE_SIZE - row * SPRITE_SIZE;
            if (unityY_base < 0) return null;
            int rectX = col * SPRITE_SIZE;
            int rectY = unityY_base + BLOCK_SIZE;
            var rect = new Rect(rectX, rectY, BLOCK_SIZE, BLOCK_SIZE);
            var sprite = Sprite.Create(_atlasTexture, rect, ISO_PIVOT, BLOCK_PPU);
            sprite.name = $"block_{index}";
            return sprite;
        }

        public string GetDebugInfo() =>
            _atlasTexture != null
                ? $"Atlas: {_atlasTexture.width}×{_atlasTexture.height}, sprites(nome)={_byName.Count}"
                : "Atlas NÃO carregado";
    }
}
