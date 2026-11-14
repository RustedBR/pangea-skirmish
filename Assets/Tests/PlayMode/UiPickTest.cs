using System.Collections;
using NUnit.Framework;
using PangeaSkirmish;
using PangeaSkirmish.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>2026-11-14: pixels de tela → painel da UI Toolkit. Um botão de verdade precisa ser achado
/// pelo ponto de TELA onde ele aparece, e não pelo ponto espelhado na vertical.</summary>
public class UiPickTest
{
    [UnityTest]
    public IEnumerator ScreenPoint_FindsTheButtonUnderIt_NotTheMirroredOne()
    {
        LogAssert.ignoreFailingMessages = true;
        var screen = PangeaScreen.Spawn<MainMenuScreen>("MainMenuScreen");
        for (int i = 0; i < 4; i++) yield return null;
        var root = screen.GetComponent<UIDocument>().rootVisualElement;
        var btn = root.Q<Button>("btn-options");
        Assert.IsNotNull(btn);
        var c = btn.worldBound.center;                                   // unidades do painel, origem em cima
        float k = Screen.width / root.panel.visualTree.layout.width;     // escala painel → pixels
        var px = new Vector2(c.x * k, Screen.height - c.y * k);          // pixels, origem embaixo
        Assume.That(Mathf.Abs(px.y - Screen.height * 0.5f) > 30f, "botão no meio exato da tela não distingue o espelho");

        Assert.Less(Vector2.Distance(c, UIHelper.ScreenToPanel(root.panel, px)), 2f, "tela → painel volta ao centro do botão");
        Assert.IsTrue(UIHelper.IsPositionOverClickableUI(px), "acha o botão sob o ponto de tela");
        var mirrored = new Vector2(px.x, Screen.height - px.y);
        var hit = root.panel.Pick(UIHelper.ScreenToPanel(root.panel, mirrored));
        Assert.AreNotSame(btn, hit, "o ponto espelhado não é o botão");
        Object.Destroy(screen.gameObject);
        yield return null;
    }
}
