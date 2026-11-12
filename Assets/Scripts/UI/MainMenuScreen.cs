using System;
using UnityEngine.UIElements;

namespace PangeaSkirmish.UI
{
    /// <summary>
    /// Menu principal (Resources/UI/Screens/MainMenu.uxml). Mostra quem é o jogador (nome e
    /// personagem em uso, do PlayerProfile) e leva a Jogar, Perfil, Opções e Sair. As ações são
    /// injetadas pelo MainMenuManager.
    /// </summary>
    public class MainMenuScreen : PangeaScreen
    {
        protected override string UxmlResource => "UI/Screens/MainMenu";

        public Action OnMultiplayer;
        public Action OnProfile;
        public Action OnOptions;
        public Action OnQuit;

        private Label _name, _char;

        protected override void Bind()
        {
            _name = Root.Q<Label>("profile-name");
            _char = Root.Q<Label>("profile-char");

            var mp = Root.Q<Button>("btn-multiplayer");
            if (mp != null) mp.clicked += () => OnMultiplayer?.Invoke();
            var pf = Root.Q<Button>("btn-profile");
            if (pf != null) pf.clicked += () => OnProfile?.Invoke();
            var opt = Root.Q<Button>("btn-options");
            if (opt != null) opt.clicked += () => OnOptions?.Invoke();
            var quit = Root.Q<Button>("btn-quit");
            if (quit != null) quit.clicked += () => OnQuit?.Invoke();
            RefreshProfile();
        }

        /// <summary>Atualiza o cartão do jogador a partir do perfil salvo.</summary>
        public void RefreshProfile()
        {
            if (_name == null || _char == null) return;
            _name.text = PlayerProfile.HasName ? PlayerProfile.Name : "Sem nome";
            var c = PlayerProfile.Selected;
            bool ready = PlayerProfile.IsReady;
            _char.text = ready
                ? $"{c.presetName} · {RoomHUD.DescribeCharacter(c).Split('\n')[0]}"
                : PlayerProfile.MissingText + " Abra o Perfil para começar.";
            _char.EnableInClassList("main-menu__profile-char--missing", !ready);
        }
    }
}
