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
        private static Sprite _cellSprite;

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
                sr.sprite = CellSprite(); sr.color = col; sr.sortingOrder = 4900;
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

        private static Sprite CellSprite()
        {
            if (_cellSprite != null) return _cellSprite;
            var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color[64];
            for (int i = 0; i < 64; i++) px[i] = Color.white;
            tex.SetPixels(px); tex.Apply();
            _cellSprite = Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 8f);
            return _cellSprite;
        }
    }

    /// <summary>
    /// Resolve as magias do catálogo (GDD-019). Chamado pelo RoundManager no tick em que a
    /// conjuração termina; o recurso já foi gasto ao montar a fila. Devolve as linhas do histórico.
    /// </summary>
    public static class SpellResolver
    {
        /// <summary>Potência mágica: INT (GDD-019 §1).</summary>
        public static float Pot(Unit u) => u.stats.INT;

        /// <summary>Alcance efetivo da magia (embotamento tira tiles; Salto usa a andada × multiplicador).</summary>
        public static int Reach(Unit u, SpellDef s)
        {
            if (s.effect == SpellEffect.Leap) return Mathf.Max(1, Mathf.RoundToInt(u.EffectiveMoveStep * s.multiplier));
            if (s.range <= 0) return s.range;
            return u.IsDulled ? Mathf.Max(1, s.range - Tuning.Get().dullRangePenalty) : s.range;
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
                case SpellEffect.PrepTrueAim:
                    caster.trueAimCrit = Mathf.Max(0.0001f, s.critBonus);
                    log.Add($"{head}: o próximo ataque não erra (+{Mathf.RoundToInt(s.critBonus * 100)}% crítico)");
                    return log;
                case SpellEffect.PrepStoneSkin:
                    caster.stoneSkinTicks = Mathf.Max(1, Tuning.Get().ticksPerRound);
                    caster.stoneSkinMult = s.multiplier;
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
            int fp = caster.stats.Footprint;
            var center = ps.TargetTile + new Vector2Int(fp / 2, fp / 2);
            float pot = Pot(caster);
            int amount = s.Amount(pot);

            switch (s.effect)
            {
                case SpellEffect.Foresight:
                    caster.foresightTicks = Mathf.Max(caster.foresightTicks, s.durationTicks);
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
                        if (u.IsDead || u.IsHostileTo(caster) || !InArea(u, center, s.area)) continue;
                        int before = u.currentHP;
                        u.currentHP = Mathf.Min(u.stats.MaxHP, u.currentHP + amount);
                        u.burningTicks = 0; u.soakedTicks = 0;
                        healed++;
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
                    var z = new SpellZone { Kind = s.effect, TicksLeft = s.durationTicks, TickDamage = s.tickDamage, Caster = caster, Name = s.displayName };
                    for (int dx = -s.area; dx <= s.area; dx++)
                    for (int dy = -s.area; dy <= s.area; dy++)
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
            var targets = new List<Unit>();
            if (s.area > 0)
            {
                foreach (var u in all)
                    if (!u.IsDead && u.IsHostileTo(caster) && InArea(u, center, s.area)) targets.Add(u);
            }
            else
            {
                Unit t = UnitAt(ps.TargetTile, fp, all);
                // Alvo por unidade (GDD-018 UX 1): se saiu do tile mas segue ao alcance, a magia o segue.
                if (t == null && ps.TargetUnit != null && !ps.TargetUnit.IsDead &&
                    GridManager.FootprintGap(caster.anchor, fp, ps.TargetUnit.anchor, ps.TargetUnit.stats.Footprint) <= Reach(caster, s))
                    t = ps.TargetUnit;
                if (t != null && t != caster && t.IsHostileTo(caster)) targets.Add(t);
            }
            if (targets.Count == 0) { log.Add($"{head}: <color=#888888>sem alvo</color>"); return log; }

            // Projétil: Muralha no caminho absorve; Tempestade de areia faz errar 50%.
            if (s.area == 0 && field != null && targets.Count == 1)
            {
                var t = targets[0];
                var b = field.BarrierOnLine(caster.anchor, t.anchor);
                if (b != null)   // bloqueia projéteis de todos, inclusive do conjurador (GDD-019 §3.2)
                {
                    b.Hp -= amount;
                    log.Add($"{head}: acerta a {b.Name} ({Mathf.Max(0, b.Hp)} HP)");
                    if (b.Hp <= 0) { field.Remove(b); log.Add($"   a {b.Name} desmorona"); }
                    return log;
                }
                if (field.SandstormOnLine(caster.anchor, t.anchor) && BattleRng.Roll10000() < 5000)
                { log.Add($"{head}: <color=#888888>perdeu-se na tempestade de areia</color>"); return log; }
            }

            foreach (var t in targets)
            {
                if (s.effect == SpellEffect.DrainMana)
                {
                    int drained = Mathf.Min(t.currentMana, Mathf.Max(0, amount));
                    t.currentMana -= drained;
                    caster.GainMana(drained / 2);
                    log.Add($"{head}: drena {drained} de Mana de {t.unitName} (+{drained / 2} para {caster.unitName})");
                    continue;
                }

                float pierce = SpellCatalog.ArmorPierce(s);
                int def = Mathf.RoundToInt(t.stats.MagicDefense * (1f - pierce));
                int dmg = Mathf.Max(Tuning.Get().minDamageFloor, amount - def);
                dmg = StoneSkin(t, dmg, physical: false);
                t.TakeDamage(dmg);
                string hp = t.IsDead ? "<color=#ff6666>MORTO</color>" : $"{t.currentHP}/{t.stats.MaxHP} HP";
                string extra = "";

                if (!t.IsDead && s.appliesStatus == "Burning")
                { t.burningTicks = Mathf.Max(t.burningTicks, s.durationTicks); t.burningDamage = Mathf.Max(t.burningDamage, s.tickDamage); extra += " · Queimando"; }
                if (!t.IsDead && s.appliesStatus == "Soaked")
                { t.soakedTicks = Mathf.Max(t.soakedTicks, s.durationTicks); if (t.burningTicks > 0) { t.burningTicks = 0; extra += " · apagou o fogo"; } extra += " · Encharcado"; }
                if (!t.IsDead && s.effect == SpellEffect.CancelMove && t.queue.Count > 0 && t.queue[0].Type == ActionType.Move)
                { t.queue.RemoveAt(0); extra += " · movimento cancelado"; }
                if (!t.IsDead && s.pushTiles > 0)
                {
                    int tiles = s.pushTiles + ((s.effect == SpellEffect.Push && pot >= 8f) ? 1 : 0);
                    var from = s.area > 0 ? center - new Vector2Int(t.stats.Footprint / 2, t.stats.Footprint / 2) : caster.anchor;
                    int moved = Push(t, from, tiles, all, grid, field);
                    if (moved > 0) extra += $" · empurrado {moved}";
                }
                log.Add($"{head} -> {t.unitName}: {dmg}{extra}  ({hp})");
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

        /// <summary>Pele de pedra: redução percentual ×N do alvo (teto 90%). Só dano físico.</summary>
        public static int StoneSkin(Unit target, int dmg, bool physical)
        {
            if (!physical || target.stoneSkinTicks <= 0) return dmg;
            float red = Mathf.Min(0.9f, target.stats.PhysicalReduction * target.stoneSkinMult);
            return Mathf.Max(Tuning.Get().minDamageFloor, Mathf.RoundToInt(dmg * (1f - red)));
        }

        // ---------------------------------------------------------------- helpers

        private static bool InArea(Unit u, Vector2Int center, int r) =>
            GridManager.FootprintGap(u.anchor, u.stats.Footprint, center, 1) <= r;

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
