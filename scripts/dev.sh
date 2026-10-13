#!/usr/bin/env bash
# Run Rabbit Ears on a laptop.
#   bash scripts/dev.sh             build the web app, then start the box
#   bash scripts/dev.sh --no-build  start the box with the web app as last built
# Then open http://localhost:5080/tv (press f for full screen) and
# http://localhost:5080/remote. Shows come from dev-media/ unless you point
# Box__MediaRoot at a real folder:
#   Box__MediaRoot="/path/to/shows" bash scripts/dev.sh
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
command -v dotnet >/dev/null || export PATH="$HOME/.dotnet:$PATH"

[[ -d "$root/dev-media" ]] || bash "$root/scripts/make-dev-media.sh"

if [[ "${1:-}" != "--no-build" ]]; then
  (cd "$root/web" && npm install --no-audit --no-fund && npx ng build)
fi

cd "$root/src/RabbitEars.Api"
exec dotnet run
