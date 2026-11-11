#!/usr/bin/env python3
"""Gera os modelos 3D low-poly base do Pangea Skirmish (OBJ + MTL), sem dependências.

Uso:  python3 scripts/gen_models.py            -> escreve em Assets/Resources/Models/Base/
      python3 scripts/gen_models.py --preview  -> também gera docs/models-3d-preview.png (requer Pillow e numpy)

Convenções (espaço do OBJ, mão direita; a Unity inverte X ao importar):
  - 1 unidade = 1 tile. Y para cima. Frente do modelo = +Z.
  - Personagem/monstro: origem nos pés, no centro.
  - Terreno: origem no centro do TOPO (y=0 é o chão onde se pisa); o bloco desce 1 unidade.
  - Armas: origem na empunhadura, arma apontando para +Y.
  - Cada parte animável é um objeto ("o Nome") separado. Materiais são só cores (Kd);
    "team" é o material a ser tingido pela cor do time, "conduit" o do conduíte.
"""
import math
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(ROOT, "Assets", "Resources", "Models", "Base")
PREVIEW = os.path.join(ROOT, "docs", "models-3d-preview.png")

COLORS = {
    # personagem
    "skin": (0.80, 0.58, 0.42), "hair": (0.20, 0.13, 0.09), "team": (0.75, 0.22, 0.18),
    "pants": (0.36, 0.27, 0.18), "leather": (0.42, 0.26, 0.13), "eye": (0.06, 0.05, 0.05),
    "hood": (0.27, 0.35, 0.22), "trim": (0.88, 0.74, 0.34), "hide": (0.28, 0.27, 0.30),
    # terreno
    "top": (0.42, 0.62, 0.26), "side": (0.52, 0.38, 0.24), "side_dark": (0.38, 0.28, 0.19),
    # armas
    "stone": (0.62, 0.63, 0.66), "wood": (0.48, 0.33, 0.18), "bone": (0.90, 0.86, 0.74),
    "string": (0.85, 0.80, 0.65), "conduit": (0.35, 0.85, 0.95),
    "iron": (0.70, 0.73, 0.78), "iron_dark": (0.40, 0.42, 0.47),
    # feras
    "fur": (0.50, 0.36, 0.24), "fur_dark": (0.30, 0.21, 0.15), "belly": (0.72, 0.60, 0.44),
    "eye_glow": (0.95, 0.75, 0.15),
    "hide_dark": (0.16, 0.15, 0.18), "hide_belly": (0.42, 0.38, 0.40), "eye_red": (0.98, 0.22, 0.14),
    "scale": (0.30, 0.48, 0.30), "scale_dark": (0.17, 0.30, 0.20), "scale_belly": (0.74, 0.76, 0.48),
    # magias
    "element": (0.85, 0.85, 0.90), "element_core": (1.0, 1.0, 1.0),
    "fire": (0.98, 0.45, 0.10), "fire_core": (1.0, 0.88, 0.35),
    "water": (0.25, 0.58, 0.95), "water_light": (0.75, 0.90, 1.0), "air": (0.86, 0.95, 0.98),
    "rock": (0.50, 0.44, 0.38), "rock_dark": (0.33, 0.29, 0.26), "magic": (0.72, 0.48, 1.0),
    "soul": (0.80, 0.95, 1.0), "soul_dark": (0.20, 0.35, 0.50),
    "leaf": (0.36, 0.72, 0.30), "leaf_dark": (0.22, 0.46, 0.20),
}

# ----------------------------------------------------------------- geometria

def rect(w, d, y, cx=0.0, cz=0.0):
    return [(cx - w / 2, y, cz - d / 2), (cx + w / 2, y, cz - d / 2),
            (cx + w / 2, y, cz + d / 2), (cx - w / 2, y, cz + d / 2)]

def ngon(n, r, y, cx=0.0, cz=0.0):
    return [(cx + r * math.cos(2 * math.pi * i / n), y, cz + r * math.sin(2 * math.pi * i / n))
            for i in range(n)]

def apex(n, y, cx=0.0, cz=0.0):
    return [(cx, y, cz)] * n

def _sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def _dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def _cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
def _norm(a):
    l = math.sqrt(_dot(a, a)) or 1.0
    return (a[0] / l, a[1] / l, a[2] / l)
def _mean(pts):
    n = float(len(pts))
    return (sum(p[0] for p in pts) / n, sum(p[1] for p in pts) / n, sum(p[2] for p in pts) / n)

def _newell(poly):
    nx = ny = nz = 0.0
    for i, a in enumerate(poly):
        b = poly[(i + 1) % len(poly)]
        nx += (a[1] - b[1]) * (a[2] + b[2])
        ny += (a[2] - b[2]) * (a[0] + b[0])
        nz += (a[0] - b[0]) * (a[1] + b[1])
    return (nx, ny, nz)

def _dedupe(poly):
    out = []
    for p in poly:
        if not out or max(abs(p[i] - out[-1][i]) for i in range(3)) > 1e-9:
            out.append(p)
    if len(out) > 1 and max(abs(out[0][i] - out[-1][i]) for i in range(3)) <= 1e-9:
        out.pop()
    return out

def loft(bottom, top, mat, rot=(0, 0, 0), pos=(0, 0, 0)):
    """Sólido CONVEXO entre dois anéis de n pontos. Devolve [(poligono, material)]."""
    n = len(bottom)
    polys = [list(reversed(bottom)), list(top)]
    for i in range(n):
        j = (i + 1) % n
        polys.append([bottom[i], bottom[j], top[j], top[i]])
    centre = _mean(bottom + top)
    faces = []
    for poly in polys:
        poly = _dedupe(poly)
        if len(poly) < 3:
            continue
        nrm = _newell(poly)
        if _dot(nrm, nrm) < 1e-14:
            continue
        if _dot(nrm, _sub(_mean(poly), centre)) < 0:   # orienta para fora
            poly = list(reversed(poly))
        faces.append(([_xform(p, rot, pos) for p in poly], mat))
    return faces

