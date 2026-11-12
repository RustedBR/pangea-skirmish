#!/usr/bin/env python3
"""Gera os efeitos sonoros de combate do Pangea Skirmish por síntese (numpy), sem amostras externas.

Uso:  python3 scripts/gen_sfx.py
Saída: Assets/Resources/Audio/sfx_*.wav (PCM 16 bit mono 44,1 kHz) — o nome do arquivo é o
nome usado em AudioManager.PlayNamed. Sons que já existiam (hit, death, heal, miss, dodge...)
não são tocados aqui: só os novos. Determinístico (semente fixa): rodar de novo dá os mesmos bytes.
"""
import os, wave
import numpy as np

SR = 44100
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "Resources", "Audio")
rng = np.random.default_rng(7)

def t(d): return np.linspace(0, d, int(SR * d), endpoint=False)
def noise(d): return rng.uniform(-1, 1, int(SR * d))
def env(n, a=0.005, r=0.1, curve=2.0):
    """Ataque linear curto e queda exponencial."""
    e = np.ones(n); na = max(1, int(SR * a))
    e[:na] = np.linspace(0, 1, na)
    k = np.arange(n - na) / max(1, n - na)
    e[na:] = (1 - k) ** curve
    return e
def sweep(d, f0, f1, shape="sin"):
    x = t(d); f = f0 * (f1 / f0) ** (x / d)
    ph = 2 * np.pi * np.cumsum(f) / SR
    return np.sin(ph) if shape == "sin" else np.sign(np.sin(ph)) if shape == "sq" else 2 * (ph / (2 * np.pi) % 1) - 1
def lp(x, cutoff):
    """Passa-baixa de um polo."""
    a = np.exp(-2 * np.pi * cutoff / SR); y = np.empty_like(x); acc = 0.0
    for i, v in enumerate(x):
        acc = a * acc + (1 - a) * v; y[i] = acc
    return y
def hp(x, cutoff): return x - lp(x, cutoff)
def bp(x, lo, hi): return lp(hp(x, lo), hi)
def mix(*parts):
    n = max(len(p) for p in parts); y = np.zeros(n)
    for p in parts: y[:len(p)] += p
    return y
def pad(x, before=0.0, total=None):
    y = np.concatenate([np.zeros(int(SR * before)), x])
    if total: y = np.concatenate([y, np.zeros(max(0, int(SR * total) - len(y)))])
    return y

def slash():      # lâmina cortando o ar + toque metálico curto
    w = bp(noise(0.22), 900, 7000) * env(int(SR * 0.22), 0.03, 0.1, 1.6) * sweep(0.22, 1.0, 1.0) ** 0
    w *= np.linspace(0.3, 1.0, len(w)) ** 2 * np.linspace(1, 0, len(w))
    ring = (np.sin(2 * np.pi * 2600 * t(0.18)) + 0.6 * np.sin(2 * np.pi * 3900 * t(0.18))) * env(int(SR * 0.18), 0.001, 0.1, 3) * 0.25
    return mix(w * 1.6, pad(ring, 0.08))
def chop():       # machado: zunido grave e pancada surda
    w = bp(noise(0.20), 300, 2500) * np.linspace(0.2, 1, int(SR * 0.20)) ** 2
    thud = sweep(0.22, 180, 55) * env(int(SR * 0.22), 0.002, 0.2, 2.2)
    crack = bp(noise(0.06), 1500, 6000) * env(int(SR * 0.06), 0.001, 0.05, 3)
    return mix(w * 0.9, pad(thud * 1.2, 0.17), pad(crack * 0.7, 0.17))
def bow():        # corda soltando + flecha indo embora
    tw = (sweep(0.16, 520, 300, "saw") * 0.6 + np.sin(2 * np.pi * 980 * t(0.16)) * 0.3) * env(int(SR * 0.16), 0.001, 0.15, 3)
    fl = bp(noise(0.28), 1800, 9000) * np.linspace(1, 0, int(SR * 0.28)) ** 1.5 * 0.5
    return mix(tw, pad(fl, 0.02))
def thrust():     # cajado: sopro curto e batida de madeira
    w = bp(noise(0.14), 500, 3500) * env(int(SR * 0.14), 0.04, 0.1, 1.5) * 0.7
    knock = (sweep(0.09, 420, 240) + 0.5 * sweep(0.09, 900, 600)) * env(int(SR * 0.09), 0.001, 0.08, 3)
    return mix(w, pad(knock, 0.10))
def bite():       # fera: rosnado e estalo de mandíbula
    g = sweep(0.26, 95, 70, "saw") * (0.6 + 0.4 * np.sin(2 * np.pi * 31 * t(0.26))) * env(int(SR * 0.26), 0.03, 0.2, 1.4)
    g = lp(g, 900)
    snap = bp(noise(0.05), 1200, 5000) * env(int(SR * 0.05), 0.001, 0.04, 3)
    return mix(g * 1.3, pad(snap, 0.20), pad(sweep(0.06, 300, 120) * env(int(SR * 0.06), 0.001, 0.05, 2) * 0.8, 0.20))
