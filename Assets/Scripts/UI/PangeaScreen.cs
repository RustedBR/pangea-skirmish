using UnityEngine;
using UnityEngine.UIElements;

namespace PangeaSkirmish.UI
{
    /// <summary>
    /// Classe-base de toda tela em UI Toolkit do Pangea Skirmish.
    ///
    /// Carrega o PanelSettings compartilhado e o UXML da tela via Resources (mesmo estilo
    /// Resources-first do resto do projeto), monta a árvore visual e chama Bind() — onde o
    /// controller concreto liga os elementos nomeados do UXML à lógica do jogo. Substitui os
    /// métodos BuildXxxPanel() gigantes da UI UGUI antiga por um Bind() enxuto sobre o layout.
    ///
    /// Uso:
    ///   var screen = PangeaScreen.Spawn&lt;MainMenuScreen&gt;("MainMenu");   // cria GameObject + UIDocument
    ///   screen.SetVisible(false);                                       // esconder/mostrar
    ///
    /// Cada tela concreta:
    ///   - define UxmlResource (ex.: "UI/Screens/MainMenu")
    ///   - implementa Bind() consultando Root.Q&lt;T&gt;("nome") e ligando callbacks.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public abstract class PangeaScreen : MonoBehaviour
    {
        public const string PanelSettingsResource = "UI/PangeaPanelSettings";

        private static PanelSettings _sharedPanel;

        protected UIDocument Document { get; private set; }

        /// <summary>Raiz da árvore visual desta tela (null até Awake rodar).</summary>
        protected VisualElement Root => Document != null ? Document.rootVisualElement : null;

        /// <summary>Caminho Resources do UXML da tela, sem extensão. Ex.: "UI/Screens/MainMenu".</summary>
        protected abstract string UxmlResource { get; }

        private bool _bound;

        protected virtual void Awake()
        {
            Document = GetComponent<UIDocument>();
            if (Document == null) Document = gameObject.AddComponent<UIDocument>();

            if (_sharedPanel == null)
            {
                _sharedPanel = Resources.Load<PanelSettings>(PanelSettingsResource);
                if (_sharedPanel == null)
                    Debug.LogError($"[PangeaScreen] PanelSettings não encontrado em Resources/{PanelSettingsResource}. " +
                                   "Rode 'Pangea Skirmish/UI/Setup UI Toolkit' no Editor.");
            }
            Document.panelSettings = _sharedPanel;

            var uxml = Resources.Load<VisualTreeAsset>(UxmlResource);
            if (uxml == null)
                Debug.LogError($"[PangeaScreen] UXML não encontrado em Resources/{UxmlResource} " +
                               $"({GetType().Name}). Crie a tela via 'Pangea Skirmish/UI/New UI Screen…'.");
            Document.visualTreeAsset = uxml; // atribuir reconstrói a árvore imediatamente
        }

        protected virtual void OnEnable()  => TryBind();
        protected virtual void OnDisable() => _bound = false;

        private void TryBind()
        {
            if (_bound || Document == null || Document.visualTreeAsset == null || Root == null) return;
            _bound = true;
            Bind();
        }

        /// <summary>Liga os elementos nomeados do UXML à lógica. Chamado 1× quando a árvore está pronta.</summary>
        protected abstract void Bind();

        /// <summary>Mostra/esconde a tela sem destruí-la (mantém estado).</summary>
        public void SetVisible(bool visible)
        {
            if (Root != null)
                Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Cria um GameObject com UIDocument + o controller da tela e o retorna.</summary>
        public static T Spawn<T>(string goName) where T : PangeaScreen
        {
            var go = new GameObject(goName, typeof(UIDocument));
            return go.AddComponent<T>();
        }
    }
}
