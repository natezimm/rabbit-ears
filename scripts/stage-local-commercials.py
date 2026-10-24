#!/usr/bin/env python3
"""Stage verified private sound-on commercial previews for LOCAL Rabbit Ears playback.

A deliberately unapproved version-1 media catalog is accepted by the dedicated
localhost-only Compose preview and rejected by the existing public v2 approval
gate. No original or staged clip is ever added to Git or Docker images.
"""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
PREVIEW_SCRIPT = ROOT / "scripts/prepare-commercial-review.py"
spec = importlib.util.spec_from_file_location("commercial_preview", PREVIEW_SCRIPT)
preview = importlib.util.module_from_spec(spec)
spec.loader.exec_module(preview)

CHANNEL = "Retro Commercials"
CHANNEL_NUMBER = 6
PRIVATE_NOTICE = "UNREVIEWED — PRIVATE LOCAL PLAYBACK ONLY; NOT CLEARED FOR PUBLIC STREAMING"


def stage(originals, reviewed, output):
    originals = preview.archive_review.separate_from_repo(originals)
    reviewed = preview.archive_review.separate_from_repo(reviewed)
    output = preview.archive_review.separate_from_repo(output)

    if not originals.is_dir() or not reviewed.is_dir():
        raise ValueError("Originals and sound-on review directories must exist.")
    if (output.exists() or output.is_symlink()
            or output == originals or output == reviewed
            or output.is_relative_to(originals) or output.is_relative_to(reviewed)
            or originals.is_relative_to(output) or reviewed.is_relative_to(output)):
        raise ValueError("Output must be a NEW private directory, separate from originals and reviews.")

    old_umask = os.umask(0o077)
    try:
        output.mkdir(parents=True, mode=0o700)
        try:
            channel_dir = output / CHANNEL
            channel_dir.mkdir(mode=0o700)
            entries = []
            counter = 0

            for item_id, source_spec in preview.SOURCES.items():
                source = originals / source_spec["fileName"]
                # Re-check exact original's bytes/SHA, name, codecs, and duration.
                preview.source_details(item_id, source)
                review_file = reviewed / item_id / "rights-review.json"
                if not review_file.is_file() or review_file.is_symlink():
                    raise ValueError(f"Private rights review file missing for {item_id}.")
                record = json.loads(review_file.read_text())
                if (record.get("sourceItem") != item_id
                        or record.get("sourceSha256") != source_spec["sha256"]
                        or record.get("sourceFileName") != source_spec["fileName"]
                        or record.get("sourceAudioRetained") is not True):
                    raise ValueError(f"Unrecognized or altered source review for {item_id}.")

                actual_previews = record.get("candidates")
                if not isinstance(actual_previews, list) or len(actual_previews) != len(source_spec["candidates"]):
                    raise ValueError(f"Unexpected candidate count for {item_id}.")
                for spec_candidate, candidate in zip(source_spec["candidates"], actual_previews):
                    if (candidate.get("id") != spec_candidate["id"]
                            or candidate.get("startSeconds") != spec_candidate["startSeconds"]
                            or candidate.get("durationSeconds") != spec_candidate["durationSeconds"]
                            or candidate.get("soundtrackAndRightsReviewed") is not False):
                        raise ValueError("Candidate doesn't match pinned private review.")
                    name = candidate.get("previewFile")
                    if name != f"{spec_candidate['id']}.mp4":
                        raise ValueError("Unexpected clip filename in private review.")
                    private_file = reviewed / item_id / name
                    if private_file.is_symlink() or not private_file.is_file():
                        raise ValueError("Missing or symlinked candidate MP4.")
                    duration, video, audio = preview.archive_review.probe(private_file)
                    if (video.get("codec_name") != "h264"
                            or audio.get("codec_name") != "aac"
                            or abs(duration - spec_candidate["durationSeconds"]) > 1.5
                            or preview.max_audio_volume(private_file) <= preview.QUIET_MAX_DB):
                        raise ValueError("Candidate failed sound-on playback QA.")
                    counter += 1
                    label = spec_candidate["title"].split(" — ", 1)[0]
                    target_name = f"{counter:02d} - {label}.mp4"
                    target = channel_dir / target_name
                    shutil.copyfile(private_file, target)
                    target.chmod(0o600)
                    entries.append({
                        "path": f"{CHANNEL}/{target_name}",
                        "title": f"{label} (unreviewed excerpt)",
                        "creator": "Archival advertisement, publisher unverified",
                        "license": PRIVATE_NOTICE,
                        "sourceUrl": source_spec["sourcePage"],
                        "duration": round(duration, 3),
                        "approvedForPublicDemo": False,
                    })

            if len(entries) != 4:
                raise ValueError("Expected exactly four audio-preserving review excerpts.")
            # Legacy version 1 is deliberately rejected by the public demo's
            # RequireApprovedMedia=true mount; only the isolated local preview uses it.
            (output / ".demo-media.json").write_text(
                json.dumps({"version": 1, "videos": entries}, indent=2) + "\n"
            )
            (output / "LOCAL_ONLY_DO_NOT_DEPLOY.txt").write_text(
                "UNAPPROVED preview media.\n"
                "NEVER serve through compose.media.yaml, port 8620, public nginx, or a public domain.\n"
                "This folder is only for demo/compose.local-commercials.yaml on 127.0.0.1:8760.\n"
                "All original soundtracks require full audiovisual and source-specific rights review.\n"
            )
            print(f"Prepared private CH {CHANNEL_NUMBER:02d}: {len(entries)} sound-on clips at {output}")
            return {"version": 1, "videos": entries}
        except BaseException:
            shutil.rmtree(output)
            raise
    finally:
        os.umask(old_umask)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--originals", type=Path, required=True)
    parser.add_argument("--reviews", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(argv)
    if not shutil.which("ffmpeg") or not shutil.which("ffprobe"):
        parser.error("FFmpeg and ffprobe are required.")
    try:
        stage(args.originals, args.reviews, args.output)
    except (OSError, ValueError, KeyError, json.JSONDecodeError,
            subprocess.CalledProcessError, subprocess.TimeoutExpired) as exc:
        print(f"Could not stage private local commercials: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
