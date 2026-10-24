# Rabbit Ears — Portfolio Demo & Appliance Completion Plan

**Planning date:** October 20, 2026  
**Repository:** `natezimm/rabbit-ears` (`main`)  
**Target environment:** Public Docker demo behind nginx on shared GCP VM, bound to `127.0.0.1:8620`; separate Ubuntu mini-PC/TV installation  
**Status:** Implementation plan only. This document does not represent completed rights reviews, deployments, hardware validation, or changed code.

## 1. Outcome and definition of done

Transform Rabbit Ears from an already functioning synthetic-clip demo into a polished, legally supportable, five-channel nostalgic-TV portfolio experience while preserving its existing deterministic TV state machine, SignalR client synchronization, isolated visitor rooms, read-only media mounting, and Linux/mpv appliance functionality.

A release is complete when:

- [ ] The public demo opens quickly, displays working footage, and explains in one glance how to channel-surf, enable audio, and control the TV from a phone.
- [ ] **Five channels numbered CH 02–CH 06** contain a small, rights-reviewed catalog of approximately 10–12 edited clips, with at least two playable clips per channel; the actual number can be reduced if clearance is incomplete.
- [ ] Every published rendition, including its audio and any additions, is cleared for the actual public streaming use and relevant territories; a private evidence ledger records the decision. No title is considered cleared merely because it appears on an archive or public-domain list.
- [ ] The existing `scripts/prepare-demo-media.py` prepares reproducible, browser-compatible 480p-or-smaller MP4 assets and a public credit manifest. The application still works with generated fallback clips and **does not bundle licensed media in Git or the Docker image**.
- [ ] The desktop demo offers a visible **Enable sound** control when needed and a prominent one-click **What's on** channel guide. The existing full remote and QR remain functional; neither remote nor page keeps an independent TV state.
- [ ] Media credits contain title, creator, source URL, license or public-domain basis, license URL when appropriate, and a clear description of excerpting or other changes. The MIT code license is not misrepresented as licensing the videos.
- [ ] New browser, API, Python, and release smoke tests pass, including real H.264/AAC playback and phone-first interactions.
- [ ] The current port 8620/nginx setup survives deployment; rollback to both the previous library and generated-only fallback is documented and verified.
- [ ] Ubuntu mini-PC/mpv/Flirc hardware is exercised on the physical system; documentation states observed results and known limitations, not just design intent.
- [ ] README communicates engineering depth, architecture, live demo, controls, testing, deployment boundaries, and truthful hardware status within a recruiter-friendly first screen.

## 2. Existing implementation: reuse, do not rebuild

Verified on `main` at planning time:

| Existing capability | Implementation | Consequence |
| --- | --- | --- |
| Pure channel scheduler, Live/Resume/Random, play tokens | `src/RabbitEars.Core/Television.cs` and tests | No playback-domain redesign for the demo |
| Server-authoritative TV, SignalR, per-room demo isolation, idle eviction | `src/RabbitEars.Api/TvService.cs`, `TvHub.cs`, `web/src/app/core/tv-link.ts` | Use current state and command APIs; do not create a parallel frontend playlist |
| WebGL curved TV, two buffered browser video decks, mpv counterpart | `web/src/app/tv/*`, `src/RabbitEars.Api/MpvScreen.cs` | Keep existing renderer; only improve onboarding and demonstrate parity |
| In-TV blocked-audio hint and gesture hook | `tv-screen.html`, `tv-screen.ts`, `tube.ts` | Add a conspicuous accessible button and test the existing logic |
| Full channel guide and direct tune actions inside embedded remote | `web/src/app/remote/remote.html`, `remote.ts` | Expose guide sooner on the demo; avoid duplicate state and complex new API |
| Manifest-driven media transcode and metadata | `scripts/prepare-demo-media.py`, `demo/media-manifest.example.json` | Extend clip/credit capabilities rather than create a replacement script |
| Read-only external media mount and runtime credit endpoint | `demo/compose.media.yaml`, `src/RabbitEars.Api/DemoMedia.cs`, `/api/demo/credits` | Media remains separate from code/image and can change without rebuilding |
| Existing generated fallback channels | `scripts/make-dev-media.sh`, Dockerfile | Keep as default for local builds and CI |
| Channel editor for home only | `/admin`, `docs/channel-administration.md` | Do not expose admin rights in public demo |
| Browser test suite and CI | `web/e2e/admin.spec.ts`, `docs/browser-tests.md`, `.github/workflows/ci.yml` | Extend to demo/audio/real playback; current fixtures do not decode actual video |