def block():      # escudo de couro e osso aparando o golpe
    th = sweep(0.20, 210, 90) * env(int(SR * 0.20), 0.001, 0.18, 2.4)
    cl = (np.sin(2 * np.pi * 760 * t(0.14)) + 0.5 * np.sin(2 * np.pi * 1180 * t(0.14))) * env(int(SR * 0.14), 0.001, 0.12, 3.5) * 0.5
    n = bp(noise(0.05), 800, 4000) * env(int(SR * 0.05), 0.001, 0.04, 3) * 0.8
    return mix(th * 1.2, cl, n)
def cast_start(): # começa a conjurar: zumbido que sobe
    x = t(0.55); f = 180 * (3.2) ** (x / 0.55)
    ph = 2 * np.pi * np.cumsum(f) / SR
    y = (np.sin(ph) + 0.5 * np.sin(2.01 * ph) + 0.3 * np.sin(3.02 * ph)) * (0.7 + 0.3 * np.sin(2 * np.pi * 9 * x))
    return y * np.linspace(0.1, 1, len(x)) * env(len(x), 0.05, 0.15, 0.8) * 0.5
def spell_fire(): # labareda: sopro crepitante + explosão surda
    wh = bp(noise(0.34), 300, 4500) * np.linspace(0.3, 1, int(SR * 0.34)) ** 1.5
    crack = hp(noise(0.34), 5000) * (rng.uniform(0, 1, int(SR * 0.34)) > 0.985) * 3
    boom = sweep(0.40, 130, 42) * env(int(SR * 0.40), 0.002, 0.35, 2)
    tail = lp(noise(0.40), 700) * env(int(SR * 0.40), 0.01, 0.3, 2) * 0.8
    return mix(wh * 0.8, crack * 0.5, pad(boom * 1.3, 0.26), pad(tail, 0.26))
def spell_water():# jato + respingo
    j = bp(noise(0.24), 900, 6000) * np.linspace(0.4, 1, int(SR * 0.24))
    drops = np.zeros(int(SR * 0.42))
    for k in range(7):
        st = int(SR * (0.22 + 0.025 * k + rng.uniform(0, 0.01)))
        d = sweep(0.06, 700 + 260 * k, 1500 + 300 * k) * env(int(SR * 0.06), 0.002, 0.05, 2.5) * (0.6 - 0.06 * k)
        drops[st:st + len(d)] += d[:len(drops) - st]
    sp = bp(noise(0.20), 1500, 8000) * env(int(SR * 0.20), 0.002, 0.18, 2.5) * 0.7
    return mix(j * 0.7, drops, pad(sp, 0.22))
def spell_earth():# pedra rachando e caindo
    rum = lp(noise(0.50), 180) * env(int(SR * 0.50), 0.02, 0.4, 1.5) * 3.0
    cr = np.zeros(int(SR * 0.5))
    for k in range(6):
        st = int(SR * (0.03 + 0.06 * k + rng.uniform(0, 0.02)))
        c = bp(noise(0.05), 400, 3500) * env(int(SR * 0.05), 0.001, 0.04, 3) * (1.0 - 0.1 * k)
        cr[st:st + len(c)] += c[:len(cr) - st]
    th = sweep(0.25, 110, 45) * env(int(SR * 0.25), 0.002, 0.22, 2)
    return mix(rum, cr, pad(th * 1.2, 0.05))
def spell_air():  # rajada: sopro que varre
    n = noise(0.50); x = t(0.50)
    y = np.zeros_like(n); acc = 0.0
    cut = 500 + 3500 * np.sin(np.pi * x / 0.50) ** 2          # filtro que abre e fecha
    for i in range(len(n)):
        a = np.exp(-2 * np.pi * cut[i] / SR); acc = a * acc + (1 - a) * n[i]; y[i] = acc
    y = hp(y, 250) * np.sin(np.pi * x / 0.50) ** 1.3
    wh = np.sin(2 * np.pi * np.cumsum(900 + 500 * np.sin(np.pi * x / 0.50)) / SR) * np.sin(np.pi * x / 0.50) ** 2 * 0.12
    return mix(y * 2.2, wh)
def spell_magic():# arcano: sino cristalino descendo
    x = t(0.55)
    y = sum(np.sin(2 * np.pi * f * x * (1 - 0.12 * x)) * a for f, a in ((880, 1), (1320, .6), (1760, .45), (2640, .25)))
    sh = hp(noise(0.55), 6000) * env(len(x), 0.01, 0.4, 2.5) * 0.25
    return mix(y * env(len(x), 0.003, 0.5, 2.2) * 0.35, sh)