def box(w, h, d, mat, y0=0.0, cx=0.0, cz=0.0, rot=(0, 0, 0), pos=(0, 0, 0)):
    return loft(rect(w, d, y0, cx, cz), rect(w, d, y0 + h, cx, cz), mat, rot, pos)

def _xform(p, rot, pos):
    x, y, z = p
    rx, ry, rz = (math.radians(a) for a in rot)
    y, z = y * math.cos(rx) - z * math.sin(rx), y * math.sin(rx) + z * math.cos(rx)
    x, z = x * math.cos(ry) + z * math.sin(ry), -x * math.sin(ry) + z * math.cos(ry)
    x, y = x * math.cos(rz) - y * math.sin(rz), x * math.sin(rz) + y * math.cos(rz)
    return (x + pos[0], y + pos[1], z + pos[2])

# ----------------------------------------------------------------- modelos
# Cada modelo: lista de (nome_do_objeto, faces)
#
# CONTRATO COM O JOGO (UnitModel3D): humanoides têm as partes Torso, Head, ArmL, ArmR,
# LegL, LegR; feras têm Body, Head, Tail, LegFL, LegFR, LegBL, LegBR. As articulações são
# pivôs criados a partir dos bounds de cada parte, então NÃO renomeie nem junte partes.
# Armas ficam com o cabo na origem e a ponta em +Y. Props de magia usam o material
# "element" (pintado em runtime com a cor do elemento) e "element_core".

def humanoid(kind="base"):
    """Humanoide ~0.92 de altura. Cabeça grande: é onde mora a alma (GDD-013).
    kind: base | warrior | tank | archer | mage — muda silhueta e adereços."""
    tank, archer, mage, warrior = kind == "tank", kind == "archer", kind == "mage", kind == "warrior"
    lw = 0.13 if tank else 0.10 if (archer or mage) else 0.11          # largura da perna
    aw = 0.11 if tank else 0.08 if archer else 0.09                    # largura do braço
    sh = 0.26 if tank else 0.21 if archer else 0.23                    # ombro (x do braço)
    tw0, tw1 = (0.36, 0.44) if tank else (0.26, 0.32) if archer else (0.30, 0.37)

    def leg(sx):
        f = (box(lw, 0.30, lw + 0.01, "pants", cx=sx * 0.08)
             + box(lw + 0.01, 0.05, lw + 0.04, "leather", cx=sx * 0.08, cz=0.015))
        if tank:   f += box(lw + 0.02, 0.10, lw + 0.03, "bone", y0=0.10, cx=sx * 0.08, cz=0.01)      # caneleira
        if archer: f += box(lw + 0.01, 0.13, lw + 0.02, "leather", y0=0.05, cx=sx * 0.08)            # bota alta
        return f

    def arm(sx):
        sleeve = "team"
        f = box(aw, 0.22, aw + 0.01, sleeve, y0=0.40, cx=sx * sh)
        f += box(aw - 0.01, 0.12, aw, "skin", y0=0.28, cx=sx * sh)
        if warrior: f += box(aw + 0.05, 0.07, aw + 0.06, "leather", y0=0.57, cx=sx * sh)             # ombreira
        if tank:
            f += loft(rect(aw + 0.07, aw + 0.08, 0.55, sx * sh), rect(aw + 0.03, aw + 0.04, 0.66, sx * sh), "bone")
            f += box(aw + 0.01, 0.07, aw + 0.02, "leather", y0=0.30, cx=sx * sh)                     # bracelete
        if archer and sx > 0: f += box(aw + 0.01, 0.10, aw + 0.02, "leather", y0=0.29, cx=sx * sh)   # braçadeira
        if mage:                                                                                     # manga larga
            f += loft(rect(aw + 0.06, aw + 0.07, 0.33, sx * sh), rect(aw + 0.01, aw + 0.02, 0.50, sx * sh), "team")
        return f

    torso = loft(rect(tw0, 0.17 if not tank else 0.21, 0.30), rect(tw1, 0.20 if not tank else 0.24, 0.62), "team")
    torso += box(tw0 + 0.02, 0.05, 0.19 if not tank else 0.23, "leather", y0=0.29)                   # cinto
    if warrior:
        torso += box(0.07, 0.34, 0.03, "leather", y0=0.30, cx=-0.06, cz=0.10, rot=(0, 0, -28), pos=(0.02, 0.02, 0))  # bandoleira
        torso += box(0.10, 0.10, 0.03, "bone", y0=0.46, cz=0.105)                                    # amuleto
    if tank:
        torso += loft(rect(0.30, 0.04, 0.36, 0, 0.115), rect(0.36, 0.04, 0.60, 0, 0.125), "bone")    # peitoral de osso
        for x in (-0.10, 0.0, 0.10):
            torso += box(0.04, 0.22, 0.02, "leather", y0=0.37, cx=x, cz=0.14)
    if archer:
        torso += box(0.10, 0.34, 0.09, "leather", y0=0.36, cx=0.07, cz=-0.14, rot=(0, 0, 14))        # aljava
        for dx in (-0.025, 0.0, 0.025):
            torso += box(0.014, 0.10, 0.014, "bone", y0=0.70, cx=0.01 + dx, cz=-0.14)
        torso += box(0.05, 0.36, 0.02, "leather", y0=0.30, cx=0.05, cz=0.10, rot=(0, 0, 30), pos=(-0.03, 0, 0))
    if mage:
        torso += loft(rect(0.44, 0.34, 0.07), rect(tw0 + 0.02, 0.19, 0.33), "team")                  # saia do manto
        torso += box(0.46, 0.035, 0.36, "trim", y0=0.07)
        torso += box(0.06, 0.26, 0.02, "trim", y0=0.34, cz=0.105)
        torso += loft(apex(4, 0.47, 0, 0.12), ngon(4, 0.035, 0.52, 0, 0.12), "conduit")              # conduíte no peito
        torso += loft(ngon(4, 0.035, 0.52, 0, 0.12), apex(4, 0.57, 0, 0.12), "conduit")

    head = (box(0.26, 0.24, 0.24, "skin", y0=0.63)
            + box(0.045, 0.045, 0.02, "eye", y0=0.74, cx=-0.065, cz=0.12)
            + box(0.045, 0.045, 0.02, "eye", y0=0.74, cx=0.065, cz=0.12))
    if tank:        # elmo de crânio com chifres
        head += box(0.30, 0.13, 0.28, "bone", y0=0.80)
        head += box(0.30, 0.10, 0.05, "bone", y0=0.70, cz=-0.12)
        head += box(0.05, 0.12, 0.03, "bone", y0=0.69, cz=0.125)                                     # nasal
        for sx in (-1, 1):
            head += loft(ngon(4, 0.035, 0.0), apex(4, 0.16), "bone", rot=(0, 0, -sx * 55), pos=(sx * 0.14, 0.86, 0.0))
    elif archer:    # capuz
        head += loft(rect(0.31, 0.29, 0.78), rect(0.20, 0.20, 0.93, 0, -0.03), "hood")
        head += box(0.31, 0.17, 0.07, "hood", y0=0.63, cz=-0.125)
        for sx in (-1, 1):
            head += box(0.03, 0.17, 0.20, "hood", y0=0.66, cx=sx * 0.142, cz=-0.02)
    elif mage:      # chapéu pontudo com aba
        head += box(0.28, 0.16, 0.06, "hair", y0=0.70, cz=-0.11)
        head += box(0.40, 0.03, 0.40, "team", y0=0.85)
        head += loft(ngon(6, 0.14, 0.88), ngon(6, 0.07, 1.04, 0, -0.03), "team")
        head += loft(ngon(6, 0.07, 1.04, 0, -0.03), apex(6, 1.20, 0.02, -0.10), "team")
        head += loft(ngon(6, 0.145, 0.88), ngon(6, 0.135, 0.92), "trim")
    else:
        head += box(0.28, 0.08, 0.26, "hair", y0=0.85) + box(0.28, 0.16, 0.06, "hair", y0=0.70, cz=-0.11)
        if warrior:
            head += box(0.285, 0.04, 0.265, "team", y0=0.80)                                         # faixa
            head += box(0.05, 0.14, 0.03, "team", y0=0.66, cx=0.10, cz=-0.145)

    return [("Torso", torso), ("Head", head),
            ("ArmL", arm(-1)), ("ArmR", arm(1)), ("LegL", leg(-1)), ("LegR", leg(1))]

