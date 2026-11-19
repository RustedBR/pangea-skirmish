using UnityEngine;
using UnityEngine.UIElements;

namespace PangeaSkirmish
{
    /// <summary>
    /// Overlay do tutorial (GDD-027 §7): painel não-bloqueante com instrução da lição, progresso,
    /// feedback e botões Pular/Repetir/Próxima. Construído em código (UIDocument próprio), aditivo —
    /// não toca na BattleHUD. O container raiz tem pickingMode Ignore para o jogo continuar recebendo
    /// input; só o painel e seus botões interceptam cliques.
    /// </summary>
    public class TutorialOverlay : MonoBehaviour
    {
        private UIDocument _doc;
        private Label _progress, _title, _instruction, _feedback;
        private Button _skip, _repeat, _next;
        private VisualElement _panel, _buttons;

        public System.Action OnSkip, OnRepeat, OnNext;

        private void Awake()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null) _doc = gameObject.AddComponent<UIDocument>();
            var panel = Resources.Load<PanelSettings>("UI/PangeaPanelSettings");
            if (panel != null) _doc.panelSettings = panel;
            _doc.sortingOrder = 9000;   // acima do HUD, abaixo de nothing crítico
            Build();
        }

        private void Build()
        {
            var root = _doc.rootVisualElement;
            root.Clear();
            root.pickingMode = PickingMode.Ignore;   // deixa o input do jogo passar
            root.style.width = Length.Percent(100);
            root.style.height = Length.Percent(100);

            _panel = new VisualElement();
            _panel.pickingMode = PickingMode.Position;
            _panel.style.position = Position.Absolute;
            _panel.style.left = 16;
            _panel.style.top = 48;
            _panel.style.maxWidth = 380;
            _panel.style.backgroundColor = new Color(0.95f, 0.91f, 0.82f, 0.97f); // pergaminho
            _panel.style.borderTopLeftRadius = _panel.style.borderTopRightRadius =
                _panel.style.borderBottomLeftRadius = _panel.style.borderBottomRightRadius = 10;
            _panel.style.borderTopWidth = _panel.style.borderBottomWidth =
                _panel.style.borderLeftWidth = _panel.style.borderRightWidth = 2;
            var border = new Color(0.42f, 0.29f, 0.12f, 1f);
            _panel.style.borderTopColor = _panel.style.borderBottomColor =
                _panel.style.borderLeftColor = _panel.style.borderRightColor = border;
            _panel.style.paddingTop = _panel.style.paddingBottom = 12;
            _panel.style.paddingLeft = _panel.style.paddingRight = 14;
            root.Add(_panel);

            _progress = new Label { style = { fontSize = 11, color = new Color(0.5f, 0.38f, 0.2f), marginBottom = 2 } };
            _progress.style.unityFontStyleAndWeight = FontStyle.Bold;
            _panel.Add(_progress);

            _title = new Label { style = { fontSize = 16, color = border, marginBottom = 4 } };
            _title.style.unityFontStyleAndWeight = FontStyle.Bold;
            _panel.Add(_title);

            _instruction = new Label { style = { fontSize = 13, color = new Color(0.2f, 0.15f, 0.08f), whiteSpace = WhiteSpace.Normal } };
            _panel.Add(_instruction);

            _feedback = new Label { style = { fontSize = 13, color = new Color(0.1f, 0.4f, 0.15f), whiteSpace = WhiteSpace.Normal, marginTop = 6, display = DisplayStyle.None } };
            _feedback.style.unityFontStyleAndWeight = FontStyle.Bold;
            _panel.Add(_feedback);

            _buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 10 } };
            _panel.Add(_buttons);

            _repeat = MakeButton("Repetir", () => OnRepeat?.Invoke());
            _skip = MakeButton("Pular", () => OnSkip?.Invoke());
            _next = MakeButton("Próxima ▸", () => OnNext?.Invoke());
            _next.style.display = DisplayStyle.None;
            _buttons.Add(_repeat); _buttons.Add(_skip); _buttons.Add(_next);
        }

        private Button MakeButton(string text, System.Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.style.marginRight = 8;
            b.style.paddingLeft = b.style.paddingRight = 12;
            b.style.paddingTop = b.style.paddingBottom = 6;
            b.style.fontSize = 12;
            b.style.backgroundColor = new Color(0.85f, 0.78f, 0.62f);
            b.style.color = new Color(0.25f, 0.18f, 0.08f);
            b.style.borderTopLeftRadius = b.style.borderTopRightRadius =
                b.style.borderBottomLeftRadius = b.style.borderBottomRightRadius = 6;
            b.style.borderTopWidth = b.style.borderBottomWidth = b.style.borderLeftWidth = b.style.borderRightWidth = 1;
            b.style.borderTopColor = b.style.borderBottomColor = b.style.borderLeftColor = b.style.borderRightColor = new Color(0.42f, 0.29f, 0.12f);
            return b;
        }

        /// <summary>Mostra a instrução da lição (estado esperando o jogador agir).</summary>
        public void ShowLesson(string progress, string title, string instruction)
        {
            if (_progress != null) _progress.text = progress;
            if (_title != null) _title.text = title;
            if (_instruction != null) _instruction.text = instruction;
            if (_feedback != null) _feedback.style.display = DisplayStyle.None;
            if (_next != null) _next.style.display = DisplayStyle.None;
            if (_skip != null) _skip.style.display = DisplayStyle.Flex;
            if (_repeat != null) _repeat.style.display = DisplayStyle.Flex;
            gameObject.SetActive(true);
        }

        /// <summary>Mostra o resultado da mecânica (gate fechou) + botão Próxima.</summary>
        public void ShowFeedback(string title, string feedback, bool ok)
        {
            if (_title != null) _title.text = title;
            if (_instruction != null) _instruction.text = "";
            if (_feedback != null)
            {
                _feedback.style.display = DisplayStyle.Flex;
                _feedback.style.color = ok ? new Color(0.1f, 0.4f, 0.15f) : new Color(0.6f, 0.15f, 0.1f);
                _feedback.text = (ok ? "✓ " : "✗ ") + feedback;
            }
            if (_next != null) _next.style.display = DisplayStyle.Flex;
            if (_skip != null) _skip.style.display = DisplayStyle.None;
            if (_repeat != null) _repeat.style.display = DisplayStyle.None;
        }

        /// <summary>Relatório final do banco de testes.</summary>
        public void ShowReport(string report)
        {
            if (_progress != null) _progress.text = "Tutorial concluído";
            if (_title != null) _title.text = "Relatório de mecânicas";
            if (_instruction != null) _instruction.text = report;
            if (_feedback != null) _feedback.style.display = DisplayStyle.None;
            if (_buttons != null) _buttons.style.display = DisplayStyle.None;
        }

        public void HideOverlay() => gameObject.SetActive(false);
    }
}