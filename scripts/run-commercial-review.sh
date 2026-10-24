#!/usr/bin/env bash
# Local-only, sound-on vintage commercial review. Never publishes or approves video.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ORIGINALS="${HOME}/Movies/RabbitEars/Originals"
REVIEW="${HOME}/Movies/RabbitEars/CommercialReview"

for required in python3 ffmpeg ffprobe curl shasum; do
  if ! command -v "$required" >/dev/null 2>&1; then
    echo "Missing $required; install prerequisites (macOS: brew install ffmpeg)." >&2
    exit 1
  fi
done

mkdir -p "$ORIGINALS" "$REVIEW"
chmod 700 "$ORIGINALS" "$REVIEW"

fetch_exact() {
  local name="$1" url="$2" expected_sha="$3" expected_bytes="$4"
  local output="${ORIGINALS}/${name}"
  if [[ ! -f "$output" ]]; then
    echo "Downloading archived source: ${name}"
    (
      set -euo pipefail
      local tmp="${output}.partial.$$"
      trap 'rm -f "$tmp"' EXIT
      curl --fail --location --retry 2 --connect-timeout 20 --max-time 600 \
        --proto '=https' --proto-redir '=https' --output "$tmp" "$url"
      local actual_sha actual_bytes
      actual_sha="$(shasum -a 256 "$tmp" | awk '{print $1}')"
      actual_bytes="$(wc -c < "$tmp" | tr -d '[:space:]')"
      if [[ "$actual_sha" != "$expected_sha" || "$actual_bytes" != "$expected_bytes" ]]; then
        echo "Source integrity mismatch. Not keeping unexpected edition: ${name}" >&2
        exit 1
      fi
      mv -n "$tmp" "$output"
    )
  fi
  if [[ -L "$output" ]]; then
    echo "Refusing symlinked source: $output" >&2
    exit 1
  fi
  local actual_sha actual_bytes
  actual_sha="$(shasum -a 256 "$output" | awk '{print $1}')"
  actual_bytes="$(wc -c < "$output" | tr -d '[:space:]')"
  if [[ "$actual_sha" != "$expected_sha" || "$actual_bytes" != "$expected_bytes" ]]; then
    echo "Existing file has wrong size or SHA-256: $output" >&2
    exit 1
  fi
  echo "Verified $name (SHA-256 ${expected_sha})"
}

# SHA-256 and byte counts from Actions run 40057053268.
fetch_exact "ClassicT1948_512kb.mp4" \
  "https://archive.org/download/ClassicT1948/ClassicT1948_512kb.mp4" \
  "d7d2cf8b1e75a7abde8d150cac768f19123f82b4453e0ba1914667734aca497e" \
  "38145570"

fetch_exact "1955Chev1955_512kb.mp4" \
  "https://archive.org/download/1955Chev1955/1955Chev1955_512kb.mp4" \
  "60fcbb2496c602bf7f8a0b5fe4efb25a0e2acd9f93488081c46ca6fe9cce5b2a" \
  "37344324"

echo "Preparing private sound-on previews..."
python3 "${REPO}/scripts/prepare-commercial-review.py" \
  --item classic-tv \
  --source "${ORIGINALS}/ClassicT1948_512kb.mp4" \
  --workspace "$REVIEW"

python3 "${REPO}/scripts/prepare-commercial-review.py" \
  --item chevrolet-1955 \
  --source "${ORIGINALS}/1955Chev1955_512kb.mp4" \
  --workspace "$REVIEW"

echo "Review each source's original audio and specific editorial/rights questions."
echo "Both reviews are PENDING, not cleared for publication."
echo "Open ${REVIEW}/classic-tv/index.html"
echo "Open ${REVIEW}/chevrolet-1955/index.html"
if command -v open >/dev/null 2>&1; then
  open "${REVIEW}/classic-tv/index.html"
  open "${REVIEW}/chevrolet-1955/index.html"
fi