def character(): return humanoid("base")
def warrior():   return humanoid("warrior")
def tank():      return humanoid("tank")
def archer():    return humanoid("archer")
def mage():      return humanoid("mage")

def terrain():
    """Bloco de 1 tile. Topo em y=0 (com chanfro), corpo desce até y=-1."""
    top = loft(rect(1.0, 1.0, -0.12), rect(0.94, 0.94, 0.0), "top")
    side = box(1.0, 0.38, 1.0, "side", y0=-0.50)
    base = loft(rect(0.96, 0.96, -1.0), rect(1.0, 1.0, -0.50), "side_dark")
    return [("Top", top), ("Side", side + base)]

# ----------------------------------------------------------------- armas

def sword(blade="stone", guard="bone", length=0.52, width=0.075):
    """Lâmina com guarda. Pedra lascada (base), madeira (treino) ou ferro."""
    grip = loft(ngon(6, 0.022, -0.07), ngon(6, 0.022, 0.07), "leather")
    pommel = loft(ngon(6, 0.032, -0.10), ngon(6, 0.024, -0.07), guard)
    gd = box(0.17 if blade != "wood" else 0.12, 0.03, 0.05, guard, y0=0.07)
    tip = 0.10 + length
    bl = (loft(rect(width, 0.022, 0.10), rect(width * 0.75, 0.016, tip - 0.12), blade)
          + loft(rect(width * 0.75, 0.016, tip - 0.12), apex(4, tip), blade))
    if blade == "iron":
        bl += box(0.012, length - 0.16, 0.026, "iron_dark", y0=0.12)                                 # sulco
    return [("Grip", grip + pommel + gd), ("Blade", bl)]

def wooden_sword(): return sword("wood", "leather", 0.44, 0.07)
def iron_sword():   return sword("iron", "iron_dark", 0.58, 0.08)

