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
        // Managed stripping: DESLIGADO.
        // Com HIGH, o UnityLinker remove o código de Multiplayer (NetBootstrap/RoomHUD/Relay)
        // porque a referência é via AddComponent<RoomHUD>() reflexivo em runtime — o linker
        // não enxerga e stripa como dead code. Resultado: clique em MP -> código ausente ->
        // abort() silencioso no browser (exceptionSupport=None esconde a exceção).
        // link.xml (preserve=all) NÃO basta: o IL2CPP ainda faz DCE em nível de método.
        // Desligar o stripping garante que TODO o PangeaSkirmish.dll entra no wasm.
        // wasm fica ~70-90MB mas exceptionSupport=None mantém < 100MB (limite Pages).
        PlayerSettings.SetManagedStrippingLevel(
            UnityEditor.Build.NamedBuildTarget.WebGL, ManagedStrippingLevel.Disabled);
        // exceptionSupport None: OBRIGATÓRIO para deploy — medição 2026-07-10:
        // Full+Disabled=153MB, Full+Medium=123MB (Pages rejeita >100MB, GH001);
        // None+Disabled=71MB, None+Medium=58MB. Para debug com exceções visíveis
        // use _TestBuilds.BuildBaseline etc. (builds locais WebGL_*_full).
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
        // COMPRESSÃO BROTLI + DECOMPRESSION FALLBACK.
        // As 9 variantes de 2026-07-10 (_TestBuilds) todas mediram com compressionFormat
        // Disabled — variaram exceptionSupport e stripping, que são justamente as duas
        // alavancas que arriscam quebrar o multiplayer, e nunca tocaram na compressão,
        // que não tem risco funcional nenhum.
        // O motivo de estar Disabled era o GitHub Pages: ele não deixa configurar
        // Content-Encoding, então um .br servido cru chega ao browser como bytes que
        // ele não sabe descomprimir. decompressionFallback resolve exatamente isso —
        // embute o descompressor no loader JS, e o cliente descomprime sem depender de
        // header nenhum do servidor. É o caminho suportado pelo Unity para hospedagem
        // estática sem controle de headers.
        // Custo: loader um pouco maior e CPU de descompressão no carregamento.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.decompressionFallback = true;
        // TEMPLATE OBRIGATÓRIO: forçar o custom PangeaSkirmish (free-aspect + 3-chaves).
        // Se o build usar o template DEFAULT, o index.html fica com dataUrl vazio
        // ("DefaultCompany"/"My project") e o jogo NAO CARREGA (404 no wasm).
        // O BuildPlayer programático IGNORA o template em memória a menos que seja
        // salvo em disco ANTES do build — por isso SaveAssets() é obrigatório aqui.
        PlayerSettings.WebGL.template = "PROJECT:PangeaSkirmish";
        AssetDatabase.SaveAssets();

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

        FixWebGLIndexUrls(opts.locationPathName);
    }

    private static void FixWebGLIndexUrls(string locationPathName)
    {
        var indexPath = System.IO.Path.Combine(locationPathName, "index.html");
        if (!System.IO.File.Exists(indexPath))
        {
            Debug.LogWarning("[BuildWebGL] index.html não encontrado para post-fix.");
            return;
        }

        // Os nomes reais dependem da compressão:
        //   - sem compressão            -> WebGL.wasm
        //   - Brotli/Gzip puros         -> WebGL.wasm.br / .gz  (exigem Content-Encoding do servidor)
        //   - com decompressionFallback -> WebGL.wasm.unityweb  (o loader descomprime sozinho)
        // O loader NUNCA é comprimido, senão não haveria como carregá-lo.
        // Hardcodar ".wasm" quebraria silenciosamente o build comprimido — daí a
        // detecção pelo que existe em disco.
        var buildDir = System.IO.Path.Combine(locationPathName, "Build");
        if (!System.IO.Directory.Exists(buildDir))
        {
            Debug.LogWarning($"[BuildWebGL] pasta {buildDir} não encontrada para post-fix.");
            return;
        }

        var loader = FindBuildFile(buildDir, ".loader.js");
        var data = FindBuildFile(buildDir, ".data.unityweb", ".data.br", ".data.gz", ".data");
        var framework = FindBuildFile(buildDir, ".framework.js.unityweb", ".framework.js.br", ".framework.js.gz", ".framework.js");
        var code = FindBuildFile(buildDir, ".wasm.unityweb", ".wasm.br", ".wasm.gz", ".wasm");

        if (loader == null || data == null || framework == null || code == null)
        {
            Debug.LogError($"[BuildWebGL] post-fix abortado — faltou arquivo em {buildDir}. " +
                           $"loader={loader} data={data} framework={framework} code={code}");
            return;
        }

        var html = System.IO.File.ReadAllText(indexPath);
        var original = html;
        html = html.Replace("var loaderUrl = buildUrl + \"/\";", $"var loaderUrl = buildUrl + \"/{loader}\";")
                   .Replace("dataUrl: buildUrl + \"/\",", $"dataUrl: buildUrl + \"/{data}\",")
                   .Replace("frameworkUrl: buildUrl + \"/\",", $"frameworkUrl: buildUrl + \"/{framework}\",")
                   .Replace("codeUrl: buildUrl + \"/\",", $"codeUrl: buildUrl + \"/{code}\",");

        if (html != original)
        {
            System.IO.File.WriteAllText(indexPath, html);
            Debug.Log($"[BuildWebGL] Index URLs corrigidas: loader={loader} data={data} framework={framework} code={code}");
        }
        else
        {
            Debug.Log("[BuildWebGL] index.html já estava com URLs resolvidas — nada a corrigir.");
        }
    }

    /// Procura na pasta Build o primeiro arquivo que termina com um dos sufixos, na ordem dada.
    /// Retorna só o nome (sem diretório), ou null se nenhum existir.
    private static string FindBuildFile(string buildDir, params string[] suffixes)
    {
        var files = System.IO.Directory.GetFiles(buildDir).Select(System.IO.Path.GetFileName).ToArray();
        foreach (var suffix in suffixes)
        {
            var hit = files.FirstOrDefault(f => f.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit;
        }
        return null;
    }

    private struct SceneAssetEntry
    {
        public string Path;
        public SceneAssetEntry(string path) { Path = path; }
    }
}
