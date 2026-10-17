#!/usr/bin/env bash
# Runs at desktop login: waits for the box to be on the air, then puts the TV
# on the screen full screen and brings it back if it ever exits. The service
# says which player to use: mpv, which plays the shows exactly as they are, or
# the /tv page in Chromium.
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
port=5080
url="http://localhost:$port/tv"

# Wait until the service is listening. Plain bash, so there is nothing to install.
until (exec 3<>"/dev/tcp/127.0.0.1/$port") 2>/dev/null; do
  sleep 1
done

# One GET, also in plain bash. Prints the reply's body.
ask() {
  local reply
  exec 3<>"/dev/tcp/127.0.0.1/$port" || return 1
  printf 'GET %s HTTP/1.0\r\nHost: localhost\r\n\r\n' "$1" >&3
  reply="$(cat <&3)"
  exec 3<&-
  printf '%s' "${reply#*$'\r\n\r\n'}"
}

player="$(ask /api/player)"
kind="$(sed -n 's/.*"kind":"\([^"]*\)".*/\1/p' <<<"$player")"
socket="$(sed -n 's/.*"socket":"\([^"]*\)".*/\1/p' <<<"$player")"

if [[ "$kind" == mpv ]]; then
  if command -v mpv >/dev/null; then
    # The service runs as its own user and reaches mpv through this socket,
    # in a folder that belongs to the service's group. mpv makes the socket
    # private to its own user whatever the umask, so open it to the group as
    # soon as it exists. The old socket goes first, or it would be mistaken
    # for the new one.
    while true; do
      rm -f "$socket"
      (
        for _ in $(seq 150); do
          if [[ -S "$socket" ]]; then chmod 660 "$socket"; break; fi
          sleep 0.2
        done
      ) &
      mpv --config-dir="$here/mpv" --input-ipc-server="$socket"
      sleep 2
    done
  fi
  echo "mpv is not installed (sudo apt install mpv); showing the TV in the browser instead." >&2
fi

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
