#!/usr/bin/env python3
"""Simulador de combate do Pangea Skirmish (regras do GDD-020, 2026-11-13).

Replica as fórmulas do jogo em lutas DOIS CONTRA DOIS numa linha (distância em células):
armas com forma e dano por tile (alvo 3×3 centralizado), DEX encurtando os ticks, defesa fixa
da VIT, esquiva que tira tiles (AGI), reações Esquivar/Bloquear com custo de Stamina, golpe
poderoso, Brasa/Chama arcana com Mana e Concentrar, interrupção de conjuração e feras de
três tamanhos. Não simula terreno, empurrões nem magias de área.

  python3 scripts/balance_sim.py                # tabelas do estado atual (lê os números abaixo)
  python3 scripts/balance_sim.py --proposal     # atual × proposta lado a lado
Os nomes dos parâmetros seguem o GameTuning.asset. Saída: % de vitórias por build (2v2),
duração das lutas, duelo de armas com atributos iguais e jogador médio contra cada fera.
"""
import random, sys, statistics, itertools

# ----------------------------------------------------------------------------- números
def tuning(**kw):
    p = dict(
        hp_base=30, hp_per_vit=3, mana_base=6, mana_per_wis=1.5,
        dmg_pct_per_attr=0.06, armor_per_vit=0.25, magic_resist_per_wis=1,
        base_hit=0.9, hit_per_dex=0.03, dodge_per_agi=0.05, crit_per_dex=0.02, crit_mul=1.5, variance=0.15,
        dodge_agi_per_tile=3, block_flat_per_vit=0.5,
        stamina_base=1, stamina_per_body=0.25, stamina_regen_ticks=3, agi_regen_from=6, agi_regen_step=3,
        react_dodge=1, react_block=1, reaction_ticks=1,
        power_cost=2, power_mult=1.5, power_extra_ticks=1,
        tick_dex_factor=10, move_base=2, move_per_agi=0.67, move_cap=6,
        base_range=1, unarmed_damage=1, unarmed_ticks=2, min_damage=1,
        mana_regen_ticks=2, wis_regen_step=4, concentrate_mana=3, ticks_concentrate=2,
        spell_base_mult=2, spell_pot_per_int=3, spell_tick_mult=2, spell_range_mult=2, spell_range_per_int=0.5,
        interrupt_frac=0.25,
        beast_budget_frac=0.55, beast_mul=(1, 2, 4),
        dodge_rule="tiles",          # "tiles": tira max(1, AGI/3) tiles | "frac": tira uma fração; golpe de 1 tile vira chance
        dodge_frac_per_agi=0.06, dodge_frac_cap=0.6,
        beast_weapon=None,           # (dano/tile, ticks, alcance extra, tiles) — None = garras de desarmado
        weapons={},                  # sobrescreve entradas da tabela de armas
        spells={},                   # sobrescreve entradas das magias
    )
    p.update(kw)
    return p

# arma: dano/tile, ticks, alcance, tiles centralizados num 3×3, dureza, magia ×, +alcance de magia, escala
WEAPONS = {
    "Hatchet":        (3, 1, 1, 3, 2, 1.00, 0, "STR"),
    "IronAxe":        (5, 3, 2, 6, 2, 1.00, 0, "STR"),
    "WoodenSword":    (8, 2, 3, 2, 3, 1.00, 0, "STR"),
    "IronSword":      (6, 2, 4, 3, 3, 1.00, 0, "STR"),
    "WoodenStaff":    (6, 2, 7, 3, 2, 1.10, 0, "DEX"),
    "Scepter":        (5, 2, 6, 4, 2, 1.15, 1, "DEX"),
    "ApprenticeWand": (2, 1, 5, 4, 1, 1.00, 0, "DEX"),
    "ArcaneStaff":    (6, 2, 7, 3, 2, 1.30, 2, "DEX"),
    "ShortBow":       (9, 1, 8, 1, 0, 1.00, 0, "DEX"),
    "LongBow":        (28, 3, 10, 1, 0, 1.00, 0, "DEX"),
}
# magias usadas pelos conjuradores: custo Mana, ticks base, alcance base, dano base, pot, queimando (dano, ticks), perfura
SPELLS = {
    "Ember":       (2, 2, 4, 4, 1.0, (2, 3), 0.0),
    "ArcaneFlame": (4, 2, 5, 5, 1.3, None, 0.5),
}
# builds de 30 pontos (STR, VIT, DEX, AGI, INT, WIS), arma, magia preferida
BUILDS = {
    "Guerreiro":  ((9, 7, 5, 5, 1, 3), "IronSword", None),
    "Tanque":     ((6, 12, 4, 3, 1, 4), "IronAxe", None),
    "Ágil":       ((6, 4, 7, 10, 1, 2), "Hatchet", None),
    "Arqueiro":   ((2, 5, 10, 8, 1, 4), "LongBow", None),
    "Mago":       ((1, 5, 5, 4, 9, 6), "ArcaneStaff", "Ember"),
    "Equilibrado":((5, 5, 5, 5, 5, 5), "WoodenStaff", "Ember"),
}

