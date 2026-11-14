using System.Collections;
using NUnit.Framework;
using PangeaSkirmish;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

/// <summary>GDD-022: regra dos dois toques, arrasto e pinça do PointerInput.</summary>
public class PointerInputTouchTest : InputTestFixture
{
    private Touchscreen _ts;

    public override void Setup()
    {
        base.Setup();
        _ts = InputSystem.AddDevice<Touchscreen>();
        PointerInput.ResetPendingTap();
    }

    private IEnumerator Tap(Vector2 p, int id = 1)
    {
        BeginTouch(id, p); yield return null; _ = PointerInput.Position;
        EndTouch(id, p); yield return null;
    }

    [UnityTest]
    public IEnumerator FirstTapPreviews_SecondTapSameSpotClicks()
    {
        yield return Tap(new Vector2(200, 200));
        Assert.IsFalse(PointerInput.ClickThisFrame, "1º toque só aponta");
        Assert.AreEqual(new Vector2(200, 200), PointerInput.Position);
        Assert.IsTrue(PointerInput.IsTouch);

        yield return Tap(new Vector2(210, 205), 2);
        Assert.IsTrue(PointerInput.ClickThisFrame, "2º toque perto do 1º confirma");
    }

    [UnityTest]
    public IEnumerator SecondTapElsewhere_OnlyMovesPreview()
    {
        yield return Tap(new Vector2(200, 200));
        yield return Tap(new Vector2(600, 400), 2);
        Assert.IsFalse(PointerInput.ClickThisFrame, "toque longe vira nova prévia");
        Assert.AreEqual(new Vector2(600, 400), PointerInput.Position);
        yield return Tap(new Vector2(600, 400), 3);
        Assert.IsTrue(PointerInput.ClickThisFrame);
    }

    [UnityTest]
    public IEnumerator OneFingerDrag_PansAndIsNotATap()
    {
        BeginTouch(1, new Vector2(100, 100)); yield return null; _ = PointerInput.Position;
        MoveTouch(1, new Vector2(160, 100)); yield return null;
        Assert.IsTrue(PointerInput.Dragging, "arrasto de 60 px vira pan");
        EndTouch(1, new Vector2(160, 100)); yield return null;
        Assert.IsFalse(PointerInput.ClickThisFrame);
        yield return Tap(new Vector2(160, 100), 2);
        Assert.IsFalse(PointerInput.ClickThisFrame, "o arrasto não conta como 1º toque");
    }

    [UnityTest]
    public IEnumerator TwoFingerPinch_ReportsSpread()
    {
        BeginTouch(1, new Vector2(300, 300)); BeginTouch(2, new Vector2(400, 300));
        yield return null; _ = PointerInput.PinchDelta;
        MoveTouch(1, new Vector2(250, 300)); MoveTouch(2, new Vector2(450, 300));
        yield return null;
        Assert.Greater(PointerInput.PinchDelta, 50f, "abrir os dedos = pinça positiva");
        EndTouch(1, new Vector2(250, 300)); EndTouch(2, new Vector2(450, 300));
        yield return null;
        Assert.IsFalse(PointerInput.ClickThisFrame, "pinça não vira toque");
    }

    /// <summary>2026-11-14: celular tem resolução real alta; um tremor de dedo de ~20 px não pode virar arrasto,
    /// e o 2º toque a ~50 px do 1º ainda confirma (antes: 14 px e 40 px fixos).</summary>
    [UnityTest]
    public IEnumerator HighResPhone_SmallJitterIsStillATap()
    {
        Assume.That(Mathf.Min(Screen.width, Screen.height) >= 600, "tela do teste pequena demais para medir a escala");
        BeginTouch(1, new Vector2(300, 300)); yield return null; _ = PointerInput.Position;
        MoveTouch(1, new Vector2(300 + PointerInput.DragThreshold * 0.8f, 300)); yield return null; _ = PointerInput.Position;
        EndTouch(1, new Vector2(300 + PointerInput.DragThreshold * 0.8f, 300)); yield return null;
        Assert.IsFalse(PointerInput.Dragging, "tremor abaixo do limite não vira arrasto");
        var second = new Vector2(300 + PointerInput.ConfirmRadius * 0.9f, 300);
        yield return Tap(second, 2);
        Assert.IsTrue(PointerInput.ClickThisFrame, "2º toque dentro do raio escalado confirma");
        Assert.Greater(PointerInput.ConfirmRadius, PointerInput.ConfirmRadiusBase - 0.01f);
    }

    /// <summary>2026-11-14: o botão ✓ do celular confirma o 1º toque sem precisar acertar o mesmo ponto.</summary>
    [UnityTest]
    public IEnumerator ConfirmButton_ClicksAtFirstTap()
    {
        PointerInput.RequestConfirm(); yield return null;
        Assert.IsFalse(PointerInput.ClickThisFrame, "sem 1º toque, o ✓ não faz nada");
        // Um ponto livre: telas deixadas por outros testes podem ter um botão ali, e toque em botão não é toque no mundo.
        var at = new Vector2(420, 260);
        for (int i = 0; i < 40 && PangeaSkirmish.UI.UIHelper.IsPositionOverClickableUI(at); i++) at = new Vector2(30 + i * 37 % Screen.width, 20 + i * 23 % Screen.height);
        Assume.That(!PangeaSkirmish.UI.UIHelper.IsPositionOverClickableUI(at), "sem ponto livre de UI na tela do teste");
        yield return Tap(at);
        Assert.IsTrue(PointerInput.HasPendingTap);
        PointerInput.RequestConfirm();
        // O clique vale no quadro em que alguém lê o input primeiro: este (LateUpdate de uma câmera) ou o próximo.
        bool clicked = false;
        for (int i = 0; i < 2 && !clicked; i++) { yield return new WaitForEndOfFrame(); clicked = PointerInput.ClickThisFrame; }
        Assert.IsTrue(clicked, "✓ vira o clique");
        Assert.AreEqual(at, PointerInput.Position, "no ponto do 1º toque");
        Assert.IsFalse(PointerInput.HasPendingTap, "e consome a prévia");
    }
}
