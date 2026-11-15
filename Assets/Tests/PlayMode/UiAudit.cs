using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Auditoria de UI (2026-11-14): percorre a árvore visível de uma tela e lista problemas de
/// escala (sai da tela), encaixe (texto maior que a caixa), espaçamento (janela sem margem
/// interna) e contraste (texto contra o fundo real atrás dele).
/// </summary>
public static class UiAudit
{
    /// <summary>Cor média do miolo do pergaminho (Resources/UI/Theme/paper.png), fundo das janelas.</summary>
    public static readonly Color Parchment = new Color(211 / 255f, 190 / 255f, 145 / 255f);
    /// <summary>Contraste mínimo (WCAG 3:1 para texto de interface em negrito/grande).</summary>
    public const float MinContrast = 3f;
    public const float MinPadding = 3.5f;   // 4px de USS chegam como 3,99 depois da escala do painel

    public static List<string> Run(VisualElement root, string screen)
    {
        var bad = new List<string>();
        var bounds = root.worldBound;
        Walk(root, root, bounds, screen, bad, false);
        return bad;
    }

    private static bool Visible(VisualElement e) =>
        e.resolvedStyle.display != DisplayStyle.None && e.resolvedStyle.visibility != Visibility.Hidden
        && e.resolvedStyle.opacity > 0.05f && e.worldBound.width > 0.5f && e.worldBound.height > 0.5f;

    private static string Name(VisualElement e)
    {
        var sb = new StringBuilder(e.GetType().Name);
        if (!string.IsNullOrEmpty(e.name)) sb.Append('#').Append(e.name);
        foreach (var c in e.GetClasses()) { sb.Append('.').Append(c); break; }
        if (e is TextElement t && !string.IsNullOrEmpty(t.text)) sb.Append(" \"").Append(t.text.Length > 24 ? t.text.Substring(0, 24) + "…" : t.text).Append('"');
        return sb.ToString();
    }

    private static void Walk(VisualElement e, VisualElement root, Rect screenRect, string screen, List<string> bad, bool inScroll)
    {
        if (!Visible(e)) return;
        var r = e.worldBound;
        bool scrollContent = inScroll;

        // Escala: nada visível sai da tela (conteúdo rolável pode, é a rolagem que mostra).
        if (!scrollContent && e != root && e.pickingMode != PickingMode.Ignore | e is TextElement)
            if (r.xMin < screenRect.xMin - 1f || r.yMin < screenRect.yMin - 1f || r.xMax > screenRect.xMax + 1f || r.yMax > screenRect.yMax + 1f)
                bad.Add($"[{screen}] fora da tela: {Name(e)} {Fmt(r)} em {Fmt(screenRect)}");

        // Encaixe: o texto cabe na própria caixa e a caixa cabe no pai (na horizontal).
        if (e is TextElement te && !string.IsNullOrEmpty(te.text) && !(e is Button))
        {
            var p = e.parent;
            if (p != null && Visible(p) && !scrollContent && p.style.overflow.value != Overflow.Hidden && HasBox(p))
                if (r.xMin < p.worldBound.xMin - 1.5f || r.xMax > p.worldBound.xMax + 1.5f)
                    bad.Add($"[{screen}] texto vaza da caixa: {Name(e)} {Fmt(r)} no pai {Name(p)} {Fmt(p.worldBound)}");
            if (te.resolvedStyle.whiteSpace == WhiteSpace.NoWrap && te.style.overflow.value != Overflow.Hidden)
            {
                var need = te.MeasureTextSize(te.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                if (need.x > r.width + 2f) bad.Add($"[{screen}] texto maior que a caixa: {Name(e)} precisa {need.x:0} e tem {r.width:0}");
            }

            // Contraste: cor do texto contra o primeiro fundo opaco atrás dele.
            var fg = te.resolvedStyle.color;
            var bg = BackgroundBehind(e);
            if (bg.HasValue && fg.a > 0.3f)
            {
                float cr = Contrast(fg, bg.Value);
                if (cr < MinContrast) bad.Add($"[{screen}] contraste {cr:0.0}:1 baixo: {Name(e)} texto #{ColorUtility.ToHtmlStringRGB(fg)} sobre #{ColorUtility.ToHtmlStringRGB(bg.Value)}");
            }
        }

        // Espaçamento: janelas e painéis têm margem interna.
        if (e.ClassListContains("bh-window") || e.ClassListContains("pg-panel"))
        {
            var s = e.resolvedStyle;
            if (s.paddingLeft < MinPadding || s.paddingRight < MinPadding || s.paddingTop < MinPadding || s.paddingBottom < MinPadding)
                bad.Add($"[{screen}] sem margem interna: {Name(e)} ({s.paddingLeft:0},{s.paddingTop:0},{s.paddingRight:0},{s.paddingBottom:0})");
        }

        bool childScroll = scrollContent || e is ScrollView;
        foreach (var c in e.Children()) Walk(c, root, screenRect, screen, bad, childScroll);
    }

    /// <summary>A caixa aparece na tela? (fundo ou borda). Contêiner invisível só posiciona: texto fora dele não é vazamento.</summary>
    private static bool HasBox(VisualElement e)
    {
        var s = e.resolvedStyle;
        return s.backgroundColor.a > 0.05f || s.backgroundImage.texture != null || s.backgroundImage.sprite != null
            || (s.borderLeftWidth > 0f && s.borderLeftColor.a > 0.05f);
    }

    /// <summary>Fundo efetivo: sobe pela árvore até um fundo opaco (cor sólida ou textura de pergaminho).</summary>
    public static Color? BackgroundBehind(VisualElement e)
    {
        for (var a = e; a != null; a = a.parent)
        {
            var s = a.resolvedStyle;
            if (s.backgroundImage.texture != null || s.backgroundImage.sprite != null)
            {
                if (a.ClassListContains("bh-window") || a.ClassListContains("pg-panel")) return Parchment;
                continue;   // ícones e prévias: não são fundo de texto
            }
            if (s.backgroundColor.a > 0.6f) return s.backgroundColor;
        }
        return null;   // sobre o mundo 3D: sem fundo conhecido, não dá para medir
    }

    public static float Contrast(Color a, Color b)
    {
        float la = Lum(a), lb = Lum(b);
        return (Mathf.Max(la, lb) + 0.05f) / (Mathf.Min(la, lb) + 0.05f);
    }

    private static float Lum(Color c)
    {
        float Ch(float v) => v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
        return 0.2126f * Ch(c.r) + 0.7152f * Ch(c.g) + 0.0722f * Ch(c.b);
    }

    private static string Fmt(Rect r) => $"({r.xMin:0},{r.yMin:0} {r.width:0}×{r.height:0})";
}