def axe(head="stone", big=False):
    """Machado: cabo de madeira, cabeça em cunha amarrada com couro."""
    L = 0.62 if big else 0.42
    shaft = loft(ngon(6, 0.022, -0.10), ngon(6, 0.019, L), "wood")
    wrap = loft(ngon(6, 0.027, -0.04), ngon(6, 0.027, 0.06), "leather")
    hy = L - (0.16 if big else 0.12)
    hh = 0.20 if big else 0.13
    reach = 0.20 if big else 0.13
    # cunha: grossa junto ao cabo, fio largo e fino na ponta (+X)
    blade = loft([(0.0, hy + hh * 0.30, -0.03), (0.0, hy + hh * 0.70, -0.03), (0.0, hy + hh * 0.70, 0.03), (0.0, hy + hh * 0.30, 0.03)],
                 [(reach, hy - 0.02, -0.006), (reach, hy + hh + 0.02, -0.006), (reach, hy + hh + 0.02, 0.006), (reach, hy - 0.02, 0.006)], head)
    tie = box(0.06, hh * 0.5, 0.07, "leather", y0=hy + hh * 0.25)
    extra = []
    if big:   # contrapeso em espigão
        extra = loft(rect(0.05, 0.05, 0.0), apex(4, 0.12), head, rot=(0, 0, 90), pos=(-0.02, hy + hh * 0.5, 0))
    return [("Grip", shaft + wrap + tie), ("Blade", blade + extra)]

def hatchet():  return axe("stone", False)
def iron_axe(): return axe("iron", True)

def bow(R=0.36, span=62.0, tips=False):
    """Arco no plano YZ, curvando para trás (-Z). Corda entre as pontas."""
    seg = 8
    def pt(deg):
        a = math.radians(deg)
        return (0.0, R * math.sin(a), R * math.cos(a) - R)
    limbs = []
    for i in range(seg):
        a0 = -span + 2 * span * i / seg
        a1 = -span + 2 * span * (i + 1) / seg
        p0, p1 = pt(a0), pt(a1)
        t0 = 0.030 - 0.016 * abs(a0) / span     # afina nas pontas
        t1 = 0.030 - 0.016 * abs(a1) / span
        b = [(p0[0] - t0 / 2, p0[1], p0[2] - t0 / 2), (p0[0] + t0 / 2, p0[1], p0[2] - t0 / 2),
             (p0[0] + t0 / 2, p0[1], p0[2] + t0 / 2), (p0[0] - t0 / 2, p0[1], p0[2] + t0 / 2)]
        t = [(p1[0] - t1 / 2, p1[1], p1[2] - t1 / 2), (p1[0] + t1 / 2, p1[1], p1[2] - t1 / 2),
             (p1[0] + t1 / 2, p1[1], p1[2] + t1 / 2), (p1[0] - t1 / 2, p1[1], p1[2] + t1 / 2)]
        limbs += loft(b, t, "wood")
    grip = box(0.04, 0.12, 0.045, "leather", y0=-0.06)
    tip = pt(span)
    string = box(0.006, 2 * tip[1], 0.006, "string", y0=-tip[1], cz=tip[2])
    extra = []
    if tips:
        for sy in (-1, 1):
            extra += box(0.03, 0.05, 0.03, "bone", y0=sy * tip[1] - 0.025, cz=tip[2])
    return [("Limbs", limbs + grip + extra), ("String", string)]

def short_bow(): return bow(0.27, 58.0)
def long_bow():  return bow(0.47, 64.0, tips=True)

def staff(kind="base"):
    """Cajados e varinhas. O conduíte é a pedra que ressoa com os elementos (GDD-013 §6)."""
    if kind == "wand":          # varinha curta: graveto com lasca de conduíte
        shaft = loft(ngon(5, 0.016, -0.08), ngon(5, 0.010, 0.26), "wood")
        wrap = loft(ngon(5, 0.020, -0.05), ngon(5, 0.020, 0.03), "leather")
        gem = loft(apex(4, 0.25), ngon(4, 0.028, 0.29), "conduit") + loft(ngon(4, 0.028, 0.29), apex(4, 0.35), "conduit")
        return [("Shaft", shaft + wrap), ("Conduit", gem)]
    if kind == "scepter":       # cetro: curto, cabeça de osso abraçando a pedra
        shaft = loft(ngon(6, 0.022, -0.12), ngon(6, 0.020, 0.34), "wood")
        wrap = loft(ngon(6, 0.027, -0.05), ngon(6, 0.027, 0.06), "leather")
        crown = loft(ngon(6, 0.03, 0.32), ngon(6, 0.06, 0.38), "bone")
        for k in range(4):
            a = math.radians(45 + 90 * k)
            crown += loft(ngon(4, 0.014, 0.0), apex(4, 0.12), "bone", pos=(0.05 * math.cos(a), 0.37, 0.05 * math.sin(a)))
        gem = loft(apex(4, 0.39), ngon(4, 0.04, 0.44), "conduit") + loft(ngon(4, 0.04, 0.44), apex(4, 0.51), "conduit")
        return [("Shaft", shaft + wrap + crown), ("Conduit", gem)]
    if kind == "wooden":        # cajado de madeira: sem pedra, nó retorcido no topo
        shaft = loft(ngon(6, 0.024, -0.35), ngon(6, 0.020, 0.52), "wood")
        wrap = loft(ngon(6, 0.028, -0.05), ngon(6, 0.028, 0.07), "leather")
        knob = (loft(ngon(6, 0.020, 0.52), ngon(6, 0.045, 0.58), "wood")
                + loft(ngon(6, 0.045, 0.58), ngon(6, 0.030, 0.66, 0.02, 0.0), "wood")
                + loft(ngon(6, 0.030, 0.66, 0.02, 0.0), apex(6, 0.70, 0.04, 0.0), "wood"))
        charm = box(0.012, 0.09, 0.012, "string", y0=0.44, cx=0.035) + box(0.03, 0.03, 0.015, "bone", y0=0.41, cx=0.035)
        return [("Shaft", shaft + wrap + knob + charm), ("Conduit", [])]
    arcane = kind == "arcane"
    shaft = loft(ngon(6, 0.024, -0.35), ngon(6, 0.020, 0.55), "wood")
    wrap = (loft(ngon(6, 0.028, -0.05), ngon(6, 0.028, 0.07), "leather")
            + loft(ngon(6, 0.030, 0.50), ngon(6, 0.030, 0.55), "leather"))
    prongs = []
    n = 3 if arcane else 2
    for k in range(n):
        a = math.radians(360.0 * k / n)
        prongs += loft(ngon(5, 0.016, 0.0), ngon(5, 0.008, 0.19 if arcane else 0.17), "bone" if arcane else "wood",
                       rot=(28 * math.sin(a), 0, -28 * math.cos(a)), pos=(0.012 * math.cos(a), 0.54, 0.012 * math.sin(a)))
    r = 0.065 if arcane else 0.05
    gem = (loft(apex(4, 0.60), ngon(4, r, 0.68), "conduit") + loft(ngon(4, r, 0.68), apex(4, 0.80 if arcane else 0.76), "conduit"))
    if arcane:
        gem += loft(ngon(6, 0.034, 0.20), ngon(6, 0.034, 0.24), "conduit")                           # anel no cabo
    return [("Shaft", shaft + wrap + prongs), ("Conduit", gem)]

