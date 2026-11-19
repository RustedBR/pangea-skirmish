using System.Collections.Generic;
using NUnit.Framework;
using PangeaSkirmish;

/// <summary>Testes do motor do tutorial (GDD-027): progresso por gate, asserção, skip/repeat, relatório.</summary>
public class TutorialRunnerTests
{
    private static TutorialLesson Lesson(string id, int ch, string mech,
                                         System.Func<bool> gate = null,
                                         System.Func<string> assertion = null,
                                         System.Action setup = null)
    {
        var l = new TutorialLesson(id, ch, "Título " + id, mech, "instrução", "feedback");
        l.gate = gate; l.assertion = assertion; l.setup = setup;
        return l;
    }

    [SetUp] public void SetUp() => TutorialTestLog.Reset();

    [Test]
    public void Begin_EntersFirstLesson_RunsSetup_WaitsGate()
    {
        bool setupRan = false;
        var r = new TutorialRunner(new List<TutorialLesson>
        {
            Lesson("a", 0, "MecA", gate: () => false, setup: () => setupRan = true)
        });
        TutorialLesson entered = null;
        r.OnEnterLesson = l => entered = l;
        r.Begin();
        Assert.IsTrue(setupRan, "setup deve rodar ao entrar");
        Assert.AreEqual(TutorialRunner.State.WaitingGate, r.CurrentState);
        Assert.AreEqual("a", entered.id);
        Assert.AreEqual(0, r.Index);
    }

    [Test]
    public void Tick_GateFalse_StaysWaiting()
    {
        var r = new TutorialRunner(new List<TutorialLesson> { Lesson("a", 0, "MecA", gate: () => false) });
        r.Begin();
        r.Tick();
        Assert.AreEqual(TutorialRunner.State.WaitingGate, r.CurrentState);
        Assert.AreEqual(TutorialResult.Pending, r.Current.result);
    }

    [Test]
    public void Tick_GateTrue_AssertionNull_RecordsOK_EntersFeedback()
    {
        var r = new TutorialRunner(new List<TutorialLesson> { Lesson("a", 0, "MecA", gate: () => true) });
        TutorialLesson fb = null;
        r.OnFeedback = l => fb = l;
        r.Begin();
        r.Tick();
        Assert.AreEqual(TutorialRunner.State.Feedback, r.CurrentState);
        Assert.AreEqual(TutorialResult.OK, r.Current.result);
        Assert.AreEqual("a", fb.id);
        Assert.AreEqual(1, TutorialTestLog.Entries.Count);
        Assert.AreEqual(TutorialResult.OK, TutorialTestLog.Entries[0].result);
    }

    [Test]
    public void Tick_AssertionFails_RecordsFailedWithDetail()
    {
        var r = new TutorialRunner(new List<TutorialLesson>
        {
            Lesson("a", 0, "MecA", gate: () => true, assertion: () => "aliado atingido")
        });
        r.Begin();
        r.Tick();
        Assert.AreEqual(TutorialResult.Failed, r.Current.result);
        Assert.AreEqual("aliado atingido", r.Current.detail);
        Assert.AreEqual(TutorialResult.Failed, TutorialTestLog.Entries[0].result);
    }

    [Test]
    public void Advance_MovesToNextLesson()
    {
        var r = new TutorialRunner(new List<TutorialLesson>
        {
            Lesson("a", 0, "MecA", gate: () => true),
            Lesson("b", 1, "MecB", gate: () => true)
        });
        r.Begin();
        r.Tick();           // a → feedback
        r.Advance();        // → b
        Assert.AreEqual(1, r.Index);
        Assert.AreEqual("b", r.Current.id);
        Assert.AreEqual(TutorialRunner.State.WaitingGate, r.CurrentState);
    }

    [Test]
    public void Skip_RecordsSkipped_AndAdvances()
    {
        var r = new TutorialRunner(new List<TutorialLesson>
        {
            Lesson("a", 0, "MecA", gate: () => false),
            Lesson("b", 0, "MecB", gate: () => true)
        });
        r.Begin();
        r.Skip();
        Assert.AreEqual(TutorialResult.Skipped, TutorialTestLog.Entries[0].result);
        Assert.AreEqual("b", r.Current.id);
    }

    [Test]
    public void Repeat_RerunsSetup_StaysOnLesson()
    {
        int setups = 0;
        var r = new TutorialRunner(new List<TutorialLesson>
        {
            Lesson("a", 0, "MecA", gate: () => false, setup: () => setups++)
        });
        r.Begin();
        Assert.AreEqual(1, setups);
        r.Repeat();
        Assert.AreEqual(2, setups);
        Assert.AreEqual(0, r.Index);
        Assert.AreEqual(TutorialRunner.State.WaitingGate, r.CurrentState);
    }

    [Test]
    public void FullSequence_ReachesDone_FiresCompleted()
    {
        bool completed = false;
        var r = new TutorialRunner(new List<TutorialLesson>
        {
            Lesson("a", 0, "MecA", gate: () => true),
            Lesson("b", 1, "MecB", gate: () => true)
        });
        r.OnCompleted = () => completed = true;
        r.Begin();
        r.Tick(); r.Advance();   // a ok → b
        r.Tick(); r.Advance();   // b ok → done
        Assert.IsTrue(r.IsDone);
        Assert.IsTrue(completed);
        Assert.AreEqual(2, TutorialTestLog.Entries.Count);
    }

    [Test]
    public void LessonWithoutGate_CompletesOnFirstTick()
    {
        var r = new TutorialRunner(new List<TutorialLesson> { Lesson("a", 0, "Info") });
        r.Begin();
        r.Tick();
        Assert.AreEqual(TutorialRunner.State.Feedback, r.CurrentState);
        Assert.AreEqual(TutorialResult.OK, r.Current.result);
    }

    [Test]
    public void Report_CountsOkFailSkip()
    {
        var r = new TutorialRunner(new List<TutorialLesson>
        {
            Lesson("a", 0, "MecA", gate: () => true),
            Lesson("b", 0, "MecB", gate: () => true, assertion: () => "erro"),
            Lesson("c", 0, "MecC", gate: () => false)
        });
        r.Begin();
        r.Tick(); r.Advance();   // a OK
        r.Tick(); r.Advance();   // b Failed
        r.Skip();                // c Skipped
        string rep = TutorialTestLog.Report();
        StringAssert.Contains("1 OK", rep);
        StringAssert.Contains("1 falhas", rep);
        StringAssert.Contains("1 puladas", rep);
        StringAssert.Contains("MecB", rep);
        StringAssert.Contains("erro", rep);
    }

    [Test]
    public void ToJson_IsWellFormed()
    {
        var r = new TutorialRunner(new List<TutorialLesson> { Lesson("a", 0, "MecA", gate: () => true) });
        r.Begin(); r.Tick();
        string json = TutorialTestLog.ToJson();
        StringAssert.StartsWith("{\"entries\":[", json);
        StringAssert.Contains("\"mechanic\":\"MecA\"", json);
        StringAssert.Contains("\"result\":\"OK\"", json);
        StringAssert.EndsWith("]}", json);
    }
}