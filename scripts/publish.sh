#!/usr/bin/env bash
# Build everything into publish/ ready to copy to the mini PC.
# The result is self-contained: the box does not need .NET or Node installed.
#
#   bash scripts/publish.sh                # linux-x64 (Intel/AMD mini PCs)
#   bash scripts/publish.sh linux-arm64    # e.g. a Raspberry Pi
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
runtime="${1:-linux-x64}"
command -v dotnet >/dev/null || export PATH="$HOME/.dotnet:$PATH"

(cd "$root/web" && npm install --no-audit --no-fund && npx ng build)

rm -rf "$root/publish"
dotnet publish "$root/src/RabbitEars.Api" -c Release -r "$runtime" --self-contained true -o "$root/publish/app"
cp -R "$root/deploy" "$root/publish/deploy"
cp "$root/scripts/convert-media.sh" "$root/publish/"

# deploy.sh carries on from here by itself; only a build on its own needs the hint.
if [[ -z "${DEPLOYING:-}" ]]; then
  echo
  echo "Built $root/publish for $runtime. Send it to the box with:"
  echo "  bash scripts/deploy.sh YOU@rabbitears.local --no-build"
fi
