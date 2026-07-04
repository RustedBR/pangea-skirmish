using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// VFX de magia: projétil em arco, impacto, aura de buff e efeito de tile.
    /// Carrega sprites do kit BDragon1727 (Resources.LoadAll — sheets fatiados).
    /// Sempre com fallback gerado (círculo tingido) para nunca ficar invisível se
    /// um path estiver errado. Logs de debug controlados por GameTuning.spellVfxDebugLogs.
    /// </summary>
    public static class SpellVfx
    {
        private static readonly Dictionary<string, Sprite[]> _cache = new Dictionary<string, Sprite[]>();
        private static Sprite _fallbackCircle;

        private static void DLog(string msg)
        {
            if (Tuning.Get().spellVfxDebugLogs) Debug.Log("[SpellVfx] " + msg);
        }

        // ── Carregamento de frames ─────────────────────────────
        private static Sprite[] LoadFrames(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_cache.TryGetValue(path, out var cached)) return cached;

            var frames = Resources.LoadAll<Sprite>(path);
            if (frames == null || frames.Length == 0)
            {
                var single = Resources.Load<Sprite>(path);
                frames = single != null ? new[] { single } : null;
            }
            _cache[path] = frames;
            DLog($"LoadFrames('{path}') -> {(frames == null ? "NULL (usando fallback)" : frames.Length + " frame(s)")}");
            return frames;
        }

        private static Sprite FallbackCircle()
        {
            if (_fallbackCircle != null) return _fallbackCircle;
            const int S = 24;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            float r = S * 0.5f;
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = x - r + 0.5f, dy = y - r + 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / r;
                // Núcleo sólido + borda suave
                float a = d <= 0.75f ? 1f : (d <= 1f ? 1f - (d - 0.75f) / 0.25f : 0f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            _fallbackCircle = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 32f);
            return _fallbackCircle;
        }

        private static string SheetForElement(SpellElement e)
        {
            var T = Tuning.Get();
            switch (e)
            {
                case SpellElement.Physical: return T.projectileSheetPhysical;
                case SpellElement.Magic:    return T.projectileSheetMagic;
                case SpellElement.Fire:     return T.projectileSheetFire;
                case SpellElement.Water:    return T.projectileSheetWater;
                case SpellElement.Air:      return T.projectileSheetAir;
                case SpellElement.Earth:    return T.projectileSheetEarth;
                default:                    return null;
            }
        }

        private static GameObject MakeVfx(string name, Vector3 pos, Color tint,
            Sprite[] frames, int sortingOrder, float scale, out SpriteRenderer sr, out bool usingFallback)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * scale;
            sr = go.AddComponent<SpriteRenderer>();
            usingFallback = frames == null || frames.Length == 0;
            if (!usingFallback)
            {
                sr.sprite = frames[0];
                sr.color = Color.white;
            }
            else
            {
                sr.sprite = FallbackCircle();
                sr.color = tint;
            }
            sr.sortingOrder = sortingOrder;
            return go;
        }

        // ── Projétil (Unit) ────────────────────────────────────
        public static IEnumerator PlayProjectile(Unit caster, Unit target, SpellElement element, List<string> log)
        {
            if (caster == null || target == null)
            {
                DLog("PlayProjectile abortado: caster/target null");
                yield break;
            }
            Vector3 from = caster.HeadWorld;
            Vector3 to = target.HeadWorld;
            DLog($"Projétil {caster.unitName} -> {target.unitName} ({SpellBook.ElementName(element)}) de {from} para {to}");
            yield return FlyArc(from, to, element, $"SpellProj_{element}");
        }

        private static IEnumerator FlyArc(Vector3 from, Vector3 to, SpellElement element, string name)
        {
            var T = Tuning.Get();
            var frames = LoadFrames(SheetForElement(element));
            Color tint = SpellBook.ElementColor(element);
            var go = MakeVfx(name, from, tint, frames, 20000, T.spellProjectileScale, out var sr, out bool fb);

            // Os sheets de bullet têm variantes de COR do mesmo projétil. Usar um frame
            // FIXO evita o projétil "piscar" entre cores; spellProjectileFrame = -1 anima tudo.
            int frameIdx = T.spellProjectileFrame;
            if (!fb && frameIdx >= 0 && frames.Length > 0)
                sr.sprite = frames[Mathf.Clamp(frameIdx, 0, frames.Length - 1)];

            float dist = Vector3.Distance(from, to);
            float speed = Mathf.Max(0.01f, T.spellProjectileSpeed);
            float dur = Mathf.Max(0.05f, dist / speed);
            float peak = T.spellProjectileArc * Mathf.Min(dist, 8f) * 0.25f;
            float fps = Mathf.Max(1f, T.spellVfxFps);

            // Aponta o sprite na direção do voo
            Vector3 d = to - from;
            if (d.sqrMagnitude > 0.0001f)
                go.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);

            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / dur);
                Vector3 pos = Vector3.Lerp(from, to, p);
                pos.y += Mathf.Sin(p * Mathf.PI) * peak;
                go.transform.position = pos;
                // Só cicla frames se o índice fixo estiver desligado (-1).
                if (!fb && frameIdx < 0 && frames.Length > 1)
                    sr.sprite = frames[(int)(t * fps) % frames.Length];
                yield return null;
            }
            Object.Destroy(go);
        }

        // ── Impacto (explosão no alvo) ─────────────────────────
        public static IEnumerator PlayImpact(Unit target, SpellElement element, List<string> log)
        {
            if (target == null) { DLog("PlayImpact abortado: target null"); yield break; }
            DLog($"Impacto em {target.unitName} ({SpellBook.ElementName(element)})");
            yield return Burst(target.HeadWorld, Tuning.Get().impactSheetPath, element, "SpellImpact", rise: 0f, frameStart: Tuning.Get().impactFrameStart, frameCount: Tuning.Get().impactFrameCount);
        }

        // ── Aura de buff (Self) ────────────────────────────────
        public static IEnumerator PlaySelfBuff(Unit caster, SpellElement element, List<string> log)
        {
            if (caster == null) { DLog("PlaySelfBuff abortado: caster null"); yield break; }
            DLog($"Aura de buff em {caster.unitName} ({SpellBook.ElementName(element)})");
            yield return Burst(caster.HeadWorld, Tuning.Get().auraSheetPath, element, "SpellAura", rise: 0.5f, frameStart: Tuning.Get().auraFrameStart, frameCount: Tuning.Get().auraFrameCount);
        }

        // ── Efeito no tile ─────────────────────────────────────
        public static IEnumerator PlayTileVfx(Vector3 worldPos, SpellElement element, List<string> log)
        {
            DLog($"VFX de tile em {worldPos} ({SpellBook.ElementName(element)})");
            yield return Burst(worldPos, Tuning.Get().impactSheetPath, element, "SpellTileVfx", rise: 0f, frameStart: Tuning.Get().impactFrameStart, frameCount: Tuning.Get().impactFrameCount);
        }

        /// <summary>Explosão/aura estática: toca os frames uma vez, cresce um pouco, opcional sobe, e some.</summary>
        private static IEnumerator Burst(Vector3 pos, string sheetPath, SpellElement element, string name, float rise, int frameStart, int frameCount)
        {
            var T = Tuning.Get();
            var frames = LoadFrames(sheetPath);
            Color tint = SpellBook.ElementColor(element);
            var go = MakeVfx(name, pos, tint, frames, 20001, T.spellVfxScale, out var sr, out bool fb);

            float fps = Mathf.Max(1f, T.spellVfxFps);
            int startFrame = 0;
            int win;
            if (!fb && frames.Length > 0)
            {
                startFrame = Mathf.Clamp(frameStart, 0, frames.Length - 1);
                win = (frameCount <= 0) ? frames.Length - startFrame : Mathf.Min(frameCount, frames.Length - startFrame);
                if (win < 1) win = 1;
            }
            else win = 6;
            float dur = win / fps;
            Vector3 start = pos;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / dur);
                if (!fb && frames.Length > 0)
                    sr.sprite = frames[startFrame + Mathf.Min(win - 1, (int)(t * fps))];
                else
                {
                    // Fallback: pulso que cresce e some
                    float s = T.spellVfxScale * (0.6f + p);
                    go.transform.localScale = Vector3.one * s;
                    var c = tint; c.a = 1f - p; sr.color = c;
                }
                if (rise > 0f) go.transform.position = start + Vector3.up * (rise * p);
                yield return null;
            }
            Object.Destroy(go);
        }
    }
}
