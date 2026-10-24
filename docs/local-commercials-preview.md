# Watch real 1950s advertisements in Rabbit Ears — LOCAL CH 06 only

**Status: usable local preview, not publication clearance.** This is a hands-on way to test the actual Rabbit Ears TV, CRT, channel guide, phone remote and **original archive audio**, without loosening the public media approval gate.

## One-command experience on your Mac

Requires Docker Desktop running, Python 3.10+, curl and FFmpeg. Install FFmpeg with `brew install ffmpeg`. The checkout must contain PR #35.

```bash
cd ~/rabbit-ears  # Adjust if your checkout lives elsewhere
git pull
bash scripts/start-local-commercials.sh
```

The launcher only runs on macOS. It:

1. Downloads the two exact Internet Archive files verified in [GitHub Actions run #40057053268](https://github.com/natezimm/rabbit-ears/actions/runs/40057053268), if not already present, to `~/Movies/RabbitEars/Originals`. Checks bytes and SHA-256, rejecting unexpected editions.
2. Makes four **private sound-on** H.264/AAC candidate excerpts and their original-audio rights checklists in `~/Movies/RabbitEars/CommercialReview`. On an existing review, no old rights notes are overwritten.
3. Copies those four candidates to an isolated `~/Movies/RabbitEars/LocalCommercialsLibrary` and prepares **unapproved version 1 preview metadata**, labeled `UNREVIEWED — PRIVATE LOCAL PLAYBACK ONLY`.
4. Starts a **separate** Docker Compose project with the real app at **http://127.0.0.1:8760/demo**. Only this loopback port is published. It mounts private media read-only; Docker runs as the same user/group as the owner of your local video files.
5. Confirms the app reports **CH 06 — Retro Commercials** with **four** episodes before opening your browser.

Click **Enable sound** on the TV, then select CH 06. Try the on-page remote and QR phone remote; the latter may not connect from a different device because this preview deliberately binds to localhost only. For phone testing, run a separate *approved* test arrangement; do not expose pending commercial footage on your LAN or to the internet.

The four broad *scouting* windows are:

| Source | Window | What to check |
| --- | --- | --- |
| Classic TV | 2:30–3:54, Remington neighborhood | Exact start/end, possible adjacent spots, original narration/jingles |
| Classic TV | 4:20–5:45, Mum neighborhood | Exact spot boundaries, original music and speech |
| Chevrolet | 0:00–1:04 | Does the opening contain one complete ad or multiple spots? |
| Chevrolet | 4:10–5:38 | Identify a complete color 1955 automobile commercial |

**These are not final edits.** We know both originals contain decodable AAC audio, but nobody has fully watched/listened to them in this workflow. Some windows may contain other commercials. An archive's U.S. public-domain notice does not itself clear every jingle, performer, altered soundtrack, publicity claim, or territory.

## Stop safely

```bash
bash scripts/stop-local-commercials.sh
```

This stops **only** the local preview project. It leaves your normal Rabbit Ears installation, GCP port **8620**, public Compose project, media mount and nginx unchanged.

To start again, run the start script. To regenerate reviews after a change, first back up any notes and remove **only** the private `CommercialReview` and `LocalCommercialsLibrary` folders; the originals may be kept. The scripts refuse to overwrite existing notes or unapproved media directories.

## Tests and safety architecture

- `tests/test_prepare_commercial_review.py`: original soundtrack retention, audible AAC, exact source integrity, private notes, no publishing.
- `tests/test_stage_local_commercials.py`: synthetic end-to-end original → candidate preview → local library, four video entries, no automatic rights approval, and launcher syntax.
- `tests/smoke-local-commercials.sh`: Docker's actual media scanner, CH 06, media credits, and HTTP range-streaming using **synthetic generated audio/video only**. The same unapproved version-1 library must return zero episodes under `Box__Demo__RequireApprovedMedia=true`—the real public release gate.
- `demo/compose.local-commercials.yaml`: standalone local project with only `127.0.0.1:8760` published. Do **not** combine it with `demo/compose.yaml` or `demo/compose.media.yaml`, copy the previews onto a server, or link port 8760 to nginx.

The local version-1 manifest intentionally permits experimentation. **For a real public release**, source-by-source audiovisual/rights review must happen first, then every clip must be encoded through `scripts/prepare-demo-media.py` using the existing **approved version-2 manifest**. The public `demo/compose.media.yaml` keeps `Box__Demo__RequireApprovedMedia=true` and refuses these unapproved preview clips.

## Next after visual and sound review

Use the private review pages and watch each proposed excerpt in full. Record exact ad boundaries, whether music/jingles are present and reusable, identifiable talent, territory suitability, rights evidence and appropriate credits. Replace any clip whose soundtrack cannot be cleared rather than muting it. Only then prepare approved CH 06 assets and stage a rollback-safe public release.
