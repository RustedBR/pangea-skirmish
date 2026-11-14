using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Ciclo dia/noite da descida do Coop (GDD-024). O tempo só anda quando o grupo descansa:
    /// dia → entardecer → noite → dia. À noite as feras ganham orçamento e o mapa escurece.
    /// </summary>
    public static class DayNight
    {
        public const int Day = 0, Dusk = 1, Night = 2;

        public static int Current { get; private set; }

        private static Light _sun;
        private static float _baseIntensity = -1f;
        private static Color _baseColor;
        private static Color _baseAmbient;

        public static string Name(int t) => t == Dusk ? "Entardecer" : t == Night ? "Noite" : "Dia";
        public static string Icon(int t) => t == Dusk ? "◐" : t == Night ? "☾" : "☀";

        /// <summary>Multiplicador do orçamento das feras pela hora.</summary>
        public static float BeastMult(int t) => t == Dusk ? 1.15f : t == Night ? 1.30f : 1f;

        /// <summary>Começo de partida: volta para o dia.</summary>
        public static void Reset() => Set(Day);

        public static void Set(int t)
        {
            Current = Mathf.Clamp(t, Day, Night);
            Apply();
        }

        private static void Apply()
        {
            if (_sun == null)
            {
                _sun = RenderSettings.sun;
                if (_sun == null)
                    foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                        if (l.type == LightType.Directional) { _sun = l; break; }
                _baseIntensity = -1f;
            }
            if (_sun == null) return;
            if (_baseIntensity < 0f)
            {
                _baseIntensity = _sun.intensity; _baseColor = _sun.color; _baseAmbient = RenderSettings.ambientLight;
            }
            float k = Current == Dusk ? 0.7f : Current == Night ? 0.4f : 1f;
            Color tint = Current == Dusk ? new Color(1f, 0.72f, 0.5f) : Current == Night ? new Color(0.55f, 0.65f, 1f) : Color.white;
            _sun.intensity = _baseIntensity * k;
            _sun.color = _baseColor * tint;
            RenderSettings.ambientLight = _baseAmbient * Mathf.Lerp(0.5f, 1f, k);
        }
    }
}