def flex():       # magia Física: o corpo retesa (pulso grave + estalo)
    p = sweep(0.30, 70, 160) * env(int(SR * 0.30), 0.04, 0.2, 1.4)
    o = np.sin(2 * np.pi * 240 * t(0.30)) * env(int(SR * 0.30), 0.02, 0.25, 2) * 0.4
    return mix(p * 1.2, o, pad(bp(noise(0.04), 1000, 5000) * env(int(SR * 0.04), 0.001, 0.03, 3) * 0.5, 0.12))
def leap():       # salto: impulso + pouso
    up = bp(noise(0.22), 400, 3000) * np.sin(np.pi * t(0.22) / 0.22) * 0.7
    land = sweep(0.14, 150, 60) * env(int(SR * 0.14), 0.001, 0.12, 2.5)
    return mix(up, pad(land, 0.34), pad(bp(noise(0.05), 300, 2500) * env(int(SR * 0.05), 0.001, 0.04, 3) * 0.6, 0.34))
def interrupt():  # conjuração quebrada: nota que desaba + estalo
    y = (sweep(0.30, 900, 150) + 0.5 * sweep(0.30, 1350, 200, "sq") * 0.4) * env(int(SR * 0.30), 0.002, 0.28, 1.6) * 0.5
    z = hp(noise(0.08), 3000) * env(int(SR * 0.08), 0.001, 0.07, 3) * 0.7
    return mix(y, z)
def soul():       # a alma desce: tom etéreo caindo, com eco
    x = t(1.1); f = 780 * (0.28) ** (x / 1.1)
    ph = 2 * np.pi * np.cumsum(f) / SR
    y = (np.sin(ph) + 0.4 * np.sin(1.5 * ph) + 0.25 * np.sin(2.01 * ph)) * (0.75 + 0.25 * np.sin(2 * np.pi * 6 * x))
    y *= env(len(x), 0.08, 0.9, 1.3) * 0.38
    echo = np.concatenate([np.zeros(int(SR * 0.16)), y * 0.35])[:len(y)]
    return mix(y, echo, hp(noise(1.1), 7000) * env(len(x), 0.1, 0.9, 2) * 0.06)
def burn():       # Queimando por tick: chiado curto
    return mix(hp(noise(0.16), 3500) * env(int(SR * 0.16), 0.01, 0.14, 2) * 0.6, lp(noise(0.16), 500) * env(int(SR * 0.16), 0.01, 0.14, 2) * 0.8)
def wall():       # Muralha erguendo do chão
    rum = lp(noise(0.60), 160) * np.sin(np.pi * t(0.60) / 0.60) ** 0.7 * 3.2
    gr = bp(noise(0.60), 300, 1800) * (rng.uniform(0, 1, int(SR * 0.60)) > 0.6) * np.sin(np.pi * t(0.60) / 0.60) * 0.5
    th = sweep(0.20, 120, 50) * env(int(SR * 0.20), 0.002, 0.18, 2)
    return mix(rum, gr, pad(th * 1.2, 0.42))
def spell_pick(): # escolher magia no menu
    return (np.sin(2 * np.pi * 1320 * t(0.10)) + 0.5 * np.sin(2 * np.pi * 1980 * t(0.10))) * env(int(SR * 0.10), 0.002, 0.09, 2.5) * 0.4

SOUNDS = {
    "sfx_slash": slash, "sfx_chop": chop, "sfx_bow": bow, "sfx_thrust": thrust, "sfx_bite": bite,
    "sfx_block": block, "sfx_cast_start": cast_start,
    "sfx_spell_fire": spell_fire, "sfx_spell_water": spell_water, "sfx_spell_earth": spell_earth,
    "sfx_spell_air": spell_air, "sfx_spell_magic": spell_magic,
    "sfx_flex": flex, "sfx_leap": leap, "sfx_interrupt": interrupt, "sfx_soul": soul,
    "sfx_burn": burn, "sfx_wall": wall, "sfx_spell_pick": spell_pick,
}

def main():
    os.makedirs(OUT, exist_ok=True)
    for name, fn in SOUNDS.items():
        y = np.asarray(fn(), dtype=np.float64)
        y = y - y.mean()
        peak = np.max(np.abs(y)) or 1.0
        y = y / peak * 0.82                                   # normaliza com folga
        n = len(y); fade = min(n, int(SR * 0.008))
        y[-fade:] *= np.linspace(1, 0, fade)                  # sem clique no fim
        pcm = (np.clip(y, -1, 1) * 32767).astype("<i2")
        with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
            w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes(pcm.tobytes())
        print("%-18s %5.2fs" % (name, n / SR))

if __name__ == "__main__":
    main()
