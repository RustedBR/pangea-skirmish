using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace PangeaSkirmish.UI
{
    /// <summary>
    /// Helpers para conviver com UI Toolkit a partir de Input do gameplay
    /// (câmera, grid, etc.) — substituto do EventSystem.IsPointerOverGameObject()
    /// que não funciona com UIDocument (Canvas UGUI vazio dispara falso positivo).
    /// </summary>
    public static class UIHelper
    {
        /// <summary>
        /// Retorna true se o ponteiro do mouse está sobre um elemento clicável da UI
        /// (Button, Toggle, ou qualquer elemento com classe .pg-button). Usado para
        /// não pintar o grid / não dar zoom quando o jogador está clicando na UI.
        /// </summary>
        public static bool IsPointerOverClickableUI()
        {
            if (Mouse.current == null) return false;
            var pos = Mouse.current.position.ReadValue();
            return IsPositionOverClickableUI(pos);
        }

        /// <summary>
        /// True se algum campo de texto da UI está com o foco do teclado.
        ///
        /// Usado para não deixar teclas de gameplay dispararem enquanto o jogador
        /// digita: sem isso, nomear um mapa no Sandbox com uma palavra contendo
        /// "q" ou "e" girava a vista do mapa a cada letra.
        /// </summary>
        public static bool IsTextInputFocused()
        {
            var docs = UnityEngine.Object.FindObjectsOfType<UIDocument>();
            foreach (var doc in docs)
            {
                if (doc == null || !doc.enabled || doc.rootVisualElement == null) continue;
                var panel = doc.rootVisualElement.panel;
                var focused = panel?.focusController?.focusedElement as VisualElement;
                if (focused == null) continue;
                // O TextField delega o foco a um TextElement interno, então não basta
                // testar o próprio elemento focado — subimos até achar um TextField.
                if (focused is TextField) return true;
                if (focused.GetFirstAncestorOfType<TextField>() != null) return true;
            }
            return false;
        }

        public static bool IsPositionOverClickableUI(Vector2 screenPos)
        {
            var docs = UnityEngine.Object.FindObjectsOfType<UIDocument>();
            foreach (var doc in docs)
            {
                if (doc == null || !doc.enabled || doc.rootVisualElement == null) continue;
                var panel = doc.rootVisualElement.panel;
                if (panel == null) continue;
                var ele = panel.Pick(screenPos);
                if (ele == null) continue;
                if (ele is Button || ele is Toggle) return true;
                // classes de botão do tema (pg-button, bh-menu-btn, etc.)
                if (ele.ClassListContains("pg-button")
                    || ele.ClassListContains("bh-menu-btn")
                    || ele.ClassListContains("bh-confirm-btn")
                    || ele.ClassListContains("bh-inline-btn")
                    || ele.ClassListContains("bh-prompt-btn")) return true;
            }
            return false;
        }
    }
}
