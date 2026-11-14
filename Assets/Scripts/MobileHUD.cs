using PangeaSkirmish.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace PangeaSkirmish
{
    /// <summary>
    /// Ajustes de celular (GDD-022), ativos só em navegador de celular (ou com ?mobile=1 na URL,
    /// para testar no desktop): UI maior, aviso para girar o aparelho em retrato, botões de toque
    /// (Voltar = Esc, tela cheia, ✓ Confirmar) e qualidade/fps mais leves.
    /// Feito em UI Toolkit, no tema do jogo (2026-11-14; antes era IMGUI, fora da identidade visual).
    /// </summary>
    public class MobileHUD : MonoBehaviour
    {
        /// <summary>True enquanto o dedo está sobre um botão desta HUD ou o aviso de retrato cobre a tela.</summary>
        public static bool PointerOver { get; private set; }
        public static bool Active { get; private set; }

        /// <summary>Resolução de referência da UI no celular: menor = UI maior.</summary>
        private static readonly Vector2Int MobileReference = new Vector2Int(1120, 630);

        private UIDocument _doc;
        private Button _back, _full, _confirm;
        private VisualElement _portrait;
        private bool _askedFullscreen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            Active = false; PointerOver = false;
            bool forced = !string.IsNullOrEmpty(Application.absoluteURL) && Application.absoluteURL.Contains("mobile=1");
            if (!Application.isMobilePlatform && !forced) return;
            Create(applyDeviceSettings: Application.isMobilePlatform);
        }

        /// <summary>Cria a HUD de toque (também usado pelos testes).</summary>
        public static MobileHUD Create(bool applyDeviceSettings)
        {
            Active = true;
            var go = new GameObject("MobileHUD");
            DontDestroyOnLoad(go);
            var hud = go.AddComponent<MobileHUD>();
            if (applyDeviceSettings)
            {
                // UI maior: todas as telas usam o mesmo PanelSettings. Só no aparelho (não regrava o asset no editor).
                var ps = Resources.Load<PanelSettings>(PangeaScreen.PanelSettingsResource);
                if (ps != null) ps.referenceResolution = MobileReference;
                Application.targetFrameRate = 30;
                QualitySettings.SetQualityLevel(0, true);
            }
            return hud;
        }

        private void Awake()
        {
            var settings = Resources.Load<PanelSettings>(PangeaScreen.PanelSettingsResource);
            if (settings == null) return;
            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = settings;
            _doc.sortingOrder = 4000;                      // acima das telas, abaixo do LOG
            var root = _doc.rootVisualElement;
            if (root == null) return;
            var theme = Resources.Load<StyleSheet>("UI/Theme/PangeaTheme");
            if (theme != null) root.styleSheets.Add(theme);
            root.AddToClassList("pg-theme");
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.right = 0; root.style.top = 0; root.style.bottom = 0;

            _back = Make("← Voltar", "mobile-back", () => PointerInput.RequestBack());
            _back.style.left = 12; _back.style.top = 12; _back.style.width = 130;
            _full = Make("⛶", "mobile-full", () => Screen.fullScreen = true);
            _full.style.left = 150; _full.style.top = 12; _full.style.width = 52;
            _confirm = Make("✓ Confirmar", "mobile-confirm", () => PointerInput.RequestConfirm());
            _confirm.AddToClassList("pg-button--primary");
            _confirm.style.right = 12; _confirm.style.top = Length.Percent(42); _confirm.style.width = 190; _confirm.style.height = 64;
            _confirm.style.fontSize = 22;

            _portrait = new VisualElement { name = "mobile-portrait" };
            _portrait.style.position = Position.Absolute;
            _portrait.style.left = 0; _portrait.style.right = 0; _portrait.style.top = 0; _portrait.style.bottom = 0;
            _portrait.style.backgroundColor = new Color(0.04f, 0.06f, 0.14f, 0.97f);
            _portrait.style.alignItems = Align.Center; _portrait.style.justifyContent = Justify.Center;
            var msg = new Label("↻\nGire o celular para jogar\n(modo paisagem)");
            msg.AddToClassList("pg-title"); msg.style.whiteSpace = WhiteSpace.Normal; msg.style.fontSize = 28;
            _portrait.Add(msg);
            root.Add(_portrait);
            Refresh();
        }

        private Button Make(string text, string name, System.Action onClick)
        {
            var b = new Button(onClick) { text = text, name = name };
            b.AddToClassList("pg-button");
            b.style.position = Position.Absolute; b.style.height = 52; b.style.fontSize = 18;
            _doc.rootVisualElement.Add(b);
            return b;
        }

        private static void Show(VisualElement e, bool on) { if (e != null) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None; }

        private void Refresh()
        {
            bool portrait = Screen.height > Screen.width;
            Show(_portrait, portrait);
            Show(_back, !portrait);
            Show(_full, !portrait && !Screen.fullScreen);
            Show(_confirm, !portrait && PointerInput.HasPendingTap);   // só enquanto um 1º toque espera o 2º

            bool over = portrait;
            var panel = _doc != null && _doc.rootVisualElement != null ? _doc.rootVisualElement.panel : null;
            if (!over && panel != null && PointerInput.Available)
            {
                var p = UIHelper.ScreenToPanel(panel, PointerInput.Position);
                foreach (var b in new[] { _back, _full, _confirm })
                    if (b != null && b.resolvedStyle.display == DisplayStyle.Flex && b.worldBound.Contains(p)) { over = true; break; }
            }
            PointerOver = over;
        }

        private void Update()
        {
            // 1º toque pede tela cheia (o navegador só aceita a partir de um gesto do usuário).
            if (!_askedFullscreen && PointerInput.IsTouch && !Screen.fullScreen)
            {
                _askedFullscreen = true;
                Screen.fullScreen = true;
            }
            Refresh();
        }

        private void OnDestroy() { Active = false; PointerOver = false; }
    }
}
