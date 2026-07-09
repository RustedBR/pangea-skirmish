using System;
using UnityEngine.UIElements;

namespace PangeaSkirmish.UI
{
    /// <summary>
    /// Menu principal em UI Toolkit (tela-piloto da migração UGUI → UI Toolkit).
    /// Layout em Resources/UI/Screens/MainMenu.uxml (edite no UI Builder).
    /// As ações são injetadas por quem cria a tela (MainMenuManager) via os callbacks
    /// públicos — lidos no clique, então podem ser atribuídos depois de Spawn().
    /// </summary>
    public class MainMenuScreen : PangeaScreen
    {
        protected override string UxmlResource => "UI/Screens/MainMenu";

        public Action OnMultiplayer;
        public Action OnCreateMap;
        public Action OnCreateCharacter;
        public Action OnOptions;
        public Action OnQuit;

        protected override void Bind()
        {
            var cm = Root.Q<Button>("btn-create-map");
            if (cm != null) cm.clicked += () => OnCreateMap?.Invoke();

            var cc = Root.Q<Button>("btn-create-char");
            if (cc != null) cc.clicked += () => OnCreateCharacter?.Invoke();

            var mp = Root.Q<Button>("btn-multiplayer");
            if (mp != null) mp.clicked += () => OnMultiplayer?.Invoke();

            var opt = Root.Q<Button>("btn-options");
            if (opt != null) opt.clicked += () => OnOptions?.Invoke();

            var quit = Root.Q<Button>("btn-quit");
            if (quit != null) quit.clicked += () => OnQuit?.Invoke();
        }
    }
}