**Out of scope:** new authentication or user accounts, public video upload, public `/admin`, generic archive crawler, automatic copyright judgment, channel scheduling rewrite, DRM circumvention, unrestricted streaming of mainstream 1980s/1990s commercial television, or moving a private media library into a public container.

## 3. Workstream A — Source clearance and content editorial plan

### A1. Establish the permission gate (must precede publishing media)

- [ ] Maintain a **private `media-manifest.json` and rights evidence ledger** outside Git. Track each **specific file/rendition** rather than just movie titles. Fields: canonical title, release year, source page, direct file source, producer/creator, file hash, rights basis, evidence links/screenshots, soundtrack/music rights, restorations or colorization, any added subtitles/logos, international territory suitability, permitted edits/excerpts, license URL, attribution requirements, reviewer, review date, decision (`approved`/`pending`/`rejected`), notes.
- [ ] Prefer (1) demonstrably original U.S. public-domain copies whose online streaming and applicable territories have been reviewed, (2) explicit worldwide public streaming licenses including adaptation where clipping is intended, and (3) explicitly permissioned archival footage with documented rights to reuse both the picture and sound.
- [ ] Treat 1931–1977 unrenewed/no-notice claims as requiring specific evidence. A 1936 cartoon is **not** public domain merely because it is old. Do not assume Internet Archive hosting or YouTube availability grants redistribution rights.
- [ ] Check underlying songs, newly recorded scores, restored/added material, announcers, third-party broadcast clips, and uploader claims. Avoid modern remastered, colorized, or rescored copies unless separately licensed or proven free of protected additions.
- [ ] Treat `NC`, `ND`, or `SA` conditions carefully. Prefer PD/CC0/CC BY for edited public promotional demos. Do not treat a job-seeking portfolio as automatically noncommercial. If CC BY-SA is selected, evaluate the adaptation and share-alike obligations. For CC BY-ND, don't publish edited/adapted excerpts unless permission separately permits them.
- [ ] Consider international visitors. A work entering the **U.S.** public domain does not establish worldwide public-domain status; record territorial analysis or choose worldwide-licensed alternatives. Request legal review for unresolved cases before public release.
- [ ] Mark each proposed clip **Blocked from deployment** until evidence is complete. Replace anything unresolved with another approved clip or existing generated footage; never make exceptions to fill all five channels.

Recommended primary reading:

- Duke Public Domain Day 2024 — https://cspd.law.duke.edu/publicdomainday/2024
- Duke Public Domain Day 2026 — https://cspd.law.duke.edu/publicdomainday/2026
- LOC *Night of the Living Dead* catalog — https://www.loc.gov/item/91783945/night-of-the-living-dead-1968/
- Prelinger/Internet Archive reuse FAQ — https://archivesupport.zendesk.com/hc/en-us/articles/360004715031-Prelinger-Archive
- Creative Commons attribution guidance — https://wiki.creativecommons.org/index.php?title=Recommended_practices_for_attribution
- Creative Commons license categories — https://creativecommons.org/cc-licenses/

### A2. Proposed five-channel lineup (candidates, not preapproved files)

| Channel | Editorial identity | Candidate clips | Screening note |
| --- | --- | --- | --- |
| **CH 02 — Saturday Cartoons** | Lively black-and-white and color 4:3 shorts | *Steamboat Willie* (1928 original), *Popeye the Sailor Meets Sindbad the Sailor* (1936), one 1930 Betty Boop-era original | *Steamboat Willie*'s 1928 original is US PD; confirm exact media rendition. Popeye and Betty Boop copy/edition/audio need file-level review |
| **CH 03 — Comedy Classics** | Slapstick/sitcom-like late-night TV | *Soup to Nuts* (1930 original), *Disorder in the Court* (1936 original) | 1930 originals entered US PD in 2026. Verify *Disorder* status, exact copies, and any added music |
| **CH 04 — Late Night Movies** | Black-and-white features and creature feature | *Night of the Living Dead* (1968 original black-and-white), *The General* (1926 original) | Use original versions; investigate any restoration/new musical score; consider content sensitivity of horror scenes |
| **CH 05 — Retro Learning** | Nostalgic educational and how-it-works films | Two independently verified Prelinger-era instructional films (to be selected) | Prefer complete original soundtrack with a documented reuse basis; do not invent or strip sound to work around unclear rights |
| **CH 06 — Retro Commercials** | Period television/screen commercials with original sound | Short selections from *Classic Television Commercials (Part I)* and *1955 Chevrolet Screen Ads*; third selection only if cleared | The specific archive items list U.S. public domain, but review every chosen ad's music/jingle, performers, branding, edition and territories before release |

