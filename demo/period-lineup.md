# Acquire and release the period lineup

The tape slot is already implemented. These hosted stations use the existing
server broadcast clock and `/media` endpoints; they do not use the VCR's local
blob URLs. No archive network access is needed by viewers or CI.

## Acquire originals

Run from the repository root on the VM or a laptop with Python 3.10+, ffmpeg and
ffprobe. Use a private working directory outside the checkout and served tree.
Allow at least 2 GB free space for the three originals, previews and prepared
files, plus space for the existing rollback release. Inspect `df -h` first.

```bash
mkdir -p "$HOME/rabbit-period-work"
python3 scripts/fetch-demo-sources.py demo/period-sources.json "$HOME/rabbit-period-work/originals"
cp demo/period-manifest.example.json "$HOME/rabbit-period-work/media-manifest.json"
```

The catalog pins exact filenames. Each download is size/checksum checked and
written atomically. Its directory contains the original MP4, archive metadata
and a SHA-256 source lock. Repeating the command verifies pinned local bytes
without contacting the archive. A failed download is retried up to three times;
a checksum disagreement stops rather than publishing the wrong file. To change
an edition, use a new working directory and repeat review. Do not delete a lock
to bypass a mismatch. Interrupted finalization may leave an unpinned file; use a
fresh directory, retaining the old directory for diagnosis.

The catalog contains Robot Reality (1990), Awesome Power (1988), and Of Ice and
Fire (1992). These are unapproved candidates, not a declaration that every frame
or soundtrack contribution is cleared. The manifest's 02:00–02:45 trims are
initial review samples, not final broadcast selections.

## Produce review samples without approving publication

This command reads the private manifest, preserves the original picture and
sound, and makes three short local previews. It does not set approval fields or
create public prepared-library metadata.

```bash
python3 - "$HOME/rabbit-period-work" <<'PY'
import json, pathlib, subprocess, sys
root = pathlib.Path(sys.argv[1])
previews = root / 'previews'
previews.mkdir(exist_ok=True)
manifest = json.loads((root / 'media-manifest.json').read_text())
for item in manifest['videos']:
    subprocess.run(['ffmpeg', '-hide_banner', '-y', '-ss', str(item['clipStartSeconds']),
        '-i', str(root / item['source']), '-t', str(item['clipDurationSeconds']),
        '-map', '0:v:0', '-map', '0:a:0', '-c:v', 'libx264', '-threads', '2',
        '-pix_fmt', 'yuv420p', '-crf', '25', '-c:a', 'aac', '-b:a', '96k',
        '-movflags', '+faststart', str(previews / (item['channel'] + '.mp4'))], check=True)
PY
```

Watch and listen to these, then inspect the credits and complete proposed final
segments. Select coherent 3–8-minute segments where suitable. Exclude silent or
historical black-and-white sections and establish the underlying footage date.
Review the exact edition and soundtrack reuse basis; preserve evidence privately.
Use fewer channels or reviewed alternates if a candidate fails.

In the private manifest, correct credits/license/year, set final trim positions,
write accurate modification notices, and complete `rightsEvidence`, `reviewedBy`
and `reviewedAt`. Set `approvedForPublicDemo` only after actual review. Never use
a made-up reviewer or approve the whole collection from an archive label.
Preparation refuses an approved entry that still contains `REPLACE` text,
including the template's `license`, so every placeholder needs a real value.

## Prepare and stage

```bash
python3 scripts/fetch-demo-sources.py demo/period-sources.json "$HOME/rabbit-period-work/originals"
python3 scripts/prepare-demo-media.py "$HOME/rabbit-period-work/media-manifest.json" "$HOME/rabbit-period-work/prepared" --validate-only
python3 scripts/prepare-demo-media.py "$HOME/rabbit-period-work/media-manifest.json" "$HOME/rabbit-period-work/prepared"
```

The first command rechecks the original hashes immediately before encoding.
Preparation runs sequentially with two encoder threads and verifies each selected
output's codec, geometry, duration and non-silent audio. A failed or unapproved
manifest does not become a public release. Preserve native framing.

Copy only `prepared/`, including `.demo-media.json`, to a new VM release directory
such as `/srv/rabbitears-demo/releases/period-01`; never copy originals, previews,
private manifests or rights evidence. Do not overwrite an active release.
Use a different release name for each later attempt. Make directories readable /
traversable and files readable by the container user.

## Activate on the VM

After merging and updating `~/rabbit-ears`, retain the current image and save
`demo/.env` privately for rollback. Record whether the current deployment uses
generated media or a prepared mount. Keep the previous release directory.
Set these entries in `demo/.env`, preserving other settings:

```dotenv
RABBIT_EARS_DEMO_PORT=8620
RABBIT_EARS_DEMO_MEDIA=/srv/rabbitears-demo/releases/period-01
```

```bash
cd ~/rabbit-ears/demo
docker compose -f compose.yaml -f compose.media.yaml config
docker compose -f compose.yaml -f compose.media.yaml up -d --build
cd ..
python3 scripts/verify-demo-release.py --base-url http://127.0.0.1:8620 --media-root /srv/rabbitears-demo/releases/period-01
```

The build can be skipped with `--no-build` only when the installed image already
contains the required merged playback changes. Both Compose files are required
for all subsequent updates using prepared media. Review resolved Compose config
locally; do not publish private environment settings.

Check https://rabbitears.nathanzimmerman.com: original sound after user interaction,
credits, three hosted channels, rapid changes, tune-away/return, QR phone control,
two isolated rooms and inserting/ejecting a VCR tape. Check logs for missing files
or repeated seeks. Passing CI is not evidence of deployed media.

If validation fails, restore the saved `.env` and previous image as needed, then
recreate with both Compose files for a previously mounted library. For the
previous generated library, use:

```bash
cd ~/rabbit-ears/demo
docker compose -f compose.yaml up -d --force-recreate --no-build
```

Verify health and playback again. Do not delete rollback media until the new
release has been checked. No production activation is performed by this PR.
