using UnityEngine;
using UnityEngine.UIElements;

namespace PangeaSkirmish
{
    /// <summary>
    /// Ajustes de celular (GDD-022), ativos só em navegador de celular
    /// (ou com ?mobile=1 na URL, para testar no desktop):
    /// UI maior, aviso para girar o aparelho em retrato, botão "Voltar"
    /// (o Esc do toque) e qualidade/fps mais leves.
    /// </summary>
    public class MobileHUD : MonoBehaviour
    {
        /// <summary>True enquanto o dedo está sobre um botão desta HUD (IMGUI).</summary>
        public static bool PointerOver { get; private set; }

        public static bool Active { get; private set; }

        /// <summary>Resolução de referência da UI no celular: menor = UI maior.</summary>
        private static readonly Vector2Int MobileReference = new Vector2Int(1280, 720);

        private Rect _backRect, _fullRect;
        private GUIStyle _btn, _overlay;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            Active = false; PointerOver = false;
            bool forced = !string.IsNullOrEmpty(Application.absoluteURL) && Application.absoluteURL.Contains("mobile=1");
            if (!Application.isMobilePlatform && !forced) return;
            Active = true;

            var go = new GameObject("MobileHUD");
            DontDestroyOnLoad(go);
            go.AddComponent<MobileHUD>();

            // UI maior: todas as telas usam o mesmo PanelSettings.
            var ps = Resources.Load<PanelSettings>("UI/PangeaPanelSettings");
            if (ps != null && Application.isMobilePlatform)   // não regrava o asset no editor
                ps.referenceResolution = MobileReference;

            // Performance: celular roda a 30 fps no nível de qualidade mais baixo.
            Application.targetFrameRate = 30;
            QualitySettings.SetQualityLevel(0, true);
        }

        private bool _askedFullscreen;

        private void Update()
        {
            // 1º toque pede tela cheia (o navegador só aceita a partir de um gesto do usuário).
            if (!_askedFullscreen && PointerInput.IsTouch && !Screen.fullScreen)
            {
                _askedFullscreen = true;
                Screen.fullScreen = true;
            }
        }

        private void EnsureStyles()
        {
            if (_btn != null) return;
            float s = Mathf.Max(1f, Screen.dpi > 0 ? Screen.dpi / 160f : Screen.height / 400f);
            _btn = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(16 * s), fontStyle = FontStyle.Bold };
            _overlay = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(20 * s), alignment = TextAnchor.MiddleCenter, wordWrap = true,
                normal = { textColor = Color.white }
            };
        }

        private void OnGUI()
        {
            EnsureStyles();
            bool portrait = Screen.height > Screen.width;
            if (portrait)
            {
                // Paisagem obrigatória: cobre a tela e pede para girar.
                GUI.color = new Color(0.04f, 0.06f, 0.14f, 0.96f);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(20, 0, Screen.width - 40, Screen.height), "↻\nGire o celular para jogar\n(modo paisagem)", _overlay);
                PointerOver = true;
                return;
            }

            float h = _btn.fontSize * 2.6f, w = h * 2.6f, m = h * 0.3f;
            // Canto superior esquerdo (2026-11-14): embaixo ele cobria os avisos e o LOG.
            _backRect = new Rect(m, m, w, h);
            if (GUI.Button(_backRect, "← Voltar", _btn)) PointerInput.RequestBack();
            // Tela cheia: a barra do navegador e os botões do Android roubam espaço.
            _fullRect = new Rect(m * 2 + w, m, h, h);
            if (!Screen.fullScreen && GUI.Button(_fullRect, "⛶", _btn)) Screen.fullScreen = true;

            Vector2 p = PointerInput.Position;
            var gp = new Vector2(p.x, Screen.height - p.y);   // IMGUI tem y para baixo
            PointerOver = _backRect.Contains(gp) || (!Screen.fullScreen && _fullRect.Contains(gp));
        }
    }
}