Aim for **3 + 2 + 2 + 2 + 3 = 12** total clips, roughly **2–4 minutes each**, balancing engaging opening frames, varied audio, and natural channel rollover. For each channel select a primary and a backup work before preparation. Title and channel names should evoke the period without borrowing another network's brand or suggesting sponsorship.

### A3. Editorial rules

- [ ] Prefer **native 4:3 or 1.37:1** transfers and animated/high-motion scenes that make the CRT shader immediately visible. Keep original picture geometry; no horizontal stretching. For 16:9 sources, letterbox or choose a shot-by-shot documented crop rather than blind center crop.
- [ ] Clip selection should not start in silence or show long production cards. Existing start offsets of 6–10 seconds must not skip the interesting part; test this per excerpt. Use 1–2 shorter test clips to confirm auto-advance in CI, without shrinking all public footage.
- [ ] Keep source/editorial credits out of the surfable video if that improves continuity **only when on-page/public attribution fulfills the license**. For edits, explicitly state `excerpted`, `transcoded`, `cropped`, etc. Retain any source notices that the license requires to remain in the work.
- [ ] **Every final clip must have audible, appropriately cleared audio**: dialogue, original narration, music, or sound effects. If a soundtrack cannot be cleared, replace the clip instead of silently muting it. Avoid station/network logos or added sample music without source-specific permission.

**A acceptance:** Public rights ledger reviewed for every clip, exact SHA/source retained, safe alternate available for each uncertain candidate, 5-channel intended order settled, approval recorded before any external copy is served.

## 4. Workstream B — Extend the current media preparation contract

**Primary files:** `scripts/prepare-demo-media.py`, `demo/media-manifest.example.json`, `tests/test_prepare_demo_media.py`, `src/RabbitEars.Api/DemoMedia.cs`, `web/src/app/core/models.ts`, `web/src/app/demo/demo.html`, `tests/RabbitEars.Tests/DemoMediaTests.cs`, `tests/RabbitEars.Tests/ModelSyncTests.cs`, `demo/real-media.md`.

### B1. Media schema

- [ ] Extend private manifest entries with optional `clipStartSeconds`, `clipDurationSeconds`, `licenseUrl`, `modifications`, `year`, and `attribution` fields. Keep original `source`, `channel`, `title`, `creator`, `sourceUrl`, `license`, `rightsEvidence` semantics intact.
- [ ] Make `rightsEvidence` mandatory, nonempty and **never published**. Do **not** treat merely nonempty evidence as permission; it is a manual review record. Consider explicit `approvedForPublicDemo`/approval date in the private ledger, checked by a separate explicit `--require-approval` release validation gate.
- [ ] Define a compatible migration for existing manifest and `.demo-media.json` formats. If changing version from 1 to 2, accept supported v1 at runtime to preserve current generated/prepared releases until rollout, and extend serialization contract tests.
- [ ] Publish only public fields: channel/path, title, creator, source/attribution URL, license text and canonical license URL, modification notice, year and actual measured duration. Avoid private local filesystem paths, hashes used for rights tracking, personal notes, or evidence documents in public APIs.
- [ ] If a clip is deleted or rejected, ensure its `.demo-media.json` entry and asset cannot remain in the next release. Continue using a clean new output directory per release.

### B2. Reproducible video output

- [ ] Implement bounded clip extraction with precise, validated start/duration, reject negative or outside-length ranges, and preserve synchronized audio/video. Validate that each input has a decodable video stream and that absent audio is handled gracefully.
- [ ] Preserve pixel aspect ratio correctly (`setsar=1` after scaling as appropriate), keep 4:3 as 4:3, preserve the source crop/letterbox decision, and never stretch 16:9 to fill the tube. Target height ≤480 px, H.264 8-bit 4:2:0, AAC stereo when available, `+faststart`, browser-seekable MP4, and current bounded bitrate (~1 Mbit/s video + 96 kbit/s audio).
- [ ] Keep current atomic conversion behavior and two-thread encoding to avoid overwhelming developer machines. Optionally output a per-clip `ffprobe` QA report for width, height, SAR/DAR, frame rate, codecs, duration and average bitrate.
- [ ] Fix cache identity so a change to **clip ranges, aspect/crop mode, codec settings, or encoder version** triggers re-encode even when the source file SHA is unchanged. Preserve rerun skip for identical source + edit settings.
- [ ] Reject path traversal, symlink escapes, duplicate case-insensitive destination names, malformed URLs, wrong file types, stale assets, nonfinite durations and partially converted files. Keep generated clips independent of licensed content.
- [ ] **Optional later:** a download helper may fetch **only previously approved, explicitly listed source URLs** whose license/terms allow local downloads, with checksum verification. Do not build a general downloader or license classifier.

