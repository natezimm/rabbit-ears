#!/usr/bin/env bash
# Self-contained desktop package; only mpv and ffmpeg are external dependencies.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
runtime="${1:-osx-arm64}"
case "$runtime" in osx-arm64|osx-x64) ;; *) echo 'Use osx-arm64 or osx-x64.' >&2; exit 1;; esac
command -v dotnet >/dev/null || export PATH="$HOME/.dotnet:$PATH"
out="$root/publish/mac/$runtime"
# Build in a new directory so a failed build cannot replace a working package.
mkdir -p "$root/publish/mac"
stage="$(mktemp -d "$root/publish/mac/.build-XXXXXX")"
trap 'rm -rf "$stage"' EXIT
(cd "$root/web" && npm ci --no-audit --no-fund && npx ng build)
dotnet publish "$root/src/RabbitEars.Api" -c Release -r "$runtime" --self-contained true -o "$stage/app"
dotnet publish "$root/src/RabbitEars.Desktop" -c Release -r "$runtime" --self-contained true -o "$stage/launcher"
# Compose the shared remote bindings with desktop-only window controls.
cat "$stage/app/mpv/input.conf" "$root/desktop/input-extra.conf" > "$stage/app/mpv/input-desktop.conf"
cp "$root/desktop/Rabbit-Ears.command" "$stage/"
cp "$root/desktop/desktop.example.json" "$stage/"
cp "$root/docs/mac-setup.md" "$stage/README.md"
cp "$root/LICENSE" "$root/THIRD-PARTY-NOTICES.md" "$stage/"
chmod +x "$stage/Rabbit-Ears.command" "$stage/app/RabbitEars.Api" "$stage/launcher/RabbitEars.Desktop"
rm -rf "$out"
mv "$stage" "$out"
echo "Built $out. Start Rabbit-Ears.command on a Mac with mpv and ffmpeg installed."
