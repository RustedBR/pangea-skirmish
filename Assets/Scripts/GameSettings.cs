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
            _musicVolume = PlayerPrefs.GetFloat(KEY_MUSIC, 1f);
            _sfxVolume   = PlayerPrefs.GetFloat(KEY_SFX, 1f);
            _resWidth    = PlayerPrefs.GetInt(KEY_WIDTH, Screen.width > 0 ? Screen.width : 1920);
            _resHeight   = PlayerPrefs.GetInt(KEY_HEIGHT, Screen.height > 0 ? Screen.height : 1080);
            _fullscreen  = PlayerPrefs.GetInt(KEY_FULL, 1) == 1;
        }

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
                audio.musicSource.volume = _musicVolume;
        }
    }
}
