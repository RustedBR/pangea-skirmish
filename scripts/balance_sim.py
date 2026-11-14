#!/usr/bin/env python3
"""
Simulador de balanceamento do Pangea Skirmish (v2, reescrito do zero em 2026-11-13).

Fonte da verdade: o código do jogo. Os números saem de
  Assets/Scripts/GameTuning.cs  (defaults)  +  Assets/Resources/GameTuning.asset (valores salvos, vencem)
  Assets/Scripts/BattleTypes.cs (StatFormulas)   Assets/Scripts/Spells.cs (magias)
  Assets/Scripts/Equipment.cs   (itens, 2 mãos, kit inicial)
Nenhuma constante de jogo fica duplicada aqui; o que é comportamento (fórmulas, ordem do tick,
reações, IA) espelha RoundManager / AttackResolver / SpellResolver / Unit / IntentBuilder.

Uso (um comando):
  python3 scripts/balance_sim.py                       # relatório do estado atual
  python3 scripts/balance_sim.py --n 40 --md           # mais lutas, saída Markdown (corpo de PR)
  python3 scripts/balance_sim.py --proposta reactCostDodge=1 dodgeFracCap=0.6
                                                        # atual × proposta (mesmas sementes)
  python3 scripts/balance_sim.py --base master         # regras do ref git × árvore de trabalho
  chaves da proposta: campo do GameTuning/StatFormulas (ex. aooDamageMult=0.8),
  arma.<id>.<campo>, item.<id>.<campo>, magia.<id>.<campo>.

Simplificações (documentadas no relatório): mapa plano sem obstáculos; mira por unidade (sem
mira livre); sem posturas, cartas, Muralha/Tempestade; IA de reação = a do jogo
(RoundManager.AutoPickReaction: sempre reage quando pode).
"""
import argparse
import itertools
import math
import random
import re
import statistics
import subprocess
import sys
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
P_TUNING_CS = "Assets/Scripts/GameTuning.cs"
P_TUNING_ASSET = "Assets/Resources/GameTuning.asset"
P_TYPES = "Assets/Scripts/BattleTypes.cs"
P_SPELLS = "Assets/Scripts/Spells.cs"
P_EQUIP = "Assets/Scripts/Equipment.cs"
P_PROFILE = "Assets/Scripts/PlayerProfile.cs"

SHAPES = {"Single": 0, "Line": 1, "Arc": 2, "Square": 3}


def rint(x):
    """Mathf.RoundToInt (meio para o par, como System.Math.Round)."""
    return int(round(x))


def clamp01(x):
    return max(0.0, min(1.0, x))


# =============================================================================== regras
def _read(path, ref):
    if ref:
        return subprocess.run(["git", "show", f"{ref}:{path}"], cwd=ROOT, check=True,
                              capture_output=True, text=True).stdout
    return (ROOT / path).read_text(encoding="utf-8")


def _num(s):
    s = s.strip().rstrip("f").strip()
    if s in ("true", "false"):
        return s == "true"
    try:
        return int(s)
    except ValueError:
        return float(s)


FIELD_RE = re.compile(r"public\s+(?:float|int|bool)\s+(\w+)\s*=\s*([-\d.]+f?|true|false)\s*;")


class Rules:
    """Tudo o que o jogo define em dados, lido dos arquivos (de um ref git ou do disco)."""

    def __init__(self, ref=None):
        self.ref = ref
        cs = _read(P_TUNING_CS, ref)
        self.t = {k: _num(v) for k, v in FIELD_RE.findall(cs)}
        m = re.search(r"beastSizeMul\s*=\s*new Vector3\(([^)]*)\)", cs)
        self.t["beastSizeMul"] = [_num(x) for x in m.group(1).split(",")] if m else [1, 2, 2]
        weapons_cs = self._weapons_cs(cs)

        types = _read(P_TYPES, ref)
        sf = types[types.index("class StatFormulas"):types.index("class UnitStatBlock")]
        self.f = {k: _num(v) for k, v in FIELD_RE.findall(sf)}
        self.default_fp = int(re.search(r"DefaultFootprint\s*=\s*(\d+)", types).group(1))

        weapons_asset = self._asset(_read(P_TUNING_ASSET, ref))
        self.weapons = weapons_asset or weapons_cs
        self.spells = self._spells(_read(P_SPELLS, ref))
        self._equipment(_read(P_EQUIP, ref))
        try:
            self.budget = int(re.search(r"AttributeBudget\s*=\s*(\d+)", _read(P_PROFILE, ref)).group(1))
        except Exception:
            self.budget = 30

    # ---- parsers
    @staticmethod
    def _weapons_cs(cs):
        out = {}
        for body in re.findall(r"new WeaponDef\{([^}]*)\}", cs):
            w = {}
            for k, v in re.findall(r"(\w+)\s*=\s*([^,]+)", body):
                v = v.strip()
                if v.startswith('"'):
                    w[k] = v.strip('"')
                elif v.startswith("AttackShapeKind."):
                    w[k] = SHAPES[v.split(".")[1]]
                else:
                    w[k] = _num(v)
            out[w["id"]] = w
        return out

    def _asset(self, text):
        section, weapons, cur = None, {}, None
        for line in text.splitlines():
            m = re.match(r"^  (\w+):\s*(.*)$", line)
            if m:
                k, v = m.groups()
                section = k if v == "" else None
                if v and not v.startswith("{"):
                    try:
                        self.t[k] = _num(v)
                    except ValueError:
                        pass
                continue
            if section == "statFormulas":
                m = re.match(r"^    (\w+):\s*(.+)$", line)
                if m:
                    self.f[m.group(1)] = _num(m.group(2))
            elif section == "weapons":
                m = re.match(r"^  - (\w+):\s*(.*)$", line)
                if m:
                    cur = {m.group(1): m.group(2).strip()}
                    weapons[cur["id"]] = cur
                    continue
                m = re.match(r"^    (\w+):\s*(.*)$", line)
                if m and cur is not None:
                    k, v = m.groups()
                    try:
                        cur[k] = _num(v)
                    except ValueError:
                        cur[k] = v.strip()
        return weapons

    @staticmethod
    def _spells(text):
        sp = {}
        for m in re.finditer(r'P\("(\w+)",\s*"([^"]+)",\s*Attr\.(\w+),\s*(\d+),\s*(\d+),\s*SpellEffect\.(\w+)([^\n]*)', text):
            sid, name, attr, cost, ticks, fx, rest = m.groups()
            mult = re.search(r"mult:\s*([\d.]+)", rest)
            sp[sid] = dict(id=sid, name=name, physical=True, cost=int(cost), ticks=int(ticks), effect=fx,
                           mult=float(mult.group(1)) if mult else 1.0, range=0, area=0)
        for m in re.finditer(r'E\("(\w+)",\s*"([^"]+)",\s*(\w+|new\[\]\{[^}]*\}),\s*(\d+),\s*(\d+),\s*(-?\d+),\s*(\d+),\s*SpellEffect\.(\w+)([^\n]*)', text):
            sid, name, el, cost, ticks, rng, area, fx, rest = m.groups()
            pos = re.match(r"\s*,\s*([\d.]+)f?\s*,\s*([\d.]+)f?", rest)
            named = dict(re.findall(r"(dur|tickDmg|push|pierce):\s*([\d.]+)", rest))
            status = re.search(r'status:\s*"(\w+)"', rest)
            pure_magic = el.strip() == "Magic"
            sp[sid] = dict(id=sid, name=name, physical=False, cost=int(cost), ticks=int(ticks), range=int(rng),
                           area=int(area), effect=fx, base=float(pos.group(1)) if pos else 0.0,
                           pot=float(pos.group(2)) if pos else 0.0, dur=int(float(named.get("dur", 0))),
                           tickDmg=int(float(named.get("tickDmg", 0))), push=int(float(named.get("push", 0))),
                           pierce=max(float(named.get("pierce", 0)), 0.5 if pure_magic else 0.0),
                           status=status.group(1) if status else "")
        return sp

    def _equipment(self, text):
        self.items = {}
        for m in re.finditer(r'\bA\("(\w+)",\s*"[^"]+",\s*ItemSlot\.(\w+),\s*(\d+),\s*Rarity\.(\w+),\s*(\d+),\s*(\d+)'
                             r'(?:,\s*Attr\.(\w+),\s*(\d+))?(?:,\s*ItemEffect\.(\w+))?\)', text):
            iid, slot, w, rar, pd, md, attr, ab, fx = m.groups()
            self.items[iid] = dict(id=iid, slot=slot, weight=int(w), rarity=rar, physDef=int(pd), magDef=int(md),
                                   attr=attr, attrBonus=int(ab or 0), effect=fx or "None", shield=False, shieldHardness=0)
        for m in re.finditer(r'\bS\("(\w+)",\s*"[^"]+",\s*(\d+),\s*Rarity\.(\w+),\s*(\d+)(?:,\s*ItemEffect\.(\w+))?\)', text):
            iid, w, rar, hard, fx = m.groups()
            self.items[iid] = dict(id=iid, slot="LeftHand", weight=int(w), rarity=rar, physDef=0, magDef=0, attr=None,
                                   attrBonus=0, effect=fx or "None", shield=True, shieldHardness=int(hard))
        m = re.search(r"TwoHandedIds\s*=\s*new HashSet<string>\s*\{([^}]*)\}", text)
        self.two_handed = set(re.findall(r'"(\w+)"', m.group(1)))
        kit = text[text.index("Loadout StarterKit"):]
        kit = kit[:kit.index("// ──")]
        self.kits, self.kit_default = {}, None
        for cases, args in re.findall(r'((?:case\s+"\w+":\s*)+)return new Loadout\(([^)]*)\)', kit):
            lo = self._loadout(args)
            for wid in re.findall(r'"(\w+)"', cases):
                self.kits[wid] = lo
        self.kit_default = self._loadout(re.search(r"default:\s*return new Loadout\(([^)]*)\)", kit).group(1))

    @staticmethod
    def _loadout(args):
        parts = [a.strip() for a in args.split(",")]
        val = [p.strip('"') if p.startswith('"') else None for p in parts]   # None = weaponId
        return dict(head=val[0], torso=val[1], legs=val[2], left=val[4])

    # ---- acesso
    def weapon(self, wid):
        if wid in ("Claws", "ClawsLarge"):                 # WeaponCatalog.BeastClaws
            large = wid == "ClawsLarge"
            return dict(id=wid, damage=self.t["beastClawDamage"], attackTicks=self.t["beastClawTicks"],
                        range=1 if large else 0, shape=SHAPES["Arc"], shapeWidth=3, shapeLength=1, blockHardness=1,
                        spellPowerMult=1.0, spellRangeBonus=0)
        return self.weapons.get(wid)

    def kit(self, wid):
        k = dict(self.kits.get(wid, self.kit_default))
        k["right"] = wid
        return k

    def apply(self, overrides):
        """Aplica 'chave=valor' (proposta) sobre as regras lidas."""
        for kv in overrides or []:
            k, v = kv.split("=", 1)
            v = _num(v)
            parts = k.split(".")
            if parts[0] == "arma":
                self.weapons[parts[1]][parts[2]] = v
            elif parts[0] == "item":
                self.items[parts[1]][parts[2]] = v
            elif parts[0] == "magia":
                self.spells[parts[1]][parts[2]] = v
            elif k in self.f and k not in self.t:
                self.f[k] = v
            elif k in self.t:
                self.t[k] = v
            else:
                sys.exit(f"chave desconhecida na proposta: {k}")
        return self


