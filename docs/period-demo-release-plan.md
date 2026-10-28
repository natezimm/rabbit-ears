# Ship the 1980–1999 demo lineup in one PR

Status: acquisition tooling, tests and runbook implemented. On October 28, 2026,
the owner authorized the three 03:00–07:00 excerpts for the personal portfolio
demo based on the archive labels, accepting the documented rights uncertainty.
`demo/period-manifest.json` records that release decision; the example stays
unapproved. No agency clearance or complete watch/listen is claimed. See
[the current decision and historical review](period-media-review.md).

The release uses original sound and framing through the existing server playback
path. VM activation and public playback verification remain pending. See
[the executable runbook](../demo/period-lineup.md).

The following original plan is retained as history; its agency-clearance holds
are superseded by the owner's decision above.

Original plan, October 26, 2026. Keep this draft PR open and add
implementation, tests and review results to the same branch; do not merge a
separate planning PR. Media preparation and VM activation remain release steps,
not effects of merging GitHub code.

## Outcome and existing foundation

Visitors immediately receive real period color programming with original sound,
served locally by Rabbit Ears. Returning to a station joins its ongoing server
broadcast. Use original 1980–1999 footage, native framing and measured durations.
No modern Blender films, AI substitutes or removed silent NASA clips.

PR #36 already provides synchronized server broadcasts and approved version 2
media preparation. PR #38 already implements the tape slot: local browser files
become server-managed channels, with subfolders mapped to channels and loose
files grouped into VCR. Only the loading tab can resolve its blob URLs; video
bytes are not uploaded. Reuse this behavior without rebuilding the tape slot.

The hosted lineup uses `/media/{id}`, the existing dual-player cuts and the
read-only prepared-library mount. Keep the server authoritative and preserve
home playback, tape ownership, phone controls and isolated demo rooms.

## Candidate selection

These are candidates, not approved assets. Archive metadata was checked October
26; runtimes below are metadata values, not measured downloads. Store the full
metadata response so differences in editions and credits remain reviewable.

| Priority / channel | Archive identifier | Date evidence | Exact initial download candidate |
| --- | --- | --- | --- |
| 1 / Robotics | `gov.ntis.AVA19272` | Robot Reality, 1990 | `ava19272.vnb1_512kb.mp4` (916.88 s) |
| 1 / Weather | `gov.ntis.ava17096vnb1` | Awesome Power, 1988 | `gov.ntis.ava17096vnb1_512kb.mp4` (1037.07 s) |
| 1 / Nature | `gov.ntis.a19389vnb1` | Of Ice and Fire: The Mono Basin, 1992 | `a19389vnb1_512kb.mp4` (1252.09 s) |
| 2 / Aviation | `gov.dod.dimoc.804448` | Title says revised 1990; date field says 1991 | `gov.dod.dimoc.804448_512kb.mp4` (685.64 s); resolve edition |
| 2 / Parks | `gov.ntis.ava19377vnb1` | National Parks: An American Legacy, 1992 | Resolve exact edition; known historical black-and-white inserts |

Start with three strong channels; add Aviation and Parks in the same PR only
if their selected excerpts meet the brief. Do not fill five slots with poor or
uncertain footage. Prior FAA teletraining and Forest Service workshop candidates
remain alternates in [research notes](post-1980-demo.md).

Hold Island of Hope because its historical archival imagery is a poor match.
The Fed is a reserve candidate (metadata lists 767.23 seconds, not 22 minutes).
Exclude Computer Chronicles from this public-domain-only selection. Do not infer
that all government-hosted material or every soundtrack is public domain.

## Implementation in this PR

### 1. Resolve and acquire exact sources

Add `scripts/fetch-demo-sources.py` and a candidate source catalog under `demo/`.
Keep `prepare-demo-media.py` as the deterministic local-file encoder, with an
explicit acquisition command before it. This keeps network failures separate
from preparation and avoids changing its existing local-source contract.

- Query `https://archive.org/metadata/<identifier>` for both `metadata` and `files`.
- List eligible MP4 variants; require the catalog's exact filename rather than
  blindly taking the first h.264 result. Multiple reels are not interchangeable.
- Preserve titles, creator, date, rights notices and metadata alongside originals
  outside the served tree. Download filenames must not become arbitrary paths.
- Use HTTPS, URL-encoded path segments, timeouts, bounded retries and streaming
  downloads to temporary files. Check size and available archive checksums;
  compute SHA-256, and atomically promote only complete downloads.
