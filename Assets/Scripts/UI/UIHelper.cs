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
            if (!PointerInput.Available) return false;
            var pos = PointerInput.Position;
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

        /// <summary>
        /// Posição de tela (pixels, origem embaixo à esquerda, como o Input System entrega) → posição no
        /// painel da UI Toolkit (unidades do painel, origem em cima à esquerda). Único ponto de conversão:
        /// passar pixels crus para Pick/worldBound erra a escala no celular e espelha o Y. (2026-11-14)
        /// </summary>
        public static Vector2 ScreenToPanel(IPanel panel, Vector2 screenPos)
        {
            return RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPos.x, Screen.height - screenPos.y));
        }

        /// <summary>
        /// Mantém a proporção: altura = largura × ratio. Larguras vêm em % da tela pelo USS; altura em % encolheria
        /// em telas largas, então ela segue a largura (2026-11-14).
        /// </summary>
        public static void KeepAspect(VisualElement e, float ratio)
        {
            if (e == null) return;
            e.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float w = e.layout.width;
                if (float.IsNaN(w) || w <= 0f) return;
                float h = w * ratio;
                if (Mathf.Abs(e.resolvedStyle.height - h) > 0.5f) e.style.height = h;
            });
        }

        /// <summary>
        /// Diminui a fonte do rótulo até o texto caber na largura do pai (mínimo minSize). A caixa vem em % da
        /// tela e encolhe em telas pequenas; com fonte fixa o texto era cortado (auditoria de UI, 2026-11-14).
        /// </summary>
        public static void FitText(Label l, float minSize = 7f)
        {
            if (l == null) return;
            float baseSize = -1f;
            void Fit()
            {
                var p = l.parent;
                if (p == null || string.IsNullOrEmpty(l.text)) return;
                float avail = p.contentRect.width - l.resolvedStyle.marginLeft - l.resolvedStyle.marginRight;
                if (float.IsNaN(avail) || avail <= 0f) return;
                if (baseSize < 0f) baseSize = l.resolvedStyle.fontSize;
                if (baseSize <= 0f) return;
                l.style.fontSize = baseSize;
                float need = l.MeasureTextSize(l.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
                float size = need > avail ? Mathf.Max(minSize, Mathf.Floor(baseSize * avail / need)) : baseSize;
                if (Mathf.Abs(l.resolvedStyle.fontSize - size) > 0.1f || need > avail) l.style.fontSize = size;
            }
            l.RegisterCallback<AttachToPanelEvent>(_ => l.parent?.RegisterCallback<GeometryChangedEvent>(__ => Fit()));
            l.RegisterCallback<GeometryChangedEvent>(_ => Fit());
        }

        /// <summary>Texto para busca simples: minúsculo e sem acento ("Água" acha "agua"). 2026-11-14.</summary>
        public static string Fold(string t)
        {
            if (string.IsNullOrEmpty(t)) return "";
            var n = t.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder(n.Length);
            foreach (char c in n)
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString();
        }

        public static bool IsPositionOverClickableUI(Vector2 screenPos)
        {
            var docs = UnityEngine.Object.FindObjectsOfType<UIDocument>();
            foreach (var doc in docs)
            {
                if (doc == null || !doc.enabled || doc.rootVisualElement == null) continue;
                var panel = doc.rootVisualElement.panel;
                if (panel == null) continue;
                var ele = panel.Pick(ScreenToPanel(panel, screenPos));
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
