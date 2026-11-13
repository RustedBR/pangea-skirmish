using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace PangeaSkirmish
{
    /// <summary>
    /// Símbolos Unicode viram sprites em pixel em todo texto da interface (2026-11-13).
    /// O mapa símbolo → ícone vive aqui; o SpriteAsset (Resources/UI/TextIcons) é gerado pelo
    /// menu "Pangea/Gerar sprites de texto". Um componente em cada UIDocument troca os símbolos
    /// por &lt;sprite name=...&gt; quando o texto muda — assim nenhum texto precisa saber disso.
    /// </summary>
    public class TextIcons : MonoBehaviour
    {
        public static readonly (string sym, string icon)[] Map =
        {
            ("⚔","dmg"), ("◎","hit"), ("⌖","hit"), ("✚","hit"), ("✕","x"), ("✦","mana"), ("◆","power"),
            ("↯","dodge"), ("✔","check"), ("◈","cast"), ("⚠","warn"), ("🛡","def"), ("▣","def"),
            ("■","p_defensive"), ("○","p_neutral"), ("▲","p_aggressive"), ("✶","p_concentrated"), ("≈","p_serene"),
            ("❂","stamina"), ("⚡","stamina"), ("▼","chev_down"), ("▾","chev_down"), ("▴","chev_up"),
            ("▶","play"), ("◀","play_l"), ("✎","pencil"), ("🔥","burn"), ("🌋","burn"), ("★","star"),
            ("👁","eye"), ("💥","boom"), ("💨","wind"), ("→","arrow_r"), ("←","arrow_l"), ("−","minus"), ("≥","geq"),
        };

        private static HashSet<char> _first;
        private static Dictionary<string, string> _tag;

        private static void Init()
        {
            if (_first != null) return;
            _first = new HashSet<char>(); _tag = new Dictionary<string, string>();
            foreach (var (sym, icon) in Map) { _first.Add(sym[0]); _tag[sym] = $"<sprite name=\"{icon}\">"; }
        }

        /// <summary>Troca os símbolos do mapa pela tag de sprite. Texto sem símbolo volta igual.</summary>
        public static string Apply(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            Init();
            bool any = false;
            foreach (var c in s) if (_first.Contains(c)) { any = true; break; }
            if (!any) return s;
            var sb = new StringBuilder(s.Length + 32);
            for (int i = 0; i < s.Length; i++)
            {
                string key = char.IsHighSurrogate(s[i]) && i + 1 < s.Length ? s.Substring(i, 2) : s[i].ToString();
                if (_tag.TryGetValue(key, out var tag)) { sb.Append(tag); i += key.Length - 1; }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        private UIDocument _doc;
        private readonly List<TextElement> _buf = new List<TextElement>();

        /// <summary>Garante o componente em todos os UIDocument da cena.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnScene;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnScene;
            Attach();
        }
        private static void OnScene(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m) => Attach();
        public static void Attach()
        {
            foreach (var d in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
                if (d.GetComponent<TextIcons>() == null) d.gameObject.AddComponent<TextIcons>();
        }

        private void LateUpdate()
        {
            if (_doc == null) _doc = GetComponent<UIDocument>();
            var root = _doc != null ? _doc.rootVisualElement : null;
            if (root == null) return;
            _buf.Clear();
            root.Query<TextElement>().ToList(_buf);
            foreach (var t in _buf)
            {
                string s = t.text;
                if (string.IsNullOrEmpty(s)) continue;
                string r = Apply(s);
                if (!ReferenceEquals(r, s)) { t.enableRichText = true; t.text = r; }
            }
        }
    }
}