### B3. Credits as one authoritative output

- [ ] Reuse public `.demo-media.json` to drive `GET /api/demo/credits`; extend `DemoCredit` and Angular `DemoCredit` safely.
- [ ] Present accessible linked **Media credits** near the demo footer with Title / Author / Source / License URL / edits, sorted by channel then title. Do not display a broken placeholder link for PD works that have no license URL; instead explain the PD basis in text and link to rights reference when appropriate.
- [ ] Update `THIRD-PARTY-NOTICES.md` to distinguish third-party software/fonts from **optional externally mounted** demo media; link to the runtime credits and a checked-in public media-catalog explanation. No inaccurate claim that MIT covers footage, or that every optional clip ships in Git.
- [ ] Do not duplicate credits by hand between metadata, page, and notices. Add a small generator/check if maintaining a checked-in public media-catalog file.

**B acceptance:** Old manifest still works under documented rules; clipped/trimmed files retain correct runtime durations and begin/end points; channel Live math advances across clips; source changes force correct cache invalidation; credits are accurate; accidental publishing of `rightsEvidence` is regression-tested.

## 5. Workstream C — Demo first-run UX and navigation

**Primary files:** `web/src/app/demo/demo.html`, `demo.ts`, `demo.css`, `web/src/app/tv/tv-screen.html`, `tv-screen.ts`, `tube.ts`, `web/src/app/remote/remote.*`.

### C1. Audio unlock visible and honest

- [ ] Add a prominent, keyboard-accessible **Enable sound** CTA on top of/adjacent to the desktop CRT **when browser autoplay blocks audio**. Reuse the existing `Tube.soundBlocked` callback/state and `unlockSound()` rather than maintaining a second audio model.
- [ ] Clicking/keyboard-activating on the **TV page itself** must unmute/resume as permitted by browser policy, respect the actual `muted` and `volume` values in `TvSnapshot`, and suppress the CTA after successful unlock. Confirm whether `play()` needs to be reissued inside the gesture if playback itself was denied; do not only clear a boolean.
- [ ] If a visitor **scans the QR and only presses buttons on their phone**, the desktop browser has **not** received a user gesture and cannot be forcibly unmuted. Keep an explicit desktop-side instruction; do not promise phone controls can bypass browser security.
- [ ] Existing keyboard and pointer activation paths remain valid. CTA must not block channel-changing, fullscreen, or power interactions after dismissal. Do not break kiosk mpv, where browser autoplay restrictions do not apply.
- [ ] Ensure buttons announce actionable text to screen readers and show visible keyboard focus. The instruction should remain visible while blocked, not disappear after a timeout.

### C2. Discoverable channel guide

- [ ] Surface a compact five-row **What's on / Jump to channel** guide high in the desktop sidebar, ahead of the full remote and long instructional text. This can be a reusable extraction of the existing remote guide; do not duplicate channel state or hardcode current names.
- [ ] Derive channels, episode count/current title and active channel **only from `TvLink.state()`**. Clicking a row invokes the existing `goToChannel(number)` API. Show current selection and disconnected/empty state; allow channel changes without forcing typing digits.
- [ ] Preserve the existing `remote.html` guide for direct `/remote` users. On narrow mobile screens the TV plus tactile embedded remote must stay reachable with no horizontal overflow; do not show redundant QR prompts to someone already on their phone.
- [ ] Put a succinct onboarding line above the fold: **Choose a channel • Enable sound • Scan QR for phone remote**. Add a visible link to source and credits. Keep the nostalgic CRT central, not buried beneath explanation.

### C3. Feedback and fallback polish