def wooden_staff(): return [p for p in staff("wooden") if p[1]]
def scepter():      return staff("scepter")
def wand():         return staff("wand")
def arcane_staff(): return staff("arcane")

def shield():
    """Escudo de couro esticado em aro de madeira, com umbo de osso. Face em +Z."""
    rim = loft(ngon(8, 0.21, 0.0), ngon(8, 0.21, 0.035), "wood", rot=(90, 0, 0))
    hide = loft(ngon(8, 0.19, 0.035), ngon(8, 0.15, 0.06), "hide", rot=(90, 0, 0))
    boss = loft(ngon(6, 0.06, 0.06), apex(6, 0.11), "bone", rot=(90, 0, 0))
    mark = box(0.05, 0.26, 0.012, "team", y0=-0.13, cz=0.058) + box(0.26, 0.05, 0.012, "team", y0=-0.025, cz=0.058)
    strap = box(0.04, 0.16, 0.02, "leather", y0=-0.08, cz=-0.01)
    return [("Board", rim + hide + mark + strap), ("Boss", boss)]

# ----------------------------------------------------------------- feras

def monster(kind="beast"):
    """Feras quadrúpedes. beast = fera comum; alpha = maior, de chifres; stalker = lagarto baixo e comprido."""
    alpha, stalker = kind == "alpha", kind == "stalker"
    fur, dark, belly, eye = (("hide", "hide_dark", "hide_belly", "eye_red") if alpha else
                             ("scale", "scale_dark", "scale_belly", "eye_glow") if stalker else
                             ("fur", "fur_dark", "belly", "eye_glow"))
    by = 0.36 if stalker else 0.50                 # altura do corpo
    bl = 0.96 if stalker else 0.80                 # comprimento
    w0, w1 = (0.30, 0.38) if stalker else (0.38, 0.52) if alpha else (0.34, 0.46)
    h0, h1 = (0.22, 0.26) if stalker else (0.32, 0.46) if alpha else (0.30, 0.42)
    body = (loft(rect(w0, h0, 0.0), rect(w1, h1, bl), fur, rot=(90, 0, 0), pos=(0, by, -bl / 2 - 0.02))
            + loft(rect(w0 - 0.08, 0.10, 0.0), rect(w1 - 0.10, 0.12, bl - 0.06), belly,
                   rot=(90, 0, 0), pos=(0, by - h1 / 2 + 0.04, -bl / 2 + 0.02)))
    if not stalker:
        body += box(0.30 if not alpha else 0.38, 0.12 if not alpha else 0.17, 0.34, dark, y0=by + 0.16, cz=0.20)   # corcova
    spikes = []
    zs = (-0.36, -0.20, -0.04, 0.12, 0.28, 0.40) if stalker else (-0.25, -0.05, 0.15, 0.33)
    for z in zs:
        top = by + h1 / 2 - 0.02 if stalker else (by + 0.20 if z < 0.03 else by + (0.33 if alpha else 0.28))
        hgt = 0.09 if stalker else 0.19 if alpha else 0.13
        spikes += loft(rect(0.07, 0.11, 0.0), apex(4, hgt, cz=-0.03), "bone" if alpha else dark, pos=(0, top, z))
    hz = bl / 2 + 0.08
    hy = by - 0.10
    head = box(0.30 if not stalker else 0.24, 0.27 if not stalker else 0.18, 0.26, fur, y0=hy, cz=hz)
    sn = 0.32 if stalker else 0.20                 # focinho
    head += loft(rect(0.22 if not stalker else 0.18, 0.18 if not stalker else 0.12, 0.0),
                 rect(0.16 if not stalker else 0.11, 0.13 if not stalker else 0.08, sn), fur,
                 rot=(90, 0, 0), pos=(0, hy + 0.08, hz + 0.12))
    head += box(0.17 if not stalker else 0.12, 0.05, sn, dark, y0=hy - 0.03, cz=hz + 0.12 + sn / 2)                 # mandíbula
    head += box(0.07, 0.04, 0.02, "eye", y0=hy + 0.12 if not stalker else hy + 0.09, cz=hz + 0.125 + sn)            # nariz
    ey = hy + 0.18 if not stalker else hy + 0.13
    for sx in (-1, 1):
        head += box(0.05, 0.05, 0.02, eye, y0=ey, cx=sx * (0.09 if not stalker else 0.075), cz=hz + 0.135)
        if stalker:     # fileira de dentes
            for k in range(3):
                head += loft(ngon(3, 0.014, 0.0), apex(3, 0.05), "bone", rot=(180, 0, 0),
                             pos=(sx * 0.05, hy + 0.035, hz + 0.20 + k * 0.08))
        else:           # presas
            head += loft(ngon(4, 0.028 if not alpha else 0.036, 0.0), apex(4, 0.17 if not alpha else 0.24), "bone",
                         rot=(160, 0, 0), pos=(sx * 0.075, hy + 0.03, hz + 0.24))
            head += loft(rect(0.07, 0.04, 0.0), apex(4, 0.11), dark, rot=(0, 0, -sx * 20), pos=(sx * 0.11, hy + 0.26, hz - 0.06))
        if alpha:       # chifres curvos para a frente
            head += loft(ngon(4, 0.04, 0.0), ngon(4, 0.026, 0.16), "bone", rot=(20, 0, -sx * 50), pos=(sx * 0.13, hy + 0.24, hz - 0.02))
            head += loft(ngon(4, 0.026, 0.0), apex(4, 0.14), "bone", rot=(60, 0, -sx * 25), pos=(sx * 0.25, hy + 0.34, hz + 0.02))
    def leg(sx, z, big):
        w = (0.14 if big else 0.12) * (1.2 if alpha else 0.9 if stalker else 1.0)
        top = by - h1 / 2 + 0.10
        x = sx * (0.15 if not stalker else 0.19)
        return (loft(rect(w * 0.75, w * 0.8, 0.05, x, z), rect(w, w * 1.15, max(top, 0.16), x, z), fur)
                + box(w * 0.85, 0.05, w * 1.1, dark, cx=x, cz=z + 0.02)
                + (loft(ngon(3, 0.02, 0.0), apex(3, 0.07), "bone", rot=(90, 0, 0), pos=(x, 0.02, z + w * 0.55)) if (alpha or stalker) else []))
    tl = 0.62 if stalker else 0.34
    tail = loft(rect(0.07 if not stalker else 0.14, 0.07 if not stalker else 0.12, 0.0), rect(0.03, 0.03, tl), dark,
                rot=(-125 if not stalker else -100, 0, 0), pos=(0, by + (0.10 if not stalker else 0.02), -bl / 2))
    fz, bz = (0.30, -0.36) if stalker else (0.26, -0.30)
    return [("Body", body + spikes), ("Head", head), ("Tail", tail),
            ("LegFL", leg(-1, fz, True)), ("LegFR", leg(1, fz, True)),
            ("LegBL", leg(-1, bz, False)), ("LegBR", leg(1, bz, False))]

