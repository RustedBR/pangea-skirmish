#!/usr/bin/env python3
"""Texturas da identidade visual (GDD-025): pergaminho e tinta sobre o azul.

  python3 scripts/gen_ui.py        # grava em Assets/Resources/UI/Theme/

paper.png        painel de pergaminho (9-slice, borda 24) — menus e fichas
paper_inset.png  rebaixo no pergaminho (9-slice, borda 10) — cartões, listas, campos
Tamanhos em potência de 2 (o importador padrão do Unity reescala o que não for).
Determinístico (semente fixa): rodar de novo gera os mesmos arquivos.
"""
import os, random
from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.join(os.path.dirname(__file__), "..", "Assets", "Resources", "UI", "Theme")
CREAM, EDGE, INK, LINE = (238, 225, 192), (206, 184, 140), (58, 52, 70), (150, 124, 88)

def lerp(a, b, t): return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))

def paper(size, radius, vignette, seed, frame=True):
    rnd = random.Random(seed)
    img = Image.new("RGBA", (size, size))
    px = img.load()
    for y in range(size):
        for x in range(size):
            d = min(x, y, size - 1 - x, size - 1 - y)            # distância da borda
            t = max(0.0, 1.0 - d / vignette) ** 1.6              # escurece perto da borda
            c = lerp(CREAM, EDGE, t)
            n = rnd.randint(-5, 5)
            px[x, y] = (max(0, min(255, c[0] + n)), max(0, min(255, c[1] + n)), max(0, min(255, c[2] + n - 1)), 255)
    img = img.filter(ImageFilter.GaussianBlur(0.6))              # fibra suave, sem chuvisco
    d = ImageDraw.Draw(img)
    if frame:                                                    # filete interno a tinta sépia
        d.rounded_rectangle((6, 6, size - 7, size - 7), radius=max(2, radius - 3), outline=LINE + (150,), width=1)
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, size - 1, size - 1), radius=radius, fill=255)
    img.putalpha(mask)
    ImageDraw.Draw(img).rounded_rectangle((0, 0, size - 1, size - 1), radius=radius, outline=INK + (255,), width=2)
    return img

def inset(size, radius):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((0, 0, size - 1, size - 1), radius=radius, fill=(160, 128, 78, 46), outline=LINE + (190,), width=1)
    d.line((radius, 1, size - 1 - radius, 1), fill=(96, 74, 46, 70))   # sombra no topo: parece rebaixado
    return img

if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    paper(128, 10, 26, 2026).save(os.path.join(OUT, "paper.png"))
    inset(32, 5).save(os.path.join(OUT, "paper_inset.png"))
    print("ok:", sorted(f for f in os.listdir(OUT) if f.endswith(".png")))
