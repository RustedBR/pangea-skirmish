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
            var go = World3D.Spawn(model, null);
            if (go == null) { onArrive?.Invoke(); return; }
            Spawned++; LastModel = model;
            if (tint.HasValue) Tint(go.transform, tint.Value);
            Runner.StartCoroutine(Runner.Fly(go, from, to, Mathf.Max(0.03f, duration), arc, scale, onArrive));
        }

        /// <summary>Impacto no lugar: o modelo cresce, gira e encolhe.</summary>
        public static void Burst(string model, Vector3 at, float duration, Color? tint, float scale = 1f, float rise = 0.2f)
        {
            if (!Application.isPlaying || !World3D.Has(model)) return;
            var go = World3D.Spawn(model, null);
            if (go == null) return;
            Spawned++; LastModel = model;
            if (tint.HasValue) Tint(go.transform, tint.Value);
            Runner.StartCoroutine(Runner.Pop(go, at, Mathf.Max(0.05f, duration), scale, rise));
        }

        /// <summary>A alma sai da cabeça, paira um instante e desce para dentro do planeta (GDD-013).</summary>
        public static void SoulDescent(Vector3 head, float scale = 1f)
        {
            if (!Application.isPlaying || !World3D.Has("Soul")) return;
            var go = World3D.Spawn("Soul", null);
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

        // ---------------------------------------------------------------- magias

        /// <summary>
        /// Visual de uma magia resolvendo (chamado pelo RoundManager junto do SpellResolver):
        /// pose de soltar, projétil na cor do elemento e impacto no alvo, ou efeito no próprio corpo.
        /// </summary>
        public static void PlaySpell(Unit caster, PlannedSpell ps, GridManager grid)
        {
            var s = ps.Def;
            if (s == null || caster == null || !Application.isPlaying) return;
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
