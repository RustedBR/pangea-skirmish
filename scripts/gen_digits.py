#!/usr/bin/env python3
"""Dígitos em pixel para os números de dano e cura (2026-11-13).

Glifos 5×7 em branco com contorno escuro de 1 px (célula 7×9), ampliados 4× sem
suavização para Assets/Resources/UI/Icons/num_*.png. A cor (vermelho no dano, verde
na cura) é aplicada no jogo, multiplicando o branco; o contorno fica escuro.

  python3 scripts/gen_digits.py
"""
from pathlib import Path
from PIL import Image

OUT = Path(__file__).resolve().parent.parent / "Assets" / "Resources" / "UI" / "Icons"
SCALE = 4
G = {
"0": ["01110","10001","10011","10101","11001","10001","01110"],
"1": ["00100","01100","00100","00100","00100","00100","01110"],
"2": ["01110","10001","00001","00110","01000","10000","11111"],
"3": ["11110","00001","00001","01110","00001","00001","11110"],
"4": ["00010","00110","01010","10010","11111","00010","00010"],
"5": ["11111","10000","11110","00001","00001","10001","01110"],
"6": ["00110","01000","10000","11110","10001","10001","01110"],
"7": ["11111","00001","00010","00100","01000","01000","01000"],
"8": ["01110","10001","10001","01110","10001","10001","01110"],
"9": ["01110","10001","10001","01111","00001","00010","01100"],
"plus":  ["00000","00100","00100","11111","00100","00100","00000"],
"minus": ["00000","00000","00000","11111","00000","00000","00000"],
"bang":  ["00100","00100","00100","00100","00100","00000","00100"],
}
W, H = 7, 9
def glyph(rows):
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    on = {(x + 1, y + 1) for y, r in enumerate(rows) for x, c in enumerate(r) if c == "1"}
    for (x, y) in on:
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                p = (x + dx, y + dy)
                if 0 <= p[0] < W and 0 <= p[1] < H and p not in on:
                    img.putpixel(p, (20, 10, 10, 255))
    for p in on: img.putpixel(p, (255, 255, 255, 255))
    return img.resize((W * SCALE, H * SCALE), Image.NEAREST)
if __name__ == "__main__":
    for k, rows in G.items():
        glyph(rows).save(OUT / f"num_{k}.png")
    print(f"{len(G)} dígitos em {OUT}")
