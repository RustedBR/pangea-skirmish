using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace PangeaSkirmish.UI
{
    /// <summary>
    /// Tela de Opções (UI Toolkit). Ajusta volume (música/SFX), resolução e fullscreen.
    /// Persiste em GameSettings (PlayerPrefs) e aplica em runtime.
    /// </summary>
    public class OptionsScreen : PangeaScreen
    {
        protected override string UxmlResource => "UI/Screens/Options";

        public Action OnBack;

        private Slider _musicSlider;
        private Slider _sfxSlider;
        private DropdownField _resolutionDropdown;
        private Toggle _fullscreenToggle;

        protected override void Bind()
        {
            _musicSlider = Root.Q<Slider>("opt-music");
            _sfxSlider   = Root.Q<Slider>("opt-sfx");
            _resolutionDropdown = Root.Q<DropdownField>("opt-resolution");
            _fullscreenToggle   = Root.Q<Toggle>("opt-fullscreen");

            // Popula resoluções suportadas
            var resolutions = GetSupportedResolutions();
            if (_resolutionDropdown != null)
            {
                _resolutionDropdown.choices = resolutions;
                _resolutionDropdown.value = CurrentResolutionLabel();
            }

            // Valores iniciais dos GameSettings
            if (_musicSlider != null)
            {
                _musicSlider.value = GameSettings.MusicVolume;
                _musicSlider.RegisterValueChangedCallback(evt =>
                {
                    GameSettings.MusicVolume = evt.newValue;
                    ApplyAudio();
                });
            }

            if (_sfxSlider != null)
            {
                _sfxSlider.value = GameSettings.SfxVolume;
                _sfxSlider.RegisterValueChangedCallback(evt =>
                {
                    GameSettings.SfxVolume = evt.newValue;
                    AudioManager.I?.Play(AudioManager.I.sfxUIClick); // feedback sonoro
                });
            }

            if (_resolutionDropdown != null)
            {
                _resolutionDropdown.RegisterValueChangedCallback(evt => ApplyResolution(evt.newValue));
            }

            if (_fullscreenToggle != null)
            {
                _fullscreenToggle.value = GameSettings.Fullscreen;
                _fullscreenToggle.RegisterValueChangedCallback(evt =>
                {
                    GameSettings.Fullscreen = evt.newValue;
                    GameSettings.Apply();
                });
            }

            var back = Root.Q<Button>("opt-back");
            if (back != null) back.clicked += () => OnBack?.Invoke();
        }

        private void ApplyAudio()
        {
            var audio = AudioManager.I;
            if (audio != null && audio.musicSource != null)
                audio.musicSource.volume = GameSettings.MusicVolume;
        }

        private void ApplyResolution(string label)
        {
            var (w, h) = ParseResolution(label);
            if (w > 0 && h > 0)
            {
                GameSettings.ResolutionWidth = w;
                GameSettings.ResolutionHeight = h;
                GameSettings.Apply();
            }
        }

        private static System.Collections.Generic.List<string> GetSupportedResolutions()
        {
            var list = new System.Collections.Generic.List<string>
            {
                "1280x720", "1366x768", "1600x900", "1920x1080", "2560x1440"
            };
            return list;
        }

        private static string CurrentResolutionLabel()
        {
            return $"{GameSettings.ResolutionWidth}x{GameSettings.ResolutionHeight}";
        }

        private static (int, int) ParseResolution(string label)
        {
            var parts = label.Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
                return (w, h);
            return (0, 0);
        }
    }
}

