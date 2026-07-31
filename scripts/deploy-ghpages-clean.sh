#!/usr/bin/env bash
# deploy-ghpages-clean.sh — deploy do GitHub Pages (canal OFICIAL do projeto).
#
# HISTÓRICO DAS DECISÕES QUE MOLDARAM ESTE SCRIPT
#
#  2026-07-09: build DEVE ter exceptionSupport = None (BuildWebGL.cs força). Com
#    FullWithStacktrace o wasm descomprimido passa de 100MB e o Pages rejeita com
#    GH001 (limite de 100MB POR ARQUIVO).
#
#  2026-07-09: clone LIMPO de origin/gh-pages em vez de worktree reusado, para não
#    arrastar commit ancestral gordo (que também dispara GH001).
#
#  2026-07-31: os .br passam a ser publicados COMPRIMIDOS.
#    Antes este script descomprimia todo .br no deploy, porque o loader do Unity
#    não descomprime brotli sozinho quando o servidor não manda Content-Encoding —
#    e o GitHub Pages não deixa configurar header. Isso anulava o ganho inteiro da
#    compressão: publicava-se o wasm de ~54MB.
#    A causa real era PlayerSettings.WebGL.decompressionFallback = false. Com o
#    fallback ligado (BuildWebGL.cs), o descompressor vai embutido no loader JS e o
#    cliente descomprime sozinho, sem depender de header nenhum. Os artefatos
#    comprimidos vão para o Pages como estão.
#    Nota sobre os nomes: com decompressionFallback o Unity emite ".unityweb"
#    (WebGL.wasm.unityweb), NÃO ".br" — ".br" é o formato de quando se espera que o
#    servidor mande Content-Encoding. Por isso a resolução de nome abaixo tenta
#    .unityweb primeiro.
#
#  2026-07-31: publicação como COMMIT ÓRFÃO. Antes era `push origin HEAD:gh-pages`
#    fast-forward, o que fez a branch acumular 21 builds WebGL completos e ~468 MiB
#    de pack num repositório público. Agora cada deploy substitui o anterior: a
#    branch tem sempre exatamente 1 commit.
#
# Uso:  ./scripts/deploy-ghpages-clean.sh
#       KEEP_HISTORY=1 ./scripts/deploy-ghpages-clean.sh   # modo antigo (append)
set -euo pipefail

cd "$(dirname "$0")/.."
ROOT="$(pwd)"

BUILD_DIR="$ROOT/Build/WebGL"
[[ -f "$BUILD_DIR/index.html" ]] || { echo "ERRO: $BUILD_DIR/index.html ausente. Rode o build primeiro."; exit 1; }

KEEP_HISTORY="${KEEP_HISTORY:-0}"
REMOTE_URL="$(git remote get-url origin)"

CLONE=$(mktemp -d)
cleanup() { cd "$ROOT" 2>/dev/null || true; rm -rf "$CLONE"; }
trap cleanup EXIT

echo ">> Preparando árvore de deploy em $CLONE"
git -C "$CLONE" init -q
git -C "$CLONE" remote add origin "$REMOTE_URL"

if [[ "$KEEP_HISTORY" == "1" ]]; then
  echo ">> KEEP_HISTORY=1 — mantendo histórico da gh-pages"
  git -C "$CLONE" fetch --depth=1 origin gh-pages 2>&1 | tail -2
  git -C "$CLONE" checkout -q FETCH_HEAD
  find "$CLONE" -maxdepth 1 ! -name '.git' ! -name '.' -exec rm -rf {} +
fi

cd "$CLONE"

# ---- Copia os artefatos do build -------------------------------------------
cp "$BUILD_DIR/index.html" .
for d in Build TemplateData StreamingAssets; do
  [[ -d "$BUILD_DIR/$d" ]] && cp -r "$BUILD_DIR/$d" .
done

# ---- project_id do UGS ------------------------------------------------------
# Sem project_id, SignInAnonymouslyAsync/Relay falham SILENCIOSAMENTE no WebGL:
# exceptionSupport=None esconde a exceção e vira abort(""). O Unity só grava o
# project_id quando os serviços UGS estão com m_Enabled=1; aqui estão 0, então
# injetamos na mão.
CFG=$(find . -name "UnityServicesProjectConfiguration.json" | head -1)
if [[ -n "$CFG" ]]; then
  python3 - "$CFG" <<'PY'
import json, sys
p = sys.argv[1]
with open(p) as f:
    cfg = json.load(f)
keys = cfg.get("Keys", [])
vals = cfg.get("Values", [])
PID = "dcd4e4ce-3fe1-4eac-8305-78719eaa279f"
if "project_id" not in keys:
    keys.append("project_id")
    vals.append({"m_Value": PID, "m_IsReadOnly": False})
    cfg["Keys"] = keys
    cfg["Values"] = vals
    with open(p, "w") as f:
        json.dump(cfg, f, indent=0)
    print("   project_id injetado:", PID)
else:
    print("   project_id ja presente, ok")
PY
fi

