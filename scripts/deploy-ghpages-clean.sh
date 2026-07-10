#!/usr/bin/env bash
# deplay-ghpages-clean.sh — deploy GitHub Pages a partir de CLONE LIMPO de origin/gh-pages.
#
# CORREÇÃO (skill unity-webgl-deploy, 2026-07-09):
#  - Build DEVE ter exceptionSupport = None (BuildWebGL.cs força). Senão o wasm
#    descomprimido vira 112MB e o Pages rejeita (GH001, limite 100MB/arquivo).
#    Com None, wasm descomprimido ~54MB (passa).
#  - Usamos CLONE LIMPO de origin/gh-pages (não worktree reusado) para não
#    arrastar commit ancestral gordo (que também causa GH001).
#  - Abordagem B (Pages): descomprimir os .br no deploy + ajustar index.html
#    (loader do Unity NÃO descomprime brotli sozinho no Pages).
set -euo pipefail

cd "$(dirname "$0")/.."
ROOT="$(pwd)"

BUILD_DIR="$ROOT/Build/WebGL"
[[ -f "$BUILD_DIR/index.html" ]] || { echo "ERRO: $BUILD_DIR/index.html ausente. Rode o build primeiro."; exit 1; }

# 1) Clone LIMPO de origin/gh-pages
CLONE=$(mktemp -d)
echo ">> Clone limpo de origin/gh-pages em $CLONE"
git -C "$CLONE" init -q 2>/dev/null || true
git -C "$CLONE" remote add origin "$(git remote get-url origin)" 2>/dev/null || \
  git -C "$CLONE" remote set-url origin "$(git remote get-url origin)"
git -C "$CLONE" fetch origin gh-pages 2>&1 | tail -2
git -C "$CLONE" checkout -q origin/gh-pages
cd "$CLONE"

# 2) Limpa conteúdo antigo (mantém .git)
find . -maxdepth 1 ! -name '.git' ! -name '.' -exec rm -rf {} +

# 3) Copia build
cp -r "$BUILD_DIR/index.html" .
cp -r "$BUILD_DIR/Build" . 2>/dev/null || true
cp -r "$BUILD_DIR/TemplateData" . 2>/dev/null || true
cp -r "$BUILD_DIR/StreamingAssets" . 2>/dev/null || true

# 4) Descomprime .br (Pages não serve brotli)
echo ">> Descomprimindo .br..."
find . -name "*.br" | while read -r f; do
  out="${f%.br}"
  if command -v brotli >/dev/null 2>&1; then
     brotli -d -c "$f" > "$out"
  else
     python3 -c "import brotli; open('$out','wb').write(brotli.decompress(open('$f','rb').read()))"
  fi
  rm -f "$f"
done

# 5) Ajusta index.html (placeholders -> nomes reais, sem .br)
sed -i \
  -e 's|{{DATA_URL}}|WebGL.data|g' \
  -e 's|{{FRAMEWORK_URL}}|WebGL.framework.js|g' \
  -e 's|{{CODE_URL}}|WebGL.wasm|g' \
  -e 's|{{LOADING_URL}}|WebGL.loader.js|g' \
  -e 's|{{COMPANY_NAME}}|Pangea Skirmish|g' \
  -e 's|{{PRODUCT_NAME}}|Pangea Skirmish|g' \
  -e 's|{{PRODUCT_VERSION}}|1.0|g' \
  -e 's|{{BACKGROUND_FILENAME}}||g' \
  -e 's|{{PROGRESS_BAR}}|TemplateData/progress-bar.png|g' \
  -e 's|{{PROGRESS_BAR_FULL}}|TemplateData/progress-bar-full.png|g' \
  index.html
# tambem troca qualquer .br residual por extensão limpa (segurança)
sed -i -e 's|\.data\.br|.data|g' -e 's|\.wasm\.br|.wasm|g' -e 's|\.js\.br|.js|g' index.html

# FALLBACK: se o Unity NAO substituiu os 3-chaves {{{DATA_URL}}} (template custom
# nao aplicado no headless), os URLs ficam Vazios (buildUrl + "/") e o jogo
# NAO CARREGA (404 no wasm). Preenche os nomes reais:
sed -i \
  -e 's|var loaderUrl = buildUrl + "/";|var loaderUrl = buildUrl + "/WebGL.loader.js";|g' \
  -e 's|dataUrl: buildUrl + "/",|dataUrl: buildUrl + "/WebGL.data",|g' \
  -e 's|frameworkUrl: buildUrl + "/",|frameworkUrl: buildUrl + "/WebGL.framework.js",|g' \
  -e 's|codeUrl: buildUrl + "/",|codeUrl: buildUrl + "/WebGL.wasm",|g' \
  -e 's|companyName: "DefaultCompany",|companyName: "Pangea Skirmish",|g' \
  -e 's|productName: "My project",|productName: "Pangea Skirmish",|g' \
  index.html

echo ">> Verificando tamanho do wasm (limite 100MB)..."
WASM=$(find . -name 'WebGL.wasm' | head -1)
if [[ -n "$WASM" ]]; then
  SZ=$(du -m "$WASM" | cut -f1)
  echo "   WebGL.wasm = ${SZ}MB"
  if [[ "$SZ" -gt 100 ]]; then
    echo "ERRO: wasm ${SZ}MB > 100MB (limite Pages). Build com exceptionSupport=None!"
    exit 1
  fi
fi

# VERIFICACAO DE TEMPLATE (nao publicar build com template default/quebrado)
echo ">> Verificando template do index.html..."
if ! grep -q "WebGL.data" index.html; then
  echo "ERRO: index.html sem 'WebGL.data' — build usou template DEFAULT (jogo nao carrega)."
  echo "       Rebuild com PlayerSettings.WebGL.template=PROJECT:PangeaSkirmish."
  exit 1
fi

# 6) Commit + push (fast-forward, sem --force)
git add -f -A
if git diff --cached --quiet; then
  echo ">> Nada mudou."
else
  git -c user.email="hermes@nous" -c user.name="Hermes" commit -m "deploy: GitHub Pages $(date +%Y-%m-%dT%H:%M:%S)"
  git push origin HEAD:gh-pages
  echo ">> Publicado em https://rustedbr.github.io/pangea-skirmish/"
fi

cd "$ROOT"
rm -rf "$CLONE"
echo ">> Concluído."
