# AGENTS.md

Guidance for coding agents working in this repository. `CLAUDE.md` imports this
file, so edit this one.

Rabbit Ears plays folders of old shows and movies as TV channels on a curved CRT
picture. One ASP.NET Core process (.NET 10) owns the TV; an Angular 22 app draws
the TV page and a phone remote. It runs on a Linux mini PC in kiosk mode.

## Commands

```bash
bash scripts/dev.sh              # build the web app, then run the box at http://localhost:5080
bash scripts/dev.sh --no-build   # run with the web app as last built
dotnet test                      # all backend tests (xUnit)
dotnet test --filter "FullyQualifiedName~TelevisionTests.Typed_digits_tune_after_a_pause"   # one test
dotnet test --filter "FullyQualifiedName~ShuffleDeckTests"                                  # one class
(cd web && npx ng build)         # web app only; output goes to src/RabbitEars.Api/wwwroot
(cd web && npm run test:browser) # real app + API in Chromium, desktop and phone sizes
```

- Open `/tv` for the television (`f` for full screen) and `/remote` for the phone
  remote. The first run generates short test shows in `dev-media/`.
- For frontend work with live reload: run the API (`cd src/RabbitEars.Api && dotnet run`),
  then `cd web && npm start` and use port 4200. It proxies `/api`, `/media` and
  `/hub` to 5080.
- The API serves the Angular build from `wwwroot`, which is git-ignored. `dotnet run`
  alone serves a stale or missing page until the web app has been built.
- There is no linter. Playwright tests in `web/e2e/` run the built Angular app
  against isolated ASP.NET Core processes with temporary media/state directories.
  Install Chromium once with `(cd web && npx playwright install chromium)`.
  See `docs/browser-tests.md` for commands and report artifacts. .NET warnings
  are errors (`Directory.Build.props`).
- `/admin` manages home channel metadata in `StateDir/channels.json` through
  `ChannelCatalog`. `TvService` serializes metadata saves and rescans. Folder
  keys identify runtime/saved channels independently of display names; keep this
  distinction intact when editing `Television`. Admin APIs are disabled in demo
  mode and writes require the admin header and a same-origin request.
- `scripts/prepare-demo-media.py` prepares local, cleared sources from a manifest;
  `demo/compose.media.yaml` mounts the resulting library read-only. Prepared
  `.demo-media.json` supplies durations and public credits. Run
  `python3 -m unittest discover -s tests -p 'test_*.py'` with ffmpeg installed.
- `docker build -t rabbit-ears . && docker run --rm -p 8080:8080 rabbit-ears` runs
  the public demo site (see "Demo mode" below). `demo/` has the compose file and
  nginx config for hosting it.
- `scripts/` runs on the laptop (`publish.sh`, `deploy.sh YOU@host`,
  `convert-media.sh`). `deploy/` runs on the box. `docs/box-setup.md` is the
  step-by-step for bringing a box up.

## Architecture

Diagrams of all this are in `docs/architecture.md` and the reasons in
`docs/adr/`; update them when a change alters the shape they describe.
Finished plans live in `docs/archive/`.

**The server is the only source of truth.** The TV page and the phone remote send
button presses and render whatever state comes back. Neither holds its own idea
of the channel or volume, which is why they cannot disagree.

- `RabbitEars.Core/Television.cs` is the TV as a pure state machine: no disk, no
  web, no threads. Time and randomness are injected, so tests single-step it.
  New TV behaviour belongs here, with a test in `TelevisionTests.cs`.
- `RabbitEars.Api/TvService.cs` is the one television. `Press` takes a lock,
  applies a command, and if `Revision` changed, broadcasts a full `TvSnapshot`
  to every client over SignalR. A background loop ticks the state machine, saves
  `state.json` when dirty, and rescans the media folder.
- `TvHub.cs` is the wire: remote buttons in, plus three reports from the TV page
  (`EpisodeEnded`, `EpisodeFailed`, `ReportPosition`).

Three conventions carry most of the design:

- **Play tokens.** Every "play this" instruction has a unique token that the TV
  page echoes in its reports. A report with a stale token is ignored, so a late
  "episode ended" cannot skip an episode on a channel the viewer has since
  switched to. Position reports for a channel just left still land, which is how
  resume works.
- **Sequence counters.** `bannerSeq`, `volumeSeq` and `message.seq` in the
  snapshot tick when that piece of on-screen display should flash. Clients
  compare numbers; there are no separate "show the banner" events.
- **Snapshot ordering.** Snapshots carry `boot` and `revision`. Clients drop an
  older revision from the same boot (`web/src/app/core/tv-link.ts`).

The TV page (`web/src/app/tv/`) has three parts:

- `tube.ts` runs two `<video>` elements. One is on screen while the other loads
  the next episode muted and seeks to its start point; the cut happens only when
  it can play, so a channel change never shows a dead frame.
- `crt-renderer.ts` and `crt-shader.ts` copy each video frame into a WebGL
  texture and draw it through the shader (curvature, corner mask, vignette,
  scanlines). Snow, glitch and colour bars are generated in the shader.
