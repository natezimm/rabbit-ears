#!/usr/bin/env bash
# Launch REAL (but unapproved) sound-on archived commercial previews on Mac localhost ONLY.
# Does not publish media, change GCP, or modify the regular Rabbit Ears Compose project.
set -euo pipefail

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "This launcher is restricted to a local Mac. Never run it on the production GCP VM." >&2
  exit 1
fi

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ORIGINALS="${HOME}/Movies/RabbitEars/Originals"
REVIEWS="${HOME}/Movies/RabbitEars/CommercialReview"
LIBRARY="${HOME}/Movies/RabbitEars/LocalCommercialsLibrary"
PROJECT="rabbit-ears-commercial-review"
COMPOSE="${REPO}/demo/compose.local-commercials.yaml"
URL="http://127.0.0.1:8760/demo"

for program in docker python3 curl ffmpeg ffprobe shasum; do
  if ! command -v "$program" >/dev/null 2>&1; then
    echo "Missing $program. Install Docker Desktop and FFmpeg (brew install ffmpeg)." >&2
    exit 1
  fi
done

docker info >/dev/null 2>&1 || { echo "Start Docker Desktop first." >&2; exit 1; }
docker compose version >/dev/null

if [[ ! -f "${REVIEWS}/classic-tv/rights-review.json" ||
      ! -f "${REVIEWS}/chevrolet-1955/rights-review.json" ]]; then
  echo "Obtaining the two original reels and preparing PRIVATE previews (with sound)."
  bash "${REPO}/scripts/run-commercial-review.sh"
fi

if [[ ! -d "$LIBRARY" ]]; then
  python3 "${REPO}/scripts/stage-local-commercials.py" \
    --originals "$ORIGINALS" --reviews "$REVIEWS" --output "$LIBRARY"
fi

# Fail closed on stale/malformed staged media rather than mounting an arbitrary directory.
python3 - "$LIBRARY" <<'PY'
import json
from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve()
if not (root / "LOCAL_ONLY_DO_NOT_DEPLOY.txt").is_file():
    raise SystemExit("Refusing a library without the local-only warning marker.")
data = json.loads((root / ".demo-media.json").read_text())
if data.get("version") != 1 or len(data.get("videos", [])) != 4:
    raise SystemExit("Expected four unapproved local v1 previews.")
for entry in data["videos"]:
    p = entry.get("path")
    if not isinstance(p, str) or not p.startswith("Retro Commercials/") or not p.endswith(".mp4"):
        raise SystemExit("Unexpected preview path.")
    clip = (root / p).resolve()
    if not clip.is_relative_to(root) or not clip.is_file():
        raise SystemExit("Local preview is missing or escapes staged library.")
    if entry.get("approvedForPublicDemo") is not False or "UNREVIEWED" not in entry.get("license", ""):
        raise SystemExit("Unexpected public rights status for a private preview.")
print("Verified four private, unapproved CH 06 candidates.")
PY

# This is a distinct Compose project, dedicated to loopback 8760, never 8620.
export RABBIT_EARS_LOCAL_MEDIA="$LIBRARY"
export RABBIT_EARS_LOCAL_UID="$(id -u)"
export RABBIT_EARS_LOCAL_GID="$(id -g)"
docker compose -p "$PROJECT" -f "$COMPOSE" up -d --build

if ! curl --silent --show-error --fail --retry 10 --retry-delay 1 --retry-connrefused \
  "http://127.0.0.1:8760/api/library" -o /dev/null; then
  docker compose -p "$PROJECT" -f "$COMPOSE" ps >&2
  exit 1
fi

python3 - <<'PY'
import json
import urllib.request
report = json.load(urllib.request.urlopen("http://127.0.0.1:8760/api/library", timeout=10))
channels = report.get("channels", [])
assert len(channels) == 1, f"Expected one local preview channel, got {channels}"
channel = channels[0]
assert channel.get("number") == 6, f"Expected CH 06, got {channel}"
assert channel.get("episodes") == 4, f"Expected four sound-on excerpts, got {channel}"
print("Rabbit Ears local CH 06 is live on loopback with four original-audio excerpts.")
PY

echo "Opening local, PRIVATE Rabbit Ears TV: $URL"
open "$URL"
echo "On-screen: enable sound and select CH 06 to hear the archive clips."
echo "Stop without touching production: RABBIT_EARS_LOCAL_MEDIA=\"$LIBRARY\" RABBIT_EARS_LOCAL_UID=\"$(id -u)\" RABBIT_EARS_LOCAL_GID=\"$(id -g)\" docker compose -p \"$PROJECT\" -f \"$COMPOSE\" down"