- [ ] Validate loading states, blank/snow transitions, episode rollover and next-channel behavior with real assets and poor network conditions. A failed clip must not trap a viewer on black permanently.
- [ ] Test reduced-motion/high-contrast/accessibility decisions for intense flash/static effects; ensure users can read guidance in dark mode. If added UX would force a large visual redesign, favor targeted tweaks and clear controls.
- [ ] Avoid logo/trademark-shaped decoration or unauthorized show thumbnails. Keep existing generated GIF until replacement media screenshots have independently cleared rights.

**C acceptance:** A first-time desktop visitor can unmute with an obvious click and select any channel without knowing keyboard shortcuts; phone-first behavior is accurately explained; direct keyboard and phone remote still work; mobile screen has no horizontal scrolling and focus works.

## 6. Workstream D — Automated validation and CI

### D1. Python prep tests (`tests/test_prepare_demo_media.py`)

- [ ] Clip start/end valid/invalid boundaries; exact output duration tolerance; audio/video sync; missing-audio inputs; short files.
- [ ] Encoding assertions: H.264/AAC when source has audio, 8-bit yuv420p, max dimensions, correct SAR/DAR for native 4:3 and non-4:3, MP4 `moov` ahead of media data, range-friendly seeking.
- [ ] Cache fingerprint sensitivity to segment range/codec config; second identical run skips; stale output/rejected entries cannot silently remain.
- [ ] Manifest validation for duplicate paths, unsafe names/symlinks, malformed URL/license data, approval field, and exclusion of private rights evidence from published JSON.

### D2. Backend tests (`tests/RabbitEars.Tests/`)

- [ ] Demo media reader supports intended metadata versions and ignores corrupt/unscanned paths.
- [ ] Credits endpoint only exposes approved public metadata; Live-channel scheduling consumes prepared durations to advance past multiple clips without `ffprobe`.
- [ ] Model sync validates expanded metadata contract, plus the existing C#/TypeScript TV contract remains unchanged.
- [ ] Demo mode still disallows admin and rescan, persists no visitor state, evicts idle rooms and ignores stale play tokens.

### D3. Playwright (`web/e2e/demo.spec.ts` and fixtures)

- [ ] Extend existing real API test fixtures to support demo mode. Validate visible TV, sidebar guide, QR/remote room URL, current channel indicator and one-click tune on desktop and 390px phone.
- [ ] Distinct visitor rooms cannot modify one another; phone remote with matching `?room=` controls original TV; incoming room URL overrides independently stored room id as designed.
- [ ] With browser autoplay blocked, phone-only channel changes do not magically enable desktop sound; desktop **Enable sound** click unlocks it, honors mute/volume, and removes CTA. Test gesture timing without flaky arbitrary sleeps.
- [ ] Include a small **ffmpeg-created H.264/AAC fixture** and run playback/seek/end/advance through real HTTP media and `video.currentTime`, in addition to placeholder-only control tests. Generate fixture inside CI, not committed movie clips; ensure test tolerances account for browser timing.
- [ ] Check loading/failure state, rapid channel flips, no stale episode-end advances, keyboard digits, power/last/mute, and horizontal overflow. Verify screenshots/traces on failure.

### D4. CI and release gates

- [ ] Keep existing `dotnet test`, Angular build, Chromium Playwright, Python media tests and demo image smoke test in `.github/workflows/ci.yml`.
- [ ] Add isolated **prepared-media integration** smoke test with generated fixtures: `--validate-only`, prepare, optional read-only mount, `/api/library`, `/api/demo/credits`, byte-range `206`, status and two room requests.
- [ ] Ensure current synthetic default container smoke test still passes without external licenses or private media; no network-dependent film downloads in CI.
- [ ] Make all required checks green before merge and save CI artifacts for failed browser cases; do not claim Safari/Firefox or physical hardware is CI-verified when it isn't.

**D acceptance:** Full CI green on PR and `main`, repeatable with no external media, and one successful real-video Playwright run per desktop/phone test project where practical.

## 7. Workstream E — Public deployment and recovery on shared GCP VM

### E1. Preflight

- [ ] Confirm live nginx upstream remains **`127.0.0.1:8620`**, container listener `8080`, HTTPS working, SignalR WebSocket upgrade headers and HTTP range support.
- [ ] Check baseline `docker compose ps`, `docker stats`, `df -h`, `free -h`, disk and network usage alongside other portfolio apps. Respect existing `mem_limit: 256m`; note image build can require much more memory and CPU than serving.
- [ ] Record exact `main` commit/image tag, active library path, currently configured Compose files, and a tested recovery command before changes.
- [ ] If shared VM build pressure is unacceptable, use a separately built/tagged image from CI or workstation, with an authenticated registry/pull procedure; this optimization is optional and should not complicate initial media-only deployment.

