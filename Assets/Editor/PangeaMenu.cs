namespace PangeaSkirmish.Editor
{
    /// <summary>
    /// Raiz ÚNICA de todos os menus de editor do projeto.
    /// Todo [MenuItem(...)] das ferramentas de editor deve derivar destas constantes
    /// para que tudo fique sob a mesma aba "Pangea Skirmish" e nunca mais divirja
    /// entre dois roots ("Pangea" vs "Pangea Skirmish"), como acontecia antes.
    ///
    /// Uso: [MenuItem(PangeaMenu.Animation + "Weapon Anim Editor")]
    /// (concatenação de const string é constante em tempo de compilação — válida em atributos.)
    /// </summary>
    internal static class PangeaMenu
    {
        public const string Root = "Pangea Skirmish/";

        public const string Build     = Root + "Build/";      // builds e utilitários de build
        public const string Animation = Root + "Animation/";  // criação/edição de animações
        public const string UI        = Root + "UI/";         // ferramentas de UI
        public const string Content   = Root + "Content/";    // geração de conteúdo (unidades, etc.)
        public const string Debug     = Root + "Debug/";      // testes/diagnóstico em Play mode
        public const string Project   = Root + "Project/";    // setup de projeto/cenas
    }
}
