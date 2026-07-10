using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace PangeaSkirmish
{
    /// <summary>
    /// Flash de tela inteira (overlay UI Toolkit). Cria um VisualElement que ocupa
    /// a tela toda e fica acima de tudo (picked:false para não bloquear cliques).
    /// </summary>
    public class ScreenFlash : MonoBehaviour
    {
        private VisualElement _image;
        private static ScreenFlash _instance;

        public static ScreenFlash Create(UIDocument doc)
        {
            if (doc == null || doc.rootVisualElement == null) return null;
            if (_instance != null) return _instance;

            var ve = new VisualElement();
            ve.name = "ScreenFlash";
            ve.style.position = Position.Absolute;
            ve.style.left = 0; ve.style.top = 0; ve.style.right = 0; ve.style.bottom = 0;
            ve.style.backgroundColor = new Color(1f, 0f, 0f, 0f);
            ve.pickingMode = PickingMode.Ignore; // não bloqueia cliques de jogo

            doc.rootVisualElement.Add(ve);
            ve.BringToFront(); // acima de tudo

            var sf = doc.gameObject.AddComponent<ScreenFlash>();
            sf._image = ve;
            _instance = sf;
            return sf;
        }

        public void FlashRed(float duration = -1f, float intensity = -1f)
        {
            if (_image == null) return;
            var T = Tuning.Get();
            if (duration < 0f) duration = T.flashDurationRed;
            if (intensity < 0f) intensity = T.flashIntensityRed;
            var c = T.flashRedColor;
            StartCoroutine(FlashCoroutine(new Color(c.r, c.g, c.b, intensity), duration));
        }

        public void FlashWhite(float duration = -1f, float intensity = -1f)
        {
            if (_image == null) return;
            var T = Tuning.Get();
            if (duration < 0f) duration = T.flashDurationWhite;
            if (intensity < 0f) intensity = T.flashIntensityWhite;
            StartCoroutine(FlashCoroutine(new Color(1f, 1f, 1f, intensity), duration));
        }

        private IEnumerator FlashCoroutine(Color peakColor, float duration)
        {
            _image.style.backgroundColor = peakColor;
            float holdRatio = Tuning.Get().flashHoldRatio;
            float hold = duration * holdRatio;
            float fade = Mathf.Max(0.01f, duration - hold);

            yield return new WaitForSeconds(hold);

            float elapsed = 0f;
            while (elapsed < fade)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / fade;
                Color c = peakColor;
                c.a = Mathf.Lerp(peakColor.a, 0f, t);
                _image.style.backgroundColor = c;
                yield return null;
            }

            _image.style.backgroundColor = new Color(peakColor.r, peakColor.g, peakColor.b, 0f);
        }
    }
}
