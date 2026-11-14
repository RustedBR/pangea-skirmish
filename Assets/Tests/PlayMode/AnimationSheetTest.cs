using System.Collections;
using System.IO;
using NUnit.Framework;
using PangeaSkirmish;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Folha de quadros (GDD-025, frente 2): grava docs/anim/&lt;ator&gt;-&lt;animação&gt;.png com 10 quadros
/// igualmente espaçados no tempo, em duas vistas (em cima: câmera do jogo; embaixo: de lado).
/// Usa Time.captureDeltaTime, então cada quadro avança um passo fixo: o resultado não depende do fps.
/// </summary>
public class AnimationSheetTest
{
    private const int Frames = 10, Size = 220;

    [UnityTest]
    public IEnumerator Sheets_AllClips()
    {
        LogAssert.ignoreFailingMessages = true;
        string dir = Path.Combine(Application.dataPath, "..", "docs", "anim");
        Directory.CreateDirectory(dir);
        var stage = AnimationStage.Create(Size);
        yield return null;
        int sheets = 0;
        try
        {
            foreach (var actor in AnimationStage.Actors)
            {
                stage.SetActor(actor.id);
                yield return null;
                Assert.IsNotNull(stage.Actor.Model3D, $"modelo 3D do ator {actor.id}");
                foreach (var clip in AnimationStage.Clips)
                {
                    // Golpes para todos; o resto só na espada e na fera (as outras armas animam igual).
                    bool attack = clip.id.StartsWith("attack");
                    bool full = actor.id == "IronSword" || (actor.id == "Fera" && clip.id != "cast" && clip.id != "release" && clip.id != "flex" && clip.id != "block");
                    if (!attack && !full) continue;

                    stage.ResetActor();
                    Time.captureDeltaTime = 0.1f;                                              // assenta por tempo, não por quadros
                    for (int k = 0; k < AnimationStage.SettleFrames; k++) yield return null;   // o modelo assenta antes do quadro 0
                    float seconds = stage.Play(clip.id);
                    Time.captureDeltaTime = seconds / (Frames - 1);
                    var sheet = new Texture2D(Size * Frames, Size * 2, TextureFormat.RGB24, false);
                    for (int i = 0; i < Frames; i++)
                    {
                        yield return new WaitForEndOfFrame();
                        var game = stage.Capture(AnimationStage.View.Game);
                        var side = stage.Capture(AnimationStage.View.Side);
                        sheet.SetPixels(i * Size, Size, Size, Size, game.GetPixels());
                        sheet.SetPixels(i * Size, 0, Size, Size, side.GetPixels());
                        Object.Destroy(game); Object.Destroy(side);
                        yield return null;
                    }
                    Time.captureDeltaTime = 0f;
                    sheet.Apply();
                    File.WriteAllBytes(Path.Combine(dir, $"{actor.id}-{clip.id}.png"), sheet.EncodeToPNG());
                    Object.Destroy(sheet);
                    sheets++;
                }
            }
        }
        finally
        {
            Time.captureDeltaTime = 0f;
            Object.Destroy(stage.gameObject);
        }
        Assert.GreaterOrEqual(sheets, 20, "folhas gravadas em docs/anim");
        yield return null;
    }
}

/// <summary>Visualizador: abre, toca uma animação, guarda nota e devolve o tempo do jogo ao normal ao fechar.</summary>
public class AnimViewerTest
{
    [UnityTest]
    public IEnumerator Viewer_Plays_SavesNotes_AndRestoresTime()
    {
        LogAssert.ignoreFailingMessages = true;
        string backup = PlayerPrefs.GetString("pangea.animnotes.v1", "");
        PlayerPrefs.DeleteKey("pangea.animnotes.v1");
        var viewer = PangeaSkirmish.UI.PangeaScreen.Spawn<PangeaSkirmish.UI.AnimViewerScreen>("AnimViewerScreen");
        for (int i = 0; i < 4; i++) yield return null;
        var root = viewer.GetComponent<UnityEngine.UIElements.UIDocument>().rootVisualElement;
        var stage = Object.FindFirstObjectByType<AnimationStage>();
        Assert.IsNotNull(stage, "palco criado");

        UnityEngine.UIElements.Button attack = null;
        root.Query<UnityEngine.UIElements.Button>().ForEach(b => { if (b.text == "Atacar") attack = b; });
        Assert.IsNotNull(attack, "botão da animação Atacar");
        using (var e = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { e.target = attack; attack.SendEvent(e); }
        float t = 0f;
        while (t < 3f && (stage.Actor == null || stage.Actor.Model3D == null || stage.Actor.Model3D.CurrentAction != "attack")) { t += Time.unscaledDeltaTime; yield return null; }
        Assert.AreEqual("attack", stage.Actor.Model3D.CurrentAction, "o golpe toca depois de assentar");

        root.Q<UnityEngine.UIElements.TextField>("notes").value = "braço sobe rápido demais";
        UnityEngine.UIElements.Button slow = null;
        root.Query<UnityEngine.UIElements.Button>().ForEach(b => { if (b.text == "0,25×" || b.text == "0.25×") slow = b; });
        Assert.IsNotNull(slow, "botão de câmera lenta");
        using (var e = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { e.target = slow; slow.SendEvent(e); }
        yield return null;
        Assert.AreEqual(0.25f, Time.timeScale, 0.001f, "câmera lenta");
        StringAssert.Contains("IronSword/attack", viewer.AllNotesText() + PlayerPrefs.GetString("pangea.animnotes.v1", ""));

        Object.Destroy(viewer.gameObject);
        yield return null; yield return null;
        Assert.AreEqual(1f, Time.timeScale, 0.001f, "fechar devolve o tempo normal");
        Assert.IsNull(Object.FindFirstObjectByType<AnimationStage>(), "palco destruído junto");
        if (backup.Length > 0) PlayerPrefs.SetString("pangea.animnotes.v1", backup); else PlayerPrefs.DeleteKey("pangea.animnotes.v1");
    }
}
