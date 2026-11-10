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
    # terreno
    "top": (0.42, 0.62, 0.26), "side": (0.52, 0.38, 0.24), "side_dark": (0.38, 0.28, 0.19),
    # armas
    "stone": (0.62, 0.63, 0.66), "wood": (0.48, 0.33, 0.18), "bone": (0.90, 0.86, 0.74),
    "string": (0.85, 0.80, 0.65), "conduit": (0.35, 0.85, 0.95),
    # monstro
    "fur": (0.50, 0.36, 0.24), "fur_dark": (0.30, 0.21, 0.15), "belly": (0.72, 0.60, 0.44),
    "eye_glow": (0.95, 0.75, 0.15),
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

def character():
    """Humanoide ~0.92 de altura. Cabeça grande: é onde mora a alma (GDD-013)."""
    def leg(sx):
        return (box(0.11, 0.30, 0.12, "pants", cx=sx * 0.08)
                + box(0.12, 0.05, 0.15, "leather", cx=sx * 0.08, cz=0.015))
    def arm(sx):
        return (box(0.09, 0.22, 0.10, "team", y0=0.40, cx=sx * 0.23)
                + box(0.08, 0.12, 0.09, "skin", y0=0.28, cx=sx * 0.23))
    torso = (loft(rect(0.30, 0.17, 0.30), rect(0.37, 0.20, 0.62), "team")
             + box(0.32, 0.05, 0.19, "leather", y0=0.29))
    head = (box(0.26, 0.24, 0.24, "skin", y0=0.63)
            + box(0.28, 0.08, 0.26, "hair", y0=0.85)
            + box(0.28, 0.16, 0.06, "hair", y0=0.70, cz=-0.11)
            + box(0.045, 0.045, 0.02, "eye", y0=0.74, cx=-0.065, cz=0.12)
            + box(0.045, 0.045, 0.02, "eye", y0=0.74, cx=0.065, cz=0.12))
    return [("Torso", torso), ("Head", head),
            ("ArmL", arm(-1)), ("ArmR", arm(1)), ("LegL", leg(-1)), ("LegR", leg(1))]

def terrain():
    """Bloco de 1 tile. Topo em y=0 (com chanfro), corpo desce até y=-1."""
    top = loft(rect(1.0, 1.0, -0.12), rect(0.94, 0.94, 0.0), "top")
    side = box(1.0, 0.38, 1.0, "side", y0=-0.50)
    base = loft(rect(0.96, 0.96, -1.0), rect(1.0, 1.0, -0.50), "side_dark")
    return [("Top", top), ("Side", side + base)]

def sword():
    """Lâmina de pedra lascada com guarda de osso."""
    grip = loft(ngon(6, 0.022, -0.07), ngon(6, 0.022, 0.07), "leather")
    pommel = loft(ngon(6, 0.032, -0.10), ngon(6, 0.024, -0.07), "bone")
    guard = box(0.17, 0.03, 0.05, "bone", y0=0.07)
    blade = (loft(rect(0.075, 0.022, 0.10), rect(0.055, 0.016, 0.50), "stone")
             + loft(rect(0.055, 0.016, 0.50), apex(4, 0.62), "stone"))
    return [("Grip", grip + pommel + guard), ("Blade", blade)]

def bow():
    """Arco no plano YZ, curvando para trás (-Z). Corda entre as pontas."""
    R, seg, span = 0.36, 8, 62.0
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
    return [("Limbs", limbs + grip), ("String", string)]

