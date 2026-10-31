# Rabbit Ears on Mac

Rabbit Ears runs as your signed-in user: the launcher starts the API and an mpv
window, and closing that window stops both. The API owns channels, saved positions
and volume, just as on the Linux box. No Docker, service installation or sudo is
needed to run the package. Windows support is separate work.

## Build and start

Install the .NET SDK version in `global.json`, Node from `.nvmrc`, and native mpv
and ffmpeg. With Homebrew already installed:

```bash
brew install mpv ffmpeg
```

From the repository root on an Apple silicon Mac:

```bash
bash scripts/publish-mac.sh osx-arm64
./publish/mac/osx-arm64/Rabbit-Ears.command
```

An Intel build uses `osx-x64` in both commands. The CI job builds and tests Apple
silicon; Intel packaging is available but needs its own manual validation.
The output is self-contained for .NET and the web UI. It still requires mpv and
ffprobe (provided by ffmpeg). You can move the **whole** `osx-arm64` folder anywhere
and double-click `Rabbit-Ears.command` in Finder. Keep its `app/` and `launcher/`
folders beside it. Do not copy only the `.command` file.

This is an unsigned developer package, not a notarized `.app` or DMG. Locally built
packages are the recommended first test. For a downloaded package macOS may require
approval through System Settings → Privacy & Security after you attempt to open
it; approve only a build you trust. Do not disable Gatekeeper. Folder access and
local-network prompts depend on macOS and the selected media location.

The first launch creates `~/Movies/RabbitEars`. Add one folder per channel, such as
`~/Movies/RabbitEars/Cartoons/episode.mkv`. Empty libraries display the no-channels
screen. Use `/admin` to rescan after adding files. Native mpv plays the original
files; supported formats still depend on its decoder build.

- Remote: <http://127.0.0.1:5080/remote>
- Channel administration: <http://127.0.0.1:5080/admin>
- In the player: arrows change channels/volume, numbers tune, `m` mutes, `p`
  toggles TV standby, `f` toggles full screen, `Escape` leaves full screen, `q`
  quits. Closing the window also quits. Ctrl+C in the launcher's Terminal works too.

Standby never shuts down your Mac. The player prevents display sleep while playing
through its existing mpv setting; the launcher does not change macOS power settings.
An unexpected child-process exit stops the other child and reports a log location;
it does not enter a relaunch loop.

## Choose a library and enable a phone remote

From the built package directory:

```bash
./Rabbit-Ears.command --media "$HOME/Movies/My TV library" --port 5080 --lan
```

`--lan` binds the API to all IPv4 interfaces. Open
`http://<your Mac's home-network IP>:5080/remote` on a phone on the same network.
Use System Settings → Network to find that address, and allow incoming connections
for the API if macOS prompts. `--local` returns to loopback-only access. This is a
home-network application with no accounts: do not forward the port on your router
or expose it to the internet. The hosted demo remains a separate deployment.

## Settings and saved state

Defaults are deliberately separate from the installed package:

| Item | Default |
| --- | --- |
| Profile JSON | `~/Library/Application Support/RabbitEars/desktop.json` |
| State, channel metadata, logs | `~/Library/Application Support/RabbitEars/` |
| Media | `~/Movies/RabbitEars/` |
| Bind address | `127.0.0.1:5080` |
| mpv socket | Random private directory under `/tmp`, removed on normal exit |

To save launch preferences, copy `desktop.example.json` into the profile directory
as `desktop.json` and edit it. Accepted JSON keys are `media`, `state`, `port`,
`lan`, `mpv`, `ffprobe`. Relative paths resolve against the profile file's directory;
`~/` expands to your home directory. Missing explicitly selected folders are errors.

Precedence is command-line options, `RABBITEARS_*` environment variables, JSON,
then defaults. For example `RABBITEARS_MEDIA`, `RABBITEARS_PORT` and
`RABBITEARS_LAN=true`. `--config FILE` or `RABBITEARS_CONFIG` selects another JSON
file. Existing `Box__MediaRoot`, `Box__StateDir` and `Box__Ffprobe` variables are also
accepted, below their `RABBITEARS_*` equivalents. `--mpv FILE` and `--ffprobe FILE`
accept executable paths containing spaces. Otherwise the launcher searches PATH
and the usual Homebrew bin directories.

Picture and TV settings can still use `Box__Picture__...` and `Box__Tv__...`
environment variables. The desktop launcher fixes the player to mpv, demo mode off,
power-off-at-zero-volume off and the owned IPC socket. Use `--port` and `--lan` to
set its listener rather than `ASPNETCORE_URLS` or editing packaged appsettings.

Quit before replacing the package. Saved state stays outside the release directory,
so replacing the package preserves channels and positions. The launcher locks the
state directory: two instances cannot share it. A separate profile needs both a
different state directory and a different port. Do not delete `desktop.lock` to
work around a running session; the lock is released automatically when it exits.

## Troubleshooting and validation

- Missing mpv/ffprobe: install the dependencies or give explicit executable paths.
- Occupied port: choose `--port 5081`; the launcher never stops an unrelated service.
- Startup/player failure: read `logs/api.*.log` and `logs/mpv.*.log` in the state
  folder. Each output stream keeps about 1 MiB plus one previous chunk.
- Permission errors: ensure your user can read the library and write the state
  directory. No Full Disk Access or elevated privileges should be needed for the
  default folders.
- The terminal remains open while the TV runs; this first package has no login item
  or background service. A forced kill or power loss can lose the last few seconds
  of playback state; the API periodically saves it.

Before marking a Mac release verified, record macOS version, CPU, `mpv --version`
and `ffprobe -version`, and complete these on an actual Mac:

- Fresh launch with an empty library and with MP4/MKV plus audio; paths with spaces
  and Unicode; a corrupt video and a temporarily unavailable external media drive.
- CRT picture, OSD/font, standby, channel changes, digits, volume/mute and full screen.
- Admin changes and remote control from a phone with `--lan`; local-only behavior
  with `--local`; restart preserves channel metadata, volume and resume positions.
- Close the window, quit via keyboard and Ctrl+C; no orphan API/mpv process or
  listener remains. Repeat launch, reject duplicates and reject an occupied port.
- Sleep/wake, Bluetooth/HDMI audio changes and library rescans after reconnecting
  an external drive. Upgrade the package and confirm saved state remains.

CI exercises native Unix IPC, backend tests, package construction and headless mpv
playback/clean shutdown. It cannot certify window rendering, audible sound, macOS
permission prompts, or sleep/wake. Keep those as manual acceptance checks.
