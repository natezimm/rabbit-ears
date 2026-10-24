#!/usr/bin/env python3
"""Make PRIVATE, sound-on candidate excerpts from two SHA-256-pinned archive reels.

The windows below are navigation aids, NOT confirmed ad boundaries, licenses,
or approved clips. This script never downloads media or creates a publishing
manifest. All source and output files must be outside the Git checkout.
"""
import argparse
import hashlib
import html
import importlib.util
import json
import math
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

REPO_ROOT = Path(__file__).resolve().parents[1]
REVIEW_MODULE = REPO_ROOT / "scripts/review-archive-media.py"
spec = importlib.util.spec_from_file_location("rabbit_ears_archive_review", REVIEW_MODULE)
archive_review = importlib.util.module_from_spec(spec)
spec.loader.exec_module(archive_review)

# Verified in GitHub Actions run 40057053268 (October 24, 2026).
# Do not silently accept alternate or "higher quality" derivatives.
SOURCES = {
    "classic-tv": {
        "fileName": "ClassicT1948_512kb.mp4",
        "sha256": "d7d2cf8b1e75a7abde8d150cac768f19123f82b4453e0ba1914667734aca497e",
        "bytes": 38145570,
        "durationSeconds": 536.503,
        "sourcePage": "https://archive.org/details/ClassicT1948",
        "title": "Classic Television Commercials, Part I (1948–1969)",
        "candidates": [
            {
                "id": "remington-scout",
                "title": "Remington commercial — scouting window",
                "startSeconds": 150.0, "durationSeconds": 84.0,
                "note": "Provisional vicinity based on item shotlist and technical scene changes. May contain adjacent ads.",
            },
            {
                "id": "mum-scout",
                "title": "Mum deodorant commercial — scouting window",
                "startSeconds": 260.0, "durationSeconds": 85.0,
                "note": "Provisional vicinity; watch with sound and identify actual start and end.",
            },
        ],
    },
    "chevrolet-1955": {
        "fileName": "1955Chev1955_512kb.mp4",
        "sha256": "60fcbb2496c602bf7f8a0b5fe4efb25a0e2acd9f93488081c46ca6fe9cce5b2a",
        "bytes": 37344324,
        "durationSeconds": 527.327,
        "sourcePage": "https://archive.org/details/1955Chev1955",
        "title": "1955 Chevrolet Screen Ads",
        "candidates": [
            {
                "id": "chevy-opening-scout",
                "title": "Early Chevrolet screen ad — scouting window",
                "startSeconds": 0.0, "durationSeconds": 64.0,
                "note": "Early reel segment; may include title card or multiple ads. Exact ad boundaries unverified.",
            },
            {
                "id": "chevy-middle-scout",
                "title": "Middle Chevrolet screen ad — scouting window",
                "startSeconds": 250.0, "durationSeconds": 88.0,
                "note": "Alternative reel segment for audiovisual and rights comparison; exact ad boundaries unverified.",
            },
        ],
    },
}
QUIET_MAX_DB = -55.0


def source_details(item, source):
    entry = SOURCES[item]
    given = Path(source).expanduser()
    if given.is_symlink():
        raise ValueError("Source must not be a symlink.")
    path = archive_review.separate_from_repo(given)
    if not path.is_file() or path.name != entry["fileName"]:
        raise ValueError("Wrong source file; use the exact pinned rendition.")
    size, actual_sha = archive_review.sha256_file(path)
    if size != entry["bytes"] or actual_sha != entry["sha256"]:
        raise ValueError("Source size or SHA-256 mismatch; refusing an unreviewed edition.")
    duration, video, audio = archive_review.probe(path)
    if abs(duration - entry["durationSeconds"]) > 1.0:
        raise ValueError("Source duration differs from GitHub's audited rendition.")
    return path, duration, video, audio


def max_audio_volume(path):
    result = subprocess.run(
        ["ffmpeg", "-hide_banner", "-nostdin", "-i", str(path),
         "-map", "0:a:0", "-vn", "-af", "volumedetect", "-f", "null", "-"],
        capture_output=True, text=True, check=True, timeout=120,
    )
    matches = re.findall(r"max_volume:\s*(-?(?:\d+(?:\.\d+)?|inf))\s*dB", result.stderr)
    if not matches:
        raise ValueError("Could not verify excerpt audio amplitude.")
    return float(matches[-1])


def render_candidate(source, target, candidate):
    start = float(candidate["startSeconds"])
    length = float(candidate["durationSeconds"])
    if (not math.isfinite(start) or not math.isfinite(length)
            or start < 0 or length <= 0):
        raise ValueError("A clip must have a valid positive duration and nonnegative start.")
    archive_review.ffmpeg_artifact(source, target, [
        "-ss", str(start), "-t", str(length),
        "-map", "0:v:0", "-map", "0:a:0",
        "-vf", "scale=-2:480:force_original_aspect_ratio=decrease,setsar=1",
        "-c:v", "libx264", "-threads", "2", "-preset", "veryfast",
        "-crf", "27", "-pix_fmt", "yuv420p", "-c:a", "aac",
        "-b:a", "128k", "-ac", "2", "-movflags", "+faststart",
    ])
    actual_length, output_video, output_audio = archive_review.probe(target)
    if abs(actual_length - length) > 1.5:
        raise ValueError("Output duration did not match the requested scouting window.")
    if output_video.get("codec_name") != "h264" or output_audio.get("codec_name") != "aac":
        raise ValueError("Expected an H.264/AAC preview with original-source sound.")
    max_db = max_audio_volume(target)
    if not math.isfinite(max_db) or max_db <= QUIET_MAX_DB:
        raise ValueError("Output has missing or effectively silent audio; reject this window.")
    return {"durationSeconds": round(actual_length, 3),
            "audioCodec": "aac", "videoCodec": "h264", "maxAudioVolumeDb": max_db}


