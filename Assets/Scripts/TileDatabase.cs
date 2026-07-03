using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Carrega sprites individuais de blocos isométricos do tileset TinyTactics.
    ///
    /// Layout do atlas (tileset.png 512×416):
    ///   - Sprites "tileset_*" são 64×64, mas mostram 4 blocos em 2×2 (composite)
    ///   - Bloco individual = quadrante top-left de cada tileset_N = 32×32 pixels
    ///   - Cada tileset_N: rect(col*64, 352-(row*64), 64, 64) em Unity coords
    ///     → TL quadrante: rect(col*64, 352-(row*64)+32, 32, 32)
    ///
    /// Para renderizar com o grid atual (halfW=1, halfH=0.5 para blocos 64×64):
    ///   → PPU=16: faz 32px aparecer como 2 unidades de mundo (igual bloco 64×64 com PPU=32)
    ///   → pivot (0.5, 0.75): centro do losango do topo dentro do bloco 32×32
    /// </summary>
    public class TileDatabase : MonoBehaviour
    {
        private const string TILESET_TEXTURE_PATH = "Sprites/TinyTactics/Tiles/tileset";

        // Dimensões do atlas TinyTactics (verificado no meta file)
        private const int ATLAS_HEIGHT = 416;
        private const int ATLAS_COLS = 8;        // 8 sprites de 64px por linha
        private const int SPRITE_SIZE = 64;       // tamanho do tileset_* composite
        private const int BLOCK_SIZE = 32;        // bloco individual dentro do composite
        private const float BLOCK_PPU = 16f;      // PPU para 32px → 2 unidades de mundo

        // Pivot no centro do losango do topo: pixel (16,24) em 32×32 → (0.5, 0.75)
        private static readonly Vector2 ISO_PIVOT = new Vector2(0.5f, 0.75f);

        private Texture2D _atlasTexture;
        private readonly Dictionary<int, Sprite> _cache = new();

        private void Awake()
        {
            _atlasTexture = Resources.Load<Texture2D>(TILESET_TEXTURE_PATH);
            if (_atlasTexture == null)
                Debug.LogError($"[TileDatabase] Textura não encontrada: Resources/{TILESET_TEXTURE_PATH}");
        }

        /// <summary>Retorna o sprite do bloco individual no índice do tileset.</summary>
        public Sprite GetTile(int index)
        {
            if (_cache.TryGetValue(index, out var cached))
                return cached;

            var sprite = LoadBlockAt(index);
            if (sprite != null)
                _cache[index] = sprite;

            return sprite;
        }

        private Sprite LoadBlockAt(int index)
        {
            if (_atlasTexture == null) return null;

            // Calcular posição do tileset_N no atlas
            int col = index % ATLAS_COLS;
            int row = index / ATLAS_COLS;

            // Unity usa Y-up; tileset_0..7 ficam em y=352 (= 416-64), row 1 em y=288, etc.
            int unityY_base = ATLAS_HEIGHT - SPRITE_SIZE - row * SPRITE_SIZE;
            if (unityY_base < 0)
            {
                Debug.LogWarning($"[TileDatabase] Índice {index} fora do atlas");
                return null;
            }

            // Quadrante top-left (TL) = bloco individual
            // Em Unity coords: TL = x_base, y_base+32 (metade superior)
            int rectX = col * SPRITE_SIZE;
            int rectY = unityY_base + BLOCK_SIZE;   // metade superior em coords Unity
            var rect = new Rect(rectX, rectY, BLOCK_SIZE, BLOCK_SIZE);

            // PPU=16 → 32px aparece como 2 unidades de mundo (mesmo que bloco 64×64 com PPU=32)
            var sprite = Sprite.Create(_atlasTexture, rect, ISO_PIVOT, BLOCK_PPU);
            sprite.name = $"block_{index}";
            return sprite;
        }

        /// <summary>
        /// Retorna sprite da face de penhasco (cliff) para bordas do platô elevado.
        /// dir=0: face SW (borda sul) — quadrante BL do tileset_1
        /// dir=1: face SE (borda leste) — quadrante BR do tileset_1
        /// </summary>
        public Sprite GetCliffFace(int dir)
        {
            if (_atlasTexture == null) return null;

            const int CLIFF_INDEX = 1;
            int col = CLIFF_INDEX % ATLAS_COLS;
            int row = CLIFF_INDEX / ATLAS_COLS;
            int unityY_base = ATLAS_HEIGHT - SPRITE_SIZE - row * SPRITE_SIZE;

            // BL = left column, lower half  → dir=0
            // BR = right column, lower half → dir=1
            int rectX = col * SPRITE_SIZE + (dir == 1 ? BLOCK_SIZE : 0);
            int rectY = unityY_base; // lower half in Unity Y-up = PIL bottom half

            var cacheKey = 1000 + dir;
            if (_cache.TryGetValue(cacheKey, out var cached)) return cached;

            var sprite = Sprite.Create(_atlasTexture,
                new Rect(rectX, rectY, BLOCK_SIZE, BLOCK_SIZE),
                ISO_PIVOT, BLOCK_PPU);
            sprite.name = dir == 0 ? "cliff_sw" : "cliff_se";
            _cache[cacheKey] = sprite;
            return sprite;
        }

        /// <summary>Info de debug sobre o tileset.</summary>
        public string GetDebugInfo()
        {
            return _atlasTexture != null
                ? $"Atlas: {_atlasTexture.width}×{_atlasTexture.height}, PPU={BLOCK_PPU}, bloco={BLOCK_SIZE}×{BLOCK_SIZE}px"
                : "Atlas NÃO carregado";
        }
    }
}
