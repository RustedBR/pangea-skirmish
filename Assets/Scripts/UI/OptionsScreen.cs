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
                ShowPercent(_musicSlider);
                _musicSlider.RegisterValueChangedCallback(evt =>
                {
                    GameSettings.MusicVolume = evt.newValue;
                    ShowPercent(_musicSlider);
                    ApplyAudio();
                });
            }

            if (_sfxSlider != null)
            {
                _sfxSlider.value = GameSettings.SfxVolume;
                ShowPercent(_sfxSlider);
                _sfxSlider.RegisterValueChangedCallback(evt =>
                {
                    GameSettings.SfxVolume = evt.newValue;
                    ShowPercent(_sfxSlider);
                    AudioManager.I?.Play(AudioManager.I.sfxUIClick); // feedback sonoro
                });
            }

            // Tamanho da interface: aplica ao soltar (mudar a escala durante o arrasto move o próprio slider).
            var ui = Root.Q<Slider>("opt-uiscale");
            var uiLbl = Root.Q<Label>("lbl-uiscale");
            if (ui != null)
            {
                ui.lowValue = GameSettings.UiScaleMin; ui.highValue = GameSettings.UiScaleMax;
                ui.SetValueWithoutNotify(GameSettings.UiScale);
                bool dragging = false;
                void Label() { if (uiLbl != null) uiLbl.text = $"Tamanho da interface · {Mathf.RoundToInt(ui.value * 100)}%"; }
                Label();
                ui.RegisterValueChangedCallback(evt =>
                {
                    float v = Mathf.Round(evt.newValue / 0.05f) * 0.05f;
                    if (!Mathf.Approximately(v, evt.newValue)) { ui.SetValueWithoutNotify(v); }
                    Label();
                    if (!dragging) GameSettings.UiScale = v;
                });
                ui.RegisterCallback<PointerCaptureEvent>(_ => dragging = true, TrickleDown.TrickleDown);
                ui.RegisterCallback<PointerCaptureOutEvent>(_ => { dragging = false; GameSettings.UiScale = ui.value; }, TrickleDown.TrickleDown);
            }

            var plate = Root.Q<Slider>("opt-plate");
            if (plate != null)
            {
                plate.lowValue = 0.7f; plate.highValue = 1.6f;
                plate.value = GameSettings.PlateScale;
                plate.RegisterValueChangedCallback(evt => GameSettings.PlateScale = evt.newValue);
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
                audio.musicSource.volume = GameSettings.MusicGain;
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

        /// <summary>Escreve a posição do slider em % no rótulo dele ("Música · 70%").</summary>
        private void ShowPercent(Slider s)
        {
            if (s == null || Root == null) return;
            var lbl = Root.Q<Label>(s.name == "opt-music" ? "lbl-music" : "lbl-sfx");
            if (lbl == null) return;
            string baseText = s.name == "opt-music" ? "Música" : "Efeitos (SFX)";
            lbl.text = $"{baseText} · {Mathf.RoundToInt(s.value * 100)}%";
        }
    }
}

