using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class _TestBuilds
{
    public static void BuildBaseline()    { BuildScenario("baseline",    WebGLExceptionSupport.FullWithStacktrace, ManagedStrippingLevel.Disabled); }
    public static void BuildMediumFull()  { BuildScenario("medium_full",  WebGLExceptionSupport.FullWithStacktrace, ManagedStrippingLevel.Medium); }
    public static void BuildMediumNone()  { BuildScenario("medium_none",  WebGLExceptionSupport.None,                ManagedStrippingLevel.Medium); }
    public static void BuildLowFull()    { BuildScenario("low_full",     WebGLExceptionSupport.FullWithStacktrace, ManagedStrippingLevel.Low); }
    public static void BuildLowNone()    { BuildScenario("low_none",     WebGLExceptionSupport.None,                ManagedStrippingLevel.Low); }

    private static void BuildScenario(string name, WebGLExceptionSupport exc, ManagedStrippingLevel strip)
    {
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.WebGL, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.WebGL, strip);
        PlayerSettings.WebGL.exceptionSupport = exc;
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.template = "PROJECT:PangeaSkirmish";
        AssetDatabase.SaveAssets();

        var location = $"Build/WebGL_{name}";
        var scenes = System.Array.ConvertAll(EditorBuildSettings.scenes, s => s.path);
        var opts = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = location,
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        };
        Debug.Log($"[_TestBuilds] START -> {location}");
        var report = BuildPipeline.BuildPlayer(opts);
        Debug.Log($"[_TestBuilds] RESULT -> {location}: {report.summary.result} | erros={report.summary.totalErrors} | tamanho={report.summary.totalSize} bytes");
    }
}
