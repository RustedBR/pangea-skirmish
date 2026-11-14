#!/usr/bin/env python3
"""Texturas da identidade visual (GDD-025): pergaminho e tinta sobre o azul.

  python3 scripts/gen_ui.py        # grava em Assets/Resources/UI/Theme/

paper.png        painel de pergaminho pixelado (64 px, 9-slice de borda 8, miolo que repete)
paper_inset.png  rebaixo no pergaminho (16 px, borda 4) — cartões, listas, campos
Os .meta fixam filtro "ponto" e sem compressão, para os pixels ficarem nítidos.
Determinístico (semente fixa): rodar de novo gera os mesmos arquivos.
"""
import os, random
from PIL import Image

OUT = os.path.join(os.path.dirname(__file__), "..", "Assets", "Resources", "UI", "Theme")
# Pergaminho mais escuro e PIXELADO (pedido 2026-11-14): 4 tons, sem suavização. A textura é pequena
# (64 px) e o tema a desenha em escala 2× com filtro "ponto"; o miolo (48×48) repete sem emenda.
TONES = [(212, 191, 146), (201, 178, 131), (222, 203, 160), (186, 161, 114)]   # base, sombra, luz, mancha
INK, LINE, HILITE = (46, 42, 60), (128, 102, 68), (236, 220, 180)
B = 8            # borda do 9-slice, em px da textura
N = 64

def tile_tone(rnd, coarse, x, y, m):
    """Tom do pixel (x, y) num miolo m×m que repete: manchas largas + pontilhado."""
    g = len(coarse)
    c = coarse[(y * g // m) % g][(x * g // m) % g]
    r = rnd.random()
    if c == 3 and r < 0.55: return 3
    if c in (1, 2) and r < 0.42: return c          # mancha pontilhada, não bloco chapado
    if r > 0.90: return 1
    if r > 0.84: return 2
    return 0

def paper(seed):
    rnd = random.Random(seed)
    m = N - 2 * B
    g = 6
    coarse = [[rnd.choice([0, 0, 0, 1, 2, 0, 3 if rnd.random() < 0.25 else 0]) for _ in range(g)] for _ in range(g)]
    core = [[TONES[tile_tone(rnd, coarse, x, y, m)] for x in range(m)] for y in range(m)]
    img = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    px = img.load()
    for y in range(N):
        for x in range(N):
            px[x, y] = core[(y - B) % m][(x - B) % m] + (255,)      # a borda continua o desenho do miolo
    # moldura em degraus (canto de pixel art): tinta por fora, filete claro e sépia por dentro
    def ring(d, col):
        for i in range(d, N - d):
            for (x, y) in ((i, d), (i, N - 1 - d), (d, i), (N - 1 - d, i)): px[x, y] = col + (255,)
    ring(0, INK); ring(1, INK); ring(2, HILITE); ring(4, LINE)
    for cx, cy in ((0, 0), (N - 1, 0), (0, N - 1), (N - 1, N - 1)):  # cantos cortados em 2 px
        sx, sy = (1 if cx == 0 else -1), (1 if cy == 0 else -1)
        for a, b in ((0, 0), (1, 0), (0, 1)): px[cx + a * sx, cy + b * sy] = (0, 0, 0, 0)
        px[cx + sx, cy + sy] = INK + (255,); px[cx + 2 * sx, cy + sy] = INK + (255,); px[cx + sx, cy + 2 * sy] = INK + (255,)
        px[cx + 2 * sx, cy + 2 * sy] = INK + (255,)
    return img

def inset():
    """Rebaixo no pergaminho (16 px, borda 4): mais escuro, com sombra no topo."""
    n = 16
    img = Image.new("RGBA", (n, n), TONES[3] + (120,))
    px = img.load()
    for i in range(n):
        for (x, y) in ((i, 0), (i, n - 1), (0, i), (n - 1, i)): px[x, y] = LINE + (255,)
        if 0 < i < n - 1: px[i, 1] = (96, 76, 48, 170)
    for c in ((0, 0), (n - 1, 0), (0, n - 1), (n - 1, n - 1)): px[c] = (0, 0, 0, 0)
    return img

if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    paper(2026).save(os.path.join(OUT, "paper.png"))
    inset().save(os.path.join(OUT, "paper_inset.png"))
    print("ok:", sorted(f for f in os.listdir(OUT) if f.endswith(".png")))
