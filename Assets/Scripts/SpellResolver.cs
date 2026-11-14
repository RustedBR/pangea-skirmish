using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>Zona de magia no chão (Vapor, Magma, Lama, Tempestade de areia, Muralha). GDD-019 §3.2.</summary>
    public class SpellZone
    {
        public SpellEffect Kind;
        public readonly List<Vector2Int> Cells = new List<Vector2Int>();
        public int TicksLeft;
        public int TickDamage;
        public int Hp;              // Muralha
        public Unit Caster;
        public string Name;
        public readonly List<GameObject> Visuals = new List<GameObject>();

        public bool Contains(Vector2Int cell) => Cells.Contains(cell);

        /// <summary>A unidade (footprint inteiro) pisa em alguma célula da zona?</summary>
        public bool Touches(Unit u)
        {
            if (u == null) return false;
            int fp = u.stats.Footprint;
            foreach (var c in Cells)
                if (GridManager.FootprintsOverlap(u.anchor, fp, c, 1)) return true;
            return false;
        }
    }

    /// <summary>
    /// Estado das magias no campo (zonas). Um por batalha, criado pelo RoundManager.
    /// Determinístico: só depende da ordem das unidades e do BattleRng.
    /// </summary>
    public class SpellField
    {
        public static SpellField Current;
        public readonly List<SpellZone> Zones = new List<SpellZone>();

        public bool InZone(Unit u, SpellEffect kind)
        {
            foreach (var z in Zones) if (z.Kind == kind && z.Touches(u)) return true;
            return false;
        }

        public SpellZone BarrierAt(Vector2Int cell)
        {
            foreach (var z in Zones) if (z.Kind == SpellEffect.Barrier && z.Contains(cell)) return z;
            return null;
        }

        /// <summary>Primeira Muralha que a linha de 'from' a 'to' cruza (sem contar as pontas), ou null.</summary>
        public SpellZone BarrierOnLine(Vector2Int from, Vector2Int to)
        {
            foreach (var c in LineCells(from, to, includeEnds: false))
            {
                var b = BarrierAt(c);
                if (b != null) return b;
            }
            return null;
        }

        /// <summary>A linha cruza (ou começa/termina em) uma Tempestade de areia?</summary>
        public bool SandstormOnLine(Vector2Int from, Vector2Int to)
        {
            foreach (var c in LineCells(from, to, includeEnds: true))
                foreach (var z in Zones)
                    if (z.Kind == SpellEffect.ZoneSandstorm && z.Contains(c)) return true;
            return false;
        }

        public static IEnumerable<Vector2Int> LineCells(Vector2Int a, Vector2Int b, bool includeEnds)
        {
            int n = Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
            for (int i = includeEnds ? 0 : 1; i <= (includeEnds ? n : n - 1); i++)
            {
                float t = n == 0 ? 0f : (float)i / n;
                yield return new Vector2Int(Mathf.RoundToInt(Mathf.Lerp(a.x, b.x, t)), Mathf.RoundToInt(Mathf.Lerp(a.y, b.y, t)));
            }
        }

        public void Add(SpellZone z, GridManager grid)
        {
            Zones.Add(z);
            if (grid == null) return;
            var col = ZoneColor(z.Kind);
            foreach (var c in z.Cells)
            {
                if (!grid.IsAnchorInBounds(c, 1)) continue;
                if (z.Kind == SpellEffect.Barrier && World3D.Has("WallBlock"))
                {
                    var holder = new GameObject($"Zone_{z.Name}");
                    GridAnchored.Attach(holder, c, 1);
                    var wall = World3D.Spawn("WallBlock", holder.transform);
                    if (wall != null)
                    {
                        // variação determinística por célula, para a muralha não parecer carimbada
                        wall.transform.localRotation = Quaternion.Euler(0f, 90f * ((c.x * 7 + c.y * 13) & 3), 0f);
                        wall.transform.localScale = new Vector3(1f, 0.9f + 0.1f * ((c.x + c.y) & 1), 1f);
                        holder.SetActive(true);
                        z.Visuals.Add(holder);
                        continue;
                    }
                    Object.Destroy(holder);
                }
                var g = new GameObject($"Zone_{z.Name}");
                var sr = g.AddComponent<SpriteRenderer>();
                sr.sprite = World3D.CellDiamondSprite(); sr.color = col; sr.sortingOrder = 4900;
                g.transform.localScale = new Vector3(1f, World3D.GroundStretch, 1f);
                g.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                GridAnchored.Attach(g, c, 1);
                g.SetActive(true);
                z.Visuals.Add(g);
            }
        }

        public void Remove(SpellZone z)
        {
            foreach (var g in z.Visuals) if (g != null) Object.Destroy(g);
            z.Visuals.Clear();
            Zones.Remove(z);
        }

        public void Clear()
        {
            foreach (var z in new List<SpellZone>(Zones)) Remove(z);
        }

        private static Color ZoneColor(SpellEffect k)
        {
            switch (k)
            {
                case SpellEffect.ZoneLava:      return new Color(1.00f, 0.35f, 0.10f, 0.55f);
                case SpellEffect.ZoneMud:       return new Color(0.45f, 0.32f, 0.18f, 0.55f);
                case SpellEffect.ZoneSteam:     return new Color(0.90f, 0.92f, 0.95f, 0.45f);
                case SpellEffect.ZoneSandstorm: return new Color(0.85f, 0.72f, 0.40f, 0.45f);
                case SpellEffect.Barrier:       return new Color(0.55f, 0.45f, 0.80f, 0.80f);
                default:                        return new Color(1f, 1f, 1f, 0.4f);
            }
        }
    }

    /// <summary>
    /// Resolve as magias do catálogo (GDD-019). Chamado pelo RoundManager no tick em que a
    /// conjuração termina; o recurso já foi gasto ao montar a fila. Devolve as linhas do histórico.
    /// </summary>
    public static class SpellResolver
    {
        /// <summary>Potência mágica: INT × spellPotPerINT (GDD-019 §1).</summary>
        public static float Pot(Unit u) => u.stats.INT * Tuning.Get().spellPotPerINT;

        /// <summary>Valor principal da magia para este conjurador (dano, cura, HP da Muralha).</summary>
        public static int Amount(Unit u, SpellDef s)
        {
            var w = WeaponCatalog.Get(u.weaponId);
            float conduit = (w != null && !s.IsPhysical) ? Mathf.Max(0.1f, w.spellPowerMult) : 1f;   // cajados reforçam magia
            return Mathf.RoundToInt((s.baseAmount * Tuning.Get().spellBaseMult + Pot(u) * s.potFactor) * conduit);
        }

        /// <summary>
        /// Descrição da magia com os números reais para esta unidade (o catálogo guarda o texto com
        /// marcadores: {amount} {tick} {dur} {reach} {area} {push} {mult} {crit} {wall} {drain} {skin} {skinEx}).
        /// </summary>
        public static string Describe(Unit u, SpellDef s)
        {
            if (s == null) return "";
            var T = Tuning.Get();
            int r = AreaRadius(s), side = r * 2 + 1;
            string txt = s.description ?? "";
            txt = txt.Replace("{amount}", u != null ? Amount(u, s).ToString() : s.baseAmount.ToString())
                     .Replace("{tick}", TickDamage(s).ToString())
                     .Replace("{dur}", s.durationTicks.ToString())
                     .Replace("{reach}", u != null ? Reach(u, s).ToString() : s.range.ToString())
                     .Replace("{area}", $"{side}×{side}")
                     .Replace("{push}", s.pushTiles.ToString())
                     .Replace("{mult}", s.multiplier.ToString("0.#"))
                     .Replace("{crit}", Mathf.RoundToInt(s.critBonus * 100).ToString())
                     .Replace("{wall}", (Mathf.Max(1, s.area) * AttributeStats.DefaultFootprint).ToString())
                     .Replace("{drain}", u != null ? Mathf.Max(0, s.Amount(u.stats.INT)).ToString() : "?")
                     .Replace("{skin}", Mathf.Max(1, T.ticksPerRound).ToString())
                     .Replace("{skinEx}", Mathf.RoundToInt(5 * Mathf.Max(1f, s.multiplier)).ToString());
            return txt;
        }

        /// <summary>Dano por tick (Queimando, Magma) já com o multiplicador do tuning.</summary>
        public static int TickDamage(SpellDef s) => Mathf.RoundToInt(s.tickDamage * Tuning.Get().spellTickDamageMult);

        /// <summary>Raio da área em células da grade (uma unidade ocupa 3).</summary>
        public static int AreaRadius(SpellDef s) => s.area <= 0 ? 0 : Mathf.Max(1, Mathf.RoundToInt(s.area * Tuning.Get().spellAreaMult));

        /// <summary>
        /// Alcance efetivo em células: base × spellRangeMult + INT × spellRangePerINT; o embotamento
        /// tira células. O Salto usa a andada × multiplicador.
        /// </summary>
        public static int Reach(Unit u, SpellDef s)
        {
            if (s.effect == SpellEffect.Leap) return Mathf.Max(1, Mathf.RoundToInt(u.EffectiveMoveStep * s.multiplier));
            if (s.range <= 0) return s.range;
            var T = Tuning.Get();
            var w = WeaponCatalog.Get(u.weaponId);
            int r = Mathf.RoundToInt(s.range * T.spellRangeMult + u.stats.INT * T.spellRangePerINT) + (w != null ? w.spellRangeBonus : 0)
                  + (Equipment.HasEffect(u, ItemEffect.Conduit) ? 1 : 0);
            return u.IsDulled ? Mathf.Max(1, r - T.dullRangePenalty) : Mathf.Max(1, r);
        }

        /// <summary>Limite de dano que interrompe a conjuração Elemental (GDD-019 §1).</summary>
        public static int InterruptThreshold(Unit u) =>
            Mathf.Max(1, Mathf.CeilToInt(u.stats.MaxHP * Tuning.Get().spellInterruptHpFraction));

        public static List<string> Resolve(Unit caster, PlannedSpell ps, List<Unit> all, GridManager grid, SpellField field)
        {
            var log = new List<string>();
            var s = ps.Def;
            if (s == null || caster == null || caster.IsDead) return log;
            string head = $"<color=#c9b8ff>◈</color> {caster.unitName} — <b>{s.displayName}</b> [{s.MontageText()}]";

            // ---------------- Física: preparos (consomem-se depois) ----------------
            switch (s.effect)
            {
                case SpellEffect.PrepFury:
                    caster.furyReady = s.multiplier;
                    log.Add($"{head}: o próximo ataque causa dano ×{s.multiplier:0.#}");
                    return log;
                case SpellEffect.PrepCurve:
                    caster.curveReady = true;
                    BattleFeedback.Status(caster, "Curva pronta", new Color(0.55f, 0.85f, 1f));
                    log.Add($"{head}: o próximo ataque à distância segue o alvo e contorna obstáculos");
                    return log;
                case SpellEffect.PrepTrueAim:
                    caster.trueAimCrit = Mathf.Max(0.0001f, s.critBonus);
                    log.Add($"{head}: o próximo ataque não erra (+{Mathf.RoundToInt(s.critBonus * 100)}% crítico)");
                    return log;
                case SpellEffect.PrepStoneSkin:
                    caster.stoneSkinTicks = Mathf.Max(1, Tuning.Get().ticksPerRound) + Mathf.Max(0, s.durationTicks);   // + incremento Efeito
                    caster.stoneSkinMult = s.multiplier;
                    BattleFeedback.Status(caster, "Pele de pedra", new Color(0.86f, 0.80f, 0.70f));
                    log.Add($"{head}: redução de dano ×{s.multiplier:0.#} por {caster.stoneSkinTicks} ticks");
                    return log;
                case SpellEffect.Leap:
                {
                    var dest = ps.TargetTile;
                    if (!FreeFor(caster, dest, all, grid, field))
                    { log.Add($"{head}: <color=#888888>sem espaço para pousar</color>"); return log; }
                    if (GridManager.FootprintGap(caster.anchor, caster.stats.Footprint, dest, caster.stats.Footprint) > Reach(caster, s))
                    { log.Add($"{head}: <color=#888888>longe demais</color>"); return log; }
                    caster.TeleportTo(dest);
                    log.Add($"{head}: salta por cima de tudo, sem ataque de oportunidade");
                    return log;
                }
            }

            // ---------------- Elemental ----------------
            // Curva (magia física preparada): a magia segue a unidade mirada e contorna obstáculos.
            bool curved = caster.curveReady && s.range > 0 && ps.TargetUnit != null && !ps.TargetUnit.IsDead
                          && (s.effect == SpellEffect.Damage || s.effect == SpellEffect.Push || s.effect == SpellEffect.CancelMove);
            if (curved)
            {
                ps.TargetTile = ps.TargetUnit.anchor;
                caster.curveReady = false;
                BattleFeedback.Status(caster, "Curva!", new Color(0.55f, 0.85f, 1f));
            }
            int fp = caster.stats.Footprint;
            var center = AimCenter(caster, ps);
            float potInt = caster.stats.INT;                 // limiares por INT (ex.: Rajada empurra +1 com INT ≥ 8)
            int amount = Amount(caster, s);
            int radius = AreaRadius(s);

            switch (s.effect)
            {
                case SpellEffect.Foresight:
                    caster.foresightTicks = Mathf.Max(caster.foresightTicks, s.durationTicks);
                    BattleFeedback.Status(caster, "Visão arcana", new Color(0.78f, 0.62f, 1f));
                    log.Add($"{head}: vê as filas inimigas +2 ticks à frente por {s.durationTicks} ticks");
                    return log;

                case SpellEffect.Teleport:
                {
                    var dest = ps.TargetTile;
                    if (!FreeFor(caster, dest, all, grid, field)) { log.Add($"{head}: <color=#888888>tile ocupado</color>"); return log; }
                    caster.TeleportTo(dest);
                    log.Add($"{head}: some e reaparece, sem ataque de oportunidade");
                    return log;
                }

                case SpellEffect.Heal:
                {
                    int healed = 0;
                    foreach (var u in all)
                    {
                        if (u.IsDead || u.IsHostileTo(caster) || !InArea(u, center, radius)) continue;
                        int before = u.currentHP;
                        u.currentHP = Mathf.Min(u.stats.MaxHP, u.currentHP + amount);
                        u.burningTicks = 0; u.soakedTicks = 0;
                        healed++;
                        BattleFeedback.Heal(u, u.currentHP - before);
                        log.Add($"{head}: {u.unitName} +{u.currentHP - before} HP ({u.currentHP}/{u.stats.MaxHP})");
                    }
                    if (healed == 0) log.Add($"{head}: <color=#888888>nenhum aliado na área</color>");
                    return log;
                }

                case SpellEffect.ZoneLava:
                case SpellEffect.ZoneMud:
                case SpellEffect.ZoneSteam:
                case SpellEffect.ZoneSandstorm:
                {
                    var z = new SpellZone { Kind = s.effect, TicksLeft = s.durationTicks, TickDamage = TickDamage(s), Caster = caster, Name = s.displayName };
                    for (int dx = -radius; dx <= radius; dx++)
                    for (int dy = -radius; dy <= radius; dy++)
                        z.Cells.Add(center + new Vector2Int(dx, dy));
                    field?.Add(z, grid);
                    log.Add($"{head}: zona de {z.Cells.Count} células por {s.durationTicks} ticks");
                    return log;
                }

                case SpellEffect.Barrier:
                {
                    var dir = new Vector2Int(System.Math.Sign(center.x - (caster.anchor.x + fp / 2)), System.Math.Sign(center.y - (caster.anchor.y + fp / 2)));
                    if (dir == Vector2Int.zero) dir = Vector2Int.up;
                    var perp = new Vector2Int(-dir.y, dir.x);
                    int len = Mathf.Max(1, s.area) * AttributeStats.DefaultFootprint; // "3 tiles" de unidade = 9 células
                    var z = new SpellZone { Kind = SpellEffect.Barrier, TicksLeft = s.durationTicks, Hp = amount, Caster = caster, Name = s.displayName };
                    for (int i = -len / 2; i <= len / 2; i++) z.Cells.Add(center + perp * i);
                    field?.Add(z, grid);
                    log.Add($"{head}: muralha de {z.Cells.Count} células, {z.Hp} HP, {s.durationTicks} ticks");
                    return log;
                }
            }

            // ---------------- Elemental com alvo: Damage / CancelMove / Push / DrainMana ----------------
            var targets = HostileTargets(caster, ps, all);
            if (targets.Count == 0) { log.Add($"{head}: <color=#888888>sem alvo</color>"); return log; }

            // Projétil: Muralha no caminho absorve; Tempestade de areia faz errar 50%.
            if (!curved && s.area == 0 && field != null && targets.Count == 1)
            {
                var t = targets[0];
                var b = field.BarrierOnLine(caster.anchor, t.anchor);
                if (b != null)   // bloqueia projéteis de todos, inclusive do conjurador (GDD-019 §3.2)
                {
                    b.Hp -= amount;
                    BattleFeedback.Absorbed(t, $"{b.Name} absorveu {amount}");
                    log.Add($"{head}: acerta a {b.Name} ({Mathf.Max(0, b.Hp)} HP)");
                    if (b.Hp <= 0) { field.Remove(b); log.Add($"   a {b.Name} desmorona"); }
                    return log;
                }
                if (field.SandstormOnLine(caster.anchor, t.anchor) && BattleRng.Roll10000() < 5000)
                { BattleFeedback.Miss(t); log.Add($"{head}: <color=#888888>perdeu-se na tempestade de areia</color>"); return log; }
            }

            foreach (var t in targets)
            {
                if (s.effect == SpellEffect.DrainMana)
                {
                    // Dreno não usa os multiplicadores de dano: senão esvaziava qualquer reserva num toque.
                    int drained = Mathf.Min(t.currentMana, Mathf.Max(0, s.Amount(caster.stats.INT)));
                    t.currentMana -= drained;
                    caster.GainMana(drained / 2);
                    BattleFeedback.Mana(t, -drained);
                    BattleFeedback.Mana(caster, drained / 2);
                    log.Add($"{head}: drena {drained} de Mana de {t.unitName} (+{drained / 2} para {caster.unitName})");
                    continue;
                }

                // Dano por tile (GDD-020 §2): o valor da magia é o de pegar a unidade inteira (9 tiles);
                // a área conta os tiles dela dentro do quadrado; alvo único pega todos. Esquivar tira tiles.
                int full = t.stats.Footprint * t.stats.Footprint;
                int tiles = s.area > 0 ? TilesInArea(t, center, radius) : full;
                int dodged = t.dodgeReactTiles > 0 && !curved ? AttackResolver.DodgeTilesFor(t, tiles) : 0;
                t.dodgeReactTiles = 0;
                int left = tiles - dodged;
                if (left <= 0)
                {
                    BattleFeedback.Dodge(t);
                    log.Add($"{head} -> {t.unitName}: <color=#55ccff>esquivou</color> ({tiles} tiles)");
                    continue;
                }
                float pierce = SpellCatalog.ArmorPierce(s);
                int def = Mathf.RoundToInt(t.stats.MagicDefense * (1f - pierce));
                int raw = Mathf.RoundToInt(amount * left / (float)full);
                int dmg = Mathf.Max(Tuning.Get().minDamageFloor, raw - def);
                dmg = StoneSkin(t, dmg, physical: false);
                t.TakeDamage(dmg);
                string hp = t.IsDead ? "<color=#ff6666>MORTO</color>" : $"{t.currentHP}/{t.stats.MaxHP} HP";
                string extra = "";

                if (!t.IsDead && s.appliesStatus == "Burning")
                { t.burningTicks = Mathf.Max(t.burningTicks, Equipment.HasEffect(t, ItemEffect.FireResist) ? Mathf.Max(1, s.durationTicks / 2) : s.durationTicks); t.burningDamage = Mathf.Max(t.burningDamage, TickDamage(s)); extra += " · Queimando"; BattleFeedback.Status(t, "Queimando", new Color(1f, 0.55f, 0.20f)); }
                if (!t.IsDead && s.appliesStatus == "Soaked")
                { if (!Equipment.HasEffect(t, ItemEffect.Dry)) t.soakedTicks = Mathf.Max(t.soakedTicks, s.durationTicks); if (t.burningTicks > 0) { t.burningTicks = 0; extra += " · apagou o fogo"; } extra += " · Encharcado"; BattleFeedback.Status(t, "Encharcado", new Color(0.45f, 0.72f, 1f)); }
                if (!t.IsDead && s.effect == SpellEffect.CancelMove)
                {
                    // A andada que termina neste tick também para: a magia resolve antes dela (2026-11-14).
                    bool any = t.moveCancelledThisTick == false && t.pendingMoveThisTick;
                    t.moveCancelledThisTick = true;
                    if (t.queue.Count > 0 && t.queue[0].Type == ActionType.Move) { t.queue.RemoveAt(0); any = true; }
                    if (any) { extra += " · movimento cancelado"; BattleFeedback.Status(t, "Movimento cancelado", new Color(0.80f, 0.70f, 0.55f)); }
                }
                if (!t.IsDead && s.pushTiles > 0)
                {
                    int pushN = s.pushTiles + ((s.effect == SpellEffect.Push && potInt >= 8f) ? 1 : 0);
                    var from = s.area > 0 ? center - new Vector2Int(t.stats.Footprint / 2, t.stats.Footprint / 2) : caster.anchor;
                    int moved = Push(t, from, pushN, all, grid, field);
                    if (moved > 0) { extra += $" · empurrado {moved}"; BattleFeedback.Status(t, "Empurrado", new Color(0.86f, 0.95f, 0.98f)); }
                }
                string tileTag = left < full ? $" ({left}/{full} tiles{(dodged > 0 ? $", −{dodged} esquiva" : "")})" : "";
                log.Add($"{head} -> {t.unitName}: {dmg}{tileTag}{extra}  ({hp})");
            }
            return log;
        }

        // ---------------------------------------------------------------- ticks

        /// <summary>Um tick global: Queimando, lava, timers de status e zonas. Devolve linhas do histórico.</summary>
        public static List<string> TickEffects(List<Unit> all, SpellField field)
        {
            var log = new List<string>();
            foreach (var u in all)
            {
                if (u.IsDead) continue;
                if (u.burningTicks > 0)
                {
                    u.TakeDamage(u.burningDamage);
                    AudioManager.I?.PlayNamed("sfx_burn", null, 0.7f);
                    log.Add($"🔥 {u.unitName} queima: {u.burningDamage}  ({(u.IsDead ? "MORTO" : u.currentHP + "/" + u.stats.MaxHP + " HP")})");
                    u.burningTicks--;
                }
                if (u.soakedTicks > 0) u.soakedTicks--;
                if (u.stoneSkinTicks > 0) u.stoneSkinTicks--;
                if (u.foresightTicks > 0) u.foresightTicks--;
            }
            if (field == null) return log;
            foreach (var z in new List<SpellZone>(field.Zones))
            {
                if (z.Kind == SpellEffect.ZoneLava && z.TickDamage > 0)
                    foreach (var u in all)
                    {
                        if (u.IsDead || u == z.Caster || !z.Touches(u)) continue;
                        u.TakeDamage(z.TickDamage);
                        log.Add($"🌋 {u.unitName} na lava: {z.TickDamage}  ({(u.IsDead ? "MORTO" : u.currentHP + "/" + u.stats.MaxHP + " HP")})");
                    }
                z.TicksLeft--;
                if (z.TicksLeft <= 0) field.Remove(z);
            }
            return log;
        }

        // ---------------------------------------------------------------- Física no ataque

        /// <summary>Fúria: o maior entre o golpe atual e base × multiplicador (não acumula com Golpe poderoso).</summary>
        public static int ApplyFury(Unit attacker, int dmg, int aimDmg)
        {
            if (attacker.furyReady <= 0f) return dmg;
            int fury = Mathf.RoundToInt(attacker.stats.PhysicalDamage * attacker.furyReady) + aimDmg;
            attacker.furyReady = 0f;
            return Mathf.Max(dmg, fury);
        }

        /// <summary>Olho certeiro: não erra e soma crítico. Alvo no Vapor anula (GDD-019 §3.2).</summary>
        public static bool TryTrueAim(Unit attacker, Unit target, out HitResult res)
        {
            res = HitResult.Hit;
            if (attacker.trueAimCrit <= 0f) return false;
            float bonus = attacker.trueAimCrit;
            attacker.trueAimCrit = 0f;
            if (SpellField.Current != null && SpellField.Current.InZone(target, SpellEffect.ZoneSteam)) return false;
            float crit = Mathf.Clamp01(attacker.stats.CritChance + bonus);
            res = BattleRng.Roll10000() < Mathf.RoundToInt(crit * 10000f) ? HitResult.Critical : HitResult.Hit;
            return true;
        }

        /// <summary>Pele de pedra: a defesa fixa da VIT conta ×N (a ×1 já foi aplicada). Só dano físico.</summary>
        public static int StoneSkin(Unit target, int dmg, bool physical)
        {
            if (!physical || target.stoneSkinTicks <= 0) return dmg;
            int extra = Mathf.RoundToInt(target.stats.PhysicalDefense * Mathf.Max(0f, target.stoneSkinMult - 1f));
            return Mathf.Max(Tuning.Get().minDamageFloor, dmg - extra);
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>
        /// Centro da magia (2026-11-14). Alvo travado numa unidade: o centro DELA, onde estiver
        /// (o tamanho é o do alvo, não o de quem conjura). Mirada num tile: o tile + metade de quem conjura.
        /// </summary>
        public static Vector2Int AimCenter(Unit caster, PlannedSpell ps, Vector2Int? targetAnchor = null)
        {
            if (ps.TargetUnit != null && !ps.TargetUnit.IsDead)
                return AttackShape.Center(targetAnchor ?? ps.TargetUnit.anchor, ps.TargetUnit.stats.Footprint);
            int fp = caster != null ? caster.stats.Footprint : AttributeStats.DefaultFootprint;
            return ps.TargetTile + new Vector2Int(fp / 2, fp / 2);
        }

        /// <summary>Células que a magia vai pegar, para a prévia na grade: a área em volta do centro,
        /// ou o corpo inteiro do alvo travado (alvo único pega todos os tiles dele).</summary>
        public static List<Vector2Int> PreviewCells(Unit caster, PlannedSpell ps, Vector2Int? targetAnchor = null)
        {
            var cells = new List<Vector2Int>();
            var s = ps.Def;
            if (s == null) return cells;
            var c = AimCenter(caster, ps, targetAnchor);
            if (s.area > 0)
            {
                int r = AreaRadius(s);
                for (int dx = -r; dx <= r; dx++) for (int dy = -r; dy <= r; dy++) cells.Add(new Vector2Int(c.x + dx, c.y + dy));
                return cells;
            }
            Vector2Int a; int fp;
            if (ps.TargetUnit != null && !ps.TargetUnit.IsDead) { a = targetAnchor ?? ps.TargetUnit.anchor; fp = ps.TargetUnit.stats.Footprint; }
            else { a = ps.TargetTile; fp = caster != null ? caster.stats.Footprint : AttributeStats.DefaultFootprint; }
            for (int x = 0; x < fp; x++) for (int y = 0; y < fp; y++) cells.Add(new Vector2Int(a.x + x, a.y + y));
            return cells;
        }

        private static bool InArea(Unit u, Vector2Int center, int r) =>
            GridManager.FootprintGap(u.anchor, u.stats.Footprint, center, 1) <= r;

        /// <summary>Tiles do footprint da unidade dentro do quadrado centro ± r.</summary>
        public static int TilesInArea(Unit u, Vector2Int center, int r)
        {
            int fp = u.stats.Footprint, n = 0;
            for (int x = u.anchor.x; x < u.anchor.x + fp; x++)
                for (int y = u.anchor.y; y < u.anchor.y + fp; y++)
                    if (System.Math.Abs(x - center.x) <= r && System.Math.Abs(y - center.y) <= r) n++;
            return n;
        }

        /// <summary>Inimigos que a magia de alvo/área vai atingir (sem efeitos). Mesma regra do Resolve.</summary>
        private static List<Unit> HostileTargets(Unit caster, PlannedSpell ps, List<Unit> all)
        {
            var targets = new List<Unit>();
            var s = ps.Def;
            if (s == null) return targets;
            int fp = caster.stats.Footprint;
            var center = AimCenter(caster, ps);
            if (s.area > 0)
            {
                int radius = AreaRadius(s);
                foreach (var u in all)
                    if (!u.IsDead && u.IsHostileTo(caster) && InArea(u, center, radius)) targets.Add(u);
            }
            else
            {
                // Alvo travado (GDD-018 UX 1; 2026-11-14): a magia segue a unidade enquanto ela estiver ao
                // alcance, mesmo que outra pise no tile mirado. Sem trava (ou fora de alcance): quem estiver no tile.
                Unit t = null;
                if (ps.TargetUnit != null && !ps.TargetUnit.IsDead &&
                    GridManager.FootprintGap(caster.anchor, fp, ps.TargetUnit.anchor, ps.TargetUnit.stats.Footprint) <= Reach(caster, s))
                    t = ps.TargetUnit;
                if (t == null && ps.TargetUnit == null) t = UnitAt(ps.TargetTile, fp, all);
                if (t != null && t != caster && t.IsHostileTo(caster)) targets.Add(t);
            }
            return targets;
        }

        /// <summary>Quem vai tomar DANO desta magia (para a reação Esquivar antes de resolver).</summary>
        public static List<Unit> DamageTargets(Unit caster, PlannedSpell ps, List<Unit> all)
        {
            var s = ps.Def;
            if (s == null || s.IsPhysical || caster == null || caster.IsDead) return new List<Unit>();
            switch (s.effect)
            {
                case SpellEffect.Damage: case SpellEffect.Push: case SpellEffect.CancelMove: break;
                default: return new List<Unit>();
            }
            return HostileTargets(caster, ps, all);
        }

        private static Unit UnitAt(Vector2Int anchor, int fp, List<Unit> all)
        {
            foreach (var u in all)
                if (!u.IsDead && GridManager.FootprintsOverlap(u.anchor, u.stats.Footprint, anchor, fp)) return u;
            return null;
        }

        private static bool FreeFor(Unit mover, Vector2Int dest, List<Unit> all, GridManager grid, SpellField field)
        {
            int fp = mover.stats.Footprint;
            if (grid != null && !grid.IsAnchorInBounds(dest, fp)) return false;
            foreach (var u in all)
                if (u != mover && !u.IsDead && GridManager.FootprintsOverlap(u.anchor, u.stats.Footprint, dest, fp)) return false;
            if (field != null)
                foreach (var z in field.Zones)
                    if (z.Kind == SpellEffect.Barrier)
                        foreach (var c in z.Cells)
                            if (GridManager.FootprintsOverlap(dest, fp, c, 1)) return false;
            return true;
        }

        /// <summary>Empurra tile a tile para longe de 'from' até bater em algo. Devolve quantos tiles andou.</summary>
        private static int Push(Unit t, Vector2Int from, int tiles, List<Unit> all, GridManager grid, SpellField field)
        {
            var dir = new Vector2Int(System.Math.Sign(t.anchor.x - from.x), System.Math.Sign(t.anchor.y - from.y));
            if (dir == Vector2Int.zero) return 0;
            int moved = 0;
            for (int i = 0; i < tiles; i++)
            {
                var next = t.anchor + dir;
                if (!FreeFor(t, next, all, grid, field)) break;
                t.TeleportTo(next);
                moved++;
            }
            return moved;
        }
    }
}
