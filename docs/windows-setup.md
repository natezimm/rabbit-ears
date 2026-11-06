# Rabbit Ears on Windows

The desktop launcher runs the API and native mpv in your signed-in session. Closing
the player stops both and saves the television. No Docker, WSL, Windows service or
administrator access is needed to run the package. The phone remote is optional.

## Prerequisites and build

Use Windows 11 and install mpv and FFmpeg (which includes ffprobe). Use builds linked
from [mpv's installation page](https://mpv.io/installation/) and
[FFmpeg's download page](https://ffmpeg.org/download.html). Add their executable
folders to PATH, or supply `--mpv` and `--ffprobe` paths. Use `mpv.exe`, not mpv.net.
Keep each dependency's DLLs beside its executable. Scripts are not accepted as
Windows dependency executables.

For a source build, install the .NET SDK in `global.json` and Node from `.nvmrc`.
From PowerShell in the repository root:

```powershell
git switch main
git pull --ff-only
.\scripts\publish-windows.ps1 win-x64
.\publish\windows\win-x64\Rabbit-Ears.ps1
```

The build includes the .NET runtime and Angular assets. A downloaded CI package
needs only mpv and ffprobe; no SDK or Node installation. Extract the entire ZIP,
then run `Rabbit-Ears.ps1` from its `win-x64` directory. If local script policy
blocks it, run the executable directly instead of changing system policy:

```powershell
.\launcher\RabbitEars.Desktop.exe
```

Packages are unsigned developer builds, not signed installers. Review Windows
security prompts for your own build; do not disable SmartScreen globally.

## Library, controls and settings

First launch creates `~/Videos/RabbitEars`. Put a show folder under it with MP4 or
MKV episodes, then rescan at <http://127.0.0.1:5080/admin>. An empty library shows
the no-channels screen. The remote is <http://127.0.0.1:5080/remote>.

Arrow keys change channels and volume; digits tune; `m` mutes; `p` toggles standby;
`f` toggles full screen; Escape leaves full screen; `q` or closing the window quits.
Ctrl+C in the launcher's terminal also stops the session. Standby never shuts down
Windows. Quit the TV normally before closing its terminal to allow state to save.

State, channel metadata and bounded logs live in `%LOCALAPPDATA%/RabbitEars`.
Copy `desktop.example.json` there as `desktop.json` to persist launch preferences.
JSON keys are `media`, `state`, `port`, `lan`, `mpv`, `ffprobe`. For Windows JSON
paths use forward slashes or escape backslashes, e.g. `C:/Media/TV shows`.
Relative paths resolve against the profile file's folder; `~/` expands to your home.

```powershell
.\Rabbit-Ears.ps1 --media 'D:\TV shows' --mpv 'C:\Tools\mpv\mpv.exe' --ffprobe 'C:\Tools\ffmpeg\bin\ffprobe.exe'
```

Precedence: command line, `RABBITEARS_*` environment, JSON, then defaults.
`--config FILE` selects another profile. Existing `Box__MediaRoot`, `Box__StateDir`
and `Box__Ffprobe` overrides remain supported below their `RABBITEARS_*` equivalents.
Use `--port 5081` if another program owns 5080; Rabbit Ears never kills that program.
A profile lock prevents concurrent writers. Quit before replacing package binaries;
upgrades preserve state outside the package, and media is never copied or deleted.

## Phone remote and Parallels

Launch with `--lan` only on a trusted home network. On the phone, open
`http://<Windows LAN IP>:5080/remote`. Allow incoming connections for the packaged
`RabbitEars.Api.exe` on **Private networks only** if Windows Firewall prompts. The
launcher does not modify firewall rules. Do not forward this port on your router.

On an Apple silicon Mac, use Windows 11 ARM in Parallels and build/download
`win-arm64`. The API and launcher are ARM64; external x64 mpv/ffprobe builds may run
under Windows emulation. Record which dependency architectures you used. An ARM
package build is not proof of native ARM playback compatibility.

Shared VM networking may allow local playback without making the VM reachable from
your phone. Test playback inside Windows first, then configure VM networking for
home-LAN access if needed. A Parallels test does not certify physical x64 GPU, HDMI
sound or Flirc hardware. Start the trial once the Windows CI package is ready.

## Diagnostics and acceptance

Logs are in `%LOCALAPPDATA%/RabbitEars/logs`. Missing libraries/executables, duplicate
profiles and occupied ports produce errors in the launcher's terminal.

Windows IPC uses a random local `\\.\pipe\rabbitears-...` name. The client requires
same-user pipe ownership; [mpv 0.41.0](https://github.com/mpv-player/mpv/blob/v0.41.0/input/ipc-win.c)
restricts access to the owning user and rejects remote clients. Run both as the same
unelevated user. Do not replace the endpoint with a remote pipe or grant broad pipe
permissions. A private stdin shutdown message lets normal quit and launcher EOF
save API state without exposing a network shutdown endpoint.

CI uses mpv 0.41.0 and records exact mpv/FFmpeg/.NET versions. Native x64 checks cover
pipe request/reply/events, disconnect/cancellation, real headless video playback,
duplicate/port rejection, volume persistence, crash cleanup and graceful API exit.
ARM64 artifacts are provided for manual testing. Linux/Mac checks remain enabled.

On the actual Windows VM, record Windows version, CPU architecture, build.json,
`mpv --version` and `ffprobe -version`. Check:

- MP4/MKV with sound, CRT effects, OSD/font, resizing/full screen, keyboard controls.
- Paths with spaces and Unicode, empty library, corrupt files, missing external drive.
- Channel admin saves, phone remote, standby, volume/mute and channel/position restore.
- Normal quit, Ctrl+C, reopen, sleep/wake and switching audio output.
- No leftover owned API/mpv after quit; package replacement keeps saved state.

For the automated smoke test from source, use PowerShell 7:

```powershell
dotnet test
.\tests\smoke-windows.ps1 -Package publish/windows/win-x64
```

Uninstall by quitting and removing the package. Remove `%LOCALAPPDATA%/RabbitEars`
only if you also want to discard settings, channel metadata and playback state.
Your media folder is independent. Autostart, signing and installers are later work.