- Write a source lock recording identifier, exact filename, size, SHA-256 and
  retrieval date. Reuse matching downloads; report mismatches and do not silently
  replace pinned originals. Never set approval fields from archive metadata.
- Keep binaries and private review evidence out of Git and the Docker image.

### 2. Review footage and choose excerpts

Download the three primary recordings and inspect selected sections with sound.
Check credits, production/underlying footage dates, soundtrack, color, quality,
and the public-domain basis for the exact edition and any partner contributions.
Archive labels are evidence to examine, not an automatic approval decision.

Produce 30–60-second previews with original sound for Nathan to judge the fit.
Then choose coherent multi-minute segments (target 3–8 minutes where practical)
so final stations do not repeat a tiny preview endlessly. Preserve 4:3 framing;
skip old historical inserts, blank leaders and silent sections. Avoid adding
heavy analog processing on top of the existing CRT shader.

Record exact trims, measured durations, source hashes and credits. If a source
fails review, use a reviewed alternate or ship fewer channels; record why.
The existing version 2 approval fields require an actual human review. Leave
candidates unapproved until that review is complete; do not invent a reviewer.

### 3. Prepare the release

Add a lineup-specific, explicitly unapproved manifest template and acquisition /
preparation instructions. The private completed version 2 manifest identifies
channel, title, original year, source, exact trim, public credit/license fields,
modifications and review evidence. Runtime seconds come from ffprobe, not this
table. Never publish private reviewer details or original source paths.

Run existing validate-only and preparation commands into a new staging directory.
Retain the current H.264/AAC, fast-start, geometry, non-silence and cached-output
checks. Generate `.demo-media.json` and keep the mount read-only. On the shared
VM, download/encode sequentially with bounded ffmpeg threads; check free space
for originals, staging and rollback before starting. Laptop preparation plus
transfer is also supported if VM encoding competes with the live apps.

### 4. Validate and deploy

Extend `demo/real-media.md` with exact commands for acquiring, reviewing,
preparing, staging, activating and rolling back this lineup. Reuse
`scripts/verify-demo-release.py`; extend it only for demonstrated coverage gaps.

1. Run the Python suite with mocked network fixtures, including ambiguous files,
   missing entries, failed/partial downloads, checksum mismatches, unsafe names
   and successful cache reuse. Keep real Archive downloads out of CI.
2. Run prepared-library Docker smoke tests for v2 approval, public credits,
   mounted inventory and byte ranges. Run existing backend and browser tests.
3. In browser tests, use generated fixtures to verify hosted channel switching,
   return-to-live behavior and phone sync; retain VCR insert/eject/folder coverage.
   Add regressions only where existing tests leave gaps.
4. Before production activation, finish media review and verify the candidate
   release with the mounted-library checks. Build the app image if the deployed
   version predates the merged playback/tape work.
5. Stage under `/srv/rabbitears-demo/releases/<release-id>`. Preserve the previous
   `.env` media path and release. Set `RABBIT_EARS_DEMO_MEDIA` to the new directory,
   retain port **8620**, and recreate using both Compose files. Do not prepare
   into the directory currently being served.
6. Run `verify-demo-release.py --base-url http://127.0.0.1:8620 --media-root <release>`.
   Check the public HTTPS page, actual audible playback, rapid channel changes,
   rejoining a broadcast, QR phone control, two isolated rooms and VCR eject
   returning to the hosted lineup. Watch logs for missing media or repeated seeks.
7. On failure restore the previous mount/config and recreate with both Compose
   files. If the previous release was generated media, use the documented
   base-Compose fallback. Retain rollback media until the new release is verified.

## Definition of done

- [ ] Three or more reviewed period channels, with original audible sound and
      accurate credits; any omitted candidates have a recorded reason.
- [ ] Exact sources pinned and acquisition reproducible; interrupted downloads
      cannot be mistaken for complete files.
- [ ] Selected excerpts reviewed for visual fit and reuse, with v2 approvals.
- [ ] Prepared metadata, codec/audio QA and release checks pass.
- [ ] Existing tape slot, remote and dual-player behavior pass regression checks.
- [ ] One implementation PR passes CI and includes the release runbook.
- [ ] After merge, the VM serves the new library on the existing domain/port,
      public smoke checks pass, and the previous library remains recoverable.

A merged PR alone does not complete the last checkbox. Record deployment results
separately from CI results; no auto-publication of candidate media is introduced.

Out of scope: tape-slot redesign, uploads, new authentication, antenna mechanics,
station bumpers, new player timing logic and mandatory ntsc-rs processing.
