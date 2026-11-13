using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Efeitos 3D das ações (GDD-019): projéteis, impactos, a alma que desce. Só visual — nada
    /// aqui altera o estado do jogo, então não entra na simulação determinística do lockstep.
    /// Os modelos vêm de Resources/Models/Base (scripts/gen_models.py).
    /// </summary>
    public class ActionFx : MonoBehaviour
    {
        private static ActionFx _runner;
        /// <summary>Quantos efeitos foram disparados desde o último reset (testes).</summary>
        public static int Spawned { get; private set; }
        /// <summary>Nome do último modelo de efeito disparado (testes).</summary>
        public static string LastModel { get; private set; }
        public static void ResetCounters() { Spawned = 0; LastModel = null; }

        private static ActionFx Runner
        {
            get
            {
                if (_runner == null)
                {
                    var go = new GameObject("ActionFx");
                    _runner = go.AddComponent<ActionFx>();
                }
                return _runner;
            }
        }

        private static readonly Dictionary<string, Color> _tintColors = new Dictionary<string, Color>();

        /// <summary>Pinta o material "element" (e clareia o miolo) com a cor do elemento.</summary>
        public static void Tint(Transform root, Color c)
        {
            _tintColors.Clear();
            _tintColors["element"] = c;
            _tintColors["element_core"] = Color.Lerp(c, Color.white, 0.75f);
            World3D.Paint(root, _tintColors, Color.white);
        }

        /// <summary>Modelo de projétil/impacto de cada elemento.</summary>
        public static string ModelFor(SpellElement e)
        {
            switch (e)
            {
                case SpellElement.Fire:  return "Flame";
                case SpellElement.Water: return "WaterDrop";
                case SpellElement.Earth: return "RockShard";
                case SpellElement.Air:   return "Gust";
                default:                 return "SpellOrb";
            }
        }

        /// <summary>
        /// Lança um modelo de <paramref name="from"/> a <paramref name="to"/> em <paramref name="duration"/> s,
        /// com arco opcional; some ao chegar. <paramref name="tint"/> pinta o material "element".
        /// </summary>
        public static void Projectile(string model, Vector3 from, Vector3 to, float duration, Color? tint,
                                      float arc = 0f, float scale = 1f, System.Action onArrive = null)
        {
            if (!Application.isPlaying || !World3D.Has(model)) { onArrive?.Invoke(); return; }
            var go = World3D.Spawn(model, Runner.transform);
            if (go == null) { onArrive?.Invoke(); return; }
            Spawned++; LastModel = model;
            if (tint.HasValue) Tint(go.transform, tint.Value);
            Runner.StartCoroutine(Runner.Fly(go, from, to, Mathf.Max(0.03f, duration), arc, scale, onArrive));
        }

        /// <summary>Impacto no lugar: o modelo cresce, gira e encolhe.</summary>
        public static void Burst(string model, Vector3 at, float duration, Color? tint, float scale = 1f, float rise = 0.2f)
        {
            if (!Application.isPlaying || !World3D.Has(model)) return;
            var go = World3D.Spawn(model, Runner.transform);
            if (go == null) return;
            Spawned++; LastModel = model;
            if (tint.HasValue) Tint(go.transform, tint.Value);
            Runner.StartCoroutine(Runner.Pop(go, at, Mathf.Max(0.05f, duration), scale, rise));
        }

        /// <summary>A alma sai da cabeça, paira um instante e desce para dentro do planeta (GDD-013).</summary>
        public static void SoulDescent(Vector3 head, float scale = 1f)
        {
            AudioManager.I?.PlayNamed("sfx_soul", null, 0.8f);
            if (!Application.isPlaying || !World3D.Has("Soul")) return;
            var go = World3D.Spawn("Soul", Runner.transform);
            if (go == null) return;
            Spawned++; LastModel = "Soul";
            Runner.StartCoroutine(Runner.Soul(go, head, scale));
        }

        private IEnumerator Fly(GameObject go, Vector3 from, Vector3 to, float dur, float arc, float scale, System.Action onArrive)
        {
            var t = go.transform;
            t.localScale = Vector3.one * scale;
            Vector3 prev = from;
            for (float e = 0f; e < dur; e += Time.deltaTime)
            {
                float k = e / dur;
                var p = Vector3.Lerp(from, to, k);
                p.y += Mathf.Sin(k * Mathf.PI) * arc;
                t.position = p;
                var d = p - prev;
                if (d.sqrMagnitude > 1e-8f) t.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
                prev = p;
                yield return null;
            }
            Destroy(go);
            onArrive?.Invoke();
        }

        private IEnumerator Pop(GameObject go, Vector3 at, float dur, float scale, float rise)
        {
            var t = go.transform;
            float spin = Random.Range(0f, 360f);
            for (float e = 0f; e < dur; e += Time.deltaTime)
            {
                float k = e / dur;
                float s = Mathf.Sin(k * Mathf.PI);                       // cresce e encolhe
                t.position = at + Vector3.up * rise * k;
                t.rotation = Quaternion.Euler(0f, spin + k * 200f, 0f);
                t.localScale = Vector3.one * scale * Mathf.Lerp(0.2f, 1.25f, s);
                yield return null;
            }
            Destroy(go);
        }

        private IEnumerator Soul(GameObject go, Vector3 head, float scale)
        {
            var t = go.transform;
            // 1) sai da cabeça e paira
            for (float e = 0f; e < 0.45f; e += Time.deltaTime)
            {
                float k = e / 0.45f;
                t.position = head + Vector3.up * 0.35f * scale * k;
                t.localScale = Vector3.one * scale * Mathf.Lerp(0.2f, 1f, k);
                t.rotation = Quaternion.Euler(0f, 135f + Mathf.Sin(e * 9f) * 12f, 0f);
                yield return null;
            }
            // 2) desce para dentro do planeta, acelerando
            Vector3 top = t.position;
            for (float e = 0f; e < 0.9f; e += Time.deltaTime)
            {
                float k = e / 0.9f;
                t.position = top + Vector3.down * (k * k) * 3.2f * scale;
                t.localScale = Vector3.one * scale * Mathf.Lerp(1f, 0.55f, k);
                yield return null;
            }
            Destroy(go);
        }

        // ---------------------------------------------------------------- marcas no chão

        /// <summary>Marcas de chão criadas desde o último reset (testes).</summary>
        public static int GroundMarks { get; private set; }
        public static void ResetGroundMarks() => GroundMarks = 0;
        public static IEnumerable<Vector2Int> FootprintCells(Vector2Int anchor, int fp)
        {
            for (int dx = 0; dx < fp; dx++)
            for (int dy = 0; dy < fp; dy++)
                yield return new Vector2Int(anchor.x + dx, anchor.y + dy);
        }

        /// <summary>
        /// Acende células da grade na cor dada, pulsa e apaga em <paramref name="duration"/> s.
        /// É o "onde aconteceu" de um ataque ou magia enquanto a ação é executada.
        /// </summary>
        public static void GroundFlash(GridManager grid, IEnumerable<Vector2Int> cells, Color color, float duration = 0.8f, float peakAlpha = 0.70f)
        {
            if (grid == null || cells == null || !Application.isPlaying) return;
            var quads = new List<SpriteRenderer>();
            foreach (var c in cells)
            {
                if (!grid.IsAnchorInBounds(c, 1)) continue;
                var g = new GameObject("GroundMark");
                g.transform.SetParent(Runner.transform, false);
                var sr = g.AddComponent<SpriteRenderer>();
                sr.sprite = World3D.CellDiamondSprite(); sr.sortingOrder = 4950;
                sr.color = new Color(color.r, color.g, color.b, 0f);
                g.transform.localScale = new Vector3(1f, World3D.GroundStretch, 1f);
                g.transform.rotation = World3D.GroundRotation;
                GridAnchored.Attach(g, c, 1);
                g.SetActive(true);
                quads.Add(sr);
                GroundMarks++;
            }
            if (quads.Count > 0) Runner.StartCoroutine(Runner.FlashQuads(quads, color, Mathf.Max(0.15f, duration), peakAlpha));
        }

        private IEnumerator FlashQuads(List<SpriteRenderer> quads, Color color, float dur, float peak)
        {
            for (float e = 0f; e < dur; e += Time.deltaTime)
            {
                float k = e / dur;
                // sobe rápido, dois pulsos, some
                float a = (k < 0.12f ? k / 0.12f : 1f - (k - 0.12f) / 0.88f) * (0.75f + 0.25f * Mathf.Cos(k * Mathf.PI * 4f));
                var col = new Color(color.r, color.g, color.b, Mathf.Clamp01(a) * peak);
                foreach (var q in quads) if (q != null) q.color = col;
                yield return null;
            }
            foreach (var q in quads) if (q != null) Destroy(q.gameObject);
        }

        /// <summary>
        /// Marca onde um ataque acontece: as células do alvo (vermelho) e, à distância, a trilha
        /// do golpe do atacante até ele (claro). Ataque em tile marca a área golpeada.
        /// </summary>
        public static void MarkAttack(GridManager grid, Unit attacker, PlannedAttack atk)
        {
            if (grid == null || attacker == null) return;
            int afp = attacker.stats.Footprint;
            // GDD-020: marca exatamente os tiles que a FORMA da arma pega.
            Vector2Int aim;
            if (atk.Mode == AttackMode.Unit && atk.TargetUnit != null && !atk.TargetUnit.IsDead)
                aim = AttackShape.Center(atk.TargetUnit.anchor, atk.TargetUnit.stats.Footprint);
            else if (atk.Mode == AttackMode.Tile) aim = atk.TargetTile;
            else return;

            var cells = AttackResolver.CellsFor(attacker, aim);
            Unit tgt = atk.Mode == AttackMode.Unit ? atk.TargetUnit : null;
            // Marcas mais longas para dar tempo de ler (pedido 2026-11-13): 1,9 s ataques.
            float dur = Tuning.Get().attackMarkSeconds;
            GroundFlash(grid, cells, new Color(1f, 0.26f, 0.20f), dur, 0.72f);

            // Trilha: só quando há chão entre os dois (ataque à distância ou de alcance longo).
            var from = attacker.anchor + new Vector2Int(afp / 2, afp / 2);
            var trail = new List<Vector2Int>();
            foreach (var c in SpellField.LineCells(from, aim, includeEnds: false))
            {
                if (GridManager.FootprintsOverlap(attacker.anchor, afp, c, 1) || cells.Contains(c)) continue;
                if (tgt != null && GridManager.FootprintsOverlap(tgt.anchor, tgt.stats.Footprint, c, 1)) continue;   // dentro do alvo não é trilha
                trail.Add(c);
            }
            if (trail.Count > 0) GroundFlash(grid, trail, new Color(1f, 0.90f, 0.75f), dur * 0.75f, 0.45f);
        }

        /// <summary>Marca onde uma magia cai: a área (ou o alvo) na cor do elemento, e a trilha até lá.</summary>
        public static void MarkSpell(GridManager grid, Unit caster, PlannedSpell ps)
        {
            var s = ps.Def;
            if (grid == null || caster == null || s == null) return;
            if (s.range < 0 && s.effect != SpellEffect.Leap) return;          // sem alvo no chão
            if (s.IsPhysical && s.effect != SpellEffect.Leap) return;
            int fp = caster.stats.Footprint;
            Color col = ElementInfo.Color(s.PrimaryElement);
            var center = ps.TargetTile + new Vector2Int(fp / 2, fp / 2);
            var cells = new List<Vector2Int>();
            if (s.effect == SpellEffect.Barrier) return;                       // a própria Muralha já marca o chão
            if (s.area > 0)
            {
                int r = SpellResolver.AreaRadius(s);
                for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++) cells.Add(center + new Vector2Int(dx, dy));
            }
            else cells.AddRange(FootprintCells(ps.TargetTile, fp));
            // 2,4 s para magias (pedido 2026-11-13).
            float dur = Tuning.Get().spellMarkSeconds;
            GroundFlash(grid, cells, col, dur, 0.75f);

            var from = caster.anchor + new Vector2Int(fp / 2, fp / 2);
            var trail = new List<Vector2Int>();
            foreach (var c in SpellField.LineCells(from, center, includeEnds: false))
            {
                if (GridManager.FootprintsOverlap(caster.anchor, fp, c, 1) || cells.Contains(c)) continue;
                trail.Add(c);
            }
            if (trail.Count > 0) GroundFlash(grid, trail, Color.Lerp(col, Color.white, 0.5f), dur * 0.7f, 0.40f);
        }

        // ---------------------------------------------------------------- magias

        /// <summary>
        /// Visual de uma magia resolvendo (chamado pelo RoundManager junto do SpellResolver):
        /// pose de soltar, projétil na cor do elemento e impacto no alvo, ou efeito no próprio corpo.
        /// </summary>
        /// <summary>Som de uma magia: pelo efeito (Salto, Muralha, Física) ou pelo elemento principal.</summary>
        public static string SfxFor(SpellDef s)
        {
            if (s == null) return null;
            if (s.effect == SpellEffect.Leap) return "sfx_leap";
            if (s.IsPhysical) return "sfx_flex";
            if (s.effect == SpellEffect.Barrier) return "sfx_wall";
            switch (s.PrimaryElement)
            {
                case SpellElement.Fire:  return "sfx_spell_fire";
                case SpellElement.Water: return "sfx_spell_water";
                case SpellElement.Earth: return "sfx_spell_earth";
                case SpellElement.Air:   return "sfx_spell_air";
                default:                 return "sfx_spell_magic";
            }
        }

        public static void PlaySpell(Unit caster, PlannedSpell ps, GridManager grid)
        {
            var s = ps.Def;
            if (s == null || caster == null || !Application.isPlaying) return;
            AudioManager.I?.PlayNamed(SfxFor(s));
            var model = caster.Model3D;
            Color col = ElementInfo.Color(s.PrimaryElement);
            float sc = model != null ? model.ModelScale : 1f;
            Vector3 muzzle = model != null ? model.MuzzleWorld : caster.transform.position + Vector3.up * 0.6f;

            if (s.IsPhysical)
            {
                if (s.effect != SpellEffect.Leap && model != null) model.PlayFlex();
                if (s.effect != SpellEffect.Leap)
                    Burst("SpellOrb", caster.transform.position + Vector3.up * 0.5f * sc, 0.45f, col, sc * 1.6f, 0.5f * sc);
                return;
            }

            if (model != null) model.PlayCastRelease(col);
            if (s.range < 0 || grid == null)        // sem alvo (Visão arcana): pulso no conjurador
            {
                Burst("SpellOrb", muzzle, 0.5f, col, sc * 1.4f, 0.4f * sc);
                return;
            }

            int fp = caster.stats.Footprint;
            Vector3 target = grid.AnchorToWorldCenter(ps.TargetTile, fp);
            string proj = ModelFor(s.PrimaryElement);
            string second = s.IsPair ? ModelFor(s.elements[1]) : null;
            Color? tint = proj == "SpellOrb" ? col : (Color?)null;

            switch (s.effect)
            {
                case SpellEffect.Teleport:
                    Burst("Gust", muzzle, 0.4f, null, sc * 1.4f);
                    Burst("SpellOrb", target + Vector3.up * 0.4f * sc, 0.5f, col, sc * 1.5f, 0.5f * sc);
                    return;
                case SpellEffect.Heal:
                    Projectile("WaterDrop", muzzle, target + Vector3.up * 0.4f * sc, 0.22f, null, 0.5f, sc,
                               () => Burst("HealLeaf", target, 0.8f, null, sc * 2.2f, 0.5f * sc));
                    return;
                case SpellEffect.Barrier:
                case SpellEffect.ZoneLava:
                case SpellEffect.ZoneMud:
                case SpellEffect.ZoneSteam:
                case SpellEffect.ZoneSandstorm:
                    Projectile(proj, muzzle, target + Vector3.up * 0.1f, 0.25f, tint, 0.7f, sc,
                               () => { if (second != null) Burst(second, target, 0.6f, second == "SpellOrb" ? col : (Color?)null, sc * 2.4f, 0.3f * sc); });
                    return;
            }

            // Dano / empurrão / cancelar / drenar: projétil reto e impacto. Par mostra o segundo elemento no impacto.
            float area = 1f + s.area * 0.9f;
            Projectile(proj, muzzle, target + Vector3.up * 0.45f * sc, 0.20f, tint, 0.05f, sc * (s.area > 0 ? 1.3f : 1f), () =>
            {
                Burst(proj, target + Vector3.up * 0.2f * sc, 0.45f, tint, sc * 1.8f * area, 0.3f * sc);
                if (second != null) Burst(second, target + Vector3.up * 0.3f * sc, 0.55f, second == "SpellOrb" ? col : (Color?)null, sc * 1.4f * area, 0.5f * sc);
            });
        }
    }
}
