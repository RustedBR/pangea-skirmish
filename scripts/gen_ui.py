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
TONES = [(168, 142, 98), (150, 124, 84), (182, 158, 112), (126, 100, 64)]   # 2026-11-14: bem mais escuro e velho
STAIN = (104, 80, 50)                                                      # manchas de idade
INK, LINE, HILITE = (38, 30, 26), (96, 74, 46), (196, 172, 124)
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
    """Pergaminho velho: miolo pontilhado com manchas de idade; borda rasgada e queimada.
    A borda (8 px) repete em 9-slice, então o rasgo também repete ao longo do painel."""
    rnd = random.Random(seed)
    m = N - 2 * B
    g = 6
    coarse = [[rnd.choice([0, 0, 0, 1, 2, 0, 3 if rnd.random() < 0.35 else 0]) for _ in range(g)] for _ in range(g)]
    core = [[TONES[tile_tone(rnd, coarse, x, y, m)] for x in range(m)] for y in range(m)]
    for _ in range(3):                                   # manchas redondas (umidade, idade)
        cx, cy, r = rnd.randrange(m), rnd.randrange(m), rnd.randint(3, 6)
        for y in range(m):
            for x in range(m):
                dx = min(abs(x - cx), m - abs(x - cx)); dy = min(abs(y - cy), m - abs(y - cy))
                d2 = dx * dx + dy * dy
                if d2 <= r * r and rnd.random() < 0.75: core[y][x] = STAIN
                elif d2 <= (r + 1) * (r + 1) and rnd.random() < 0.5: core[y][x] = TONES[1]
    img = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    px = img.load()
    for y in range(N):
        for x in range(N):
            px[x, y] = core[(y - B) % m][(x - B) % m] + (255,)
    # Rasgo: a profundidade do corte varia ao longo de cada lado (0–4 px); fora dele é transparente,
    # e logo dentro do corte fica uma faixa queimada escura. O mesmo perfil nos 4 lados, com fase diferente.
    prof = []
    depth = 1
    for i in range(N):
        depth = max(0, min(4, depth + rnd.choice([-1, 0, 0, 1])))
        prof.append(depth)
    def cut(x, y, d):
        if d < 0: return
        for k in range(d): px[x(k), y(k)] = (0, 0, 0, 0)
        px[x(d), y(d)] = INK + (255,)
        if d + 1 < B: px[x(d + 1), y(d + 1)] = (92, 66, 40, 255)
    for i in range(N):
        cut(lambda k: i, lambda k: k, prof[i])                          # topo
        cut(lambda k: i, lambda k: N - 1 - k, prof[(i + 17) % N])       # base
        cut(lambda k: k, lambda k: i, prof[(i + 33) % N])               # esquerda
        cut(lambda k: N - 1 - k, lambda k: i, prof[(i + 49) % N])       # direita
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