# ---- Resolve os nomes reais dos artefatos -----------------------------------
# Dependem da compressão: com Brotli o Unity emite .data.br/.wasm.br/.framework.js.br.
# O loader NUNCA é comprimido (não haveria como carregá-lo).
find_artifact() {
  for suf in "$@"; do
    local hit
    hit=$(find Build -maxdepth 1 -name "*$suf" | head -1)
    [[ -n "$hit" ]] && { basename "$hit"; return 0; }
  done
  return 1
}
LOADER=$(find_artifact ".loader.js") || { echo "ERRO: loader não encontrado em Build/"; exit 1; }
DATA=$(find_artifact ".data.unityweb" ".data.br" ".data.gz" ".data") || { echo "ERRO: data não encontrado"; exit 1; }
FRAMEWORK=$(find_artifact ".framework.js.unityweb" ".framework.js.br" ".framework.js.gz" ".framework.js") || { echo "ERRO: framework não encontrado"; exit 1; }
CODE=$(find_artifact ".wasm.unityweb" ".wasm.br" ".wasm.gz" ".wasm") || { echo "ERRO: wasm não encontrado"; exit 1; }
echo ">> Artefatos: loader=$LOADER data=$DATA framework=$FRAMEWORK code=$CODE"

# ---- index.html: placeholders do template custom ----------------------------
sed -i \
  -e "s|{{DATA_URL}}|$DATA|g" \
  -e "s|{{FRAMEWORK_URL}}|$FRAMEWORK|g" \
  -e "s|{{CODE_URL}}|$CODE|g" \
  -e "s|{{LOADING_URL}}|$LOADER|g" \
  -e 's|{{COMPANY_NAME}}|Pangea Skirmish|g' \
  -e 's|{{PRODUCT_NAME}}|Pangea Skirmish|g' \
  -e 's|{{PRODUCT_VERSION}}|1.0|g' \
  -e 's|{{BACKGROUND_FILENAME}}||g' \
  -e 's|{{PROGRESS_BAR}}|TemplateData/progress-bar.png|g' \
  -e 's|{{PROGRESS_BAR_FULL}}|TemplateData/progress-bar-full.png|g' \
  index.html

# FALLBACK: se o Unity não aplicou o template custom no headless, os URLs ficam
# vazios (buildUrl + "/") e o jogo não carrega (404 no wasm).
sed -i \
  -e "s|var loaderUrl = buildUrl + \"/\";|var loaderUrl = buildUrl + \"/$LOADER\";|g" \
  -e "s|dataUrl: buildUrl + \"/\",|dataUrl: buildUrl + \"/$DATA\",|g" \
  -e "s|frameworkUrl: buildUrl + \"/\",|frameworkUrl: buildUrl + \"/$FRAMEWORK\",|g" \
  -e "s|codeUrl: buildUrl + \"/\",|codeUrl: buildUrl + \"/$CODE\",|g" \
  -e 's|companyName: "DefaultCompany",|companyName: "Pangea Skirmish",|g' \
  -e 's|productName: "My project",|productName: "Pangea Skirmish",|g' \
  index.html

# ---- Guardas antes de publicar ----------------------------------------------
echo ">> Verificando limite de 100MB por arquivo (GH001)..."
FAIL=0
while IFS= read -r f; do
  SZ=$(du -m "$f" | cut -f1)
  printf "   %5s MB  %s\n" "$SZ" "${f#./}"
  if [[ "$SZ" -gt 100 ]]; then
    echo "ERRO: ${f#./} tem ${SZ}MB > 100MB — o Pages rejeita com GH001."
    FAIL=1
  fi
done < <(find . -path ./.git -prune -o -type f -size +1M -print)
[[ "$FAIL" == "1" ]] && exit 1

echo ">> Verificando index.html..."
grep -q "$CODE" index.html || { echo "ERRO: index.html não referencia $CODE — template default ou sed falhou."; exit 1; }
grep -q "buildUrl + \"/\";" index.html && { echo "ERRO: sobrou URL vazia no index.html — o jogo não carregaria."; exit 1; }

# ---- Publica ----------------------------------------------------------------
git add -f -A
if [[ "$KEEP_HISTORY" == "1" ]]; then
  if git diff --cached --quiet; then echo ">> Nada mudou."; exit 0; fi
  git -c user.email="hermes@nous" -c user.name="Hermes" commit -q -m "deploy: GitHub Pages $(date +%Y-%m-%dT%H:%M:%S)"
  git push origin HEAD:gh-pages
else
  # Commit órfão: a branch passa a ter exatamente 1 commit, sempre.
  git -c user.email="hermes@nous" -c user.name="Hermes" commit -q -m "deploy: GitHub Pages $(date +%Y-%m-%dT%H:%M:%S)"
  echo ">> Publicando como commit órfão (substitui o histórico da gh-pages)"
  git push --force origin HEAD:gh-pages
fi

echo ">> Publicado em https://rustedbr.github.io/pangea-skirmish/"
echo ">> Concluído."