def make_review(item, source_path, workspace):
    if item not in SOURCES:
        raise ValueError("Unknown reel.")
    source, duration, video, audio = source_details(item, source_path)
    entry = SOURCES[item]
    private = archive_review.private_workspace(workspace)
    destination = private / item
    if destination.exists() or destination.is_symlink():
        raise FileExistsError("Review already exists; use a new private workspace to preserve prior notes.")
    # Restrict newly generated review files and any nested output.
    previous_umask = os.umask(0o077)
    try:
        destination.mkdir(mode=0o700)
        previews = []
        try:
            for candidate in entry["candidates"]:
                start, length = candidate["startSeconds"], candidate["durationSeconds"]
                if start + length > duration - 0.5:
                    raise ValueError(f"Candidate {candidate['id']} extends beyond the source.")
                output_file = f"{candidate['id']}.mp4"
                qa = render_candidate(source, destination / output_file, candidate)
                previews.append({
                    **candidate,
                    "previewFile": output_file,
                    "technicalQA": qa,
                    "soundtrackAndRightsReviewed": False,
                    "actualAdStartSeconds": None,
                    "actualAdEndSeconds": None,
                    "approvedForPublicDemo": False,
                    "editorialNotes": "",
                    "musicAndJingles": "UNREVIEWED",
                    "narratorTalentAndTrademarks": "UNREVIEWED",
                })
            review_data = {
                "purpose": "PRIVATE AUDIOVISUAL SCOUTING, NOT LICENSE APPROVAL",
                "sourceItem": item, "sourceTitle": entry["title"],
                "sourcePage": entry["sourcePage"],
                "sourceFileName": entry["fileName"],
                "sourceSha256": entry["sha256"],
                "sourceBytes": entry["bytes"],
                "sourceDurationSeconds": round(duration, 3),
                "sourceVideoCodec": video.get("codec_name"),
                "sourceAudioCodec": audio.get("codec_name"),
                "sourceAudioRetained": True,
                "soundtrackReviewed": False,
                "fullPlaybackReviewed": False,
                "permissionToStreamPubliclyEstablished": False,
                "territoriesReviewed": False,
                "reviewer": None, "reviewedAt": None,
                "rightsEvidence": "",
                "approvedForPublicDemo": False,
                "candidates": previews,
                "warning": "Never copy these clips into the public media mount or flip approval based on technical QA.",
            }
            archive_review.write_new(destination / "rights-review.json",
                                     json.dumps(review_data, indent=2) + "\n")
            sections = []
            for p in previews:
                src = html.escape(p["previewFile"], quote=True)
                title = html.escape(p["title"])
                note = html.escape(p["note"])
                start, end = p["startSeconds"], p["startSeconds"] + p["durationSeconds"]
                sections.append(
                    f"<section><h2>{title}</h2><p>Source {start:.1f}–{end:.1f}s. "
                    f"{note}</p><video controls preload='metadata' src='{src}'></video></section>"
                )
            page = ("""<!doctype html><html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Rabbit Ears Private Sound-On Review</title>
<style>body{font:16px/1.55 system-ui;margin:2rem auto;max-width:850px;padding:1rem}
video{display:block;width:100%;max-width:800px}section{margin:2rem 0}
.warning{border:2px solid #b45309;padding:1rem}</style></head><body>
<h1>Sound-on commercial scouting</h1>
<p class="warning"><strong>PRIVATE, PENDING RIGHTS REVIEW.</strong>
These are broad scouting windows, not verified commercial boundaries. Hearing audio
does not mean the soundtrack is licensed. Do not deploy these previews.</p>
<p>Play every candidate from beginning to end WITH sound. Check for music/jingles,
announcers, performances, logos, original edition, territory and portfolio use.
Record exact ad boundaries and evidence in <code>rights-review.json</code>.</p>
""" + "\n".join(sections) + "</body></html>")
            archive_review.write_new(destination / "index.html", page)
        except BaseException:
            shutil.rmtree(destination)
            raise
        return review_data, destination
    finally:
        os.umask(previous_umask)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--item", choices=SOURCES.keys(), required=True)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--workspace", type=Path, required=True)
    args = parser.parse_args(argv)
    if not shutil.which("ffmpeg") or not shutil.which("ffprobe"):
        parser.error("Install FFmpeg and ffprobe.")
    try:
        _, folder = make_review(args.item, args.source, args.workspace)
    except (OSError, ValueError, subprocess.CalledProcessError,
            subprocess.TimeoutExpired, json.JSONDecodeError) as exc:
        print(f"Private review failed: {exc}", file=sys.stderr)
        return 1
    print("Private review viewer:", folder / "index.html")
    print("Approval status: PENDING — no content published or authorized.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
