# 0005 — Desktop launcher owns the session

## Context

The Linux box uses systemd and a kiosk session. A Mac user needs to run the same
television without system services, Linux paths or a separate frontend.

## Decision

A small .NET desktop launcher owns the API and mpv as child processes of the
signed-in user. The launcher supports macOS and Windows. It passes explicit paths and listener
settings to the existing API; Linux configuration defaults remain unchanged.

Profile settings and state live outside releases. A profile lock prevents concurrent
writers. IPC uses a short private Unix socket directory on Mac and a random local named
pipe on Windows. Executables receive argument
lists, never interpolated shell commands. The listener is loopback-only unless the
user explicitly enables LAN access. Desktop standby cannot shut down the computer.

Closing mpv, cancelling the launcher or losing either child ends the session. SIGTERM
allows the Mac API to save; a private stdin command or EOF stops the Windows API; a bounded timeout escalates only for the owned child. Failed
startup unwinds the same way. Crashes report logs instead of relaunching indefinitely.

## Consequences

The existing state machine, native player and browser remote remain authoritative.
Mac packages include a self-contained API, launcher and built web UI, while mpv and
ffmpeg remain external prerequisites. Signing, notarization, native installers,
and autostart are separate work. Native CI can test
processes and IPC; actual graphics, sound and OS prompts need manual Mac validation.

Windows uses the same JSON protocol over `NamedPipeClientStream`, with bounded,
cancellable connection attempts and same-user ownership checks. A desktop-only
stdin lifetime avoids exposing HTTP shutdown or broadcasting console signals to
unrelated processes. Windows x64 CI exercises the real package; the ARM64 package
is a candidate for Parallels testing, not a claim of hardware validation.