### E2. Stage immutable media release

- [ ] Prepare and test all files on development machine, not the shared VM. Keep original source and private manifest there. Stage output to a **new versioned VM directory** such as `/srv/rabbitears-demo/releases/library-2026-10-a`, including `.demo-media.json`.
- [ ] Confirm `ffprobe` report, playable streams, permissions for the container's nonroot user, expected 5 channel folders, all credits, no stale files, no private ledger.
- [ ] Update `~/rabbit-ears/demo/.env` to set `RABBIT_EARS_DEMO_PORT=8620` and `RABBIT_EARS_DEMO_MEDIA=<versioned directory>`.
- [ ] Validate expanded Compose config **before** swapping: `docker compose -f compose.yaml -f compose.media.yaml config`.
- [ ] Deploy with **both files**: `docker compose -f compose.yaml -f compose.media.yaml up -d --no-build` (build/update image separately if code changed). Never use plain `docker compose up` for a configured media release—it drops the override.

### E3. Post-deploy smoke / rollback

- [ ] `curl -fsS http://127.0.0.1:8620/api/health` returns healthy status.
- [ ] `curl -fsS http://127.0.0.1:8620/api/library` shows intended five channels and expected playable episode counts; `/api/demo/credits` corresponds exactly to the prepared library.
- [ ] `curl -I` or explicit `Range` GET confirms partial responses (`206`) for real MP4 content; check through nginx HTTPS and directly at localhost.
- [ ] Exercise QR on real phone, direct tune, audio gesture, idle/reconnect, two private browser rooms, rollover, desktop and phone layout, and simulated limited bandwidth.
- [ ] Observe disk, container memory, CPU, egress and error logs under light concurrent usage. Media egress—not app RAM—may become the cost driver; preserve a conservative cap and avoid aggressive preload.
- [ ] Roll back to the prior staged media directory by restoring `.env` and recreating with **both Compose files**. Roll back to generated-only default by recreating with `compose.yaml` alone (and verifying the old override was removed). Retain a prior release until smoke checks pass.
- [ ] Keep a short `demo/release-checklist.md` and if useful a shell script that checks health/library/credits/ranges without destructive actions.

**E acceptance:** Public demo uses actual cleared footage at the current URL without impacting other apps, ports, data, or reverse proxies; a rollback is demonstrated and recorded.

## 8. Workstream F — Physical Ubuntu mini-PC / TV verification

**Primary files:** `docs/box-setup.md`, `scripts/deploy.sh`, `deploy/*`, `src/RabbitEars.Api/MpvScreen.cs`, `MpvPlayer.cs`, `src/RabbitEars.Api/mpv/*`.

- [ ] Establish exact target Ubuntu/graphics/mpv versions, connected TV and Flirc model. Update outdated README/install-guide statements **after** testing. Keep home unit separate from public demo; do not expose port `5080` to the public Internet.
- [ ] From laptop, deploy to physical machine using documented `bash scripts/deploy.sh YOU@rabbitears.local --test-clips`; verify `/api/health`, `/api/library`, auto-login, systemd service health and kiosk startup across a real reboot.
- [ ] Verify mpv Unix socket connectivity, service/user permissions (a prior black-screen issue had required a fix), and that video files play natively without transcoding. Test MP4, MKV and at least one awkward codec/container if available.
- [ ] Check 1080p HDMI output, correct TV audio sink, smooth 4:3 CRT shader, channel banner and volume OSD, snow/glitch/static, power tube-collapse, black frames and playback latency. Compare browser and mpv appearance/behavior; record any acceptable rendering differences.
- [ ] Exercise Flirc mapping for arrows, digits/Enter, M/P/L/I, hold-to-repeat protection, standby/wake and optional graceful poweroff at zero volume.
- [ ] Exercise phone remote from same LAN with device handoff; confirm server-authoritative state, disconnected/reconnected clients, multiple rapid commands, return to prior channel, Live catchup while away, Resume channel persistence and power-cycle recovery.
- [ ] Observe CPU, RAM, decoder/GPU pressure, heat/noise, TV input and audio behavior after an extended session. Test clean reboots and services restarting. Do **not** test unsafe sudden power loss as if it guaranteed filesystem integrity.
- [ ] Capture screenshot/short demonstration **using rights-safe media**, logs or a concise hardware-test matrix, known issues and actual model/version; optionally link from the README.

