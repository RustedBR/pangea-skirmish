using System.Collections;
using NUnit.Framework;
using PangeaSkirmish;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

/// <summary>Toque (2026-11-14): dois toques sem ação armada, um toque com ação armada, ✓, arrasto e pinça.</summary>
public class TouchInputTests : InputTestFixture
{
    private Touchscreen _ts;

    public override void Setup()
    {
        base.Setup();
        // O módulo de UI do Input System segura ações da instância trocada pelo fixture e estoura
        // NullReference no "UI/Click". É do ambiente de teste; o que importa aqui são os Asserts.
        UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
        foreach (var d in Object.FindObjectsByType<UnityEngine.UIElements.UIDocument>(FindObjectsSortMode.None)) Object.Destroy(d.gameObject);
        // Sobras de testes anteriores (EventSystem da partida em rede) ligam ações de UI à instância
        // velha do Input System e estouram NullReference quando o fixture troca os dispositivos.
        foreach (var es in Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None)) Object.Destroy(es.gameObject);
        _ts = InputSystem.AddDevice<Touchscreen>();
        PointerInput.ResetPendingTap();
        PointerInput.SingleTapMode = false;
    }

    public override void TearDown() { PointerInput.SingleTapMode = false; base.TearDown(); }

    private IEnumerator Tap(Vector2 p, int id = 1)
    {
        BeginTouch(id, p); yield return null; _ = PointerInput.Position;
        EndTouch(id, p); yield return null;
    }

    [UnityTest]
    public IEnumerator WithoutArmedAction_FirstTapPreviews_SecondConfirms()
    {
        UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;   // NRE interno do InputForUI sob o fixture
        yield return null;
        yield return Tap(new Vector2(400, 300));
        Assert.IsFalse(PointerInput.ClickThisFrame, "1º toque só mostra");
        Assert.IsTrue(PointerInput.HasPendingTap);
        yield return Tap(new Vector2(405, 302), 2);
        Assert.IsTrue(PointerInput.ClickThisFrame, "2º toque no mesmo ponto confirma");
    }

    [UnityTest]
    public IEnumerator WithArmedAction_OneTapConfirms()
    {
        UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;   // NRE interno do InputForUI sob o fixture
        yield return null;
        PointerInput.SingleTapMode = true;
        yield return Tap(new Vector2(400, 300));
        Assert.IsTrue(PointerInput.ClickThisFrame, "ação armada: um toque no alvo já vale");
    }

    [UnityTest]
    public IEnumerator ConfirmButton_UsesFirstTap()
    {
        UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;   // NRE interno do InputForUI sob o fixture
        yield return null;
        yield return Tap(new Vector2(420, 260));
        PointerInput.RequestConfirm();
        bool clicked = false;
        for (int i = 0; i < 2 && !clicked; i++) { yield return new WaitForEndOfFrame(); clicked = PointerInput.ClickThisFrame; }
        Assert.IsTrue(clicked);
        Assert.AreEqual(new Vector2(420, 260), PointerInput.Position);
    }

    [UnityTest]
    public IEnumerator Drag_Pans_AndPinch_Zooms_WithoutClicking()
    {
        UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;   // NRE interno do InputForUI sob o fixture
        yield return null;
        BeginTouch(1, new Vector2(100, 100)); yield return null; _ = PointerInput.Position;
        MoveTouch(1, new Vector2(100 + PointerInput.DragThreshold * 3f, 100)); yield return null;
        Assert.IsTrue(PointerInput.Dragging);
        EndTouch(1, new Vector2(100 + PointerInput.DragThreshold * 3f, 100)); yield return null;
        Assert.IsFalse(PointerInput.ClickThisFrame);

        BeginTouch(2, new Vector2(300, 300)); BeginTouch(3, new Vector2(400, 300)); yield return null; _ = PointerInput.PinchDelta;
        MoveTouch(2, new Vector2(250, 300)); MoveTouch(3, new Vector2(450, 300)); yield return null;
        Assert.Greater(PointerInput.PinchDelta, 50f);
        EndTouch(2, new Vector2(250, 300)); EndTouch(3, new Vector2(450, 300)); yield return null;
        Assert.IsFalse(PointerInput.ClickThisFrame);
    }
}
