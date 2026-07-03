using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace PangeaSkirmish
{
    /// <summary>
    /// Flash de tela inteira (overlay UI). Cria via ScreenFlash.Create(canvas).
    /// </summary>
    public class ScreenFlash : MonoBehaviour
    {
        private Image _image;
        private static ScreenFlash _instance;

        public static ScreenFlash Create(Canvas canvas)
        {
            if (canvas == null) return null;
            if (_instance != null) return _instance;

            var go = new GameObject("ScreenFlash");
            go.transform.SetParent(canvas.transform, false);
            var sf = go.AddComponent<ScreenFlash>();

            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 0f, 0f, 0f); // transparente inicial
            img.raycastTarget = false;

            // Stretch to fill entire screen
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            sf._image = img;
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
            _image.color = peakColor;
            float holdRatio = Tuning.Get().flashHoldRatio;
            float hold = duration * holdRatio;
            float fade = Mathf.Max(0.01f, duration - hold);

            // Segura no pico, depois desvanece
            yield return new WaitForSeconds(hold);

            // Fade out
            float elapsed = 0f;
            while (elapsed < fade)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / fade;
                Color c = peakColor;
                c.a = Mathf.Lerp(peakColor.a, 0f, t);
                _image.color = c;
                yield return null;
            }

            _image.color = new Color(peakColor.r, peakColor.g, peakColor.b, 0f);
        }
    }
}
