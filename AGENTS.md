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
dotnet test --filter "FullyQualifiedName~ShuffleBagTests"                                   # one class
(cd web && npx ng build)         # web app only; output goes to src/RabbitEars.Api/wwwroot
```

- Open `/tv` for the television (`f` for full screen) and `/remote` for the phone
  remote. The first run generates short test shows in `dev-media/`.
- For frontend work with live reload: run the API (`cd src/RabbitEars.Api && dotnet run`),
  then `cd web && npm start` and use port 4200. It proxies `/api`, `/media` and
  `/hub` to 5080.
- The API serves the Angular build from `wwwroot`, which is git-ignored. `dotnet run`
  alone serves a stale or missing page until the web app has been built.
- There is no linter and no frontend test suite. .NET warnings are errors
  (`Directory.Build.props`).
- `docker build -t rabbit-ears . && docker run --rm -p 8080:8080 rabbit-ears` runs
  the public demo site (see "Demo mode" below). `render.yaml` deploys it to Render.
- `scripts/` runs on the laptop (`publish.sh`, `deploy.sh YOU@host`,
  `convert-media.sh`). `deploy/` runs on the box. `docs/box-setup.md` is the
  step-by-step for bringing a box up.

## Architecture

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

**Demo mode** (`Box:Demo:Enabled`, set by the `Dockerfile`) is the public demo
site. `TvService` then runs one `Television` per room instead of one for the
house: the browser makes up a room id (`tv-link.ts`, kept in `localStorage` or
taken from `?room=`), the hub joins the matching SignalR group, and snapshots go
to that group only. Nothing is saved, idle rooms are switched off, `/api/rescan`
is off, and `/` redirects to `/demo` (TV, remote and QR code on one page). At
home the room id is ignored and everything behaves as before. Demo mode is not
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

## Project rules

- **Home network only.** There are no accounts, logins or pairing, by design.
  Do not add authentication.
- **Credit to NostalgiaBox lives in exactly three places:** the "Inspired by"
  paragraph in `README.md`, `THIRD-PARTY-NOTICES.md`, and the comment at the top
  of `web/src/app/tv/crt-shader.ts`. Leave those as they are and do not mention
  that project anywhere else. Describe what code does, not where it came from.
- **Culture-invariant.** The API builds with `InvariantGlobalization`; avoid
  culture-sensitive formatting and comparisons.
- **Commit and push only when asked.**

## Style

- C#: file-scoped namespaces, XML doc summaries that explain intent.
- Angular: standalone components, signals, `OnPush`, zoneless; separate `.html`
  and `.css` files; no `Component` suffix on file or class names; `ears-`
  selector prefix.
- Comments say why, not what, and match the density of the surrounding code.