**F acceptance:** Repeatable clean boot-to-playing-TV path on the real mini PC; controls and synchronization demonstrated; docs clearly separate verified behavior from untested features.

## 9. Workstream G — README, architecture, portfolio narrative

**Primary files:** `README.md`, `AGENTS.md` (only where engineering conventions change), `docs/*`, `THIRD-PARTY-NOTICES.md`, `docs/demo.gif`, portfolio-site entry/link.

- [ ] Rewrite the top of README as **product hook → live demo → animated screenshot → 3-minute try-it instructions → engineering highlights**; keep setup/how-to sections below, not removed. Only link a verified live deployment URL.
- [ ] Add 5–7 succinct engineering highlights with clickable source/test links: deterministic `Television` + `TimeProvider`/randomness, Live catch-up, play-token race guards, snapshot boot/revision sequencing, SignalR shared-state model, double-buffered WebGL CRT / mpv IPC dual renderer, no-persistence isolated demo rooms and eviction, `ModelSyncTests`, Playwright real-browser tests.
- [ ] Include small Mermaid architecture graphic showing **remote/web CRT → SignalR → authoritative TvService → pure Television → browser or mpv → media library**, with demo room boundary and home/single-TV separation labeled correctly. Do not accidentally suggest mpv is in the cloud demo.
- [ ] Add a modest 'Tradeoffs & boundaries' section: local only home auth assumption, browser autoplay, real TV source rights, room link as capability not authentication, codec differences, keeping bandwidth/disk low, and known hardware limitations.
- [ ] Capture updated rights-cleared or synthetic-safe GIF/thumbnail and succinct copy for the external software-engineering portfolio: 'Built a real-time multi-client TV emulator with deterministic channel scheduling, server-authoritative playback, dual WebGL/mpv renderers, and isolated demo sessions.'
- [ ] Document test commands and link `docs/browser-tests.md`, `demo/real-media.md`, `docs/channel-administration.md`, and the hardware checklist; avoid unverifiable performance numbers and claims.
- [ ] Separate code's MIT license from rights of mounted media; update `THIRD-PARTY-NOTICES.md` and media credits to agree.

**G acceptance:** Reader can identify what the system does, launch the live demo, find source/tests, understand architecture and tradeoffs, and distinguish real validation from planned work without following several deep links.

## 10. Proposed delivery sequence / PR boundaries

Use focused, reviewable PRs; each is independently testable. Media clearance can progress in parallel but **public release waits for rights approval**.

| Sequence | Branch / proposed PR | Scope | Mandatory checks | Exit gate |
| --- | --- | --- | --- | --- |
| 1 | `demo-onboarding` | Audio CTA; first-visit prompts; prominent dynamic channel guide; mobile UX; demo Playwright | Angular build + Playwright desktop/phone + backend | First-time visitor can start, select, unmute; phone-first caveat correct |
| 2 | `demo-media-contract` | Manifest/credit metadata fields; `DemoMedia` and TypeScript contract; edition/clip evidence docs; privacy tests | `dotnet test`, Python tests, model sync, app build | Version compatibility; public credits complete; no rights evidence leaks |
| 3 | `demo-clipping-and-tests` | FFmpeg clip ranges, geometry, encoder fingerprint, media QA, real-video Playwright and image/mount smoke | Python/real browser/image CI | Repeatable 4:3 H.264/AAC; seek/rollover; all negative cases pass |
| 4 | `demo-release-operations` | Release checklists/scripts, Compose override guardrails, verification and rollback docs; optional image-build optimization | Docker integration smoke | Media swap/rollback rehearsed without touching other services |
| 5 | **Media publication runbook (operational, not a Git media PR)** | Rights ledger sign-off; prepare ~12 clips; staged read-only mount on VM port 8620 | Production checklist + live browser/device tests | Five cleared channels actually on air; backup release ready |
| 6 | `hardware-verification` | Run physical install checklist; fixes, mpv parity tests and factual docs | Relevant .NET/browser tests + manual evidence | Boot/reconnect/Flirc/audio verified on machine |
| 7 | `portfolio-readme` | Engineering-highlights, Mermaid architecture, demo GIF, accurate docs/credits, portfolio link | Link check + documentation review + CI | Recruiter-friendly story accurately reflects shipped system |