# ----------------------------------------------------------------------------- unidade
class U:
    def __init__(self, name, attrs, weapon, spell, team, x, P, fp=3, beast=False):
        self.P = P
        self.name, self.team, self.x, self.fp, self.beast = name, team, x, fp, beast
        self.S, self.V, self.D, self.A, self.I, self.W = attrs
        self.weapon, self.spell = weapon, spell
        w = P["weapons"].get(weapon) or WEAPONS.get(weapon)
        if beast and P["beast_weapon"]:
            d_, t_, r_, ti_ = P["beast_weapon"]; w = (d_, t_, r_, ti_, 0, 1.0, 0, "STR")
        if w:
            self.wdmg, self.wticks, self.wrange, self.tiles, self.hard, self.conduit, self.sbonus, self.scale = w
        else:   # desarmado (garras das feras)
            self.wdmg, self.wticks, self.wrange, self.tiles, self.hard, self.conduit, self.sbonus, self.scale = (
                P["unarmed_damage"], P["unarmed_ticks"], 0, 1, 0, 1.0, 0, "STR")
        if beast and fp >= 4: self.wrange = max(self.wrange, 1)          # fera grande: alcance 2 (base 1 + 1)
        self.range = P["base_range"] + self.wrange
        self.maxhp = round(P["hp_base"] + self.V * P["hp_per_vit"])
        self.hp = self.maxhp
        self.maxmana = round(P["mana_base"] + self.W * P["mana_per_wis"])
        self.mana = self.maxmana
        self.maxst = max(1, round(P["stamina_base"] + (self.S + self.V + self.D) * P["stamina_per_body"]))
        self.st = self.maxst
        attr = self.S if self.scale == "STR" else self.D
        self.per_tile = self.wdmg * (1 + attr * P["dmg_pct_per_attr"])
        self.hit = min(1.0, P["base_hit"] + self.D * P["hit_per_dex"])
        self.crit = self.D * P["crit_per_dex"]
        self.dodge = min(0.9, self.A * P["dodge_per_agi"])
        self.dodge_tiles = max(1, int(self.A) // P["dodge_agi_per_tile"])
        self.defense = round(self.V * P["armor_per_vit"])
        self.mdef = round(self.W * P["magic_resist_per_wis"])
        self.block = round(self.V * P["block_flat_per_vit"]) + self.hard
        self.move = max(1, min(P["move_cap"], round(P["move_base"] + self.A * P["move_per_agi"])))
        self.st_regen = max(1, P["stamina_regen_ticks"] - max(0, int(self.A) - P["agi_regen_from"]) // P["agi_regen_step"])
        self.mana_regen = max(1, P["mana_regen_ticks"] - int(self.W) // P["wis_regen_step"])
        self.busy = 0; self.action = None; self.burn = (0, 0); self.cast_taken = 0
        self.dmg_dealt = 0

    def ticks(self, base, P):
        return max(1, round(base * P["tick_dex_factor"] / (P["tick_dex_factor"] + self.D)))

    def gap(self, o):
        return abs(self.x - o.x) - (self.fp + o.fp) / 2

    @property
    def dead(self): return self.hp <= 0

def SP(P, name): return P["spells"].get(name) or SPELLS[name]

def spell_amount(u, P):
    c, t, r, base, pot, burn, pierce = SP(P, u.spell)
    return round((base * P["spell_base_mult"] + u.I * P["spell_pot_per_int"] * pot) * u.conduit)

def spell_reach(u, P):
    c, t, r, *_ = SP(P, u.spell)
    return round(r * P["spell_range_mult"] + u.I * P["spell_range_per_int"]) + u.sbonus

def take(u, dmg):
    u.hp -= dmg
    if u.action and u.action[0] == "spell": u.cast_taken += dmg

def react(t, P, vs_spell, rng):
    """IA da reação: bloqueia se VIT > AGI (só contra arma), senão esquiva. Devolve (tiles tirados, bloqueio)."""
    if t.dead: return 0, 0
    want_block = (t.V > t.A) and not vs_spell
    cost = P["react_block"] if want_block else P["react_dodge"]
    if t.st < cost: return 0, 0
    t.st -= cost; t.busy += P["reaction_ticks"]
    return (0, t.block) if want_block else (t.dodge_tiles, 0)

def weapon_hit(a, t, P, rng, power):
    if rng.random() >= a.hit: return
    crit = rng.random() < a.crit
    tiles = a.tiles
    if t.fp < 3: tiles = min(a.tiles, -(-tiles * 9 // (t.fp * t.fp)))   # alvo pequeno não esconde o corpo
    removed, block = react(t, P, False, rng)
    natural = not removed and not crit and rng.random() < t.dodge
    if P["dodge_rule"] == "frac" and (removed or natural):
        frac = min(P["dodge_frac_cap"], t.A * P["dodge_frac_per_agi"])
        if tiles <= 1: removed = 1 if rng.random() < frac else 0      # golpe de 1 tile: chance de errar
        else: removed = max(1, round(tiles * frac))
    elif natural: removed = t.dodge_tiles
    left = tiles - min(tiles, removed)
    if left <= 0: return
    raw = a.per_tile * left * (P["power_mult"] if power else 1)
    raw = raw + rng.randint(-round(raw * P["variance"]), round(raw * P["variance"]))
    dmg = max(P["min_damage"], round(raw) - t.defense)
    if crit: dmg = round(dmg * P["crit_mul"])
    dmg = max(0, dmg - block)
    take(t, dmg); a.dmg_dealt += dmg

def spell_hit(a, t, P, rng):
    c, tk, r, base, pot, burn, pierce = SP(P, a.spell)
    removed, _ = react(t, P, True, rng)
    if P["dodge_rule"] == "frac" and removed: removed = max(1, round(9 * min(P["dodge_frac_cap"], t.A * P["dodge_frac_per_agi"])))
    left = 9 - min(9, removed)
    if left <= 0: return
    dmg = max(P["min_damage"], round(spell_amount(a, P) * left / 9) - round(t.mdef * (1 - pierce)))
    take(t, dmg); a.dmg_dealt += dmg
    if burn: t.burn = (burn[0] * P["spell_tick_mult"], max(t.burn[1], burn[1]))

def decide(u, foes, P):
    alive = [f for f in foes if not f.dead]
    if not alive: return None
    tgt = min(alive, key=lambda f: (u.gap(f), f.hp))
    if u.spell:
        c, tk, r, *_ = SP(P, u.spell)
        if u.mana >= c and u.gap(tgt) <= spell_reach(u, P):
            u.mana -= c; u.cast_taken = 0
            return ("spell", tgt, u.ticks(tk, P))
        if u.mana < c and u.gap(tgt) > u.range:
            return ("conc", None, u.ticks(P["ticks_concentrate"], P))
    if u.gap(tgt) > u.range:
        return ("move", tgt, 1)
    power = u.st >= P["power_cost"] + 1
    if power: u.st -= P["power_cost"]
    return ("atk", tgt, u.ticks(u.wticks, P) + (P["power_extra_ticks"] if power else 0), power)

def fight(team_a, team_b, P, rng, max_ticks=80):
    us = team_a + team_b
    for t in range(1, max_ticks + 1):
        for u in us:
            if u.dead: continue
            if t % u.st_regen == 0: u.st = min(u.maxst, u.st + 1)
            if t % u.mana_regen == 0: u.mana = min(u.maxmana, u.mana + 1)
            if u.burn[1] > 0: take(u, u.burn[0]); u.burn = (u.burn[0], u.burn[1] - 1)
        order = sorted([u for u in us if not u.dead], key=lambda u: -(u.A + u.D + rng.random()))
        for u in order:
            if u.dead: continue
            foes = team_b if u in team_a else team_a
            if u.busy > 0:
                u.busy -= 1
                if u.busy > 0: continue
                act = u.action; u.action = None
                if act is None: continue
                kind, tgt = act[0], act[1]
                if kind == "move" and tgt is not None:
                    step = min(u.move, max(0, u.gap(tgt) - u.range))
                    u.x += step if tgt.x > u.x else -step
                elif kind == "atk" and tgt and not tgt.dead and u.gap(tgt) <= u.range:
                    weapon_hit(u, tgt, P, rng, act[3])
                elif kind == "spell" and tgt and not tgt.dead:
                    if u.cast_taken < max(1, -(-u.maxhp * P["interrupt_frac"] // 1)):
                        spell_hit(u, tgt, P, rng)
                elif kind == "conc":
                    u.mana = min(u.maxmana, u.mana + P["concentrate_mana"])
                continue
            act = decide(u, foes, P)
            if act is None: continue
            u.action = act; u.busy = act[2]
        if all(x.dead for x in team_a) or all(x.dead for x in team_b):
            return (0 if all(x.dead for x in team_b) else 1), t
    ha = sum(max(0, x.hp) / x.maxhp for x in team_a); hb = sum(max(0, x.hp) / x.maxhp for x in team_b)
    return (0 if ha >= hb else 1), max_ticks

def make(name, P, team, x):
    attrs, w, s = BUILDS[name]
    return U(name, attrs, w, s, team, x, P)

def beast(size, P, team, x, layer=1, budget=30):
    pts = max(6.0, budget * (P["beast_budget_frac"] + 0.10 * (layer - 1)))
    mul = P["beast_mul"][size]
    attrs = (round(pts * .28 * mul), round(pts * .30 * mul), round(pts * .14), round(pts * .18), 1, max(1, round(pts * .10)))
    return U(["Pequena", "Média", "Grande"][size], attrs, "Claws", None, team, x, P, fp=(1, 2, 4)[size], beast=True)

# ----------------------------------------------------------------------------- relatórios
def report(P, N=400, seed=7, label="ATUAL"):
    rng = random.Random(seed)
    names = list(BUILDS)
    teams = list(itertools.combinations_with_replacement(names, 2))
    wins = {n: [0, 0] for n in names}; lengths = []
    for ta, tb in itertools.combinations(teams, 2):
        for k in range(N // 8):
            A = [make(ta[0], P, 0, 2), make(ta[1], P, 0, 2)]
            B = [make(tb[0], P, 1, 26), make(tb[1], P, 1, 26)]
            if k % 2: A, B = B, A
            w, t = fight(A, B, P, rng)
            lengths.append(t)
            win_team = A if w == 0 else B
            for u in A + B:
                wins[u.name][1] += 1
                if u in win_team: wins[u.name][0] += 1
    out = [f"== {label}: 2 contra 2 (todas as duplas contra todas) =="]
    out.append(f"{'build':<12}{'vitórias':>9}   atributos STR/VIT/DEX/AGI/INT/WIS · arma")
    rates = {}
    for n in names:
        r = 100 * wins[n][0] / max(1, wins[n][1]); rates[n] = r
        a, w, s = BUILDS[n]
        out.append(f"{n:<12}{r:8.0f}%   {'/'.join(map(str, a))} · {w}{' + ' + s if s else ''}")
    out.append(f"duração média {statistics.mean(lengths):.1f} ticks (mediana {statistics.median(lengths):.0f})")

    # duelo de armas, atributos iguais (5 em tudo), 1v1
    wr = {w: [0, 0] for w in WEAPONS}
    for wa, wb in itertools.permutations(WEAPONS, 2):
        for k in range(N // 4):
            a = U(wa, (5, 5, 5, 5, 5, 5), wa, None, 0, 2, P); b = U(wb, (5, 5, 5, 5, 5, 5), wb, None, 1, 26, P)
            w, _ = fight([a], [b], P, rng)
            wr[wa][1] += 1; wr[wb][1] += 1
            wr[wa if w == 0 else wb][0] += 1
    out.append("duelo de armas (atributos 5 em tudo, 1v1): " + " · ".join(f"{w} {100*v[0]/v[1]:.0f}%" for w, v in wr.items()))

    # feras: jogador Equilibrado (espada de ferro) contra uma fera de cada tamanho
    for size in range(3):
        won = 0; lost = []
        for k in range(N // 2):
            pl = U("Jogador", (5, 5, 5, 5, 5, 5), "IronSword", None, 0, 2, P)
            bs = beast(size, P, 1, 26)
            w, _ = fight([pl], [bs], P, rng)
            won += (w == 0); lost.append(1 - max(0, pl.hp) / pl.maxhp)
        b = beast(size, P, 1, 0)
        out.append(f"fera {b.name:<8} (VIT {b.V}, vida {b.maxhp}, {b.per_tile:.1f}/golpe): jogador vence {100*won/(N//2):.0f}%, perde {100*statistics.mean(lost):.0f}% da vida")
    return "\n".join(out), rates

PROPOSAL = dict(   # busca automática (2026-11-13) contra as 4 metas; ver GDD-020 §13
    dodge_rule="frac",            # esquiva tira uma fração dos tiles (AGI×6%, até 60%); golpe de 1 tile vira chance de errar
    spell_base_mult=2.25,         # era 2
    spell_pot_per_int=2.5,        # era 3
    spells={"Ember": (2, 3, 4, 4, 1.0, (2, 3), 0.0), "ArcaneFlame": (4, 3, 5, 5, 1.3, None, 0.5)},  # +1 tick de conjuração
    move_base=4, move_cap=8,          # andada era 2 + AGI×0,67 até 6
    armor_per_vit=0.5,            # era 0,25
    block_flat_per_vit=0.5,
    hp_base=30,
    beast_weapon=(6, 2, 0, 3),      # garras de verdade (dano/tile, ticks, alcance extra, tiles); antes: desarmado
    beast_budget_frac=0.4, beast_mul=(1, 2, 3.0),
    weapons={"Hatchet": [5, 1, 1, 3, 2, 1.0, 0, "STR"], "IronAxe": [6, 3, 2, 6, 2, 1.0, 0, "STR"], "WoodenSword": [11, 2, 3, 2, 3, 1.0, 0, "STR"], "IronSword": [6, 2, 4, 3, 3, 1.0, 0, "STR"], "WoodenStaff": [4, 2, 7, 3, 2, 1.1, 0, "DEX"], "Scepter": [3, 2, 6, 4, 2, 1.15, 1, "DEX"], "ApprenticeWand": [4, 1, 5, 4, 1, 1.0, 0, "DEX"], "ArcaneStaff": [4, 2, 7, 3, 2, 1.3, 2, "DEX"], "ShortBow": [13, 1, 8, 1, 0, 1.0, 0, "DEX"], "LongBow": [19, 3, 10, 1, 0, 1.0, 0, "DEX"]},
)

if __name__ == "__main__":
    P = tuning()
    txt, _ = report(P)
    print(txt)
    if "--proposal" in sys.argv:
        print()
        txt2, _ = report(tuning(**PROPOSAL), label="PROPOSTA")
        print(txt2)