def monster_alpha():   return monster("alpha")
def monster_stalker(): return monster("stalker")

# ----------------------------------------------------------------- magias e ações

def arrow():
    """Flecha ao longo de +Z (ponta na frente): haste, ponta de pedra, penas."""
    shaft = box(0.014, 0.014, 0.50, "wood", y0=-0.007)
    tip = loft(rect(0.045, 0.012, 0.0), apex(4, 0.09), "stone", rot=(90, 0, 0), pos=(0, 0, 0.25))
    fl = box(0.05, 0.006, 0.09, "bone", y0=-0.003, cz=-0.19) + box(0.006, 0.05, 0.09, "bone", y0=-0.025, cz=-0.19)
    return [("Arrow", shaft + tip + fl)]

def spell_orb():
    """Orbe facetado: projétil genérico. 'element' é pintado com a cor do elemento."""
    outer = loft(apex(6, -0.16), ngon(6, 0.15, 0.0), "element") + loft(ngon(6, 0.15, 0.0), apex(6, 0.16), "element")
    core = loft(apex(4, -0.08), ngon(4, 0.075, 0.0), "element_core") + loft(ngon(4, 0.075, 0.0), apex(4, 0.08), "element_core")
    shards = []
    for k in range(3):
        a = math.radians(120 * k + 20)
        shards += loft(ngon(3, 0.03, 0.0), apex(3, 0.10), "element", rot=(0, 0, 90), pos=(0.24 * math.cos(a), 0.0, 0.24 * math.sin(a)))
    return [("Orb", outer + shards), ("Core", core)]

def flame():
    """Labareda: três línguas que afinam, miolo claro. Base em y=0."""
    f = loft(ngon(6, 0.16, 0.0), ngon(6, 0.12, 0.18), "fire") + loft(ngon(6, 0.12, 0.18), apex(6, 0.46, 0.03, 0.0), "fire")
    for k, (h, r) in enumerate(((0.30, 0.08), (0.26, 0.07), (0.34, 0.075))):
        a = math.radians(120 * k)
        f += loft(ngon(5, r, 0.0), apex(5, h), "fire", pos=(0.13 * math.cos(a), 0.0, 0.13 * math.sin(a)))
    core = loft(ngon(5, 0.08, 0.02), apex(5, 0.28), "fire_core")
    return [("Flame", f), ("Core", core)]

def rock_shard():
    """Lasca de pedra pontuda (Lasca de pedra, estilhaços). Ponta em +Z."""
    s = loft([(-0.07, -0.05, 0.0), (0.08, -0.06, 0.0), (0.06, 0.07, 0.0), (-0.05, 0.05, 0.0)], apex(4, 0.0, 0.01, 0.0), "rock",
             rot=(0, 0, 0), pos=(0, 0, 0))
    s = (loft(rect(0.13, 0.11, 0.0), rect(0.09, 0.07, 0.16), "rock", rot=(90, 0, 0), pos=(0, 0, -0.16))
         + loft(rect(0.09, 0.07, 0.0), apex(4, 0.20, 0.01, 0.01), "rock", rot=(90, 0, 0), pos=(0, 0, 0.0))
         + loft(rect(0.13, 0.11, 0.0), apex(4, 0.07), "rock_dark", rot=(-90, 0, 0), pos=(0, 0, -0.16)))
    return [("Shard", s)]

