#!/usr/bin/env bash
# Notas de uma versão: seção dos dois CHANGELOGs (inglês, depois português). Falha se faltar alguma.
# Uso: tools/release-notes.sh 1.7.0-beta.1
set -eu
v=${1#v}
section() { awk -v v="$v" 'index($0, "## [" v "]") == 1 { f = 1; next } /^## / { f = 0 } f' "$1"; }
en=$(section CHANGELOG.md); pt=$(section CHANGELOG.pt-BR.md)
[ -n "${en//[[:space:]]/}" ] && [ -n "${pt//[[:space:]]/}" ] || { echo "faltam notas da versão $v em CHANGELOG.md e/ou CHANGELOG.pt-BR.md" >&2; exit 1; }
printf '%s\n\n---\n\n## Português (BR)\n\n%s\n' "$en" "$pt"
