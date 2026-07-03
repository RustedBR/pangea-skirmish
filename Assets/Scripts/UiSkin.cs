using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Fatia sprites de montagens single-sprite do kit BDragon1727 EM RUNTIME
    /// (Sprite.Create sobre a textura), sem precisar fatiar no Sprite Editor.
    /// Rects são passados em coordenadas TOP-LEFT (como as réguas de análise) e
    /// convertidos para o sistema bottom-left do Unity. Tudo com cache; retorna
    /// null se o sheet/rect não existir (o chamador usa um fallback gerado).
    /// </summary>
    public static class UiSkin
    {
        private static readonly Dictionary<string, Sprite> _sprCache = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Texture2D> _texCache = new Dictionary<string, Texture2D>();

        public static bool Enabled => Tuning.Get().uiSkinEnabled;

        private static Texture2D LoadTex(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_texCache.TryGetValue(path, out var cached)) return cached;
            var spr = Resources.Load<Sprite>(path);
            Texture2D tex = spr != null ? spr.texture : Resources.Load<Texture2D>(path);
            _texCache[path] = tex;
            if (tex == null) Debug.LogWarning($"[UiSkin] textura não encontrada: {path}");
            return tex;
        }

        /// <summary>Fatia um sub-sprite. x,y,w,h em TOP-LEFT. Retorna null se inválido.</summary>
        public static Sprite Slice(string path, int x, int y, int w, int h, float ppu = 32f)
        {
            if (!Enabled) return null;
            string key = $"{path}:{x},{y},{w},{h}";
            if (_sprCache.TryGetValue(key, out var s)) return s;

            Sprite spr = null;
            var tex = LoadTex(path);
            if (tex != null)
            {
                int ty = tex.height - (y + h); // top-left -> bottom-left
                if (x >= 0 && ty >= 0 && x + w <= tex.width && ty + h <= tex.height)
                    spr = Sprite.Create(tex, new Rect(x, ty, w, h), new Vector2(0.5f, 0.5f), ppu);
                else
                    Debug.LogWarning($"[UiSkin] rect fora dos limites em {path}: {x},{y},{w},{h} (tex {tex.width}x{tex.height})");
            }
            _sprCache[key] = spr;
            return spr;
        }

        /// <summary>Conveniência: fatia usando um Vector4 (x,y,w,h).</summary>
        public static Sprite Slice(string path, Vector4 rect, float ppu = 32f)
            => Slice(path, (int)rect.x, (int)rect.y, (int)rect.z, (int)rect.w, ppu);

        /// <summary>
        /// Fatia um sub-sprite com borda 9-slice (Sprite.border), para uso com
        /// Image.type=Sliced (UI) ou SpriteRenderer.drawMode=Sliced (mundo).
        /// border em px: (left,bottom,right,top). x,y,w,h em TOP-LEFT.
        /// </summary>
        public static Sprite SliceSliced(string path, int x, int y, int w, int h, Vector4 border, float ppu = 32f, bool flipV = false)
        {
            if (!Enabled) return null;
            string key = $"{path}:{x},{y},{w},{h}:9:{border}:{flipV}";
            if (_sprCache.TryGetValue(key, out var s)) return s;

            Sprite spr = null;
            var tex = LoadTex(path);
            if (tex != null)
            {
                int ty = tex.height - (y + h);
                if (x >= 0 && ty >= 0 && x + w <= tex.width && ty + h <= tex.height)
                {
                    if (flipV)
                    {
                        var flipped = FlippedRegion(tex, x, ty, w, h);
                        spr = Sprite.Create(flipped, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), ppu,
                            0, SpriteMeshType.FullRect, border);
                    }
                    else
                    {
                        spr = Sprite.Create(tex, new Rect(x, ty, w, h), new Vector2(0.5f, 0.5f), ppu,
                            0, SpriteMeshType.FullRect, border);
                    }
                }
                else
                    Debug.LogWarning($"[UiSkin] rect fora dos limites em {path}: {x},{y},{w},{h} (tex {tex.width}x{tex.height})");
            }
            _sprCache[key] = spr;
            return spr;
        }

        public static Sprite SliceSliced(string path, Vector4 rect, Vector4 border, float ppu = 32f, bool flipV = false)
            => SliceSliced(path, (int)rect.x, (int)rect.y, (int)rect.z, (int)rect.w, border, ppu, flipV);

        /// <summary>
        /// Recorta uma região (coords bottom-left, já convertidas) e devolve uma cópia
        /// verticalmente espelhada, via GPU blit + readback — funciona mesmo com a
        /// textura de origem marcada como "não legível" (Read/Write desabilitado).
        /// Usado para inverter a direção do sombreado de um sprite sem editar o PNG.
        /// </summary>
        private static Texture2D FlippedRegion(Texture2D src, int x, int y, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            var prevActive = RenderTexture.active;
            Vector2 scale  = new Vector2((float)w / src.width, -(float)h / src.height);
            Vector2 offset = new Vector2((float)x / src.width, (float)(y + h) / src.height);
            Graphics.Blit(src, rt, scale, offset);
            RenderTexture.active = rt;
            var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            outTex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            outTex.Apply();
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);
            return outTex;
        }

        // ── Moldura de painel gerada (não existe no kit BDragon; estilo compatível) ──
        private static readonly Dictionary<string, Sprite> _frameCache = new Dictionary<string, Sprite>();

        /// <summary>
        /// Gera (e cacheia) uma moldura pixel-art 9-slice: contorno preto + realce claro,
        /// cantos arredondados, miolo transparente (pra combinar com o gradiente já usado
        /// nos painéis FFT). Só a geometria de canto muda com "corner"/"borderPx"; a cor
        /// pode ser ajustada ao vivo sem regenerar (ver GameTuning windowFrame*).
        /// </summary>
        public static Sprite GeneratedWindowFrame(int corner, int borderPx, Color borderColor, Color highlightColor)
        {
            string key = $"{corner}:{borderPx}:{borderColor}:{highlightColor}";
            if (_frameCache.TryGetValue(key, out var cached) && cached != null) return cached;

            int pad = 2; // margem extra pra suavizar o teste de círculo do canto
            int b = corner + pad + 1; // espessura da borda 9-slice (mesma p/ os 4 lados)
            // Miolo generoso (não só o mínimo pra caber a borda): um miolo de poucos px é
            // um caso degenerado pro Image.Type.Sliced esticar (a faixa reta da borda vira
            // uma tira quase-zero de largura na textura de origem), o que gerava um artefato
            // de "bandeirinha" nos cantos. Com bastante miolo transparente, o slice esticado
            // continua estável em qualquer tamanho de painel.
            int mid = 32;
            int size = b * 2 + mid;
            // wrapMode=Clamp é essencial: sem isso (padrão Repeat) o Image.Type.Sliced pode
            // amostrar o texel do lado OPOSTO da textura por arredondamento de UV na borda de
            // cada slice, vazando a cor do canto oposto como uma "bandeirinha" diagonal.
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float edgeDist;
                    bool outside = false;

                    bool cornerX = x < corner || x > size - 1 - corner;
                    bool cornerY = y < corner || y > size - 1 - corner;
                    if (cornerX && cornerY)
                    {
                        float ccx = x < corner ? corner : size - 1 - corner;
                        float ccy = y < corner ? corner : size - 1 - corner;
                        float dx = x - ccx, dy = y - ccy;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        if (dist > corner) outside = true;
                        edgeDist = corner - dist;
                    }
                    else
                    {
                        edgeDist = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
                    }

                    Color c;
                    if (outside) c = Color.clear;
                    else if (edgeDist < borderPx) c = borderColor;
                    else if (edgeDist < borderPx + 1f) c = highlightColor;
                    else c = Color.clear; // miolo: transparente, deixa o fill por baixo aparecer

                    px[y * size + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply();

            var spr = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 32f,
                0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            _frameCache[key] = spr;
            return spr;
        }

        private static readonly Dictionary<int, Sprite> _maskCache = new Dictionary<int, Sprite>();

        /// <summary>
        /// Gera (e cacheia) uma forma sólida de retângulo arredondado (branco opaco dentro,
        /// transparente fora), pra usar como shape de um componente Mask — assim o
        /// PREENCHIMENTO por trás (ex: o gradiente FFT) fica recortado no mesmo raio da
        /// moldura, em vez de continuar quadrado atrás do anel decorativo.
        /// </summary>
        public static Sprite GeneratedRoundedMask(int corner)
        {
            if (_maskCache.TryGetValue(corner, out var cached) && cached != null) return cached;

            int pad = 2;
            int b = corner + pad + 1;
            int mid = 32;
            int size = b * 2 + mid;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool cornerX = x < corner || x > size - 1 - corner;
                    bool cornerY = y < corner || y > size - 1 - corner;
                    bool outside = false;
                    if (cornerX && cornerY)
                    {
                        float ccx = x < corner ? corner : size - 1 - corner;
                        float ccy = y < corner ? corner : size - 1 - corner;
                        float dx = x - ccx, dy = y - ccy;
                        if (Mathf.Sqrt(dx * dx + dy * dy) > corner) outside = true;
                    }
                    px[y * size + x] = outside ? Color.clear : Color.white;
                }
            }
            tex.SetPixels(px);
            tex.Apply();

            var spr = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 32f,
                0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            _maskCache[corner] = spr;
            return spr;
        }

        // ── Fallbacks gerados ──────────────────────────────────
        private static Sprite _downArrow;

        /// <summary>Triângulo apontando para BAIXO (fallback do marcador).</summary>
        public static Sprite FallbackDownArrow()
        {
            if (_downArrow != null) return _downArrow;
            const int S = 16;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            for (int yy = 0; yy < S; yy++)
            for (int xx = 0; xx < S; xx++)
            {
                // y=0 é a base (topo do triângulo), aponta para baixo conforme y cresce
                float t = yy / (float)(S - 1);              // 0 no topo, 1 embaixo
                float halfWidth = (1f - t) * (S * 0.5f);    // estreita para baixo
                bool inside = Mathf.Abs(xx - S * 0.5f) <= halfWidth;
                tex.SetPixel(xx, yy, inside ? Color.white : Color.clear);
            }
            tex.Apply();
            _downArrow = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 32f);
            return _downArrow;
        }
    }
}
