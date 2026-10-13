#!/usr/bin/env bash
# Install or update Rabbit Ears on the mini PC. Run from the copied
# publish/ folder, with sudo, as the user who logs in to the desktop:
#
#   sudo bash ~/publish/deploy/install.sh
#
# Re-running it is the update. It keeps /etc/rabbitears/box.env, the
# shows in /srv/rabbitears/media and the saved channel/volume state.
set -euo pipefail

if [[ "$(id -u)" -ne 0 ]]; then
  echo "Run with sudo: sudo bash $0" >&2
  exit 1
fi

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
app="$here/../app"
desktop_user="${SUDO_USER:?Run with sudo from your normal login, not as root directly.}"
desktop_home="$(getent passwd "$desktop_user" | cut -d: -f6)"
desktop_uid="$(id -u "$desktop_user")"
todo=()

if [[ ! -x "$app/RabbitEars.Api" ]]; then
  echo "Cannot find the built app at $app. Run scripts/publish.sh first and copy the whole publish/ folder." >&2
  exit 1
fi

echo "==> Service account and folders"
id rabbitears >/dev/null 2>&1 || useradd --system --no-create-home --shell /usr/sbin/nologin rabbitears
install -d -m 0755 /opt/rabbitears /etc/rabbitears
# The shows folder belongs to you, so you can copy episodes in without sudo.
install -d -m 0755 -o "$desktop_user" -g "$desktop_user" /srv/rabbitears/media

echo "==> Program files"
systemctl stop rabbitears 2>/dev/null || true
rm -rf /opt/rabbitears/*
cp -R "$app/." /opt/rabbitears/
install -m 0755 "$here/kiosk.sh" /opt/rabbitears/kiosk.sh

if [[ ! -f /etc/rabbitears/box.env ]]; then
  install -m 0644 "$here/box.env" /etc/rabbitears/box.env
fi

echo "==> Clean shutdown from the remote"
# Lets the service (and nothing else) power the machine off after GOODBYE. The
# rule is checked before it goes live: a broken sudoers file locks sudo for everyone.
rule="$(mktemp)"
echo 'rabbitears ALL=(root) NOPASSWD: /usr/bin/systemctl poweroff' > "$rule"
if ! command -v visudo >/dev/null || visudo -cf "$rule" >/dev/null; then
  install -m 0440 "$rule" /etc/sudoers.d/rabbitears
else
  todo+=("The shutdown rule did not validate, so GOODBYE will not power the box off.")
fi
rm -f "$rule"

echo "==> Start on boot"
install -m 0644 "$here/rabbitears.service" /etc/systemd/system/rabbitears.service
systemctl daemon-reload
systemctl enable --now rabbitears

echo "==> Browser"
if ! command -v google-chrome >/dev/null && ! command -v google-chrome-stable >/dev/null \
    && ! command -v chromium >/dev/null && ! command -v chromium-browser >/dev/null; then
  snap install chromium || todo+=("Install a browser for the TV: sudo snap install chromium")
fi

echo "==> Full-screen TV at login"
install -d -o "$desktop_user" -g "$desktop_user" "$desktop_home/.config/autostart"
cat > "$desktop_home/.config/autostart/rabbitears-kiosk.desktop" <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=Rabbit Ears
Exec=/opt/rabbitears/kiosk.sh
X-GNOME-Autostart-enabled=true
DESKTOP
chown "$desktop_user:$desktop_user" "$desktop_home/.config/autostart/rabbitears-kiosk.desktop"

echo "==> Keep the screen on"
# A TV should never dim, lock, sleep or pop up notifications. These are your
# desktop settings, so they can only be changed while you are logged in there.
bus="/run/user/$desktop_uid/bus"
if command -v gsettings >/dev/null && [[ -S "$bus" ]]; then
  desktop_setting() {
    runuser -u "$desktop_user" -- env DBUS_SESSION_BUS_ADDRESS="unix:path=$bus" gsettings set "$@" \
      || todo+=("Could not change the desktop setting: $*")
  }
  desktop_setting org.gnome.desktop.session idle-delay 0
  desktop_setting org.gnome.desktop.screensaver lock-enabled false
  desktop_setting org.gnome.settings-daemon.plugins.power sleep-inactive-ac-type nothing
  desktop_setting org.gnome.desktop.notifications show-banners false
else
  todo+=("In Settings, set Power > Screen Blank to Never and turn Screen Lock off (the desktop was not logged in, so this could not be done for you).")
fi

if ! grep -Eqs '^\s*AutomaticLoginEnable\s*=\s*[Tt]rue' /etc/gdm3/custom.conf; then
  todo+=("Turn on Settings > System > Users > Automatic Login, so the TV comes up by itself after a power cut.")
fi

address="$(hostname -I 2>/dev/null | awk '{print $1}')"
echo
echo "==> Rabbit Ears is installed."
echo "    Shows go in /srv/rabbitears/media/<Show Name>/ (one folder per show)."
echo "    What it found:   http://${address:-THIS-BOX-ADDRESS}:5080/api/library"
echo "    Phone remote:    http://${address:-THIS-BOX-ADDRESS}:5080"
echo "    Log out and back in (or reboot) to start the TV full screen."
if [[ ${#todo[@]} -gt 0 ]]; then
  echo
  echo "==> Still to do by hand:"
  printf '    - %s\n' "${todo[@]}"
fi
