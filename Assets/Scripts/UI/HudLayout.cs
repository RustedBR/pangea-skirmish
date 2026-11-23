using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace PangeaSkirmish.UI
{
    /// <summary>
    /// Layout personalizável da HUD (2026-11-22). Qualquer painel registrado ganha:
    ///  • <b>Alt + arrastar</b>: move o painel. Ao soltar perto de um canto da tela ele encaixa ali
    ///    (segure <b>Shift</b> para posicionar livremente, sem encaixe).
    ///  • <b>Puxador ◢</b> (canto inferior direito, aparece ao passar o mouse; sem Alt): reduz ou amplia o painel.
    ///  • <b>Alt + duplo clique</b>: volta o painel ao lugar e ao tamanho originais.
    /// Posição e escala ficam em PlayerPrefs, em FRAÇÃO da tela (nunca px fixo), e sobrevivem entre sessões.
    /// Aditivo: enquanto o jogador não mexe, o painel segue o USS original.
    /// </summary>
    public static class HudLayout
    {
        private const float SnapMargin = 0.006f;     // folga do canto, fração da tela
        private const float SnapRangeX = 0.14f;      // até onde (fração da largura) o canto "puxa"
        private const float SnapRangeY = 0.18f;      // idem na altura
        private const float MinScale = 0.4f;
        private const float MaxScale = 1.25f;
        private const string KeyPrefix = "hud.layout.";

        private class State
        {
            public VisualElement El, Grip, Ghost;
            public string Id;
            public bool Custom;                      // já saiu do lugar do USS
            public float LeftFrac, TopFrac, Scale = 1f;
            // arrasto
            public bool Dragging, Resizing;
            public Vector2 PointerStart, TopLeftStart;
            public float ScaleStart, WidthStart;
            public bool AnchorRight, AnchorBottom;
            public int PointerId;
            public long LastAltDown;
            // posicionamento original (USS ou definido em código), para o Alt+duplo clique restaurar
            public StyleEnum<Position> OPosition;
            public StyleLength OLeft, OTop, ORight, OBottom;
            public StyleTranslate OTranslate;
            public StyleTransformOrigin OOrigin;
            public StyleScale OScale;
        }

        private static readonly ConditionalWeakTable<VisualElement, State> States = new ConditionalWeakTable<VisualElement, State>();

        /// <summary>O jogador já mexeu neste painel? O código que reposiciona sozinho deve respeitar.</summary>
        public static bool IsCustom(VisualElement el) => el != null && States.TryGetValue(el, out var s) && s.Custom;

        /// <summary>Registra o painel. No celular não faz nada (não há Alt, e a HUD já é adaptada).</summary>
        public static void Register(VisualElement el, string id, bool allowResize = true)
        {
            if (el == null || string.IsNullOrEmpty(id) || MobileHUD.Active) return;
            if (States.TryGetValue(el, out _)) return;
            var st = new State { El = el, Id = id };
            var os = el.style;
            st.OPosition = os.position; st.OLeft = os.left; st.OTop = os.top; st.ORight = os.right; st.OBottom = os.bottom;
            st.OTranslate = os.translate; st.OOrigin = os.transformOrigin; st.OScale = os.scale;
            States.Add(el, st);

            el.RegisterCallback<PointerDownEvent>(e => OnDown(st, e), TrickleDown.TrickleDown);
            el.RegisterCallback<PointerMoveEvent>(e => OnMove(st, e));
            el.RegisterCallback<PointerUpEvent>(e => OnUp(st, e));
            el.RegisterCallback<PointerCaptureOutEvent>(_ => EndDrag(st));
            el.RegisterCallback<AttachToPanelEvent>(_ => LoadSaved(st));
            if (allowResize) BuildGrip(st);
            if (el.panel != null) LoadSaved(st);
        }

        // ── Puxador de tamanho ────────────────────────────────────────────────
        private static void BuildGrip(State st)
        {
            var grip = new Label("◢") { pickingMode = PickingMode.Position, tooltip = "Arraste para reduzir ou ampliar a janela" };
            grip.style.position = Position.Absolute;
            grip.style.right = 1; grip.style.bottom = 1;
            grip.style.width = 16; grip.style.height = 16;
            grip.style.fontSize = 12;
            grip.style.unityTextAlign = TextAnchor.LowerRight;
            grip.style.color = new Color(0.95f, 0.71f, 0.27f, 1f);
            grip.style.opacity = 0f;
            grip.style.paddingRight = grip.style.paddingBottom = 1;
            st.Grip = grip;
            st.El.Add(grip);
            st.El.RegisterCallback<PointerEnterEvent>(_ => grip.style.opacity = 0.9f);
            st.El.RegisterCallback<PointerLeaveEvent>(_ => { if (!st.Resizing) grip.style.opacity = 0f; });

            grip.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || e.altKey) return;
                Materialize(st);
                var parent = st.El.parent; if (parent == null) return;
                st.Resizing = true; st.PointerId = e.pointerId;
                st.PointerStart = e.position; st.ScaleStart = st.Scale;
                st.WidthStart = Mathf.Max(20f, st.El.layout.width * st.Scale);
                var vis = VisualRect(st);
                st.AnchorRight = vis.center.x > parent.layout.width * 0.5f;
                st.AnchorBottom = vis.center.y > parent.layout.height * 0.5f;
                st.TopLeftStart = new Vector2(st.LeftFrac * parent.layout.width, st.TopFrac * parent.layout.height);
                grip.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            grip.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!st.Resizing || !grip.HasPointerCapture(e.pointerId)) return;
                var parent = st.El.parent; if (parent == null) return;
                float dx = ((Vector2)e.position).x - st.PointerStart.x;
                float s = Mathf.Clamp(st.ScaleStart * (st.WidthStart + dx) / st.WidthStart, MinScale, MaxScale);
                float w = st.El.layout.width, h = st.El.layout.height;
                // Mantém fixo o canto mais próximo da borda da tela.
                float left = st.TopLeftStart.x + (st.AnchorRight ? w * (st.ScaleStart - s) : 0f);
                float top  = st.TopLeftStart.y + (st.AnchorBottom ? h * (st.ScaleStart - s) : 0f);
                st.Scale = s;
                st.LeftFrac = left / Mathf.Max(1f, parent.layout.width);
                st.TopFrac = top / Mathf.Max(1f, parent.layout.height);
                Apply(st);
                e.StopPropagation();
            });
            grip.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!st.Resizing) return;
                st.Resizing = false;
                grip.ReleasePointer(e.pointerId);
                Save(st);
                e.StopPropagation();
            });
        }

        // ── Alt + arrastar ────────────────────────────────────────────────────
        private static void OnDown(State st, PointerDownEvent e)
        {
            if (e.button != 0 || !e.altKey) return;
            var parent = st.El.parent; if (parent == null) return;

            // Alt + duplo clique: restaura o original.
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            double ms = (now - st.LastAltDown) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            st.LastAltDown = now;
            if (ms < 350.0) { Reset(st); e.StopPropagation(); return; }

            Materialize(st);
            st.Dragging = true; st.PointerId = e.pointerId;
            st.PointerStart = e.position;
            st.TopLeftStart = new Vector2(st.LeftFrac * parent.layout.width, st.TopFrac * parent.layout.height);
            st.El.CapturePointer(e.pointerId);
            e.StopPropagation();   // o clique não chega aos botões de dentro
        }

        private static void OnMove(State st, PointerMoveEvent e)
        {
            if (!st.Dragging || !st.El.HasPointerCapture(e.pointerId)) return;
            var parent = st.El.parent; if (parent == null) return;
            Vector2 d = (Vector2)e.position - st.PointerStart;
            Vector2 tl = st.TopLeftStart + d;
            Vector2 size = VisualSize(st);
            tl = ClampInside(parent, tl, size);
            bool snap = !e.shiftKey;
            Vector2 target = snap ? SnapToCorner(parent, tl, size, out bool snapped) : tl;
            ShowGhost(st, snap && SnapToCornerHit(parent, tl, size), target, size);
            st.LeftFrac = tl.x / Mathf.Max(1f, parent.layout.width);
            st.TopFrac = tl.y / Mathf.Max(1f, parent.layout.height);
            Apply(st);
            e.StopPropagation();
        }

        private static void OnUp(State st, PointerUpEvent e)
        {
            if (!st.Dragging) return;
            var parent = st.El.parent;
            if (parent != null)
            {
                Vector2 size = VisualSize(st);
                Vector2 tl = new Vector2(st.LeftFrac * parent.layout.width, st.TopFrac * parent.layout.height);
                if (!e.shiftKey) tl = SnapToCorner(parent, tl, size, out _);
                tl = ClampInside(parent, tl, size);
                st.LeftFrac = tl.x / Mathf.Max(1f, parent.layout.width);
                st.TopFrac = tl.y / Mathf.Max(1f, parent.layout.height);
                Apply(st);
            }
            st.El.ReleasePointer(e.pointerId);
            EndDrag(st);
            Save(st);
            e.StopPropagation();
        }

        private static void EndDrag(State st)
        {
            st.Dragging = false;
            if (st.Ghost != null) { st.Ghost.RemoveFromHierarchy(); st.Ghost = null; }
        }

        // ── Geometria ─────────────────────────────────────────────────────────
        private static Vector2 VisualSize(State st) => new Vector2(st.El.layout.width * st.Scale, st.El.layout.height * st.Scale);

        private static Rect VisualRect(State st)
        {
            var parent = st.El.parent;
            var wb = st.El.worldBound; var pb = parent.worldBound;
            return new Rect(wb.xMin - pb.xMin, wb.yMin - pb.yMin, wb.width, wb.height);
        }

        private static Vector2 ClampInside(VisualElement parent, Vector2 tl, Vector2 size)
        {
            float W = parent.layout.width, H = parent.layout.height;
            return new Vector2(Mathf.Clamp(tl.x, 0f, Mathf.Max(0f, W - size.x)), Mathf.Clamp(tl.y, 0f, Mathf.Max(0f, H - size.y)));
        }

        private static Vector2[] Corners(VisualElement parent, Vector2 size)
        {
            float W = parent.layout.width, H = parent.layout.height, mx = W * SnapMargin, my = H * SnapMargin;
            return new[]
            {
                new Vector2(mx, my),
                new Vector2(W - size.x - mx, my),
                new Vector2(mx, H - size.y - my),
                new Vector2(W - size.x - mx, H - size.y - my),
            };
        }

        private static bool SnapToCornerHit(VisualElement parent, Vector2 tl, Vector2 size)
        {
            SnapToCorner(parent, tl, size, out bool hit);
            return hit;
        }

        /// <summary>Se o canto da janela está perto de um canto da tela, devolve a posição encaixada.</summary>
        private static Vector2 SnapToCorner(VisualElement parent, Vector2 tl, Vector2 size, out bool snapped)
        {
            snapped = false;
            float W = parent.layout.width, H = parent.layout.height;
            Vector2 best = tl; float bestD = float.MaxValue;
            foreach (var c in Corners(parent, size))
            {
                float dx = Mathf.Abs(tl.x - c.x) / Mathf.Max(1f, W), dy = Mathf.Abs(tl.y - c.y) / Mathf.Max(1f, H);
                if (dx > SnapRangeX || dy > SnapRangeY) continue;
                float dd = dx * dx + dy * dy;
                if (dd < bestD) { bestD = dd; best = c; snapped = true; }
            }
            return best;
        }

        private static void ShowGhost(State st, bool show, Vector2 pos, Vector2 size)
        {
            var parent = st.El.parent; if (parent == null) return;
            if (!show) { if (st.Ghost != null) st.Ghost.style.display = DisplayStyle.None; return; }
            if (st.Ghost == null)
            {
                st.Ghost = new VisualElement { pickingMode = PickingMode.Ignore };
                st.Ghost.style.position = Position.Absolute;
                var c = new Color(0.95f, 0.71f, 0.27f, 1f);
                st.Ghost.style.borderTopWidth = st.Ghost.style.borderBottomWidth = st.Ghost.style.borderLeftWidth = st.Ghost.style.borderRightWidth = 2;
                st.Ghost.style.borderTopColor = st.Ghost.style.borderBottomColor = st.Ghost.style.borderLeftColor = st.Ghost.style.borderRightColor = c;
                st.Ghost.style.backgroundColor = new Color(0.95f, 0.71f, 0.27f, 0.12f);
                st.Ghost.style.borderTopLeftRadius = st.Ghost.style.borderTopRightRadius = st.Ghost.style.borderBottomLeftRadius = st.Ghost.style.borderBottomRightRadius = 10;
                parent.Add(st.Ghost);
            }
            st.Ghost.style.display = DisplayStyle.Flex;
            float W = Mathf.Max(1f, parent.layout.width), H = Mathf.Max(1f, parent.layout.height);
            st.Ghost.style.left = Length.Percent(pos.x / W * 100f);
            st.Ghost.style.top = Length.Percent(pos.y / H * 100f);
            st.Ghost.style.width = Length.Percent(size.x / W * 100f);
            st.Ghost.style.height = Length.Percent(size.y / H * 100f);
        }

        // ── Aplicar / salvar ──────────────────────────────────────────────────
        /// <summary>Troca o posicionamento do USS (right/bottom/translate) por left/top explícitos, sem o painel pular.</summary>
        private static void Materialize(State st)
        {
            if (st.Custom) return;
            var parent = st.El.parent; if (parent == null) return;
            var r = VisualRect(st);
            st.LeftFrac = r.xMin / Mathf.Max(1f, parent.layout.width);
            st.TopFrac = r.yMin / Mathf.Max(1f, parent.layout.height);
            st.Scale = 1f;
            st.Custom = true;
            Apply(st);
        }

        private static void Apply(State st)
        {
            var s = st.El.style;
            s.position = Position.Absolute;
            s.left = Length.Percent(st.LeftFrac * 100f);
            s.top = Length.Percent(st.TopFrac * 100f);
            s.right = StyleKeyword.Auto;
            s.bottom = StyleKeyword.Auto;
            s.translate = new Translate(0, 0);
            s.transformOrigin = new TransformOrigin(Length.Percent(0), Length.Percent(0));
            s.scale = new Scale(new Vector3(st.Scale, st.Scale, 1f));
        }

        private static void Reset(State st)
        {
            var s = st.El.style;
            s.position = st.OPosition; s.left = st.OLeft; s.top = st.OTop; s.right = st.ORight; s.bottom = st.OBottom;
            s.translate = st.OTranslate; s.transformOrigin = st.OOrigin; s.scale = st.OScale;
            st.Custom = false; st.Scale = 1f; st.Dragging = false;
            try { PlayerPrefs.DeleteKey(KeyPrefix + st.Id); PlayerPrefs.Save(); } catch { }
        }

        private static void Save(State st)
        {
            try
            {
                PlayerPrefs.SetString(KeyPrefix + st.Id, string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F5},{1:F5},{2:F3}", st.LeftFrac, st.TopFrac, st.Scale));
                PlayerPrefs.Save();
            }
            catch { }
        }

        private static void LoadSaved(State st)
        {
            if (st.Custom) return;
            string v; try { v = PlayerPrefs.GetString(KeyPrefix + st.Id, ""); } catch { return; }
            if (string.IsNullOrEmpty(v)) return;
            var p = v.Split(',');
            if (p.Length != 3) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            if (!float.TryParse(p[0], System.Globalization.NumberStyles.Float, ci, out float l)
                || !float.TryParse(p[1], System.Globalization.NumberStyles.Float, ci, out float t)
                || !float.TryParse(p[2], System.Globalization.NumberStyles.Float, ci, out float sc)) return;
            st.LeftFrac = Mathf.Clamp01(l); st.TopFrac = Mathf.Clamp01(t); st.Scale = Mathf.Clamp(sc, MinScale, MaxScale);
            st.Custom = true;
            Apply(st);
        }

        /// <summary>Todos os painéis que aceitam mover/redimensionar. Novo painel registrado = acrescentar aqui,
        /// para a tela de Opções restaurar também os que só existem quando abertos (ex.: lista de magias).</summary>
        public static readonly string[] AllIds =
        {
            "battle.actions", "battle.ticks", "battle.intents", "battle.choice", "battle.spells", "tutorial.card",
        };

        /// <summary>Opções › "Restaurar layout da HUD": apaga as posições e tamanhos salvos de TODOS os painéis.
        /// Os painéis voltam ao padrão na próxima vez que a HUD abrir.</summary>
        public static void ResetAll() => ResetAllSaved(AllIds);

        /// <summary>Apaga todas as posições salvas (menu de opções). Os painéis voltam ao padrão na próxima abertura.</summary>
        public static void ResetAllSaved(params string[] ids)
        {
            try { foreach (var id in ids) PlayerPrefs.DeleteKey(KeyPrefix + id); PlayerPrefs.Save(); } catch { }
        }
    }
}