PR 2 and PR 3 can be merged if the diff stays reviewable; PR 7 may be drafted earlier but must be fact-checked after release/hardware validation. Keep generated media CI default; avoid Git LFS/video asset commits. Do not merge changes that require a real media source into the synthetic fallback without compatibility tests.

## 11. Full final acceptance matrix

| Case | How to verify | Required result |
| --- | --- | --- |
| Start fresh desktop | New incognito tab → `/demo` | TV plays, onboarding visible, no account required |
| Sound blocked | Default browser policy, no desktop interaction | Clear audio CTA; video still visible where muted autoplay allowed |
| Sound unlock | Click CTA on desktop tab | Unmuted audio according to server volume/mute, CTA removed |
| Phone-first | Scan QR, use remote without touching desktop tab | TV channel changes, desktop audio remains subject to gesture; hint remains |
| Channel guide | Click CH 02–CH 06 in desktop sidebar | Correct live channel selected and remote guide updates |
| Mobile | Open `/demo` on phone width | Embedded remote usable, no horizontal overflow |
| Multiple visitor rooms | Incognito browser A and B | Actions do not cross rooms; matching QR/room controls its paired TV |
| Rapid tune | Switch repeatedly during buffering | Latest command wins; stale end/position reports do not skip new episode |
| Live scheduling | Leave/return to channel across rollover | Returned position reflects elapsed time and clip durations |
| Browser compatibility | Real FFmpeg prepared MP4 over API | Frames decode, seek works, clip ends and advances |
| Credit accuracy | `/api/demo/credits` and UI | Every published asset credited, no private ledger/path data |
| Public API boundary | GET/POST admin/rescan in demo mode | Admin/mutation unavailable |
| Media transport | HTTP range through nginx | Partial content works; seeks do not download from start |
| Performance | Check concurrent sessions and transfer | No impact to other VM apps; conservative app memory and egress |
| Recovery | Revert `.env` to prior library then generated default | Both rollback paths restore known-good playback |
| Appliance | Cold reboot of real mini-PC | Kiosk auto-launches, HDMI audio and Flirc/phone controls operate |
| CI | `main` and PR workflows | Backend, Python, web browser, Docker smoke all green |
| Portfolio docs | Review README as unauthenticated recruiter | Demo, architecture, test links, limitations are easy to find |

## 12. Risk register and response

| Risk | Likely consequence | Mitigation / stop condition |
| --- | --- | --- |
| Edition or soundtrack has hidden rights | Takedown/legal exposure | File-level evidence, licensed substitutions; block publication until cleared |
| U.S. PD does not cover worldwide visitors | Territorial exposure | Document geography; prioritize worldwide-cleared works or seek counsel/limit distribution |
| A license forbids edited clips | License violation | Avoid ND/restricted edits; acquire separate permission or use an original version |
| Caching ignores clip edit settings | Wrong excerpt delivered | Fingerprint source + encoding profile + crop/clip params |
| Browsers refuse sound after remote-only action | Silent first experience | Prominent on-TV CTA; no attempted policy bypass |
| Long clips/large requests saturate VM egress | Higher GCP cost, sluggish other sites | Bounded 480p bitrate, video range, low active limits, bandwidth observation |
| Build pressure on shared VM | Other site outages | Prepare off-VM; stage images ahead; optional registry-based deployments |
| CI's placeholder media misses codec issues | Blank video in production | Real generated MP4 in integration/browser smoke tests |
| Media/Compose override lost during app update | Synthetic fallback unexpectedly appears | Explicit two-file Compose command and deployment script guard |
| mpv/Flirc hardware differs from simulation | Broken real appliance | Real device matrix, record versions and repair regressions before README claim |

## 13. Immediate next actions

1. Begin **PR 1 (`demo-onboarding`)** using existing `soundBlocked` and `TvLink.state()` signals; add test fixtures for demo mode before UI implementation.
2. In parallel, start the private clip-rights ledger. Choose **one verified source file per channel first**; do not acquire the full catalog until source suitability is established.
3. Design PR 2's additive metadata fields and migration rule before writing PR 3's FFmpeg edit changes. Preserve generated fallback and existing v1 releases.
4. After PRs 1–4 are merged, prepare an immutable five-channel media release locally, deploy it via **both** Compose files to port 8620, smoke-test and record rollback.
5. Validate the real Ubuntu mini PC, then finish README and portfolio copy with only demonstrated claims.

**Release principle:** rights and production validation are approval gates, not marketing checklist items. If fewer works are cleared, launch a smaller honest lineup with generated fallback rather than assuming streaming permission.
