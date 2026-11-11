// Assets/Editor/BuildWebGL.cs
// Build headless de WebGL para o Pangea Skirmish.
// Uso: Unity -batchmode -buildTarget WebGL -executeMethod BuildWebGL.Build -quit
// Gera em Build/WebGL/ (index.html + Build/ + StreamingAssets/).
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
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

        ApplyCanonicalConfig();

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

    /// <summary>
    /// Aplica a configuração canônica de WebGL. FONTE ÚNICA: o menu
    /// (BuildSystem.BuildWebGL) e o build headless (Build) chamam este método, para
    /// nunca mais haver duas cópias que se desalinham.
    /// </summary>
    public static void ApplyCanonicalConfig()
    {
        // =====================================================================
        // CONFIG CANÔNICA — TRAVADA EM 2026-07-10. NÃO ALTERAR SEM MEDIR.
        //
        //   ManagedStrippingLevel.Medium
        //   WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly
        //   template PROJECT:PangeaSkirmish
        //
        // Medições em builds REAIS que produziram essa escolha:
        //   Medium + Explicit  = 56MB wasm  -> OK, NÃO CRASHA          <-- canônico
        //   None   + Medium    = 56MB wasm  -> ABORT SILENCIOSO EM SALA
        //   Disabled + Explicit= 68MB
        //   High   + Explicit  = 55MB, mas risco de o strip cortar código por reflexão
        //   Full   + Disabled  = ~146MB, estoura o limite do Pages
        //
        // ATENÇÃO AO HISTÓRICO (2026-07-31): este arquivo esteve com None+Disabled,
        // que era estado de DEBUG dos commits b48064f/b89d16e ("stripping Disabled
        // (abort silencioso WebGL)"), NÃO a config final. A decisão canônica veio
        // DEPOIS, no mesmo dia, e foi gravada no ProjectSettings. Quem tomar este
        // arquivo como fonte da verdade reintroduz o abort em sala — foi o que
        // aconteceu ao publicar o build de 31/07.
        // =====================================================================
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.WebGL, ScriptingImplementation.IL2CPP);
        // Medium, não Disabled e não High. HIGH é que é perigoso: o UnityLinker remove
        // o código de Multiplayer (NetBootstrap/RoomHUD/Relay) porque a referência é
        // AddComponent<RoomHUD>() reflexivo em runtime, e link.xml (preserve=all) não
        // basta — o IL2CPP ainda faz DCE em nível de método. Medium foi medido e não
        // corta esse caminho.
        PlayerSettings.SetManagedStrippingLevel(
            UnityEditor.Build.NamedBuildTarget.WebGL, ManagedStrippingLevel.Medium);
        // ExplicitlyThrownExceptionsOnly, NUNCA None. Com None o runtime de exceções
        // some e qualquer falha vira abort("") mudo — foi medido causando abort ao
        // entrar em sala. O None de 2026-07-09 (b48064f) existiu só para caber no
        // limite de 100MB do Pages; a compressão abaixo resolve o tamanho sem pagar
        // esse preço. Para debug com stacktrace completo use _TestBuilds.BuildBaseline.
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
        // COMPRESSÃO BROTLI + DECOMPRESSION FALLBACK (2026-07-31).
        // Ortogonal às duas travas acima: mexe em tamanho, não em comportamento de
        // runtime. É o que torna Explicit viável — o motivo original de forçar None
        // era o limite de 100MB, e com Brotli o wasm cai para a casa de 12MB.
        //
        // O GitHub Pages não deixa configurar Content-Encoding, então um .br servido
        // cru chega ao browser como bytes indecifráveis. decompressionFallback embute
        // o descompressor no loader JS e o cliente descomprime sem depender de header.
        // Com o fallback o Unity emite ".unityweb", não ".br".
        //
        // ⚠ NETLIFY: o fallback DEVE ser 0 lá. O Netlify serve .br com
        // Content-Encoding: br (o browser já descomprime nativo) e o fallback ligado
        // descomprimiria DE NOVO -> "Invalid binary data file size!" (bug de
        // 2026-07-07, corrigido em e35aa5d). Como o Pages é o canal oficial e o
        // Netlify está congelado, fica 1. Se reativar o Netlify, voltar para 0.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.decompressionFallback = true;
        // TEMPLATE OBRIGATÓRIO: forçar o custom PangeaSkirmish (free-aspect + 3-chaves).
        // Se o build usar o template DEFAULT, o index.html fica com dataUrl vazio
        // ("DefaultCompany"/"My project") e o jogo NAO CARREGA (404 no wasm).
        // O BuildPlayer programático IGNORA o template em memória a menos que seja
        // salvo em disco ANTES do build — por isso SaveAssets() é obrigatório aqui.
        PlayerSettings.WebGL.template = "PROJECT:PangeaSkirmish";
        AssetDatabase.SaveAssets();
    }

    /// <summary>Corrige as URLs do index.html conforme os arquivos que existem em Build/ (.unityweb, .br, .gz ou puros).</summary>
    public static void FixWebGLIndexUrls(string locationPathName)
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
