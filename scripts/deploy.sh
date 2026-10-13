#!/usr/bin/env bash
# Send Rabbit Ears to the mini PC and install it there. Run on your laptop.
#
#   bash scripts/deploy.sh YOU@rabbitears.local                build, copy, install
#   bash scripts/deploy.sh YOU@rabbitears.local --test-clips   ...and copy the test shows too
#   bash scripts/deploy.sh --no-build                          reuse the last build
#
# The address is remembered after the first run, so an update is just
# `bash scripts/deploy.sh`. The box asks for your password up to twice: once to
# connect and once for sudo. Re-running is safe; it keeps your settings and shows.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
remembered="$root/.local/box-address"

box=""
build=true
test_clips=false
for arg in "$@"; do
  case "$arg" in
    --no-build) build=false ;;
    --test-clips) test_clips=true ;;
    -*) echo "Unknown option: $arg" >&2; exit 1 ;;
    *) box="$arg" ;;
  esac
done

if [[ -z "$box" && -f "$remembered" ]]; then
  box="$(<"$remembered")"
fi
if [[ -z "$box" ]]; then
  echo "Which box? Usage: bash scripts/deploy.sh YOU@rabbitears.local" >&2
  echo "This, typed on the box, prints its address:  echo \"\$(whoami)@\$(hostname).local\"" >&2
  exit 1
fi

if $build; then
  echo "==> Building"
  DEPLOYING=1 bash "$root/scripts/publish.sh"
elif [[ ! -x "$root/publish/app/RabbitEars.Api" ]]; then
  echo "Nothing built yet. Run without --no-build." >&2
  exit 1
fi

# One shared connection for the whole run, so the box's password is asked for once.
[[ -d "$HOME/.ssh" ]] || mkdir -m 700 "$HOME/.ssh"
connection=(-o ControlMaster=auto -o ControlPath="$HOME/.ssh/ears-%C" -o ControlPersist=120)
on_box() { ssh "${connection[@]}" "$box" "$@"; }
trap 'ssh "${connection[@]}" -O exit "$box" 2>/dev/null || true' EXIT

echo "==> Copying to $box"
# COPYFILE_DISABLE keeps macOS from adding its own hidden metadata files.
COPYFILE_DISABLE=1 tar -C "$root/publish" -czf - . \
  | on_box 'rm -rf ~/publish && mkdir -p ~/publish && tar -xzf - -C ~/publish'

mkdir -p "$root/.local"
echo "$box" > "$remembered"

echo "==> Installing (the box will ask for your password)"
ssh "${connection[@]}" -t "$box" 'sudo bash ~/publish/deploy/install.sh'

if $test_clips; then
  [[ -d "$root/dev-media" ]] || bash "$root/scripts/make-dev-media.sh"
  echo "==> Copying the test shows"
  COPYFILE_DISABLE=1 tar -C "$root/dev-media" -cf - . \
    | on_box 'tar -xf - -C /srv/rabbitears/media'
fi

host="${box#*@}"
echo
echo "Done. From this laptop:"
echo "  What the box found:  http://$host:5080/api/library"
echo "  Phone remote:        http://$host:5080"
