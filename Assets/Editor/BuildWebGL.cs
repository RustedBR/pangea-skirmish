// Assets/Editor/BuildWebGL.cs
// Build headless de WebGL para o Pangea Skirmish.
// Uso: Unity -batchmode -buildTarget WebGL -executeMethod BuildWebGL.Build -quit
// Gera em Build/WebGL/ (index.html + Build/ + StreamingAssets/).
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildWebGL
{
    public static void Build()
    {
        // Pega as cenas habilitadas no EditorBuildSettings (MainMenu, Battle, Sandbox)
        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => new SceneAssetEntry(s.path))
            .ToArray();

        // Configurações para build WebGL o MAIS LEVE possível:
        // - IL2CPP (C++ em vez de Mono): menor + mais rápido no browser
        // - Managed stripping HIGH: remove código C# não-usado
        // - Compression Brotli: .data menor
        // - Exceptions None: corta o runtime de exceções (web só)
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.WebGL, ScriptingImplementation.IL2CPP);
        PlayerSettings.stripEngineCode = true;
        PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.WebGL, ManagedStrippingLevel.High);
        // exceptionSupport DEVE ficar OFF (None) para o build caber no GitHub Pages.
        // ON embedding o runtime de exceções no wasm: 54MB (None) -> 112MB (ON),
        // e o Pages tem limite HARD de 100MB/arquivo -> push rejeitado (GH001).
        // Debug de runtime é feito no Editor / localhost (explicar ao Marcus: "abort()" silencioso
        // em WebGL com None é esperado — verificar no Editor, não no browser).
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
        // TEMPLATE OBRIGATÓRIO: forçar o custom PangeaSkirmish (free-aspect + 3-chaves).
        // Se o build headless usar o template DEFAULT, o index.html fica com dataUrl vazio
        // ("DefaultCompany"/"My project") e o jogo NAO CARREGA (404 no wasm).
        // Forçar aqui evita dependência do cache do ProjectSettings/Library.
        PlayerSettings.WebGL.template = "PROJECT:PangeaSkirmish";
        // compression brotli (.br) — o deploy descomprime antes do push (abordagem B da skill).

        var opts = new BuildPlayerOptions
        {
            scenes = scenes.Select(s => s.Path).ToArray(),
            locationPathName = "Build/WebGL",
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        };

        Debug.Log($"[BuildWebGL] Iniciando build WebGL -> {opts.locationPathName} ({scenes.Length} cenas)");
        var report = BuildPipeline.BuildPlayer(opts);
        Debug.Log($"[BuildWebGL] Resultado: {report.summary.result} | erros={report.summary.totalErrors} | tamanho={report.summary.totalSize} bytes");
        if (report.summary.result != BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }

    private struct SceneAssetEntry
    {
        public string Path;
        public SceneAssetEntry(string path) { Path = path; }
    }
}