def water_drop():
    """Gota/jato d'água. Ponta fina atrás (-Z), bojo na frente."""
    d = (loft(apex(6, 0.0), ngon(6, 0.10, 0.22), "water", rot=(90, 0, 0), pos=(0, 0, -0.22))
         + loft(ngon(6, 0.10, 0.0), ngon(6, 0.07, 0.08), "water", rot=(90, 0, 0), pos=(0, 0, 0.0))
         + loft(ngon(6, 0.07, 0.0), apex(6, 0.05), "water", rot=(90, 0, 0), pos=(0, 0, 0.08)))
    hl = box(0.03, 0.03, 0.06, "water_light", y0=0.04, cx=-0.03, cz=0.02)
    return [("Drop", d + hl)]

def gust():
    """Rajada: três arcos de vento empilhados, abertos para a frente (+Z)."""
    f = []
    for j, (y, R, th) in enumerate(((-0.10, 0.20, 0.022), (0.0, 0.27, 0.028), (0.11, 0.17, 0.02))):
        seg = 6
        for i in range(seg):
            a0 = math.radians(-70 + 140 * i / seg)
            a1 = math.radians(-70 + 140 * (i + 1) / seg)
            p0 = (R * math.sin(a0), y, R * math.cos(a0) - R * 0.5)
            p1 = (R * math.sin(a1), y, R * math.cos(a1) - R * 0.5)
            t0 = th * (1 - abs(i - seg / 2) / seg)
            t1 = th * (1 - abs(i + 1 - seg / 2) / seg)
            b = [(p0[0], p0[1] - t0, p0[2] - t0), (p0[0], p0[1] + t0, p0[2] - t0), (p0[0], p0[1] + t0, p0[2] + t0), (p0[0], p0[1] - t0, p0[2] + t0)]
            t = [(p1[0], p1[1] - t1, p1[2] - t1), (p1[0], p1[1] + t1, p1[2] - t1), (p1[0], p1[1] + t1, p1[2] + t1), (p1[0], p1[1] - t1, p1[2] + t1)]
            f += loft(b, t, "air")
    return [("Gust", f)]

def wall_block():
    """Muralha (Terra + Mágico): rocha erguida de 1 célula, com runa. Base em y=0."""
    w = (loft(rect(0.98, 0.98, 0.0), rect(0.86, 0.80, 0.62), "rock")
         + loft(rect(0.86, 0.80, 0.62), rect(0.60, 0.58, 0.92, 0.04, -0.03), "rock")
         + loft(rect(0.60, 0.58, 0.92, 0.04, -0.03), apex(4, 1.08, 0.08, -0.05), "rock_dark")
         + loft(rect(0.34, 0.30, 0.0), apex(4, 0.50), "rock_dark", pos=(-0.36, 0.0, 0.34))
         + loft(rect(0.30, 0.28, 0.0), apex(4, 0.38), "rock_dark", pos=(0.38, 0.0, -0.36)))
    rune = (box(0.05, 0.30, 0.02, "magic", y0=0.22, cz=0.455)
            + box(0.20, 0.05, 0.02, "magic", y0=0.40, cz=0.45)
            + box(0.12, 0.05, 0.02, "magic", y0=0.24, cx=0.04, cz=0.465))
    return [("Rock", w), ("Rune", rune)]

def soul():
    """Alma (GDD-013): física, nasce na cabeça, desce para dentro do planeta. Rosto + cauda."""
    s = (box(0.20, 0.18, 0.18, "soul", y0=0.0)
         + loft(rect(0.18, 0.16, 0.0), rect(0.10, 0.09, 0.14), "soul", rot=(180, 0, 0), pos=(0, 0.0, 0))
         + loft(rect(0.10, 0.09, 0.0), apex(4, 0.20, 0.03, 0.0), "soul", rot=(180, 0, 0), pos=(0, -0.14, 0)))
    eyes = (box(0.04, 0.05, 0.02, "soul_dark", y0=0.08, cx=-0.05, cz=0.09) + box(0.04, 0.05, 0.02, "soul_dark", y0=0.08, cx=0.05, cz=0.09))
    return [("Soul", s), ("Eyes", eyes)]

def heal_leaf():
    """Bálsamo (Água + Terra): broto de duas folhas com gota. Base em y=0."""
    stem = loft(ngon(5, 0.02, 0.0), ngon(5, 0.012, 0.22), "leaf_dark")
    f = []
    for sx in (-1, 1):
        f += loft(rect(0.03, 0.02, 0.0), rect(0.13, 0.02, 0.12), "leaf", rot=(0, 0, -sx * 50), pos=(sx * 0.01, 0.16, 0))
        f += loft(rect(0.13, 0.02, 0.0), apex(4, 0.12), "leaf", rot=(0, 0, -sx * 50), pos=(sx * 0.10, 0.237, 0))
    drop = loft(apex(5, 0.36), ngon(5, 0.05, 0.28), "water") + loft(ngon(5, 0.05, 0.28), apex(5, 0.23), "water")
    return [("Leaf", stem + f), ("Drop", drop)]

