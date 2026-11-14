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
}