# =============================================================================== geometria
def gap(a, fa, b, fb):
    h = max(0, a[0] - (b[0] + fb - 1), b[0] - (a[0] + fa - 1))
    v = max(0, a[1] - (b[1] + fb - 1), b[1] - (a[1] + fa - 1))
    return max(h, v)


def overlap(a, fa, b, fb):
    return a[0] < b[0] + fb and b[0] < a[0] + fa and a[1] < b[1] + fb and b[1] < a[1] + fa


def center(anchor, fp):
    return (anchor[0] + fp // 2, anchor[1] + fp // 2)


def direction(frm, to):
    dx, dy = to[0] - frm[0], to[1] - frm[1]
    if dx == 0 and dy == 0:
        return (0, 1)
    ax, ay = abs(dx), abs(dy)
    sx, sy = (dx > 0) - (dx < 0), (dy > 0) - (dy < 0)
    if ax * 2 < ay:
        sx = 0
    elif ay * 2 < ax:
        sy = 0
    return (sx, sy)


def shape_cells(w, frm, aim):
    kind = w["shape"] if w else 0
    width = max(1, w["shapeWidth"]) if w else 1
    length = max(1, w["shapeLength"]) if w else 1
    d = direction(frm, aim)
    p = (-d[1], d[0])
    cells = []
    if kind == 0:
        cells.append(aim)
    elif kind == 1:
        for i in range(length):
            k = i - (length - 1) // 2
            cells.append((aim[0] + d[0] * k, aim[1] + d[1] * k))
    elif kind == 2:
        for row in range(length):
            for k in range(width):
                o = k - (width - 1) // 2
                cells.append((aim[0] - d[0] * row + p[0] * o, aim[1] - d[1] * row + p[1] * o))
    else:
        for x in range(width):
            for y in range(length):
                cells.append((aim[0] + x - (width - 1) // 2, aim[1] + y - (length - 1) // 2))
    out = []
    for c in cells:
        if c not in out:
            out.append(c)
    return out


def tiles_on(cells, anchor, fp):
    return sum(1 for c in cells if anchor[0] <= c[0] < anchor[0] + fp and anchor[1] <= c[1] < anchor[1] + fp)


def centered_tiles(w, fp=3):
    anchor = (10, 10)
    c = center(anchor, fp)
    return max(1, tiles_on(shape_cells(w, (c[0], c[1] - 6), c), anchor, fp))


# =============================================================================== unidade
ATTRS = ("STR", "VIT", "DEX", "AGI", "INT", "WIS")


class Unit:
    def __init__(self, R, name, attrs, weapon_id, team, uid, loadout=None, fp=None, spells=(), build=None):
        self.R, self.name, self.team, self.uid, self.build = R, name, team, uid, build or name
        self.a = dict(zip(ATTRS, [float(x) for x in attrs]))
        self.fp = fp or R.default_fp
        self.spells = list(spells)
        lo = loadout or dict(head=None, torso=None, legs=None, left=None)
        lo = dict(lo, right=weapon_id)
        self.lo = lo
        self.weapon = R.weapon(weapon_id)
        w = self.weapon
        T = R.t
        self.two_handed = weapon_id in R.two_handed
        # WeaponDef.ApplyTo + corte de 1 mão (Equipment.ApplyTo)
        self.wd = w["damage"]
        if not self.two_handed and weapon_id not in ("Claws", "ClawsLarge"):
            self.wd = max(1, rint(w["damage"] * T["oneHandDamageMult"]))
        self.shape_tiles = centered_tiles(w, R.default_fp)
        self.atk_ticks = max(1, w["attackTicks"])
        self.range = T["baseAttackRange"] + w["range"]
        self.str_scales = w["range"] <= T["strDamageMaxRange"]
        armor = [R.items[i] for i in (lo["head"], lo["torso"], lo["legs"], lo["left"]) if i and i in R.items]
        for it in armor:
            if it["attrBonus"]:
                self.a[it["attr"]] += it["attrBonus"]
        self.armor = armor
        self.armor_phys = sum(i["physDef"] for i in armor)
        self.armor_mag = sum(i["magDef"] for i in armor)
        off = R.weapon(lo["left"]) if lo["left"] and lo["left"] not in R.items else None
        self.offhand = off if off and lo["left"] not in R.two_handed else None
        weight = (2 if self.two_handed else 1) + (1 if self.offhand else 0) + sum(i["weight"] for i in armor)
        self.weight = weight
        self.ow = max(0, weight - (2 + math.floor(self.a["VIT"] / 2)))
        self.shield = next((i for i in armor if i["shield"]), None)
        self.effects = {i["effect"] for i in armor}
        # estado
        self.hp = self.max_hp
        self.mana = self.max_mana
        self.stamina = self.max_stamina
        self.anchor = (0, 0)
        self.queue = []
        self.reactions = 1
        self.dodge_react = 0
        self.block_flat = 0
        self.fury = 0.0
        self.curve = False
        self.true_aim_crit = 0.0
        self.stone_ticks = 0
        self.stone_mult = 1.0
        self.burn_ticks = 0
        self.burn_dmg = 0
        self.counter_interrupted = False
        self.staggered = False
        self.bonus_dmg = False
        self.aim_bonus = False

    # ---- atributos derivados (AttributeStats / Unit)
    @property
    def F(self):
        return self.R.f

    @property
    def max_hp(self):
        return rint(self.F["hpBase"] + self.a["VIT"] * self.F["hpPerVIT"])

    @property
    def max_mana(self):
        return rint(self.F["manaBase"] + self.a["WIS"] * self.F["manaPerWIS"])

    @property
    def max_stamina(self):
        T = self.R.t
        return max(1, rint(T["staminaBase"] + (self.a["STR"] + self.a["VIT"] + self.a["DEX"]) * T["staminaPerBodyPoint"]))

    def dpt(self, wd=None):
        attr = self.a["STR"] if self.str_scales else self.a["DEX"]
        return (self.wd if wd is None else wd) * (1 + max(0.0, attr) * self.F["dmgPctPerAttr"])

    @property
    def phys_def(self):
        return self.F["armorBase"] + rint(self.a["VIT"] * self.F["armorPerVIT"]) + self.armor_phys

    @property
    def mag_def(self):
        return self.F["magicResistBase"] + rint(self.a["WIS"] * self.F["magicResistPerWIS"]) + self.armor_mag

    @property
    def crit(self):
        return clamp01(self.a["DEX"] * self.F["critPerDEX"])

    @property
    def dodge_chance(self):
        return clamp01(self.a["AGI"] * self.F["dodgePerAGI"] - self.ow * 0.04)

    @property
    def dulled(self):
        return self.max_mana > 0 and self.mana < math.ceil(self.max_mana * self.R.t["dullManaFraction"])

    @property
    def hit_chance(self):
        return clamp01(clamp01(self.F["baseHitChance"] + self.a["DEX"] * self.F["hitPerDEX"])
                       - (self.R.t["dullHitPenalty"] if self.dulled else 0))

    @property
    def eff_range(self):
        return max(1, self.range - self.R.t["dullRangePenalty"]) if self.dulled else self.range

    @property
    def move_step(self):
        T = self.R.t
        tiles = min(rint(T["moveTilesBase"] + self.a["AGI"] * T["moveTilesPerAGI"]), max(1, T["maxTilesPerMove"])) - self.ow
        return max(1, tiles)

    @property
    def stamina_regen_ticks(self):
        T = self.R.t
        bonus = max(0, math.floor(self.a["AGI"]) - T["agiRegenFrom"]) // max(1, T["agiRegenStep"])
        return max(1, T["staminaRegenTicks"] - bonus)

    @property
    def mana_regen_ticks(self):
        T = self.R.t
        return max(1, T["manaRegenTicks"] - math.floor(self.a["WIS"]) // max(1, T["wisRegenStep"]))

    def duration(self, kind, bonus=False, spell=None):
        T = self.R.t
        if kind == "move" or kind == "wait":
            return 1
        if kind == "attack":
            b = self.atk_ticks + (T["ticksPowerStrikeExtra"] if bonus else 0)
        elif kind == "spell":
            b = max(1, spell["ticks"])
        else:                                   # concentrate / breathe
            b = T["ticksConcentrate"]
        k = max(1.0, T["tickDexFactor"])
        return max(1, rint(b * k / (k + max(0.0, self.a["DEX"]))))

    def react_cost(self, kind):
        T = self.R.t
        return {"Olho certeiro": T["reactCostTrueAim"], "Esquivar": T["reactCostDodge"], "Bloquear": T["reactCostBlock"],
                "Contra-atacar": T["reactCostCounter"], "Interpor": T["reactCostIntercept"]}[kind]

    @property
    def dead(self):
        return self.hp <= 0

    def hostile(self, o):
        return o.team != self.team

    def spell_amount(self, s):
        conduit = max(0.1, self.weapon.get("spellPowerMult", 1.0)) if not s["physical"] else 1.0
        T = self.R.t
        return rint((s["base"] * T["spellBaseMult"] + self.a["INT"] * T["spellPotPerINT"] * s["pot"]) * conduit)

    def spell_reach(self, s):
        T = self.R.t
        if s["effect"] == "Leap":
            return max(1, rint(self.move_step * s["mult"]))
        if s["range"] <= 0:
            return s["range"]
        r = rint(s["range"] * T["spellRangeMult"] + self.a["INT"] * T["spellRangePerINT"]) + self.weapon.get("spellRangeBonus", 0) \
            + (1 if "Conduit" in self.effects else 0)
        return max(1, r - T["dullRangePenalty"]) if self.dulled else max(1, r)

    def block_amount(self):
        hard = self.weapon.get("blockHardness", 0) + (self.shield["shieldHardness"] if self.shield else 0)
        return max(1, rint(self.a["VIT"] * self.R.t["blockFlatPerVIT"]) + hard)

    def dodge_move_tiles(self):
        return max(1, 1 + math.floor(self.a["AGI"] / max(1.0, self.R.t["dodgeAgiPerMoveTile"])) - self.ow
                   + (1 if "LightFeet" in self.effects else 0))


# =============================================================================== batalha
class Battle:
    W, H = 40, 26

    def __init__(self, R, units, rng, disabled=None, stats=None, max_rounds=30):
        self.R, self.units, self.rng = R, units, rng
        self.disabled = disabled or {}            # time -> conjunto de mecânicas desligadas
        self.st = stats if stats is not None else Stats()
        self.max_rounds = max_rounds
        self.tick = 0
        self.provoked = set()

    # ---- helpers
    def can_use(self, u, mech):
        return mech not in self.disabled.get(u.team, ())

    def alive(self):
        return [u for u in self.units if not u.dead]

    def roll(self):
        return self.rng.randrange(10000)

    def damage(self, u, amount, src):
        if amount <= 0 or u.dead:
            return
        u.hp = max(0, u.hp - amount)
        self.st.dmg[src] += amount
        if u.queue and u.queue[0]["type"] == "spell" and not u.queue[0]["spell"]["physical"]:
            q = u.queue[0]
            q["taken"] = q.get("taken", 0) + amount
            if q["taken"] >= max(1, math.ceil(u.max_hp * self.R.t["spellInterruptHpFraction"])):
                q["interrupted"] = True

    def effective_tiles(self, att, tgt, tiles):
        std = self.R.default_fp
        if tgt.fp >= std or tiles <= 0:
            return tiles
        cap = max(1, att.shape_tiles) if att else tiles
        return min(cap, math.ceil(tiles * (std * std) / float(tgt.fp * tgt.fp)))

    def dodge_frac(self, u):
        T = self.R.t
        return clamp01(min(T["dodgeFracCap"], u.a["AGI"] * T["dodgeFracPerAGI"]))

    def dodge_tiles_for(self, u, tiles):
        f = self.dodge_frac(u)
        if tiles <= 1:
            return tiles if self.roll() < rint(f * 10000) else 0
        return min(max(rint(tiles * f), 1), tiles)

    def push_queue(self, u):
        if u.queue and u.queue[0]["elapsed"] > 0:
            u.queue[0]["elapsed"] = 0
        else:
            u.queue.insert(0, dict(type="wait", dur=1, elapsed=0))

    def free_at(self, u, anchor, ignore=()):
        if anchor[0] < 0 or anchor[1] < 0 or anchor[0] + u.fp > self.W or anchor[1] + u.fp > self.H:
            return False
        return not any(o is not u and o not in ignore and not o.dead and overlap(anchor, u.fp, o.anchor, o.fp)
                       for o in self.units)

    def reachable(self, u, frm, budget):
        out = []
        for dx in range(-budget, budget + 1):
            for dy in range(-budget, budget + 1):
                a = (frm[0] + dx, frm[1] + dy)
                if self.free_at(u, a):
                    out.append(a)
        return out

    # ---- reações (AskReaction com a IA do jogo)
    def can_react(self, u, kind):
        return (not u.dead and u.reactions > 0 and u.stamina >= u.react_cost(kind) and self.can_use(u, kind))

    def apply_reaction(self, u, kind):
        u.stamina = max(0, u.stamina - u.react_cost(kind))
        u.reactions = max(0, u.reactions - 1)
        self.push_queue(u)
        self.st.use[(u.build, kind)] += 1
        self.st.uses[kind] += 1

    def auto_pick(self, u, opts):
        if "Contra-atacar" in opts:
            return "Contra-atacar"
        if "Interpor" in opts:
            return "Interpor"
        if "Esquivar" in opts and "Bloquear" in opts:
            return "Esquivar" if u.a["AGI"] >= u.a["VIT"] else "Bloquear"
        return opts[0] if opts else None

    def provoke(self, provoker, from_anchor=None, exclude=None):
        """Golpe de oportunidade (RoundManager.Provoke)."""
        if provoker.dead or provoker in self.provoked:
            return
        at = from_anchor or provoker.anchor
        T = self.R.t
        cands = [e for e in self.units if e is not provoker and e is not exclude and not e.dead and e.hostile(provoker)
                 and self.can_react(e, "Contra-atacar") and e.range <= T["counterMaxRange"]
                 and gap(at, provoker.fp, e.anchor, e.fp) <= e.eff_range]
        if not cands:
            return
        cands.sort(key=lambda e: (-e.a["DEX"], e.uid))
        self.provoked.add(provoker)
        c = cands[0]
        self.apply_reaction(c, "Contra-atacar")
        mult = T["aooDamageMult"] * (1.3 if "SharpCounter" in c.effects else 1.0)
        res = self.strike(c, center(provoker.anchor, provoker.fp), c.weapon, c.wd, c.shape_tiles, provoker, mult,
                          tag="Contra-atacar")
        self.st.val["Contra-atacar"] += res["dealt"]
        if res["any_hit"] and not provoker.dead:
            provoker.counter_interrupted = True
            self.st.extra["Contra-atacar: interrompeu"] += 1

    def after_reaction(self, reactor, kind, trigger, cells=None, spell_from=None):
        self.provoke(reactor, exclude=trigger)
        if reactor.counter_interrupted:
            reactor.counter_interrupted = False
            reactor.dodge_react = 0
            reactor.block_flat = 0
            self.st.extra[f"{kind}: cortada por contra-ataque"] += 1
            return False
        if kind == "Esquivar" and not reactor.dead:
            self.dodge_move(reactor, cells, spell_from or trigger)
        return True

    def dodge_move(self, u, cells, frm):
        n = u.dodge_move_tiles()
        reach = self.reachable(u, u.anchor, n)
        if u.anchor not in reach:
            reach.append(u.anchor)
        best, key_best = u.anchor, None
        for a in reach:
            d = abs(a[0] - u.anchor[0]) + abs(a[1] - u.anchor[1])
            if cells is not None:
                key = (tiles_on(cells, a, u.fp), d, a[1], a[0])
            else:
                key = (-gap(a, u.fp, frm.anchor, frm.fp), d, a[1], a[0])
            if key_best is None or key < key_best:
                best, key_best = a, key
        if cells is not None:
            before = tiles_on(cells, u.anchor, u.fp)
            self.st.extra["_dodge_tiles_before"] += before
            self.st.extra["_dodge_tiles_after"] += key_best[0]
            self.st.extra["_dodge_n"] += 1
            if frm is not None:      # tiles que saíram da forma × dano por tile de quem atacou
                self.st.val["Esquivar"] += (before - key_best[0]) * frm.dpt()
            u.dodge_react = 0
        u.anchor = best

    # ---- o golpe (AttackResolver.Strike)
    def strike(self, att, aim, weapon, wd, shape_tiles, primary, mult, interceptor=None, protected=None,
               only=None, force_hit=False, tag="Ataque"):
        T, F = self.R.t, self.R.f
        res = dict(hit=[], missed=[], any_hit=False, absorbed=False, ranged=False, dealt=0)
        curve = att.curve
        if curve and primary is not None and not primary.dead:
            aim = center(primary.anchor, primary.fp)
        cells = shape_cells(weapon, center(att.anchor, att.fp), aim)
        save = (att.shape_tiles,)
        att.shape_tiles = shape_tiles
        tiles_of, order = {}, []
        for u in sorted(self.units, key=lambda x: x.uid):
            if u is att or u.dead or not u.hostile(att) or (only is not None and u is not only):
                continue
            n = tiles_on(cells, u.anchor, u.fp)
            if n <= 0:
                continue
            tiles_of[u] = self.effective_tiles(att, u, n)
            order.append(u)
        att.shape_tiles = save[0]
        if interceptor is not None and protected in tiles_of and not interceptor.dead:
            pt = tiles_of[protected]
            g = (pt + 1) // 2
            tiles_of[protected] = pt - g
            tiles_of[interceptor] = tiles_of.get(interceptor, 0) + g
            if interceptor not in order:
                order.insert(order.index(protected) + 1, interceptor)
            self.st.val["Interpor"] += g * att.dpt(wd) * mult       # tiles tirados do protegido (dano bruto)
        power = att.bonus_dmg
        att.bonus_dmg = False
        aim_dmg = rint(att.a["DEX"] * T["aimDexMultiplier"]) if att.aim_bonus else 0
        att.aim_bonus = False
        fury = att.fury
        att.fury = 0.0
        if not order:
            return res
        line_t = primary if primary is not None else order[0]
        res["ranged"] = gap(att.anchor, att.fp, line_t.anchor, line_t.fp) > 1
        if curve and res["ranged"]:
            att.curve = False
        else:
            curve = False
        dpt = att.dpt(wd)
        per_tile = dpt * mult * (T["powerStrikeMult"] if power else 1.0)
        hit_chance = att.hit_chance
        first = True
        for t in order:
            tiles = tiles_of[t]
            if tiles <= 0:
                continue
            true_aim = False
            if (t is primary or (primary is None and first)) and att.true_aim_crit > 0:
                bonus = att.true_aim_crit
                att.true_aim_crit = 0.0
                true_aim = True
                r = "crit" if self.roll() < rint(clamp01(att.crit + bonus) * 10000) else "hit"
            else:
                if self.roll() < rint(att.crit * 10000):
                    r = "crit"
                else:
                    r = "hit" if self.roll() < rint(clamp01(hit_chance) * 10000) else "miss"
            if force_hit and r == "miss":
                r = "hit"
            first = False
            if r == "miss":
                res["missed"].append(t)
                self.clear_def(t)
                continue
            removed = 0
            if curve or force_hit:
                if curve and t.dodge_chance > 0:
                    self.st.extra["Curva: ignorou esquiva"] += 1
            elif t.dodge_react > 0:
                removed = self.dodge_tiles_for(t, tiles)
            elif r != "crit" and not true_aim and self.roll() < rint(t.dodge_chance * 10000):
                removed = self.dodge_tiles_for(t, tiles)
                self.st.extra["_passive_dodge_tiles"] += removed
            removed = min(removed, tiles)
            left = tiles - removed
            if left <= 0:
                res["missed"].append(t)
                self.clear_def(t)
                continue
            raw = rint(per_tile * left) + aim_dmg
            base_raw = rint(dpt * mult * left) + aim_dmg
            if power:
                self.st.val["Golpe poderoso"] += raw - base_raw
            if fury > 0:
                fr = rint(dpt * left * fury) + aim_dmg
                if fr > raw:
                    self.st.val["Fúria"] += fr - raw
                raw = max(raw, fr)
            half = rint(raw * F["damageVariance"])
            dmg = raw + (self.rng.randint(-half, half) if half > 0 else 0)
            dmg = rint(dmg * (1 - clamp(t.a["VIT"] * F["armorPctPerVIT"], 0, F["armorPctMax"])))
            dmg = max(T["minDamageFloor"], dmg - t.phys_def)
            if r == "crit":
                dmg = rint(dmg * F["critDamageMul"])
            if t.block_flat > 0:
                before = dmg
                dmg = max(0, dmg - t.block_flat)
                self.st.val["Bloquear"] += before - dmg
                if dmg == 0:
                    res["absorbed"] = True
                    self.st.extra["Bloquear: absorveu tudo (desequilibra)"] += 1
            if dmg > 0 and t.stone_ticks > 0:
                extra = rint(t.phys_def * max(0.0, t.stone_mult - 1))
                nd = max(T["minDamageFloor"], dmg - extra)
                self.st.val["Pele de pedra"] += dmg - nd
                dmg = nd
            if dmg > 0:
                self.damage(t, dmg, tag)
                res["any_hit"] = True
                res["hit"].append(t)
                res["dealt"] += dmg
                if curve:
                    self.st.val["Curva"] += dmg
            self.clear_def(t)
        return res

    @staticmethod
    def clear_def(t):
        t.dodge_react = 0
        t.block_flat = 0

    # ---- ataque planejado (ResolveAttackSlot + AttackResolver.ResolveAttack)
    def resolve_attack(self, u, q):
        T = self.R.t
        tgt = q["target"]
        if tgt is None or tgt.dead or gap(u.anchor, u.fp, tgt.anchor, tgt.fp) > u.range:
            if q["power"]:
                u.stamina = min(u.max_stamina, u.stamina + T["staminaPowerStrike"])
            self.st.extra["Ataque perdido (alvo saiu do alcance)"] += 1
            return
        if q["power"]:
            u.bonus_dmg = True
        aim = center(tgt.anchor, tgt.fp)
        cells = shape_cells(u.weapon, center(u.anchor, u.fp), aim)
        in_shape = [o for o in sorted(self.units, key=lambda x: x.uid)
                    if o is not u and not o.dead and o.hostile(u) and tiles_on(cells, o.anchor, o.fp) > 0]
        if any(gap(u.anchor, u.fp, o.anchor, o.fp) > 1 for o in in_shape):
            self.provoke(u)
            if u.counter_interrupted or u.dead:
                u.bonus_dmg = False
                self.st.extra["Tiro cancelado por contra-ataque"] += 1
                return
        guard, guarded = None, None
        for h in in_shape:
            gs = [a for a in self.units if a is not h and a is not u and not a.dead and not a.hostile(h)
                  and self.can_react(a, "Interpor") and self.guard_spot(a, h) is not None]
            if not gs:
                continue
            gs.sort(key=lambda x: (-x.hp, x.uid))
            g = gs[0]
            self.apply_reaction(g, "Interpor")
            g.anchor = self.guard_spot(g, h)
            if self.after_reaction(g, "Interpor", u):
                guard, guarded = g, h
                break
        for h in in_shape:
            if h.dead:
                continue
            opts = [k for k in ("Esquivar", "Bloquear") if self.can_react(h, k)]
            k = self.auto_pick(h, opts)
            if not k:
                continue
            self.apply_reaction(h, k)
            if k == "Esquivar":
                h.dodge_react = 1
            else:
                h.block_flat = h.block_amount()
            self.after_reaction(h, k, u, cells)
        res = self.strike(u, aim, u.weapon, u.wd, u.shape_tiles, tgt, 1.0, guard, guarded)
        # segunda arma (GDD-021 §2.2)
        if u.offhand and not u.dead and (res["hit"] or res["missed"]):
            off = u.offhand
            if gap(u.anchor, u.fp, aim, 1) <= T["baseAttackRange"] + off["range"]:
                owd = max(1, rint(off["damage"] * T["oneHandDamageMult"]))
                r2 = self.strike(u, aim, off, owd, centered_tiles(off, self.R.default_fp), None, T["offhandDamageMult"],
                                 tag="2ª arma")
                self.st.val["2ª arma"] += r2["dealt"]
                res["hit"] += r2["hit"]
        if res["absorbed"] and not u.dead:
            u.staggered = True
        if res["ranged"] and res["missed"] and not u.dead and self.can_react(u, "Olho certeiro"):
            miss = res["missed"][0]
            self.apply_reaction(u, "Olho certeiro")
            if not miss.dead:
                u.true_aim_crit = max(0.0001, T["trueAimCritBonus"])
                a2 = center(miss.anchor, miss.fp)
                r3 = self.strike(u, a2, u.weapon, u.wd, u.shape_tiles, miss, 1.0, only=miss, force_hit=True,
                                 tag="Olho certeiro")
                self.st.val["Olho certeiro"] += r3["dealt"]

    def guard_spot(self, g, t):
        if gap(g.anchor, g.fp, t.anchor, t.fp) <= 1:
            return g.anchor
        best, bd = None, None
        for a in self.reachable(g, g.anchor, g.move_step):
            if gap(a, g.fp, t.anchor, t.fp) > 1:
                continue
            d = abs(a[0] - g.anchor[0]) + abs(a[1] - g.anchor[1])
            if bd is None or (d, a[1], a[0]) < bd:
                best, bd = a, (d, a[1], a[0])
        return best

    # ---- magias (SpellResolver.Resolve)
    def resolve_spell(self, u, q):
        T = self.R.t
        s = q["spell"]
        fx = s["effect"]
        if fx == "PrepFury":
            u.fury = s["mult"]
            return
        if fx == "PrepCurve":
            u.curve = True
            return
        if fx == "PrepStoneSkin":
            u.stone_ticks = max(1, T["ticksPerRound"])
            u.stone_mult = s["mult"]
            return
        if fx == "Leap":
            dest = q["dest"]
            if self.free_at(u, dest) and gap(u.anchor, u.fp, dest, u.fp) <= u.spell_reach(s):
                u.anchor = dest
            return
        t = q["target"]
        if t is None or t.dead or gap(u.anchor, u.fp, t.anchor, t.fp) > u.spell_reach(s):
            self.st.extra["Magia sem alvo"] += 1
            return
        if self.can_react(t, "Esquivar"):
            self.apply_reaction(t, "Esquivar")
            t.dodge_react = 1
            self.after_reaction(t, "Esquivar", u, None, spell_from=u)
            if gap(u.anchor, u.fp, t.anchor, t.fp) > u.spell_reach(s):
                pass        # a magia segue a unidade mirada enquanto ela estiver ao alcance
        full = t.fp * t.fp
        tiles = full
        dodged = self.dodge_tiles_for(t, tiles) if t.dodge_react > 0 else 0
        t.dodge_react = 0
        left = tiles - dodged
        amount = u.spell_amount(s)
        if dodged:
            self.st.val["Esquivar"] += rint(amount * dodged / full)
        if left <= 0:
            return
        d = rint(t.mag_def * (1 - s["pierce"]))
        dmg = max(T["minDamageFloor"], rint(amount * left / full) - d)
        self.damage(t, dmg, "Magia " + s["name"])
        if not t.dead and s["status"] == "Burning":
            t.burn_ticks = max(t.burn_ticks, max(1, s["dur"] // 2) if "FireResist" in t.effects else s["dur"])
            t.burn_dmg = max(t.burn_dmg, rint(s["tickDmg"] * T["spellTickDamageMult"]))

    # ---- planejamento (IA de um jogador razoável; ver relatório)
    def plan(self, u):
        T = self.R.t
        enemies = [e for e in self.alive() if e.hostile(u)]
        if not enemies:
            return
        ticks = T["maxQueueTicks"]
        st, mana = u.stamina, u.mana
        pos = u.anchor
        reserve = min(u.react_cost("Esquivar"), u.react_cost("Bloquear"))
        q = []

        def g(p, e):
            return gap(p, u.fp, e.anchor, e.fp)

        target = min(enemies, key=lambda e: (g(pos, e), e.hp, e.uid))
        sp = {s: self.R.spells[s] for s in u.spells if s in self.R.spells}

        def can_cast(sid):
            nonlocal st, mana
            if sid not in sp or not self.can_use(u, self.mech_name(sp[sid])):
                return False
            s = sp[sid]
            stp = s["cost"] // 2 if s["physical"] else 0
            mp = s["cost"] - stp
            return u.duration("spell", spell=s) <= ticks and st - stp >= (reserve if s["physical"] else 0) and mana >= mp

        def add_spell(sid, **kw):
            nonlocal st, mana, ticks
            s = sp[sid]
            stp = s["cost"] // 2 if s["physical"] else 0
            st -= stp
            mana -= s["cost"] - stp
            d = u.duration("spell", spell=s)
            ticks -= d
            q.append(dict(type="spell", dur=d, elapsed=0, spell=s, **kw))

        threat = any(g(pos, e) <= e.range + e.move_step for e in enemies)
        if threat and u.stone_ticks == 0 and "StoneSkin" in sp and u.phys_def >= 4 and can_cast("StoneSkin"):
            add_spell("StoneSkin")
        elemental = next((s for s in u.spells if s in sp and not sp[s]["physical"]), None)
        fury_planned = curve_planned = False
        guard = 0
        while ticks > 0 and guard < 12:
            guard += 1
            d0 = g(pos, target)
            if elemental and can_cast(elemental) and d0 <= u.spell_reach(sp[elemental]):
                add_spell(elemental, target=target)
                continue
            if d0 <= u.eff_range:
                if u.duration("attack") > ticks:
                    break
                if ("Fury" in sp and not fury_planned and u.fury == 0 and can_cast("Fury")
                        and u.duration("spell", spell=sp["Fury"]) + u.duration("attack") <= ticks):
                    add_spell("Fury")
                    fury_planned = True
                if ("Curve" in sp and not curve_planned and not u.curve and u.range > 1 and target.dodge_chance >= 0.15
                        and can_cast("Curve") and u.duration("spell", spell=sp["Curve"]) + u.duration("attack") <= ticks):
                    add_spell("Curve")
                    curve_planned = True
                power = (self.can_use(u, "Golpe poderoso") and st - T["staminaPowerStrike"] >= reserve
                         and u.duration("attack", True) <= ticks)
                d = u.duration("attack", power)
                if power:
                    st -= T["staminaPowerStrike"]
                ticks -= d
                q.append(dict(type="attack", dur=d, elapsed=0, target=target, power=power))
                continue
            # aproximar
            if "Leap" in sp and d0 > u.move_step + u.eff_range and can_cast("Leap"):
                best = self.best_anchor(u, pos, u.spell_reach(sp["Leap"]), target)
                if best != pos:
                    add_spell("Leap", dest=best)
                    pos = best
                    continue
            best = self.best_anchor(u, pos, u.move_step, target)
            if best == pos:
                break
            q.append(dict(type="move", dur=1, elapsed=0, dest=best))
            ticks -= 1
            pos = best
        if ticks > 0:
            if elemental and mana < sp[elemental]["cost"] and u.duration("concentrate") <= ticks:
                q.append(dict(type="concentrate", dur=u.duration("concentrate"), elapsed=0))
            elif st < u.max_stamina - 1 and u.duration("breathe") <= ticks:
                q.append(dict(type="breathe", dur=u.duration("breathe"), elapsed=0))
        u.stamina = max(0, st)
        u.mana = max(0, mana)
        u.queue = q or [dict(type="wait", dur=1, elapsed=0)]
        for a in u.queue:
            if a["type"] == "spell":
                self.st.use[(u.build, self.mech_name(a["spell"]))] += 1
                self.st.uses[self.mech_name(a["spell"])] += 1
            if a.get("power"):
                self.st.use[(u.build, "Golpe poderoso")] += 1
                self.st.uses["Golpe poderoso"] += 1

    @staticmethod
    def mech_name(s):
        return {"PrepFury": "Fúria", "Leap": "Salto", "PrepStoneSkin": "Pele de pedra", "PrepCurve": "Curva"}.get(
            s["effect"], "Magia " + s["name"])

    def best_anchor(self, u, pos, budget, target):
        here = gap(pos, u.fp, target.anchor, target.fp)
        best, bk = pos, (here,)
        for dx in range(-budget, budget + 1):
            for dy in range(-budget, budget + 1):
                a = (pos[0] + dx, pos[1] + dy)
                if a == pos or not self.free_at(u, a):
                    continue
                gg = gap(a, u.fp, target.anchor, target.fp)
                # não precisa chegar mais perto que o alcance: para no limite (anda o mínimo)
                key = (max(gg, u.eff_range) if gg <= u.eff_range else gg, abs(dx) + abs(dy), a[0], a[1])
                if gg < here and (bk == (here,) or key < bk):
                    best, bk = a, key
        return best

    # ---- round (RoundManager.ActionRoutine)
    def run(self):
        T = self.R.t
        for rnd in range(1, self.max_rounds + 1):
            for u in sorted(self.alive(), key=lambda x: x.uid):
                if not u.queue:
                    self.plan(u)
            for _ in range(max(1, T["ticksPerRound"])):
                self.do_tick()
                w = self.winner()
                if w is not None:
                    return w, rnd
        return None, self.max_rounds

    def winner(self):
        teams = {u.team for u in self.alive()}
        if len(teams) == 1:
            return teams.pop()
        if not teams:
            return "empate"
        return None

    def do_tick(self):
        T = self.R.t
        self.provoked = set()
        by_id = sorted(self.alive(), key=lambda x: x.uid)
        for u in by_id:
            if not u.dead and u.queue and u.queue[0]["type"] == "spell" and u.queue[0]["elapsed"] == 0:
                self.provoke(u)
                if u.counter_interrupted and u.queue and u.queue[0]["type"] == "spell":
                    u.queue.pop(0)
                    self.st.extra["Magia cortada ao começar"] += 1
        moves, attacks, spells = [], [], []
        for u in by_id:
            if u.dead or not u.queue:
                continue
            h = u.queue[0]
            h["elapsed"] += 1
            if h["elapsed"] < h["dur"]:
                continue
            u.queue.pop(0)
            if h["type"] == "move":
                moves.append((u, h["dest"]))
            elif h["type"] == "attack":
                attacks.append((u, h))
            elif h["type"] == "concentrate":
                u.mana = min(u.max_mana, u.mana + max(0, T["concentrateMana"]))
            elif h["type"] == "breathe":
                u.stamina = min(u.max_stamina, u.stamina + max(0, T["breatheStamina"]))
            elif h["type"] == "spell":
                spells.append((u, h))
        if moves:
            start = {u: u.anchor for u, _ in moves}
            ini = {u: self.rng.randint(1, 20) + rint(u.a["AGI"] * self.R.f["iniPerAGI"]) + rint(u.a["DEX"] * self.R.f["iniPerDEX"])
                   for u, _ in moves}
            movers = {u for u, _ in moves}
            for u, dest in sorted(moves, key=lambda m: (-ini[m[0]], m[0].uid)):
                if self.free_at(u, dest, ignore=()):
                    u.anchor = dest
                else:   # parcial: anda o que dá na direção do destino
                    best = u.anchor
                    for a in self.reachable(u, u.anchor, max(abs(dest[0] - u.anchor[0]), abs(dest[1] - u.anchor[1]))):
                        if max(abs(a[0] - dest[0]), abs(a[1] - dest[1])) < max(abs(best[0] - dest[0]), abs(best[1] - dest[1])):
                            best = a
                    u.anchor = best
            for u, _ in moves:
                if not u.dead and start[u] != u.anchor:
                    self.provoke(u, from_anchor=start[u])
        if attacks:
            ini = {u: self.rng.randint(1, 20) + rint(u.a["AGI"]) + rint(u.a["DEX"]) for u, _ in attacks}
            for u, h in sorted(attacks, key=lambda a: (-ini[a[0]], a[0].uid)):
                if not u.dead:
                    self.resolve_attack(u, h)
        for u, h in spells:
            if not u.dead:
                self.resolve_spell(u, h)
        # SpellResolver.TickEffects
        for u in self.alive():
            if u.burn_ticks > 0:
                self.damage(u, u.burn_dmg, "Queimando")
                u.burn_ticks -= 1
            if u.stone_ticks > 0:
                u.stone_ticks -= 1
        # AdvanceGlobalTick
        self.tick += 1
        for u in self.alive():
            if self.tick % u.stamina_regen_ticks == 0:
                u.stamina = min(u.max_stamina, u.stamina + 1)
            if u.max_mana > 0 and self.tick % u.mana_regen_ticks == 0:
                u.mana = min(u.max_mana, u.mana + 1)
        if self.tick % max(1, T["ticksPerRound"]) == 0:
            for u in self.alive():
                u.reactions = 1
        for u in self.units:
            if u.counter_interrupted or u.staggered:
                if not u.dead and u.queue:
                    u.queue.clear()
                u.counter_interrupted = u.staggered = False
                continue
            if not u.dead and u.queue and u.queue[0].get("interrupted"):
                u.queue.clear()
                self.st.extra["Magia interrompida por dano"] += 1


def clamp(x, lo, hi):
    return max(lo, min(hi, x))


class Stats:
    def __init__(self):
        self.use = defaultdict(int)       # (build, mecânica) -> usos
        self.uses = defaultdict(int)      # mecânica -> usos
        self.val = defaultdict(float)     # mecânica -> dano somado/evitado
        self.dmg = defaultdict(int)       # fonte -> dano causado
        self.extra = defaultdict(int)
        self.fights = 0
        self.rounds = []


# =============================================================================== cenários
# Builds de referência (30 pontos: STR, VIT, DEX, AGI, INT, WIS), arma, magias que a IA usa.
# Kit = Equipment.StarterKit(arma), lido do código.
BUILDS = {
    "Guerreiro":   ((9, 7, 5, 5, 1, 3), "IronSword", ["Fury"]),
    "Tanque":      ((6, 12, 4, 3, 1, 4), "IronAxe", ["StoneSkin", "Fury"]),
    "Ágil":        ((6, 4, 7, 10, 1, 2), "Hatchet", ["Leap", "Fury"]),
    "Arqueiro":    ((2, 5, 10, 8, 1, 4), "LongBow", ["Curve"]),
    "Mago":        ((1, 5, 5, 4, 9, 6), "ArcaneStaff", ["Ember"]),
    "Equilibrado": ((5, 5, 5, 5, 5, 5), "WoodenStaff", ["Ember"]),
}
MECHANICS = ["Esquivar", "Bloquear", "Contra-atacar", "Interpor", "Olho certeiro",
             "Golpe poderoso", "Fúria", "Salto", "Pele de pedra", "Curva"]


def make(R, build, team, uid, attrs=None, weapon=None, spells=None):
    a, w, s = BUILDS.get(build, (None, None, None))
    a, w, s = attrs or a, weapon or w, spells if spells is not None else s
    return Unit(R, build, a, w, team, uid, loadout=R.kit(w), spells=s, build=build)


def beast(R, size, team, uid, layer=1):
    """BeastAI.BuildPreset (feras sem equipamento)."""
    T = R.t
    pts = max(6.0, R.budget * (T["beastBudgetFrac"] + 0.10 * (layer - 1)))
    mul = T["beastSizeMul"][["Pequena", "Média", "Grande"].index(size)]
    fp = {"Pequena": 1, "Média": 2, "Grande": 4}[size]
    attrs = (rint(pts * 0.28 * mul), rint(pts * 0.30 * mul), rint(pts * 0.14), rint(pts * 0.18), 1,
             max(1, rint(pts * 0.10)))
    return Unit(R, f"Fera {size}", attrs, "ClawsLarge" if size == "Grande" else "Claws", team, uid, fp=fp,
                build=f"Fera {size}")


def place(battle, team_a, team_b, dist):
    """Times frente a frente, dist células entre as bordas."""
    H = battle.H
    for i, u in enumerate(team_a):
        u.anchor = (4, H // 2 - 4 + i * 5)
    for i, u in enumerate(team_b):
        u.anchor = (4 + 3 + dist, H // 2 - 4 + i * 5)
    # garante que não começam sobrepostos
    for u in team_a + team_b:
        while not battle.free_at(u, u.anchor):
            u.anchor = (u.anchor[0], u.anchor[1] + 1)


def fight(R, mk_a, mk_b, seed, dist, disabled=None, stats=None):
    rng = random.Random(seed)
    a, b = mk_a("A", 0), mk_b("B", 10)
    bt = Battle(R, a + b, rng, disabled=disabled, stats=stats)
    place(bt, a, b, dist)
    w, rounds = bt.run()
    if stats is not None:
        stats.fights += 1
        stats.rounds.append(rounds)
    return w, rounds


def team(R, names):
    return lambda side, base: [make(R, n, side, base + i) for i, n in enumerate(names)]


# ---- relatórios
def run_2v2(R, n, dist, seed0):
    names = list(BUILDS)
    teams = list(itertools.combinations_with_replacement(names, 2))
    wins, games = defaultdict(float), defaultdict(int)
    st = Stats()
    seed = seed0
    pair = {}
    for ta, tb in itertools.combinations(teams, 2):
        ws = 0.0
        for k in range(n):
            seed += 1
            A, B = (ta, tb) if k % 2 == 0 else (tb, ta)
            w, _ = fight(R, team(R, A), team(R, B), seed, dist, stats=st)
            sa = 1.0 if w == "A" else 0.5 if w is None or w == "empate" else 0.0
            sc_ta = sa if A == ta else 1 - sa
            ws += sc_ta
            for nm in ta:
                wins[nm] += sc_ta
                games[nm] += 1
            for nm in tb:
                wins[nm] += 1 - sc_ta
                games[nm] += 1
        pair[(ta, tb)] = ws / n
    rates = {nm: wins[nm] / games[nm] for nm in names}
    return rates, st, pair


def run_duel(R, n, dist, seed0):
    ws = [w for w in R.weapons]
    res = {}
    seed = seed0
    for wa in ws:
        tot = 0.0
        for wb in ws:
            if wa == wb:
                continue
            for k in range(n):
                seed += 1
                mk_a = lambda side, base, w=wa: [Unit(R, w, (5,) * 6, w, side, base, loadout=R.kit(w), build=w)]
                mk_b = lambda side, base, w=wb: [Unit(R, w, (5,) * 6, w, side, base, loadout=R.kit(w), build=w)]
                A, B = (mk_a, mk_b) if k % 2 == 0 else (mk_b, mk_a)
                w, _ = fight(R, A, B, seed, dist)
                s = 1.0 if w == "A" else 0.5 if w in (None, "empate") else 0.0
                tot += s if k % 2 == 0 else 1 - s
        res[wa] = tot / (n * (len(ws) - 1))
    return res


def run_beasts(R, n, dist, seed0):
    out = {}
    seed = seed0
    for b in BUILDS:
        row = {}
        for size in ("Pequena", "Média", "Grande"):
            won, rs = 0.0, []
            for _ in range(n):
                seed += 1
                w, r = fight(R, team(R, [b]), lambda side, base, s=size: [beast(R, s, side, base)], seed, dist)
                won += 1.0 if w == "A" else 0.5 if w in (None, "empate") else 0.0
                rs.append(r)
            row[size] = (won / n, statistics.mean(rs))
        out[b] = row
    return out


def run_ablation(R, n, dist, seed0):
    """Valor de cada mecânica: dupla com tudo × mesma dupla sem a mecânica (lados alternados)."""
    names = list(BUILDS)
    teams = list(itertools.combinations_with_replacement(names, 2))
    res = {}
    for mech in MECHANICS:
        seed = seed0
        tot, cnt = 0.0, 0
        for t in teams:
            for k in range(max(4, n)):
                seed += 1
                dis = {"B": {mech}} if k % 2 == 0 else {"A": {mech}}
                w, _ = fight(R, team(R, t), team(R, t), seed, dist, disabled=dis)
                s = 1.0 if w == "A" else 0.5 if w in (None, "empate") else 0.0
                tot += s if k % 2 == 0 else 1 - s
                cnt += 1
        res[mech] = tot / cnt
    return res


def evaluate(R, args):
    rates, st, pair = run_2v2(R, args.n, args.dist, args.seed)
    return dict(rates=rates, st=st, pair=pair,
                duel=run_duel(R, max(2, args.n // 2), args.dist, args.seed + 10 ** 6),
                beasts=run_beasts(R, args.n, args.dist, args.seed + 2 * 10 ** 6),
                abl=None if args.rapido else run_ablation(R, args.n, args.dist, args.seed + 3 * 10 ** 6))


def pct(x):
    return f"{x * 100:.0f}%"


def build_sheet(R):
    rows = ["| Build | Atributos (STR/VIT/DEX/AGI/INT/WIS) | Arma · kit | HP | Def F/M | Dano/tile × tiles | Ticks ataque | Andada | Stamina | Peso (acima) |",
            "|---|---|---|---|---|---|---|---|---|---|"]
    for b, (a, w, s) in BUILDS.items():
        u = make(R, b, "A", 0)
        kit = "/".join(x for x in (u.lo["head"], u.lo["torso"], u.lo["legs"], u.lo["left"]) if x)
        rows.append(f"| {b} | {'/'.join(str(x) for x in a)} | {w} · {kit} | {u.max_hp} | {u.phys_def}/{u.mag_def} | "
                    f"{u.dpt():.1f} × {u.shape_tiles} | {u.duration('attack')} | {u.move_step} | {u.max_stamina} | {u.weight} ({u.ow}) |")
    return "\n".join(rows)


def report(R, res, cmp=None, label_a="atual", label_b="proposta", R2=None):
    out = []

    def col(fn, x):
        a = fn(res, x)
        if cmp is None:
            return a
        return f"{a} → {fn(cmp, x)}"

    hdr = "" if cmp is None else f" ({label_a} → {label_b})"
    out.append(f"## 2 contra 2: % de vitórias por build{hdr}\n")
    out.append("Todas as duplas contra todas, lados alternados. Meta: 40%–60%.\n")
    out.append("| Build | Vitórias | Na meta? |\n|---|---|---|")
    for b in sorted(BUILDS, key=lambda x: -res["rates"][x]):
        r = (cmp or res)["rates"][b]
        ok = "✅" if 0.40 <= r <= 0.60 else ("⬆️ forte" if r > 0.60 else "⬇️ fraco")
        out.append(f"| {b} | {col(lambda d, x: pct(d['rates'][x]), b)} | {ok} |")
    rs = res["st"].rounds
    line = f"\n**Duração:** média {statistics.mean(rs):.1f} rounds, mediana {statistics.median(rs):.0f}, " \
           f"{sum(1 for r in rs if r >= 30) / len(rs) * 100:.1f}% batem o limite de 30 rounds ({res['st'].fights} lutas)."
    if cmp:
        rc = cmp["st"].rounds
        line += f" Proposta: média {statistics.mean(rc):.1f}, mediana {statistics.median(rc):.0f}."
    out.append(line)

    out.append(f"\n## Duelo de armas, atributos iguais (5 em tudo) com o kit inicial{hdr}\n")
    out.append("| Arma | Vitórias (1v1 contra as outras) |\n|---|---|")
    for w in sorted(res["duel"], key=lambda x: -res["duel"][x]):
        out.append(f"| {w} | {col(lambda d, x: pct(d['duel'][x]), w)} |")

    out.append(f"\n## Jogador sozinho contra cada fera (camada 1){hdr}\n")
    out.append("| Build | Fera Pequena | Fera Média | Fera Grande |\n|---|---|---|---|")
    for b in BUILDS:
        cells = []
        for s in ("Pequena", "Média", "Grande"):
            cells.append(col(lambda d, x: f"{pct(d['beasts'][x[0]][x[1]][0])} ({d['beasts'][x[0]][x[1]][1]:.1f} r)", (b, s)))
        out.append(f"| {b} | " + " | ".join(cells) + " |")
    fs = [beast(R, s, "B", 0) for s in ("Pequena", "Média", "Grande")]
    out.append("\nFeras: " + "; ".join(f"{f.build} {f.fp}×{f.fp} STR/VIT/DEX/AGI {int(f.a['STR'])}/{int(f.a['VIT'])}/"
                                        f"{int(f.a['DEX'])}/{int(f.a['AGI'])}, HP {f.max_hp}, dano/tile {f.dpt():.1f}" for f in fs) + ".")

    st = res["st"]
    out.append(f"\n## Reações e magias físicas (2v2){hdr}\n")
    out.append("Usos por luta; valor = dano a mais (ofensivas) ou evitado (defensivas) por uso, em HP bruto "
               "(Interpor: dano desviado para o protetor; Esquivar: tiles que saíram da forma × dano/tile). "
               "Δ vitórias = dupla com a mecânica contra a mesma dupla sem ela (50% = não muda nada).\n")
    out.append("| Mecânica | Custo | Usos/luta | Valor/uso | Δ vitórias |\n|---|---|---|---|---|")
    def costs(R):
        T = R.t
        c = {"Esquivar": f"{T['reactCostDodge']} St", "Bloquear": f"{T['reactCostBlock']} St",
             "Contra-atacar": f"{T['reactCostCounter']} St", "Interpor": f"{T['reactCostIntercept']} St",
             "Olho certeiro": f"{T['reactCostTrueAim']} St", "Golpe poderoso": f"{T['staminaPowerStrike']} St"}
        for sid, nm in (("Fury", "Fúria"), ("Leap", "Salto"), ("StoneSkin", "Pele de pedra"), ("Curve", "Curva")):
            sp = R.spells[sid]
            c[nm] = f"{sp['cost'] - sp['cost'] // 2} Mn + {sp['cost'] // 2} St"
        return c

    cost = costs(R)
    if R2 is not None:
        c2 = costs(R2)
        cost = {k: v if v == c2[k] else f"{v} → {c2[k]}" for k, v in cost.items()}

    def mech_row(d, m):
        s = d["st"]
        u = s.uses[m]
        v = s.val[m] / u if u else 0
        ab = pct(d["abl"][m]) if d.get("abl") else "—"
        return f"{u / s.fights:.2f}", f"{v:.1f}" if m != "Salto" else "—", ab

    for m in MECHANICS:
        a = mech_row(res, m)
        if cmp:
            b = mech_row(cmp, m)
            a = tuple(f"{x} → {y}" for x, y in zip(a, b))
        out.append(f"| {m} | {cost.get(m, '')} | {a[0]} | {a[1]} | {a[2]} |")
    n = st.extra["_dodge_n"]
    if n:
        out.append(f"\nEsquivar contra arma: o passo tira em média {st.extra['_dodge_tiles_before'] / n:.1f} → "
                   f"{st.extra['_dodge_tiles_after'] / n:.1f} tiles do corpo dentro da forma.")
    ex = [f"{k}: {v / st.fights:.2f}/luta" for k, v in sorted(st.extra.items()) if not k.startswith("_")]
    out.append("Eventos: " + "; ".join(ex) + ".")
    tot = sum(st.dmg.values())
    out.append("Dano por fonte: " + ", ".join(f"{k} {v / tot * 100:.0f}%" for k, v in sorted(st.dmg.items(), key=lambda x: -x[1])) + ".")
    return "\n".join(out)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--n", type=int, default=12, help="lutas por confronto (2v2: por par de duplas)")
    ap.add_argument("--dist", type=int, default=12, help="células entre os times no início")
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--base", help="ref git das regras 'atuais' (padrão: árvore de trabalho)")
    ap.add_argument("--proposta", nargs="*", help="chave=valor aplicados sobre a árvore de trabalho")
    ap.add_argument("--rapido", action="store_true", help="pula a tabela Δ vitórias (ablação)")
    ap.add_argument("--fichas", action="store_true", help="só imprime a ficha das builds")
    ap.add_argument("--md", action="store_true", help="(a saída já é Markdown; mantido por compatibilidade)")
    args = ap.parse_args()

    base = Rules(args.base)
    print(f"# Pangea Skirmish — balance_sim v2 ({'ref ' + args.base if args.base else 'árvore de trabalho'})\n")
    print(build_sheet(base))
    if args.fichas:
        return
    print()
    res = evaluate(base, args)
    cmp = None
    if args.base or args.proposta:
        prop = Rules(None).apply(args.proposta)
        cmp = evaluate(prop, args)
        label_a = args.base or "atual"
        label_b = ("árvore" if args.base else "proposta") + (" + " + " ".join(args.proposta) if args.proposta else "")
        print(f"Comparando **{label_a}** → **{label_b}** (mesmas sementes).\n")
        print(report(base, res, cmp, label_a, label_b, prop))
        return
    print(report(base, res))


if __name__ == "__main__":
    main()
