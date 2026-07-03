using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Gera um sprite de debug com grid para visualizar alinhamento dos tiles.
    /// Mostra linhas de grid isométrico para debugar posicionamento.
    /// </summary>
    public static class DebugGridSprite
    {
        public static Sprite CreateGridDebugSprite()
        {
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };

            // Limpar (transparente)
            Color clear = new Color(1, 1, 1, 0);
            for (int i = 0; i < S * S; i++)
                tex.SetPixel(i % S, i / S, clear);

            // Desenhar losango do isométrico (branco sólido)
            Vector2[] top = { new(0, 48), new(32, 64), new(64, 48), new(32, 32) };
            Vector2[] left = { new(0, 48), new(32, 32), new(32, 16), new(0, 32) };
            Vector2[] right = { new(64, 48), new(32, 32), new(32, 16), new(64, 32) };

            Color white = Color.white;

            // Preencher polígonos
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    if (InConvex(p, top) || InConvex(p, left) || InConvex(p, right))
                        tex.SetPixel(x, y, white);
                }
            }

            // Desenhar grid de debug (linhas de contorno)
            Color debug = new Color(1, 0, 0, 0.5f); // Vermelho translúcido

            // Linha horizontal no meio (y=32, altura do topo do losango)
            for (int x = 0; x < S; x++)
                tex.SetPixel(x, 32, debug);

            // Linha vertical no meio (x=32, centro do sprite)
            for (int y = 0; y < S; y++)
                tex.SetPixel(32, y, debug);

            // Diagonal de canto a canto (para ver rotação)
            for (int i = 0; i < S; i++)
            {
                tex.SetPixel(i, i, debug);
                tex.SetPixel(i, S - 1 - i, debug);
            }

            // Marcar o "ponto de pivot" (0.5, 0.75) com cor especial
            // 0.5 = x 32, 0.75 = y 48
            Color pivot = new Color(0, 1, 0, 0.8f); // Verde brilhante
            for (int x = 30; x <= 34; x++)
                for (int y = 46; y <= 50; y++)
                    tex.SetPixel(x, y, pivot);

            tex.Apply();

            // Pivot (0.5, 0.75) = centro do losango do topo (pixel 32,48 em 64x64)
            // Mesmo pivot dos tiles reais — debug mostra posicionamento REAL
            var sprite = Sprite.Create(tex, new Rect(0, 0, S, S),
                new Vector2(0.5f, 0.75f), 32);
            sprite.name = "debug_grid";

            Debug.Log("[DebugGridSprite] Sprite criado:\n" +
                     "  Branco: losango isométrico\n" +
                     "  Vermelho: grid de debug\n" +
                     "  Verde (centro): pivot (0.5, 0.75) teórico\n" +
                     "  Use em GridManager para identificar desalinhamento");

            return sprite;
        }

        private static bool InConvex(Vector2 p, Vector2[] poly)
        {
            bool pos = false, neg = false;
            for (int i = 0; i < poly.Length; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Length];
                float cross = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
                if (cross > 0.0001f) pos = true;
                else if (cross < -0.0001f) neg = true;
                if (pos && neg) return false;
            }
            return true;
        }
    }
}
