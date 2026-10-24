#!/usr/bin/env bash
# Stop ONLY the dedicated loopback sound-on commercial preview, never the public demo.
set -euo pipefail
if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "This preview control is restricted to Mac local use." >&2
  exit 1
fi
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export RABBIT_EARS_LOCAL_MEDIA="${HOME}/Movies/RabbitEars/LocalCommercialsLibrary"
export RABBIT_EARS_LOCAL_UID="$(id -u)"
export RABBIT_EARS_LOCAL_GID="$(id -g)"
docker compose -p rabbit-ears-commercial-review \
  -f "$REPO/demo/compose.local-commercials.yaml" down
echo "Stopped only the localhost Rabbit Ears commercial preview (port 8760)."