MODELS = {
    # personagens
    "Character": character, "Warrior": warrior, "Tank": tank, "Archer": archer, "Mage": mage,
    # feras
    "Monster": monster, "MonsterAlpha": monster_alpha, "MonsterStalker": monster_stalker,
    # armas (o nome é o id da arma no catálogo, quando existe)
    "Sword": sword, "WoodenSword": wooden_sword, "IronSword": iron_sword,
    "Hatchet": hatchet, "IronAxe": iron_axe,
    "Bow": bow, "ShortBow": short_bow, "LongBow": long_bow,
    "Staff": staff, "WoodenStaff": wooden_staff, "Scepter": scepter,
    "ApprenticeWand": wand, "ArcaneStaff": arcane_staff, "Shield": shield,
    # magias e ações
    "Arrow": arrow, "SpellOrb": spell_orb, "Flame": flame, "RockShard": rock_shard,
    "WaterDrop": water_drop, "Gust": gust, "WallBlock": wall_block, "HealLeaf": heal_leaf, "Soul": soul,
    # terreno
    "TerrainBlock": terrain,
}

# ----------------------------------------------------------------- escrita

def write_obj(name, objects):
    used = []
    lines = ["# Pangea Skirmish - gerado por scripts/gen_models.py (nao editar a mao)",
             "mtllib %s.mtl" % name]
    vi = ni = 0
    for oname, faces in objects:
        lines.append("o %s" % oname)
        lines.append("g %s" % oname)
        current = None
        for poly, mat in faces:
            if mat not in used:
                used.append(mat)
            if mat != current:
                lines.append("usemtl %s" % mat)
                current = mat
            n = _norm(_newell(poly))
            lines.append("vn %.5f %.5f %.5f" % n)
            ni += 1
            idx = []
            for p in poly:
                lines.append("v %.5f %.5f %.5f" % p)
                vi += 1
                idx.append("%d//%d" % (vi, ni))
            lines.append("f " + " ".join(idx))
    with open(os.path.join(OUT_DIR, name + ".obj"), "w") as f:
        f.write("\n".join(lines) + "\n")
    with open(os.path.join(OUT_DIR, name + ".mtl"), "w") as f:
        for m in used:
            f.write("newmtl %s\nKd %.3f %.3f %.3f\nKa 0 0 0\nKs 0 0 0\nd 1\nillum 1\n\n" % ((m,) + COLORS[m]))
    return sum(len(fc) for _, fc in objects)

# ----------------------------------------------------------------- preview

def render_preview(built):
    """Rasterizador ortográfico com z-buffer (numpy + Pillow)."""
    import numpy as np
    from PIL import Image, ImageDraw
    cam = _norm((0.9, 0.75, 1.3))
    right = _norm(_cross((0, 1, 0), cam))
    up = _cross(cam, right)
    light = _norm((0.35, 1.0, 0.55))
    cell, cols, ss = 300, 6, 2
    names = list(built.keys())
    rows = (len(names) + cols - 1) // cols
    W, H = cols * cell * ss, rows * cell * ss
    rgb = np.empty((H, W, 3), dtype=np.uint8)
    rgb[:] = (34, 36, 44)
    zbuf = np.full((H, W), -1e9)
    for k, name in enumerate(names):
        faces = [f for _, fs in built[name] for f in fs]
        pts = [p for poly, _ in faces for p in poly]
        xs = [_dot(p, right) for p in pts]
        ys = [_dot(p, up) for p in pts]
        span = max(max(xs) - min(xs), max(ys) - min(ys)) or 1.0
        scale = cell * ss * 0.74 / span
        ox = (k % cols + 0.5) * cell * ss - (max(xs) + min(xs)) / 2 * scale
        oy = (k // cols + 0.5) * cell * ss + (max(ys) + min(ys)) / 2 * scale
        for poly, mat in faces:
            n = _norm(_newell(poly))
            if _dot(n, cam) <= 0:
                continue
            sh = 0.42 + 0.58 * max(0.0, _dot(n, light))
            col = [int(255 * min(1.0, c * sh)) for c in COLORS[mat]]
            sp = [(ox + _dot(p, right) * scale, oy - _dot(p, up) * scale, _dot(p, cam)) for p in poly]
            for i in range(1, len(sp) - 1):          # leque de triângulos
                (x0, y0, z0), (x1, y1, z1), (x2, y2, z2) = sp[0], sp[i], sp[i + 1]
                den = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2)
                if abs(den) < 1e-9:
                    continue
                xa, xb = max(0, int(min(x0, x1, x2))), min(W - 1, int(max(x0, x1, x2)) + 1)
                ya, yb = max(0, int(min(y0, y1, y2))), min(H - 1, int(max(y0, y1, y2)) + 1)
                if xa > xb or ya > yb:
                    continue
                gx, gy = np.meshgrid(np.arange(xa, xb + 1) + 0.5, np.arange(ya, yb + 1) + 0.5)
                w0 = ((y1 - y2) * (gx - x2) + (x2 - x1) * (gy - y2)) / den
                w1 = ((y2 - y0) * (gx - x2) + (x0 - x2) * (gy - y2)) / den
                w2 = 1 - w0 - w1
                z = w0 * z0 + w1 * z1 + w2 * z2
                zb = zbuf[ya:yb + 1, xa:xb + 1]
                m = (w0 >= 0) & (w1 >= 0) & (w2 >= 0) & (z > zb)
                zb[m] = z[m]
                rgb[ya:yb + 1, xa:xb + 1][m] = col
    img = Image.fromarray(rgb).resize((cols * cell, rows * cell), Image.LANCZOS)
    draw = ImageDraw.Draw(img)
    for k, name in enumerate(names):
        draw.text((k % cols * cell + 10, k // cols * cell + 8), name, fill=(230, 230, 235), font_size=18)
    img.save(PREVIEW)

def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    built = {}
    for name, fn in MODELS.items():
        built[name] = fn()
        print("%-13s %4d faces" % (name, write_obj(name, built[name])))
    if "--preview" in sys.argv:
        render_preview(built)
        print("preview:", PREVIEW)

if __name__ == "__main__":
    main()
