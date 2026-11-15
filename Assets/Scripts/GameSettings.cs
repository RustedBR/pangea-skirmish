using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Configurações de jogo persistidas em PlayerPrefs (sobrevivem a fechar o jogo).
    /// Volume, resolução e fullscreen. Carregadas no Awake e aplicadas em todo o jogo.
    /// </summary>
    public static class GameSettings
    {
        private const string KEY_MUSIC   = "settings.musicVolume";
        private const string KEY_SFX     = "settings.sfxVolume";
        private const string KEY_WIDTH   = "settings.resWidth";
        private const string KEY_HEIGHT  = "settings.resHeight";
        private const string KEY_FULL    = "settings.fullscreen";
        private const string KEY_PLATE   = "settings.plateScale";

        /// <summary>Tamanho das placas de vida sobre as unidades (1 = médio). Opções: 0,7 a 1,6.</summary>
        public static float PlateScale
        {
            get { return Mathf.Clamp(PlayerPrefs.GetFloat(KEY_PLATE, 1f), 0.7f, 1.6f); }
            set { PlayerPrefs.SetFloat(KEY_PLATE, Mathf.Clamp(value, 0.7f, 1.6f)); PlayerPrefs.Save(); }
        }

        // ── Tamanho da interface (2026-11-15): 50% a 150%, salvo separado no celular e no PC ──
        public const float UiScaleMin = 0.5f, UiScaleMax = 1.5f;
        private static string KeyUiScale => "settings.uiScale." + (UnityEngine.Device.Application.isMobilePlatform ? "mobile" : "pc");

        public static float UiScale
        {
            get { return Mathf.Clamp(PlayerPrefs.GetFloat(KeyUiScale, 1f), UiScaleMin, UiScaleMax); }
            set { PlayerPrefs.SetFloat(KeyUiScale, Mathf.Clamp(value, UiScaleMin, UiScaleMax)); PlayerPrefs.Save(); ApplyUiScale(); }
        }

        private static bool _uiScaleRestoreHooked;
        /// <summary>Aplica o tamanho da UI no PanelSettings de todas as telas (no editor, devolve o original ao sair do Play).</summary>
        public static void ApplyUiScale()
        {
            var ps = Resources.Load<UnityEngine.UIElements.PanelSettings>(UI.PangeaScreen.PanelSettingsResource);
            if (ps == null) return;
            if (Application.isEditor && !_uiScaleRestoreHooked)
            {
                _uiScaleRestoreHooked = true;
                float keep = ps.scale;
                void Restore() { Application.quitting -= Restore; ps.scale = keep; _uiScaleRestoreHooked = false; }
                Application.quitting += Restore;
            }
            ps.scale = UiScale;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootUiScale() => ApplyUiScale();

        // Cache em memória (evita ler PlayerPrefs a todo momento)
        private static float  _musicVolume  = 1f;
        private static float  _sfxVolume    = 1f;
        private static int    _resWidth     = 1920;
        private static int    _resHeight    = 1080;
        private static bool   _fullscreen   = true;
        private static bool   _loaded;

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            _musicVolume = PlayerPrefs.GetFloat(KEY_MUSIC, 0.7f);
            _sfxVolume   = PlayerPrefs.GetFloat(KEY_SFX, 0.75f);
            _resWidth    = PlayerPrefs.GetInt(KEY_WIDTH, Screen.width > 0 ? Screen.width : 1920);
            _resHeight   = PlayerPrefs.GetInt(KEY_HEIGHT, Screen.height > 0 ? Screen.height : 1080);
            _fullscreen  = PlayerPrefs.GetInt(KEY_FULL, 1) == 1;
        }

        // ── Volume percebido (2026-11-14): a música estourava. O slider (0–1) passa por uma curva em
        //    decibéis — cada trecho do slider muda o volume na mesma proporção que o ouvido sente — e o
        //    100% fica abaixo do volume cheio do arquivo. Use MusicGain/SfxGain ao tocar som. ──
        /// <summary>Volume real no 100% do slider (fração do volume cheio do arquivo).</summary>
        public const float MusicMaxGain = 0.35f, SfxMaxGain = 0.5f;
        /// <summary>Faixa da curva: 0% do slider → silêncio; 1% → −RangeDb dB abaixo do máximo.</summary>
        public const float RangeDb = 40f;

        /// <summary>Slider (0–1) → ganho, em decibéis. 0 = mudo; 50% ≈ 10% do máximo; 100% = máximo.</summary>
        public static float SliderToGain(float slider, float maxGain)
        {
            if (slider <= 0.005f) return 0f;
            float db = (Mathf.Clamp01(slider) - 1f) * RangeDb;
            return maxGain * Mathf.Pow(10f, db / 20f);
        }

        public static float MusicGain => SliderToGain(MusicVolume, MusicMaxGain);
        public static float SfxGain   => SliderToGain(SfxVolume, SfxMaxGain);

        public static float MusicVolume
        {
            get { EnsureLoaded(); return _musicVolume; }
            set { _musicVolume = Mathf.Clamp01(value); PlayerPrefs.SetFloat(KEY_MUSIC, _musicVolume); PlayerPrefs.Save(); }
        }

        public static float SfxVolume
        {
            get { EnsureLoaded(); return _sfxVolume; }
            set { _sfxVolume = Mathf.Clamp01(value); PlayerPrefs.SetFloat(KEY_SFX, _sfxVolume); PlayerPrefs.Save(); }
        }

        public static int ResolutionWidth
        {
            get { EnsureLoaded(); return _resWidth; }
            set { _resWidth = value; PlayerPrefs.SetInt(KEY_WIDTH, _resWidth); PlayerPrefs.Save(); }
        }

        public static int ResolutionHeight
        {
            get { EnsureLoaded(); return _resHeight; }
            set { _resHeight = value; PlayerPrefs.SetInt(KEY_HEIGHT, _resHeight); PlayerPrefs.Save(); }
        }

        public static bool Fullscreen
        {
            get { EnsureLoaded(); return _fullscreen; }
            set { _fullscreen = value; PlayerPrefs.SetInt(KEY_FULL, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>
        /// Aplica as configurações atuais na tela e no áudio.
        /// Chame após carregar o jogo ou mudar uma opção.
        /// </summary>
        public static void Apply()
        {
            EnsureLoaded();
            Screen.SetResolution(_resWidth, _resHeight, _fullscreen);

            var audio = AudioManager.I;
            if (audio != null && audio.musicSource != null)
                audio.musicSource.volume = MusicGain;
        }
    }
}
