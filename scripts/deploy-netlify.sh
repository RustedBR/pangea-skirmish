#!/usr/bin/env bash
# scripts/deploy-netlify.sh
# Deploy local do Pangea Skirmish (WebGL) para o Netlify.
#
# Pré-requisitos:
#   - Unity já gerou o build em Build/WebGL (Assets/Editor/BuildWebGL.cs -> Build/BuildWebGL.Build)
#   - Token Netlify em ~/.netlify_token (ver `netlify login` ou crie um PAT em
#     https://app.netlify.com/user/applications#personal-access-tokens)
#   - netlify-cli instalado: npm install -g netlify-cli
#
# O que este script faz:
#   1. Valida que o build existe.
#   2. Substitui os placeholders {{DATA_URL}} etc. do index.html gerado pelo Unity
#      (o template customizado NÃO os substitui -> sem isso o loader nunca inicia).
#   3. Faz `netlify deploy --prod` do diretório Build/WebGL.
#   4. O netlify.toml (na raiz) aplica os Content-Encoding: br necessários.
#
# Uso:
#   ./scripts/deploy-netlify.sh            # produção
#   ./scripts/deploy-netlify.sh --preview  # deploy de preview (não mexe na prod)

set -euo pipefail

# ---- Config ----------------------------------------------------------------
BUILD_DIR="Build/WebGL"
NETLIFY_TOKEN_FILE="$HOME/.netlify_token"
SITE_ID="27c40477-b9d7-4061-ae4b-97ed24e556a9"   # pangea-skirmish-web
# ----------------------------------------------------------------------------

PREVIEW=""
if [[ "${1:-}" == "--preview" ]]; then
  PREVIEW="--preview"
fi

if [[ ! -f "$BUILD_DIR/index.html" ]]; then
  echo "ERRO: build nao encontrado em $BUILD_DIR"
  echo "Gere primeiro via Unity: BuildWebGL.Build (Assets/Editor/BuildWebGL.cs)"
  exit 1
fi

if [[ ! -f "$NETLIFY_TOKEN_FILE" ]]; then
  echo "ERRO: token Netlify nao encontrado em $NETLIFY_TOKEN_FILE"
  echo "Crie um PAT e salve em ~/.netlify_token"
  exit 1
fi

NETLIFY_AUTH_TOKEN="$(cat "$NETLIFY_TOKEN_FILE")"
export NETLIFY_AUTH_TOKEN

# 1) Substituir placeholders do index.html (o Unity NAO os substitui no template customizado)
#    OBS: o template tem `var buildUrl = "Build"` e monta as URLs como
#    `buildUrl + "/{{LOADING_URL}}"`. Entao {{LOADING_URL}} deve ser APENAS
#    "WebGL.loader.js" (SEM prefixo Build/) — o prefixo vem do buildUrl.
#    Se o index ja foi processado antes (ficou "Build/WebGL.loader.js"), os
#    sed extras removem o prefixo duplicado para nao virar "Build/Build/...".
echo ">> Aplicando placeholders no index.html..."
sed -i \
  -e 's|Build/Build/WebGL|Build/WebGL|g' \
  -e 's|{{LOADING_URL}}|WebGL.loader.js|g' \
  -e 's|{{DATA_URL}}|WebGL.data.br|g' \
  -e 's|{{FRAMEWORK_URL}}|WebGL.framework.js.br|g' \
  -e 's|{{CODE_URL}}|WebGL.wasm.br|g' \
  -e 's|{{COMPANY_NAME}}|Pangea|g' \
  -e 's|{{PRODUCT_NAME}}|Pangea Skirmish|g' \
  -e 's|{{PRODUCT_VERSION}}|1.0|g' \
  -e 's|Build/WebGL\.loader\.js|WebGL.loader.js|g' \
  -e 's|Build/WebGL\.data\.br|WebGL.data.br|g' \
  -e 's|Build/WebGL\.framework\.js\.br|WebGL.framework.js.br|g' \
  -e 's|Build/WebGL\.wasm\.br|WebGL.wasm.br|g' \
  "$BUILD_DIR/index.html"

# Checagem: nenhum placeholder deve restar
if grep -q '{{' "$BUILD_DIR/index.html"; then
  echo "AVISO: ainda restam placeholders no index.html:"
  grep -o '{{[A-Z_]*}}' "$BUILD_DIR/index.html" | sort -u
fi

# 2) Deploy
echo ">> Deploy para o Netlify (site $SITE_ID)..."
netlify deploy --prod $PREVIEW --dir="$BUILD_DIR" --site="$SITE_ID" --auth="$NETLIFY_AUTH_TOKEN"

echo ""
echo ">> Deploy concluido. URL: https://pangea-skirmish-web.netlify.app"
