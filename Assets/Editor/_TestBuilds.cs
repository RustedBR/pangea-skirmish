using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class _TestBuilds
{
    public static void BuildBaseline()    { BuildScenario("baseline",    WebGLExceptionSupport.FullWithStacktrace, ManagedStrippingLevel.Disabled); }
    public static void BuildMediumFull()  { BuildScenario("medium_full",  WebGLExceptionSupport.FullWithStacktrace, ManagedStrippingLevel.Medium); }
    public static void BuildMediumNone()  { BuildScenario("medium_none",  WebGLExceptionSupport.None,                 ManagedStrippingLevel.Medium); }
    public static void BuildMediumExplicit() { BuildScenario("medium_explicit", WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly, ManagedStrippingLevel.Medium); }
    // CONFIGURAÇÃO CANÔNICA (validada 2026-07-10): Medium + ExplicitlyThrownExceptionsOnly.
    // wasm=56MB (<100MB Pages), nao crasha em Relay/Lobby. Use esta para builds reais.
    public static void BuildRecommended()   { BuildScenario("recommended",  WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly, ManagedStrippingLevel.Medium); }
    // Teste localhost (2026-07-10): SEM stripping (Disabled) + Explicit. MAIS pesado (~110-146MB),
    // NAO serve p/ GitHub Pages. So p/ testar localmente.
    public static void BuildNoStripExplicit() { BuildScenario("nostrip_explicit", WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly, ManagedStrippingLevel.Disabled); }
    // Teste (2026-07-10): HIGH stripping + Explicit. MAIOR agressividade de strip — risco de
    // remover codigo por reflexao. Se quebrar, volta p/ Medium (canonica). Menor wasm se funcionar.
    public static void BuildHighExplicit()    { BuildScenario("high_explicit",   WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly, ManagedStrippingLevel.High); }
    public static void BuildLowFull()    { BuildScenario("low_full",     WebGLExceptionSupport.FullWithStacktrace, ManagedStrippingLevel.Low); }
    public static void BuildLowNone()    { BuildScenario("low_none",     WebGLExceptionSupport.None,                ManagedStrippingLevel.Low); }

    // CORREÇÃO OBRIGATÓRIA: o template PROJECT:PangeaSkirmish gera index.html com
    // loaderUrl/dataUrl/frameworkUrl/codeUrl apontando para a PASTA "Build/" (vazia),
    // em vez dos arquivos WebGL.*. Isso dá 404 em "Build/" e quebra o <script>.
    // Corrigimos os 4 caminhos para WebGL.loader.js/.data/.framework.js/.wasm.
    private static void FixWebGLIndexUrls()
    {
        var indexPath = "Build/WebGL/index.html";
        if (!System.IO.File.Exists(indexPath)) return;
        var txt = System.IO.File.ReadAllText(indexPath);
        txt = txt.Replace("var loaderUrl = buildUrl + \"/\";", "var loaderUrl = buildUrl + \"/WebGL.loader.js\";");
        txt = txt.Replace("dataUrl: buildUrl + \"/\",", "dataUrl: buildUrl + \"/WebGL.data\",");
        txt = txt.Replace("frameworkUrl: buildUrl + \"/\",", "frameworkUrl: buildUrl + \"/WebGL.framework.js\",");
        txt = txt.Replace("codeUrl: buildUrl + \"/\",", "codeUrl: buildUrl + \"/WebGL.wasm\",");
        System.IO.File.WriteAllText(indexPath, txt);
        Debug.Log("[_TestBuilds] index.html corrigido (WebGL.* em vez de pasta)");
    }

    private static void BuildScenario(string name, WebGLExceptionSupport exc, ManagedStrippingLevel strip)
    {
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.WebGL, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.WebGL, strip);
        PlayerSettings.WebGL.exceptionSupport = exc;
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.template = "PROJECT:PangeaSkirmish";
        AssetDatabase.SaveAssets();

        // SEMPRE pasta fixa "Build/WebGL" — o template PROJECT:PangeaSkirmish espera
        // arquivos WebGL.* (nao WebGL_{name}.*). Copiamos o cenario APOS o build.
        var location = "Build/WebGL";
        var scenes = System.Array.ConvertAll(EditorBuildSettings.scenes, s => s.path);
        var opts = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = location,
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        };
        Debug.Log($"[_TestBuilds] START -> {location} (cenario={name})");
        var report = BuildPipeline.BuildPlayer(opts);
        Debug.Log($"[_TestBuilds] RESULT -> {location}: {report.summary.result} | erros={report.summary.totalErrors} | tamanho={report.summary.totalSize} bytes");

        FixWebGLIndexUrls();

        // Copia a pasta fixa para o cenario, preservando index.html ja correto (WebGL.*)
        var dest = $"Build/WebGL_{name}";
        if (System.IO.Directory.Exists(dest)) System.IO.Directory.Delete(dest, true);
        FileUtil.CopyFileOrDirectory(location, dest);
        Debug.Log($"[_TestBuilds] COPIED -> {dest}");
    }
}
