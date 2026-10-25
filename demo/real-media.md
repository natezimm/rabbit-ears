# Replace the generated demo clips with real videos

Public-demo footage must have been produced in **1980 or later**, with a clear
picture and audible original sound. Check the underlying footage date rather
than its upload or restoration date. See [source criteria](../docs/media-rights.md).

The public demo already runs through nginx on host port **8620**, forwarding
to container port 8080. The optional media mount does not change that routing.

## Choose and prepare the content

Use episodes you have permission to stream publicly, or whose public-domain
status you have verified for the exact edition and soundtrack. Being on an
archive site or owning a copy does not by itself grant public streaming rights.
The example manifest is a template, not a cleared selection of shows.

1. Put the original files on your laptop, outside the repository. Install
   Python 3.10+ and ffmpeg (including ffprobe).
2. Copy `demo/media-manifest.example.json` to a private working directory as
   `media-manifest.json`. Add one video record per episode. Source paths are
   relative to the manifest, or absolute. Replace every placeholder. Keep the
   source page URL, creator credit, license and supporting rights evidence.
   `channel` is the show folder and `title` is the output file's name without
   an extension. Names can contain spaces; slashes and hidden names are rejected.
   For version 2, a human review must explicitly set `approvedForPublicDemo: true`
   with `reviewedBy`, `reviewedAt` (YYYY-MM-DD), and an accurate
   `modifications` notice. The checked-in example is intentionally unapproved.
   Supply `year` (1980 or later, based on underlying footage). Optional public
   attribution fields include `licenseUrl` and `attribution`. Keep private rights evidence and decisions outside Git.
3. From the repository, validate and then prepare:

   ```bash
   python3 scripts/prepare-demo-media.py /path/to/media-manifest.json /path/to/prepared-demo --validate-only
   python3 scripts/prepare-demo-media.py /path/to/media-manifest.json /path/to/prepared-demo
   ```

**Publishing gate:** New public releases must use a **version 2** manifest.
Version 2 refuses all unapproved entries instead of silently skipping them;
an evidence string is not automatic permission. Version 1 manifests remain usable locally for migration, but do **not**
provide an approval check. The public Compose media override requires version 2:
legacy prepared libraries must be reviewed and rebuilt before that mount is used. See [media rights review](../docs/media-rights.md).

The tool never downloads media. It supports optional `clipStartSeconds` and
`clipDurationSeconds` for precise excerpts and `framing: "preserve"` (default)
or `"letterbox-4:3"`. Clip ranges must fit the source duration; a zero
start is permitted, but durations must be positive. The full geometry/QA rules
are documented in [media-preparation.md](../docs/media-preparation.md).

Version 2 rejects missing audio and excerpts whose decoded peak does not exceed
-50 dBFS. Listening and picture review are still required. It encodes
480p-or-smaller H.264/AAC MP4s,
limits video bitrate to about 1 Mbit/s, and puts the MP4 index first for seeking.
It records durations so Live channels can advance across unseen episodes without
installing ffprobe in the running container. It also writes `.demo-media.json`
with public source/creator/license credits. Private rights evidence, reviewer identity, review date and original
source paths are not published. Version 2 metadata adds approval flags, license
links, modification notices, optional year and attribution. Re-running skips unchanged source content;
changed sources, crop/letterbox settings, trim ranges or encoder versions trigger re-encoding. Each prepared entry includes a codec/geometry/duration QA report. Failed output is not promoted to a finished
MP4, and metadata is only published after the full run succeeds.

Use a clean output directory when changing the selected lineup. Stale files are
reported rather than deleted automatically. Do not serve the directory while
preparation is in progress. Keep the private manifest as your rights record.

## Deploy a prepared library

Copy the complete prepared directory, including `.demo-media.json`, to a NEW
directory on the VM, for example `/srv/rabbitears-demo/releases/library-01`.
Make directories traversable and videos/metadata readable by the container's
non-root user. Do not copy your private original media collection.

In `~/rabbit-ears/demo/.env`, set:

```dotenv
RABBIT_EARS_DEMO_PORT=8620
RABBIT_EARS_DEMO_MEDIA=/srv/rabbitears-demo/releases/library-01
```

After updating the app to the version supporting prepared metadata, run:

```bash
cd ~/rabbit-ears/demo
docker compose -f compose.yaml -f compose.media.yaml config
docker compose -f compose.yaml -f compose.media.yaml up -d --no-build
curl -fsS http://127.0.0.1:8620/api/health
curl -fsS http://127.0.0.1:8620/api/library
curl -fsS http://127.0.0.1:8620/api/demo/credits
```

Use **both compose files for future updates**. The bind mount replaces the
generated library read-only. In version 2, only explicitly approved files listed
in prepared metadata are discoverable or streamable; unexpected files are excluded. The server scans it at startup; no public rescan
or admin write endpoint is enabled. To change the library, prepare/copy a new
release directory, change the `.env` path, and recreate the container. Keep the
old directory for rollback. To restore generated clips, run
`docker compose -f compose.yaml up -d --force-recreate --no-build`.

Check the public page, its Media credits section, channel changes and seeking.
Open a separate private browser window to confirm visitor rooms stay isolated,
and scan the QR with a phone to confirm it controls the original window's TV.
Watch VM disk space and network transfer as the selected library and visits grow.

## Known limits

The browser still needs compatible codecs; changing the extension is not enough.
The source/rights fields are a record, not automatic legal verification.
Generated clips remain the out-of-the-box default until you deploy source media.
No real copyrighted TV episodes are included in this repository or image.
