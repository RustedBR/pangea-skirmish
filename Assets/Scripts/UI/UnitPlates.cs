using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PangeaSkirmish
{
    /// <summary>
    /// Placas de vida desenhadas pela interface, presas acima de cada unidade (2026-11-13).
    /// Substituem as barras no mundo 3D, que encolhiam com o zoom e borravam o texto: aqui o
    /// tamanho é fixo em pixels (escala nas Opções). Mostra nome, postura, Vida com número e
    /// ícones de estado; Mana e Stamina aparecem no hover/seleção, ou piscando quando acabam;
    /// barra fina de conjuração enquanto conjura.
    /// </summary>
    public class UnitPlates : MonoBehaviour
    {
        private class Plate
        {
            public VisualElement root, hpFill, hpTrail, mpRow, mpFill, stRow, stFill, cast, castFill, icons, posture;
            public Label name, hp, mp, st;
            public float trail = 1f, hold;
            public string iconKey = "";
        }

        private VisualElement _layer;
        private readonly Dictionary<Unit, Plate> _plates = new Dictionary<Unit, Plate>();
        private static readonly Dictionary<string, Texture2D> _tex = new Dictionary<string, Texture2D>();

        /// <summary>Quantas placas estão visíveis agora (testes).</summary>
        public int VisibleCount { get; private set; }
        public static UnitPlates Instance { get; private set; }

        public void Init(VisualElement hudRoot)
        {
            Instance = this;
            _layer = new VisualElement { name = "unit-plates", pickingMode = PickingMode.Ignore };
            _layer.AddToClassList("up-layer");
            hudRoot.Add(_layer);
            _layer.SendToBack();                 // embaixo de todos os painéis da HUD
            Unit.ScreenPlatesActive = true;
        }

        private void OnDestroy()
        {
            if (Instance == this) { Instance = null; Unit.ScreenPlatesActive = false; }
        }

        private static Texture2D Icon(string n)
        {
            if (string.IsNullOrEmpty(n)) return null;
            if (_tex.TryGetValue(n, out var t) && t != null) return t;
            t = Resources.Load<Texture2D>("UI/Icons/" + n);
            if (t != null) t.filterMode = FilterMode.Point;
            _tex[n] = t;
            return t;
        }

        private static VisualElement El(string cls, VisualElement parent)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList(cls); parent?.Add(e);
            return e;
        }

        private static Label Lbl(string cls, VisualElement parent)
        {
            var l = new Label { pickingMode = PickingMode.Ignore };
            l.AddToClassList(cls); parent?.Add(l);
            return l;
        }

        private Plate Build(Unit u)
        {
            var p = new Plate();
            p.root = El("up-plate", _layer);
            p.root.AddToClassList(u.team == Team.Player ? "up-plate--ally" : "up-plate--enemy");
            var head = El("up-head", p.root);
            p.posture = El("up-icon", head);
            p.name = Lbl("up-name", head);
            var hpRow = El("up-hprow", p.root);
            var hpBg = El("up-hpbg", hpRow);
            p.hpTrail = El("up-hptrail", hpBg);
            p.hpFill = El("up-hpfill", hpBg);
            p.hp = Lbl("up-hptext", hpRow);
            p.icons = El("up-icons", hpRow);
            p.mpRow = El("up-row", p.root);
            var mi = El("up-icon up-icon--small", p.mpRow); mi.style.backgroundImage = new StyleBackground(Icon("mana"));
            var mpBg = El("up-barbg", p.mpRow); p.mpFill = El("up-mpfill", mpBg);
            p.mp = Lbl("up-small", p.mpRow);
            p.stRow = El("up-row", p.root);
            var si = El("up-icon up-icon--small", p.stRow); si.style.backgroundImage = new StyleBackground(Icon("stamina"));
            var stBg = El("up-barbg", p.stRow); p.stFill = El("up-stfill", stBg);
            p.st = Lbl("up-small", p.stRow);
            p.cast = El("up-cast", p.root);
            p.castFill = El("up-castfill", p.cast);
            return p;
        }

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (_layer == null || cam == null || _layer.panel == null) return;
            float scale = GameSettings.PlateScale;
            var seen = new HashSet<Unit>();
            int visible = 0;
            float blink = 0.55f + 0.45f * Mathf.Sin(Time.time * 9f);
            var hovered = Unit.HoveredUnit;

            foreach (var u in UnitRegistry.Tracked)
            {
                if (u == null || u.stats == null) continue;
                seen.Add(u);
                if (!_plates.TryGetValue(u, out var p)) { p = Build(u); _plates[u] = p; }
                if (u.IsDead) { p.root.style.display = DisplayStyle.None; continue; }

                Vector3 w = u.HeadWorld + Vector3.up * 0.15f;
                Vector3 vp = cam.WorldToViewportPoint(w);
                if (vp.z < 0f || vp.x < -0.1f || vp.x > 1.1f || vp.y < -0.1f || vp.y > 1.1f) { p.root.style.display = DisplayStyle.None; continue; }
                Vector2 pos = RuntimePanelUtils.CameraTransformWorldToPanel(_layer.panel, w, cam);
                p.root.style.display = DisplayStyle.Flex;
                p.root.style.left = pos.x; p.root.style.top = pos.y;
                p.root.style.scale = new Scale(new Vector3(scale, scale, 1f));
                visible++;

                // nome e postura
                if (p.name.text != u.unitName) p.name.text = u.unitName;
                string pi = PostureIcon(u.posture);
                p.posture.style.display = pi != null ? DisplayStyle.Flex : DisplayStyle.None;
                if (pi != null) p.posture.style.backgroundImage = new StyleBackground(Icon(pi));

                // vida, com rastro do dano e piscando quando acaba
                float hp = u.stats.MaxHP > 0 ? Mathf.Clamp01((float)u.currentHP / u.stats.MaxHP) : 0f;
                if (hp < p.trail) { if (p.hold <= 0f) p.hold = 0.35f; p.hold -= Time.deltaTime; if (p.hold <= 0f) p.trail = Mathf.MoveTowards(p.trail, hp, Time.deltaTime * 0.9f); }
                else { p.trail = hp; p.hold = 0f; }
                p.hpFill.style.width = new Length(hp * 100f, LengthUnit.Percent);
                p.hpTrail.style.width = new Length(p.trail * 100f, LengthUnit.Percent);
                var T = Tuning.Get();
                Color hc = Color.Lerp(T.worldHpLowColor, T.worldHpHighColor, hp);
                if (hp <= 0.25f) hc = Color.Lerp(hc, Color.white, 0.4f * (1f - blink));
                p.hpFill.style.backgroundColor = hc;
                string ht = $"{u.currentHP}/{u.stats.MaxHP}";
                if (p.hp.text != ht) p.hp.text = ht;

                // Mana e Stamina: hover/seleção, ou sozinhas piscando quando acabam
                bool detail = hovered == u || u.IsSelectedNow;
                bool manaLow = u.stats.MaxMana > 0 && u.currentMana <= Mathf.CeilToInt(u.stats.MaxMana * 0.25f);
                bool stLow = u.currentStamina <= 1;
                p.mpRow.style.display = detail || manaLow ? DisplayStyle.Flex : DisplayStyle.None;
                p.stRow.style.display = detail || stLow ? DisplayStyle.Flex : DisplayStyle.None;
                p.mpRow.style.opacity = manaLow && !detail ? blink : 1f;
                p.stRow.style.opacity = stLow && !detail ? blink : 1f;
                p.mpFill.style.width = new Length(u.stats.MaxMana > 0 ? 100f * u.currentMana / u.stats.MaxMana : 0f, LengthUnit.Percent);
                p.stFill.style.width = new Length(u.stats.MaxStamina > 0 ? 100f * u.currentStamina / u.stats.MaxStamina : 0f, LengthUnit.Percent);
                p.mp.text = $"{u.currentMana}"; p.st.text = $"{u.currentStamina}";

                // estados
                var icons = u.WorldStateIcons();
                string key = string.Join(",", icons);
                if (key != p.iconKey)
                {
                    p.iconKey = key; p.icons.Clear();
                    foreach (var n in icons) { var ic = El("up-icon", p.icons); ic.style.backgroundImage = new StyleBackground(Icon(n)); }
                }

                // conjuração (só Elemental, a que pode ser interrompida)
                bool casting = false; float prog = 0f; Color cc = new Color(0.72f, 0.48f, 1f);
                if (u.queue.Count > 0 && u.queue[0].Type == ActionType.Spell)
                {
                    var d = u.queue[0].Spell.Def;
                    if (d != null && !d.IsPhysical)
                    {
                        casting = true;
                        prog = u.queue[0].Duration > 0 ? Mathf.Clamp01((float)u.queue[0].Elapsed / u.queue[0].Duration) : 1f;
                        cc = ElementInfo.Color(d.PrimaryElement);
                    }
                }
                p.cast.style.display = casting ? DisplayStyle.Flex : DisplayStyle.None;
                if (casting) { p.castFill.style.width = new Length(Mathf.Max(4f, prog * 100f), LengthUnit.Percent); p.castFill.style.backgroundColor = cc; }
            }

            // unidades que saíram de cena
            var gone = new List<Unit>();
            foreach (var kv in _plates) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var u in gone) { _plates[u].root.RemoveFromHierarchy(); _plates.Remove(u); }
            VisibleCount = visible;
        }

        private static string PostureIcon(Posture p)
        {
            switch (p)
            {
                case Posture.Aggressive:   return "p_aggressive";
                case Posture.Defensive:    return "p_defensive";
                case Posture.Agile:        return "p_agile";
                case Posture.Focused:      return "p_focused";
                case Posture.Concentrated: return "p_concentrated";
                case Posture.Serene:       return "p_serene";
                default:                   return null;
            }
        }
    }
}
