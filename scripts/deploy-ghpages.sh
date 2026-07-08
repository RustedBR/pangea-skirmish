#!/usr/bin/env bash
# deploy-ghpages.sh — Publica o build WebGL no GitHub Pages (branch gh-pages).
#
# CUSTO: ZERO (GitHub Pages nao cobra por deploy). Use livremente.
# ATENCAO: NAO rode este script ate resolver TODAS as pendencias do jogo.
#          O repo ja esta publico e o Pages ja esta configurado (branch gh-pages).
#          Rode 1x so no final, apos validar tudo no Unity Editor (Play mode).
#
# O que faz:
#   1. Valida que Build/WebGL existe.
#   2. Remove a compressao .br (GitHub Pages NAO serve brotli com Content-Encoding).
#      Renomeia WebGL.data.br -> WebGL.data etc. e ajusta o index.html.
#   3. Copia index.html + Build/ + TemplateData/ para a branch gh-pages.
#   4. Commit + push na gh-pages.
#
# Uso:
#   ./scripts/deploy-ghpages.sh            # publica o Build/WebGL atual
#   BUILD=1 ./scripts/deploy-ghpages.sh    # (opcional) gera build novo antes
#
set -euo pipefail

cd "$(dirname "$0")/.."
ROOT="$(pwd)"

# 0) Opcional: gerar build novo
if [[ "${BUILD:-0}" == "1" ]]; then
  echo ">> Gerando build WebGL (modo headless)..."
  UNITY="/home/rusted/Unity/Hub/Editor/6000.5.1f1/Editor/Unity"
  "$UNITY" -batchmode -buildTarget WebGL -executeMethod BuildWebGL.Build -quit \
    -logFile "$ROOT/Build/build.log" || { echo "Build falhou"; exit 1; }
fi

BUILD_DIR="$ROOT/Build/WebGL"
[[ -f "$BUILD_DIR/index.html" ]] || { echo "ERRO: $BUILD_DIR/index.html ausente. Rode o build primeiro."; exit 1; }

WORK=$(mktemp -d)
echo ">> Preparando arquivos em $WORK"

# 1) Copiar estrutura
cp -r "$BUILD_DIR/index.html" "$WORK/"
[[ -d "$BUILD_DIR/Build" ]] && cp -r "$BUILD_DIR/Build" "$WORK/Build"
[[ -d "$BUILD_DIR/TemplateData" ]] && cp -r "$BUILD_DIR/TemplateData" "$WORK/TemplateData"

# 2) Remover .br: renomear e ajustar index.html
echo ">> Removendo compressao .br (GitHub Pages nao serve brotli)..."
find "$WORK" -name "*.br" | while read -r f; do
  mv "$f" "${f%.br}"
done
# Ajusta index.html: WebGL.data.br -> WebGL.data etc.
sed -i \
  -e 's|\.data\.br|.data|g' \
  -e 's|\.wasm\.br|.wasm|g' \
  -e 's|\.framework\.js\.br|.framework.js|g' \
  -e 's|\.js\.br|.js|g' \
  "$WORK/index.html"

# 3) Publicar na branch gh-pages
echo ">> Enviando para branch gh-pages..."
git worktree add --detach "$WORK/ghpages" gh-pages 2>/dev/null || {
  # se ja existe worktree, limpa
  git worktree remove --force "$WORK/ghpages" 2>/dev/null || true
  git worktree add --detach "$WORK/ghpages" gh-pages
}
cd "$WORK/ghpages"
git checkout --orphan gh-pages 2>/dev/null || git checkout gh-pages
# limpa conteudo antigo (mantem .git)
find . -maxdepth 1 ! -name '.git' ! -name '.' -exec rm -rf {} +
cp -r "$WORK/index.html" "$WORK/Build" "$WORK/TemplateData" . 2>/dev/null || true
# se nao copiou Build/TemplateData (podem nao existir), copia o que houver
[[ -d "$WORK/Build" ]] && cp -r "$WORK/Build" .
[[ -d "$WORK/TemplateData" ]] && cp -r "$WORK/TemplateData" .

git add -f -A
if git diff --cached --quiet; then
  echo ">> Nada mudou, nada para commitar."
else
  git commit -m "deploy: GitHub Pages $(date +%Y-%m-%dT%H:%M:%S)"
  git push origin gh-pages
  echo ">> Publicado em https://rustedbr.github.io/pangea-skirmish/"
fi

# limpeza
cd "$ROOT"
git worktree remove --force "$WORK/ghpages" 2>/dev/null || true
rm -rf "$WORK"
echo ">> Concluido."
