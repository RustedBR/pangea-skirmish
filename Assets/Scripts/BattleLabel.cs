using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    public class BattleLabel : MonoBehaviour
    {
        public const int SortOrderBase = 21000;

        private TextMesh _text;
        private SpriteRenderer _bg;
        private Camera _cam;

        private static readonly Dictionary<(Color32, Color32), Sprite> _bgCache = new();

        // ── Stack de labels ativos (empilhamento) ────────────
        private static readonly List<BattleLabel> _activeLabels = new();
        private Vector3 _basePos;

        private static float FindTopY(Vector3 basePos)
        {
            float topY = basePos.y;
            for (int i = _activeLabels.Count - 1; i >= 0; i--)
            {
                var lbl = _activeLabels[i];
                if (lbl == null) { _activeLabels.RemoveAt(i); continue; }
                float dist = Vector3.Distance(lbl._basePos, basePos);
                if (dist > Tuning.Get().battleLabelStackRadius) continue;
                if (lbl._topY > topY)
                    topY = lbl._topY;
            }
            return topY;
        }

        private float _topY;

        private static void Register(BattleLabel label) => _activeLabels.Add(label);

        private void OnDestroy()
        {
            _activeLabels.Remove(this);
        }

        // ── Core factory ─────────────────────────────────────

        public static BattleLabel Create(Camera cam, Vector3 worldPos, string text,
            Color textColor, Color32 bgColor, Color32 borderColor, float scale = 1f)
        {
            float topY = FindTopY(worldPos);
            var t = Tuning.Get();
            float spacing = t.battleLabelStackSpacing;
            float newY = topY + spacing;
            Vector3 finalPos = new Vector3(worldPos.x, newY, worldPos.z);

            var go = new GameObject("BattleLabel");
            go.transform.position = finalPos;
            var label = go.AddComponent<BattleLabel>();
            label._cam = cam;
            label._basePos = worldPos;
            label._topY = newY;
            Register(label);

            var bgGo = new GameObject("Bg");
            bgGo.transform.SetParent(go.transform, false);
            var bg = bgGo.AddComponent<SpriteRenderer>();
            bg.sprite = GetBgSprite(bgColor, borderColor);
            bg.sortingOrder = SortOrderBase;
            float charW = t.battleLabelCharWidth * scale;
            float pad = t.battleLabelBgPadding * scale;
            bgGo.transform.localScale = new Vector3(text.Length * charW + pad, t.battleLabelBgHeight * scale, 1f);
            label._bg = bg;

            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = Mathf.RoundToInt(t.battleLabelFontSize * scale);
            tm.characterSize = t.battleLabelCharacterSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = textColor;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = font.material;
            mr.sortingOrder = SortOrderBase + 1;
            label._text = tm;

            label.FaceCamera();
            return label;
        }

        // ── Type factories ───────────────────────────────────

        public static BattleLabel CreateAttack(Camera cam, Vector3 pos, string unitName)
        {
            var t = Tuning.Get();
            float scale = 1f;
            int maxChars = Mathf.Max(3, t.attackLabelMaxNameChars);
            string txt = unitName.Length > maxChars ? unitName[..maxChars] + ".." : unitName;
            var label = Create(cam, pos + Vector3.up * t.battleLabelHeightOffset, $"⚔ {txt}",
                t.attackLabelTextColor,
                new Color32(0x05, 0x02, 0x00, 255),
                new Color32(0x88, 0x44, 0x08, 255),
                scale);
            float dur = t.attackLabelDuration;
            label.StartCoroutine(label.AnimateRiseAndFade(dur, dur * t.attackLabelFadeRatio));
            return label;
        }

        public static BattleLabel CreateDamage(Camera cam, Vector3 pos, int damage, bool isCritical)
        {
            var t = Tuning.Get();
            if (isCritical)
            {
                float d = t.critRiseDuration;
                var label = Create(cam, pos + Vector3.up * t.battleLabelHeightOffset, $"-{damage}!",
                    t.critLabelTextColor,
                    new Color32(0x05, 0x02, 0x00, 255),
                    new Color32(0xCC, 0x77, 0x00, 255),
                    scale: t.critLabelScale);
                label.StartCoroutine(label.AnimateRiseAndFade(0f, d));
                return label;
            }
            float nd = t.damageRiseDuration;
            var normalLabel = Create(cam, pos + Vector3.up * t.battleLabelHeightOffset, $"-{damage}",
                t.damageLabelTextColor,
                new Color32(0x05, 0x01, 0x01, 255),
                new Color32(0x99, 0x22, 0x22, 255));
            normalLabel.StartCoroutine(normalLabel.AnimateRiseAndFade(0f, nd));
            return normalLabel;
        }

        public static BattleLabel CreateMiss(Camera cam, Vector3 pos)
        {
            var t = Tuning.Get();
            float d = t.missRiseDuration;
            var label = Create(cam, pos + Vector3.up * t.missLabelHeightOffset, "MISS",
                t.missLabelTextColor,
                new Color32(0x05, 0x05, 0x05, 255),
                new Color32(0x66, 0x66, 0x66, 255));
            label.StartCoroutine(label.AnimateRiseAndFade(0f, d));
            return label;
        }

        public static BattleLabel CreateSequence(Camera cam, Vector3 pos, string num, Color textColor, Color32 bgColor)
        {
            var go = new GameObject("BattleLabel");
            go.transform.position = pos;
            var label = go.AddComponent<BattleLabel>();
            label._cam = cam;
            label._basePos = pos;
            label._topY = pos.y;
            // NÃO registra — sequence labels não participam do stack de dano

            var bgGo = new GameObject("Bg");
            bgGo.transform.SetParent(go.transform, false);
            var bg = bgGo.AddComponent<SpriteRenderer>();
            bg.sprite = GetBgSprite(bgColor, new Color32(0x22, 0x22, 0x22, 255));
            bg.sortingOrder = SortOrderBase;
            var tSeq = Tuning.Get();
            float seqScale = tSeq.seqLabelScale;
            float charW = tSeq.battleLabelCharWidth * seqScale;
            float pad = tSeq.battleLabelBgPadding * seqScale;
            bgGo.transform.localScale = new Vector3(num.Length * charW + pad, tSeq.battleLabelBgHeight * seqScale, 1f);
            label._bg = bg;

            var tm = go.AddComponent<TextMesh>();
            tm.text = num;
            tm.fontSize = Mathf.RoundToInt(tSeq.battleLabelFontSize * seqScale);
            tm.characterSize = tSeq.battleLabelCharacterSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = textColor;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = font.material;
            mr.sortingOrder = SortOrderBase + 1;
            label._text = tm;

            label.FaceCamera();
            return label;
        }

        // ── Background sprite cache ──────────────────────────
        // Chip estilizado: cantos arredondados (raio 8px) + borda 2px + fundo
        // semi-transparente da cor original. Mantém a cor semântica de cada label
        // (vermelho dano, dourado crítico, verde move, etc) mas aplica o MESMO
        // design language dos botões pg-button (borda 2px + raio + fundo translúcido).
        private static Sprite GetBgSprite(Color32 bg, Color32 border)
        {
            var key = (bg, border);
            if (_bgCache.TryGetValue(key, out var sprite))
                return sprite;

            const int W = 64, H = 32;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };

            // Fundo semi-transparente (alpha da cor original, mas garantido >= .82 p/ legibilidade)
            Color bgCol = (Color)bg;
            bgCol.a = Mathf.Max(bgCol.a, 0.82f);
            Color borderCol = (Color)border;

            int radius = 8;               // raio dos cantos (igual pg-button ~6-8px)
            int borderW = 2;              // espessura da borda (igual pg-button)

            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                // Distância ao centro do canto mais próximo (p/ arredondar)
                int cx = (x < radius) ? radius : (x >= W - radius ? W - 1 - radius : -1);
                int cy = (y < radius) ? radius : (y >= H - radius ? H - 1 - radius : -1);
                bool outside = false;
                if (cx >= 0 && cy >= 0)
                {
                    int dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy > radius * radius) outside = true;
                }
                if (outside) { tex.SetPixel(x, y, Color.clear); continue; }

                // Borda: pixels a <= borderW do limite externo (considerando cantos)
                bool isBorder = false;
                if (x < borderW || x >= W - borderW || y < borderW || y >= H - borderW)
                    isBorder = true;
                // Ajuste de borda nos cantos arredondados
                if (cx >= 0 && cy >= 0)
                {
                    int dx = x - cx, dy = y - cy;
                    int dist = Mathf.RoundToInt(Mathf.Sqrt(dx * dx + dy * dy));
                    if (dist > radius - borderW && dist <= radius) isBorder = true;
                }

                tex.SetPixel(x, y, isBorder ? borderCol : bgCol);
            }
            tex.Apply();
            var s = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 32);
            s.name = $"Bg_{bg}_{border}";
            _bgCache[key] = s;
            return s;
        }

        // ── Animação ──────────────────────────────────────────

        private System.Collections.IEnumerator AnimateRiseAndFade(float riseHeight, float duration)
        {
            Vector3 start = transform.position;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // Rise
                transform.position = start + Vector3.up * (riseHeight * t);

                // Fade (text + bg)
                float alpha = 1f - t;
                if (_text != null)
                {
                    Color c = _text.color;
                    c.a = alpha;
                    _text.color = c;
                }
                if (_bg != null)
                {
                    Color c = _bg.color;
                    c.a = alpha;
                    _bg.color = c;
                }

                yield return null;
            }

            Destroy(gameObject);
        }

        // ── Helpers ──────────────────────────────────────────

        public void SetText(string t) { if (_text != null) _text.text = t; }
        public void SetColor(Color c) { if (_text != null) _text.color = c; }
        public void Dismiss() => Destroy(gameObject);

        private void LateUpdate()
        {
            FaceCamera();
        }

        private void FaceCamera()
        {
            if (_cam == null) return;
            // Fix (2026-07-20): billboard completo (não só yaw) — ver InitiativeTag.cs
            // / BillboardFace.cs para o porquê (câmera reto de cima).
            Quaternion parentRot = transform.parent != null ? transform.parent.rotation : Quaternion.identity;
            transform.rotation = Quaternion.Inverse(parentRot) * _cam.transform.rotation;
        }
    }
}