def staff():
    """Cajado com forquilha segurando a pedra-conduíte."""
    shaft = loft(ngon(6, 0.024, -0.35), ngon(6, 0.020, 0.55), "wood")
    wrap = (loft(ngon(6, 0.028, -0.05), ngon(6, 0.028, 0.07), "leather")
            + loft(ngon(6, 0.030, 0.50), ngon(6, 0.030, 0.55), "leather"))
    prongs = []
    for sx in (-1, 1):
        prongs += loft(ngon(5, 0.016, 0.0), ngon(5, 0.008, 0.17), "wood",
                       rot=(0, 0, -sx * 28), pos=(sx * 0.012, 0.54, 0.0))
    gem = (loft(apex(4, 0.60), ngon(4, 0.05, 0.67), "conduit")
           + loft(ngon(4, 0.05, 0.67), apex(4, 0.76), "conduit"))
    return [("Shaft", shaft + wrap + prongs), ("Conduit", gem)]

def monster():
    """Fera quadrúpede (~1.3 de comprimento, ~0.8 de altura), presas de osso."""
    # corpo: afunila do peito (frente, +Z) para a garupa
    body = (loft(rect(0.34, 0.30, 0.0), rect(0.46, 0.42, 0.80), "fur",
                 rot=(90, 0, 0), pos=(0, 0.50, -0.42))
            + loft(rect(0.26, 0.10, 0.0), rect(0.36, 0.12, 0.74), "belly",
                   rot=(90, 0, 0), pos=(0, 0.33, -0.38))
            + box(0.30, 0.12, 0.34, "fur_dark", y0=0.66, cz=0.20))        # corcova
    spikes = []
    for z in (-0.25, -0.05, 0.15, 0.33):
        spikes += loft(rect(0.07, 0.11, 0.0), apex(4, 0.13, cz=-0.03), "fur_dark",
                       pos=(0, 0.70 if z < 0.03 else 0.78, z))
    head = (box(0.30, 0.27, 0.26, "fur", y0=0.40, cz=0.50)
            + loft(rect(0.22, 0.18, 0.0), rect(0.16, 0.13, 0.20), "fur",
                   rot=(90, 0, 0), pos=(0, 0.48, 0.62))                    # focinho
            + box(0.17, 0.05, 0.20, "fur_dark", y0=0.37, cz=0.70)          # mandíbula
            + box(0.07, 0.04, 0.02, "eye", y0=0.52, cz=0.825)              # nariz
            + box(0.05, 0.05, 0.02, "eye_glow", y0=0.58, cx=-0.09, cz=0.635)
            + box(0.05, 0.05, 0.02, "eye_glow", y0=0.58, cx=0.09, cz=0.635))
    for sx in (-1, 1):
        head += loft(ngon(4, 0.028, 0.0), apex(4, 0.17), "bone",
                     rot=(160, 0, 0), pos=(sx * 0.075, 0.43, 0.74))        # presas
        head += loft(rect(0.07, 0.04, 0.0), apex(4, 0.11), "fur_dark",
                     rot=(0, 0, -sx * 20), pos=(sx * 0.11, 0.66, 0.44))    # orelhas
    def leg(sx, z, big):
        w = 0.14 if big else 0.12
        return (loft(rect(w * 0.75, w * 0.8, 0.05, sx * 0.15, z), rect(w, w * 1.15, 0.40, sx * 0.15, z), "fur")
                + box(w * 0.85, 0.05, w * 1.1, "fur_dark", cx=sx * 0.15, cz=z + 0.02))
    tail = loft(rect(0.07, 0.07, 0.0), rect(0.03, 0.03, 0.34), "fur_dark",
                rot=(-125, 0, 0), pos=(0, 0.60, -0.40))
    return [("Body", body + spikes), ("Head", head), ("Tail", tail),
            ("LegFL", leg(-1, 0.26, True)), ("LegFR", leg(1, 0.26, True)),
            ("LegBL", leg(-1, -0.30, False)), ("LegBR", leg(1, -0.30, False))]

MODELS = {"Character": character, "TerrainBlock": terrain, "Sword": sword,
          "Bow": bow, "Staff": staff, "Monster": monster}

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
    cell, cols, ss = 420, 3, 2
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
        draw.text((k % cols * cell + 12, k // cols * cell + 10), name, fill=(230, 230, 235), font_size=22)
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
