# Channel administration and real-content demo plan

Agreed scope, October 18, 2026. The public demo is already launched behind
nginx on host port **8620** (container port 8080).

## 1. Real-content demo

Keep the generated clips as the default, reproducible demo. Add an optional
Compose override that mounts a separate, read-only media library. Do not put
large videos in git or rebuild the application to change episodes.

Prepare a small selection of cleared episodes on a laptop, not on the shared
2 GB VM. A manifest records each local source file, destination channel/title,
source URL, creator, license and rights evidence. A preparation tool validates
the entire manifest before encoding, makes 480p H.264/AAC fast-start MP4s with
a bounded bitrate, probes durations, and writes public attribution metadata.
It must not download arbitrary shows or assume an archive listing grants rights.

The runtime reads the prepared durations so Live mode works even before an
episode has played. The demo page links to credits for the mounted content.
Generated clips continue to work without a manifest. Visitor rooms, idle
cleanup, range requests, and the existing TV/remote protocol stay intact.

Acceptance: prepare and replay a sample library; validate malformed manifests,
path traversal and failed conversion; mount it without rebuilding; verify
credits, media byte ranges, two isolated rooms, channel switching, and the
phone QR on the deployed site. Publishing selected episodes is a deployment
task once the source files and streaming rights are available.

## 2. Home channel administration

Add `/admin` for the home installation. No accounts or authentication are
introduced. All management APIs are absent in demo mode; cross-origin browser
writes are refused. The public demo never gets write access to the lineup.

An administrator can rename a display label, assign a channel number (1–999),
enable/disable a channel, choose Default/Live/Resume/Random tune-in, exclude
individual episodes or whole season folders, and rescan the media library.
The UI reports browser conversion needs and preserves unsaved edits on errors.

Keep metadata in `StateDir/channels.json`, written atomically. Use the original
folder name as the channel identity, distinct from its editable display label,
so renaming/reordering preserves playback. Episode identities remain path
hashes. Automatic numbering avoids reserved custom numbers. Reject duplicate
numbers, invalid modes and unknown episode/season references. Include a version
token so one admin tab cannot silently overwrite another tab's changes.

Applying changes updates the running lineup and broadcasts a snapshot. Keep
the current episode when it is still eligible; tune to another if excluded or
disabled; show NO CHANNELS when everything is disabled. A rescan and a save
must be serialized so an old scan cannot replace a newer edited lineup.

Acceptance: persistence after restart; rename/reorder without losing progress;
per-channel modes; exclusions and restoration; empty lineup; validation and
conflict responses; public-demo and cross-origin write rejection; web build,
backend suite, and browser checks at phone/desktop widths.

## Sequence and boundaries

1. Keep this plan alongside implementation for review.
2. Implement media preparation, optional mount, duration and credits support.
3. Implement metadata storage, lineup application, management API and UI.
4. Validate and document operating steps.

No media uploads, downloads, folder deletion, transcoding inside the app,
HDMI-CEC, account system, or changes to home-network exposure are included.
File moves/renames on disk change identities; rename the display label in admin
instead. Public demo edits, if ever added, need a separate design.

## Implementation and verification

Both workstreams are implemented in this change. Operator guides are
`docs/channel-administration.md` and `demo/real-media.md`.

- 106 backend tests passed locally with the three pre-existing Unix-socket
  `MpvConnectionTests` excluded after they failed with environment permission
  errors. CI still runs the complete suite, including these tests.
- Five Python tests passed, including a real ffmpeg conversion, duration probing,
  fast-start layout, repeat-run behavior, and failure cleanup.
- Angular production build passed; wire-model consistency checks passed.
- Browser visual checks could not run: Chromium was unavailable and its download
  returned an empty archive. Phone/desktop visual QA remains to be confirmed.
- Follow-up: `web/e2e/` now automates the admin and remote scenarios against the
  real API at desktop and phone widths, including persistence, conflicts,
  exclusions, rescans, demo restrictions and overflow. See `docs/browser-tests.md`.
  CI publishes screenshots, traces, application logs and the HTML report.
- No production/hardware deployment or actual TV episode publishing has been
  performed. Source media and verified public-streaming rights are still needed
  to replace the generated clips on the live site.
