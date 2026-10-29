# 3. mpv draws the box's screen, the browser page is the fallback

Status: accepted

## Context

The Angular `/tv` page can be the screen, in a kiosk browser, drawing the
curved tube with a WebGL shader. But browsers play only H.264/AAC in MP4, or
WebM, so a real library of MKV and AVI rips would have to be converted first,
and a weak mini PC graphics chip struggles with video plus WebGL in a browser.

## Decision

On the box, mpv is the screen (`Box:Player:Kind`, `mpv` by default). The kiosk
launcher asks `/api/player` and starts mpv with the repo's config folder. The
server drives it over mpv's JSON IPC socket: `MpvScreen` follows snapshots as
`tube.ts` does, `MpvOsd` draws the green display as ASS overlay text on the
same 4:3 frame as `tv-screen.css`, and `mpv/crt.glsl` is the browser shader's
tube maths as an mpv hook shader. mpv 0.37 has no shader parameters, so the
server writes a copy with `Picture.Crt` baked into `#define`s.

The browser page stays, as the screen in Development, on the demo site and as
a `browser` option on the box.

## Consequences

- The box plays original files in any common format, with no converting.
- Two renderers must stay visually in step: the shader maths in
  `crt-shader.ts` and `crt.glsl`, and the display layout in `MpvOsd.cs` and
  `tv-screen.css`.
- `MpvPlayer` reconnects when the kiosk restarts mpv and feeds `MpvScreen` from
  one queue, so commands never interleave.
- mpv runs as the desktop user and the service as `rabbitears`; the socket's
  directory is group-owned so both can reach it.