- `tv-screen.*` follows snapshots and draws the green on-screen display as plain
  HTML over the canvas, sized against a 4:3 frame.

With `Picture.Crt.Enabled` false, or no WebGL, the shader is skipped and the
plain video is shown.

**On the box, mpv is the screen** (`Box:Player:Kind`, `mpv` by default and
`browser` in Development). The kiosk launcher asks `/api/player` and starts mpv
with `src/RabbitEars.Api/mpv/` as its config folder; mpv plays the original
files, so the scanner accepts MKV, AVI and the like. The server drives it over
mpv's JSON control socket:

- `MpvScreen.cs` does what the TV page does, by sending mpv commands: follows
  snapshots (`loadfile` with a `start`, volume, `stop` for standby, lavfi
  `smptebars` for no channels, a lavfi snow clip before a tune), and turns
  mpv's `end-file` events into `EpisodeEnded`/`EpisodeFailed`. mpv's playlist
  entry ids play the part of play tokens. Remote keys arrive as
  `script-message ears ...` from `mpv/input.conf`.
- `MpvOsd.cs` draws the green display as ASS text in an `osd-overlay`, laid out
  on the same 4:3 frame as `tv-screen.css`. Keep the two in step.
- `mpv/crt.glsl` is `crt-shader.ts`'s tube maths as an mpv hook shader. mpv
  0.37 (Ubuntu 24.04) has no shader parameters, so the server writes a copy
  with `Picture.Crt` baked into `#define`s into the state folder.
- `MpvPlayer.cs` connects, reconnects when the kiosk restarts mpv, and feeds
  `MpvScreen` from one queue, so it never runs two things at once.

mpv runs as the desktop user and the service as `rabbitears`; the socket lives
in `/run/rabbitears-tv`, which the installer makes group-owned by `rabbitears`.
To try mpv on a laptop, run the API with `Box__Player__Kind=mpv`, then
`mpv --config-dir=src/RabbitEars.Api/bin/Debug/net10.0/mpv --input-ipc-server=.local/mpv.sock`
from the repo root.

**Demo mode** (`Box:Demo:Enabled`, set by the `Dockerfile`) is the public demo
site. `TvService` then runs one `Television` per room instead of one for the
house: the browser makes up a room id (`tv-link.ts`, kept in `localStorage` or
taken from `?room=`), the hub joins the matching SignalR group, and snapshots go
to that group only. Nothing is saved, disconnected idle rooms are removed, `/api/rescan`
is off, and `/` redirects to `/demo` (TV, remote and QR code on one page).
The demo page's VCR (`core/tape-deck.ts`) plays a visitor's own files from blob
URLs: the hub's `LoadTapes` gets only random ids and names, and `Tapes.cs` turns
them into extra channels for that room (one per show folder, loose files on
CH 00). Tape episode ids start with `tape-`, which the TV page resolves locally.
Only the loading tab can play them, so they come out when its connection drops
and it sends them again, without retuning, after a reconnect. At
home the room id is ignored and everything behaves as before. Demo tape selections
are bounded to 5 files/3 channels; browser size/duration checks keep media local.
`LoadTapes` acknowledges acceptance before the browser releases old URLs.
`DemoMediaBudget` bounds direct media concurrency and aggregate reads; room and
connection admission live in `TvService`. See `demo/protection.md` for budgets
and rollout checks. Demo mode is not
authentication and must not grow into it.

**Kept in sync by hand:** `web/src/app/core/models.ts` mirrors the records in
`RabbitEars.Core/Models.cs` and the picture options in `RabbitEars.Api/BoxOptions.cs`.
Change both sides together. `ModelSyncTests.cs` reads `models.ts` and fails when
a field, type, nullability, play reason or `DEFAULT_PICTURE` value differs from
what the server sends; a new interface also needs an entry in its `Mirrors` map.

**Media:** each folder under the media root is a channel, numbered from 2 in
A-to-Z order. Episode ids are a hash of the relative path, and `/media/{id}`
serves only files the scanner found, with range requests.

**Configuration** is the `Box` section of `appsettings.json`. Development points
the media root at `dev-media/` and state at `.local/dev-state/`. On the box,
settings are overridden in `/etc/rabbitears/box.env` using `Box__Section__Name`.
`Box:RateLimit` caps requests and hub calls per client address (`ClientRateLimit.cs`,
429 with `Retry-After`); loopback is exempt so the box's own screen is never
throttled. The demo container sets a tighter limit; `demo/compose.yaml`, where
only nginx reaches it, also trusts nginx's `X-Forwarded-For`.

## Project rules

- **Home network only.** There are no accounts, logins or pairing, by design.
  Do not add authentication.
- **Describe what code does, not where it came from.**
- **Culture-invariant.** The API builds with `InvariantGlobalization`; avoid
  culture-sensitive formatting and comparisons.
- **Commit and push only when asked.**

## Style

- C#: file-scoped namespaces, XML doc summaries that explain intent.
- Angular: standalone components, signals, `OnPush`, zoneless; separate `.html`
  and `.css` files; no `Component` suffix on file or class names; `ears-`
  selector prefix.
- Comments say why, not what, and match the density of the surrounding code.
