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
            // UnityEngine.Device: no aparelho é igual ao Application; no editor responde pelo Device Simulator (2026-11-15).
            bool mobile = UnityEngine.Device.Application.isMobilePlatform;
            if (!mobile && !forced) return;
            Create(applyDeviceSettings: mobile);
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
                if (Application.isEditor) KeepEditorSettings(ps);
                if (ps != null) ps.referenceResolution = MobileReference;
                Application.targetFrameRate = 30;
                QualitySettings.SetQualityLevel(0, true);
            }
            return hud;
        }

        /// <summary>
        /// No Device Simulator o PanelSettings e a qualidade mudam como no aparelho; no editor essas mudanças
        /// ficariam gravadas no projeto. Guarda os originais e devolve ao sair do Play.
        /// </summary>
        private static void KeepEditorSettings(PanelSettings ps)
        {
            var res = ps != null ? ps.referenceResolution : default;
            int quality = QualitySettings.GetQualityLevel();
            int fps = Application.targetFrameRate;
            void Restore()
            {
                Application.quitting -= Restore;
                if (ps != null) ps.referenceResolution = res;
                QualitySettings.SetQualityLevel(quality, true);
                Application.targetFrameRate = fps;
            }
            Application.quitting += Restore;
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
            // Posições e tamanhos em % da tela (2026-11-14).
            _back.style.left = Length.Percent(1); _back.style.top = Length.Percent(2); _back.style.width = Length.Percent(11);
            _full = Make("⛶", "mobile-full", () => Screen.fullScreen = true);
            _full.style.left = Length.Percent(12.5f); _full.style.top = Length.Percent(2); _full.style.width = Length.Percent(4.5f);
            _confirm = Make("✓ Confirmar", "mobile-confirm", () => PointerInput.RequestConfirm());
            _confirm.AddToClassList("pg-button--primary");
            _confirm.style.right = Length.Percent(1); _confirm.style.top = Length.Percent(42); _confirm.style.width = Length.Percent(16); _confirm.style.height = Length.Percent(10);
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
            b.style.position = Position.Absolute; b.style.height = Length.Percent(8); b.style.fontSize = 18;
            _doc.rootVisualElement.Add(b);
            return b;
        }

        private static void Show(VisualElement e, bool on) { if (e != null) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None; }

        private void Refresh()
        {
            bool portrait = Screen.height > Screen.width;
            Show(_portrait, portrait);
            // Voltar só quando a tela não tem como voltar sozinha (Voltar, Fechar, Cancelar, Desfazer, Sair).
            if (Time.frameCount % 10 == 0 || _screenHasBack == null) _screenHasBack = ScreenHasOwnBack();
            Show(_back, !portrait && _screenHasBack == false);
            Show(_full, !portrait && !Screen.fullScreen);
            // ✓ só no planejamento, com ação escolhida e o alvo tocado (prévia na tela) — 2026-11-15.
            Show(_confirm, !portrait && PlanningController.ArmedForTouch && PointerInput.HasPendingTap);

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

        private bool? _screenHasBack;
        private static readonly string[] BackWords = { "voltar", "fechar", "cancelar", "desfazer", "sair" };

        /// <summary>Alguma tela visível já tem um botão de voltar/fechar? Aí o Voltar flutuante sobra.</summary>
        private bool ScreenHasOwnBack()
        {
            foreach (var doc in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            {
                if (doc == _doc || doc.rootVisualElement == null || !doc.isActiveAndEnabled) continue;
                foreach (var b in doc.rootVisualElement.Query<Button>().ToList())
                {
                    if (string.IsNullOrEmpty(b.text) || b.resolvedStyle.display == DisplayStyle.None || b.resolvedStyle.visibility == Visibility.Hidden) continue;
                    var r = b.worldBound;
                    if (float.IsNaN(r.width) || r.width < 2f || r.height < 2f) continue;
                    bool shown = true;
                    for (var p = b.parent; p != null; p = p.parent)
                        if (p.resolvedStyle.display == DisplayStyle.None || p.resolvedStyle.opacity < 0.05f) { shown = false; break; }
                    if (!shown) continue;
                    string t = UIHelper.Fold(b.text);
                    foreach (var w in BackWords) if (t.Contains(w)) return true;
                }
            }
            return false;
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
