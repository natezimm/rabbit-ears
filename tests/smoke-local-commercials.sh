#!/usr/bin/env bash
# Synthetic-only integration test of loopback CH06 audio playback and prod approval gate.
set -euo pipefail

image="rabbit-ears"
if [[ $# -gt 0 ]]; then image="$1"; fi
tmp="$(mktemp -d)"
preview="rabbit-ears-local-preview-smoke"
strict="rabbit-ears-strict-preview-smoke"
cleanup() {
  docker rm -f "$preview" "$strict" >/dev/null 2>&1 || true
  rm -rf "$tmp"
}
trap cleanup EXIT

mkdir -p "$tmp/Retro Commercials"
ffmpeg -hide_banner -loglevel error -y \
  -f lavfi -i "testsrc2=size=160x120:rate=12" \
  -f lavfi -i "sine=frequency=550:sample_rate=44100" \
  -t 4 -c:v libx264 -threads 1 -pix_fmt yuv420p \
  -c:a aac -b:a 96k -movflags +faststart \
  "$tmp/Retro Commercials/Synthetic Ad.mp4"
chmod -R a+rX "$tmp"

python3 - "$tmp" <<'PY'
import json
from pathlib import Path
import sys
base = Path(sys.argv[1])
(base / ".demo-media.json").write_text(json.dumps({
    "version": 1,
    "videos": [{
        "path": "Retro Commercials/Synthetic Ad.mp4",
        "title": "Synthetic local test only",
        "creator": "Automated synthetic fixture",
        "license": "UNREVIEWED — PRIVATE LOCAL PLAYBACK ONLY",
        "sourceUrl": "https://example.com/synthetic-fixture",
        "duration": 4.0,
        "approvedForPublicDemo": False
    }]
}))
PY
chmod a+r "$tmp/.demo-media.json"

# The real demo app runs with unapproved synthetic media ONLY on loopback 8760.
docker run -d --name "$preview" -p 127.0.0.1:8760:8080 \
  -e Box__FirstChannelNumber=6 -e Box__Demo__RequireApprovedMedia=false \
  -v "$tmp:/srv/rabbitears/media:ro" "$image" >/dev/null

for attempt in $(seq 1 30); do
  if curl -fsS http://127.0.0.1:8760/api/health -o /dev/null; then break; fi
  sleep 1
done

python3 - <<'PY'
import json
import urllib.request
base = "http://127.0.0.1:8760"
library = json.load(urllib.request.urlopen(base + "/api/library"))
assert library["episodes"] == 1, library
assert len(library["channels"]) == 1, library
ch = library["channels"][0]
assert ch["number"] == 6 and ch["episodes"] == 1, ch
credits = json.load(urllib.request.urlopen(base + "/api/demo/credits"))
assert len(credits) == 1 and "UNREVIEWED" in credits[0]["license"], credits
print("LOCAL_CH06_SYNTHETIC_AUDIO_PLAYBACK=OK")
PY

media_id="$(printf '%s' 'Retro Commercials/Synthetic Ad.mp4' | sha256sum | cut -c 1-16)"
headers="$(mktemp)"
status="$(curl --silent --show-error --output /dev/null --dump-header "$headers" \
  --range 0-127 --write-out '%{http_code}' "http://127.0.0.1:8760/media/$media_id")"
test "$status" = "206"
grep -qi '^content-range: bytes 0-127/' "$headers"
rm -f "$headers"

# The SAME unapproved v1 media must disappear under the PUBLIC strict gate.
docker run -d --name "$strict" -p 127.0.0.1:8761:8080 \
  -e Box__Demo__RequireApprovedMedia=true \
  -v "$tmp:/srv/rabbitears/media:ro" "$image" >/dev/null
for attempt in $(seq 1 30); do
  if curl -fsS http://127.0.0.1:8761/api/health -o /dev/null; then break; fi
  sleep 1
done
python3 - <<'PY'
import json
import urllib.request
library = json.load(urllib.request.urlopen("http://127.0.0.1:8761/api/library"))
assert library["episodes"] == 0, library
print("PUBLIC_V2_APPROVAL_GATE=BLOCKED")
PY
