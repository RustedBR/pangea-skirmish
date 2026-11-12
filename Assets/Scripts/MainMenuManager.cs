using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace PangeaSkirmish
{
    /// <summary>
    /// Cena do menu: cria as telas (menu principal, perfil, opções, salas) e troca entre elas.
    /// Tudo em UI Toolkit, com o mesmo tema da HUD de batalha. O jogador só entra no
    /// multiplayer com o perfil pronto (nome + pelo menos um personagem); sem isso, "Jogar"
    /// abre o Perfil dizendo o que falta.
    /// </summary>
    public class MainMenuManager : MonoBehaviour
    {
        private UI.MainMenuScreen _menuScreen;
        private UI.OptionsScreen _optionsScreen;
        private UI.ProfileScreen _profileScreen;
        private RoomHUD _roomHUD;

        public UI.ProfileScreen Profile => _profileScreen;
        public RoomHUD Rooms => _roomHUD;

        private void Start()
        {
            if (AudioManager.I == null) new GameObject("AudioManager", typeof(AudioManager));
            AudioManager.I?.PlayMusic(AudioManager.I.bgmMenu);
            if (RuntimeTuning.Active == null)
            {
                var src = Resources.Load<GameTuning>("GameTuning");
                if (src != null) RuntimeTuning.Active = src;
            }
            AttributeStats.Formulas = Tuning.Get().statFormulas;

            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Tuning.Get().menuBackgroundColor;
            cam.orthographic = true;

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();
                esGo.AddComponent<InputSystemUIInputModule>();
            }

            PlayerProfile.Load();

            _menuScreen = UI.PangeaScreen.Spawn<UI.MainMenuScreen>("MainMenuScreen");
            _menuScreen.OnMultiplayer = OnPlay;
            _menuScreen.OnProfile     = () => ShowProfile("");
            _menuScreen.OnOptions     = ShowOptions;
            _menuScreen.OnQuit        = () => Application.Quit();

            _optionsScreen = UI.PangeaScreen.Spawn<UI.OptionsScreen>("OptionsScreen");
            _optionsScreen.OnBack = ShowMenu;

            _profileScreen = UI.PangeaScreen.Spawn<UI.ProfileScreen>("ProfileScreen");
            _profileScreen.OnBack = ShowMenu;
            _profileScreen.OnContinue = ShowMultiplayer;

            _roomHUD = UI.PangeaScreen.Spawn<RoomHUD>("RoomHUD");
            _roomHUD.OnBackToMenu += ShowMenu;

            ShowMenu();
        }

        /// <summary>"Jogar": exige a identidade pronta antes de qualquer modo (Coop ou PvP).</summary>
        private void OnPlay()
        {
            if (PlayerProfile.IsReady) ShowMultiplayer();
            else ShowProfile("Antes de jogar: " + PlayerProfile.MissingText);
        }

        private void HideAll()
        {
            _menuScreen?.SetVisible(false);
            _optionsScreen?.SetVisible(false);
            _profileScreen?.Hide();
            _roomHUD?.HideAll();
        }

        private void ShowMenu()
        {
            HideAll();
            _menuScreen.SetVisible(true);
            _menuScreen.RefreshProfile();
        }

        private void ShowOptions() { HideAll(); _optionsScreen.SetVisible(true); }

        private void ShowProfile(string notice) { HideAll(); _profileScreen.Show(notice); }

        private void ShowMultiplayer()
        {
            if (!PlayerProfile.IsReady) { ShowProfile("Antes de jogar: " + PlayerProfile.MissingText); return; }
            HideAll();
            _roomHUD.ShowLobbyPanel();
        }
    }
}
