#!/usr/bin/env bash
# Runs at desktop login: waits for the box to be on the air, then shows the TV
# full screen in Chromium and brings it back if it ever exits.
set -uo pipefail

port=5080
url="http://localhost:$port/tv"

browser="${RABBITEARS_BROWSER:-}"
if [[ -z "$browser" ]]; then
  for candidate in google-chrome google-chrome-stable chromium chromium-browser; do
    if command -v "$candidate" >/dev/null; then
      browser="$candidate"
      break
    fi
  done
fi
if [[ -z "$browser" ]]; then
  echo "No Chrome or Chromium found. Install one, or set RABBITEARS_BROWSER." >&2
  exit 1
fi

# Its own browser profile keeps the TV apart from any normal browsing on this
# machine. Snap-packaged Chromium may only write inside its own folder.
profile="$HOME/.config/rabbitears-kiosk"
if [[ "$(command -v "$browser")" == /snap/* ]]; then
  profile="$HOME/snap/chromium/common/rabbitears-kiosk"
fi

# Wait until the service is listening. Plain bash, so there is nothing to install.
until (exec 3<>"/dev/tcp/127.0.0.1/$port") 2>/dev/null; do
  sleep 1
done

while true; do
  # --password-store=basic: with automatic login the desktop keyring stays
  # locked, and the browser would otherwise put a password prompt over the picture.
  "$browser" \
    --kiosk \
    --autoplay-policy=no-user-gesture-required \
    --user-data-dir="$profile" \
    --password-store=basic \
    --no-first-run \
    --no-default-browser-check \
    --noerrdialogs \
    --disable-infobars \
    --disable-session-crashed-bubble \
    --disable-features=Translate \
    --overscroll-history-navigation=0 \
    "$url"
  sleep 2
done
