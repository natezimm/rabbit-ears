#!/usr/bin/env python3
"""Create a PRIVATE, sound-on review package for individual archival source reels.

This tool does not download, approve, publish, or deploy footage. Obtain the
source yourself and review every chosen advertisement and its soundtrack.
Only files OUTSIDE the Git checkout are accepted for input and output.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import html
import json
import math
import os
from pathlib import Path
import shutil
import stat
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
CATALOG = {
    "classic-tv": {
        "title": "Classic Television Commercials, Part I (1948–1969)",
        "sourcePage": "https://archive.org/details/ClassicT1948",
        "expectedFilename": "ClassicT1948_512kb.mp4",
    },
    "chevrolet-1955": {
        "title": "1955 Chevrolet Screen Ads",
        "sourcePage": "https://archive.org/details/1955Chev1955",
        "expectedFilename": "1955Chev1955_512kb.mp4",
    },
    "design-for-dreaming": {
        "title": "Design for Dreaming (1956) — music-rights concerns",
        "sourcePage": "https://archive.org/details/Designfo1956",
        "expectedFilename": "Designfo1956_512kb.mp4",
    },
}


def separate_from_repo(path, repo=ROOT):
    resolved = Path(path).expanduser().resolve()
    checkout = repo.resolve()
    if (resolved == checkout or resolved.is_relative_to(checkout)
            or checkout.is_relative_to(resolved)):
        raise ValueError("Media source and review workspace must be outside the repository.")
    return resolved


def private_workspace(path):
    original = Path(path).expanduser()
    if original.is_symlink():
        raise ValueError("Private workspace cannot be a symlink.")
    workspace = separate_from_repo(original)
    if workspace.exists():
        if not workspace.is_dir() or stat.S_IMODE(workspace.stat().st_mode) & 0o077:
            raise ValueError("Workspace must be a private directory (chmod 700).")
    else:
        workspace.mkdir(parents=True, mode=0o700)
    return workspace


def sha256_file(path):
    digest = hashlib.sha256()
    size = 0
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
            size += len(block)
    return size, digest.hexdigest()


def probe(path):
    result = subprocess.run(
        ["ffprobe", "-v", "error", "-show_format", "-show_streams", "-of", "json", str(path)],
        check=True, capture_output=True, text=True, timeout=60,
    )
    data = json.loads(result.stdout)
    videos = [s for s in data.get("streams", []) if s.get("codec_type") == "video"]
    audios = [s for s in data.get("streams", []) if s.get("codec_type") == "audio"]
    if not videos:
        raise ValueError("Expected a video stream.")
    if not audios:
        raise ValueError("Source has no audio stream; select a different sound-bearing rendition.")
    duration = float(data.get("format", {}).get("duration", 0))
    if not math.isfinite(duration) or duration <= 0 or duration > 7200:
        raise ValueError("Source duration must be between 0 and 7200 seconds.")
    return duration, videos[0], audios[0]


def ffmpeg_artifact(source, target, args):
    fd, name = tempfile.mkstemp(dir=target.parent, prefix=".pending-", suffix=target.suffix)
    os.close(fd)
    temporary = Path(name)
    try:
        subprocess.run(
            ["ffmpeg", "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
             "-i", str(source), *args, str(temporary)],
            check=True, timeout=600, capture_output=True, text=True,
        )
        if not temporary.stat().st_size:
            raise ValueError("FFmpeg returned an empty artifact.")
        os.replace(temporary, target)
    finally:
        temporary.unlink(missing_ok=True)


def write_new(path, content):
    """Do not overwrite review notes or permit an archive to erase decisions."""
    if path.exists() or path.is_symlink():
        raise FileExistsError(f"Review package already exists: {path}. Use a new private directory.")
    path.write_text(content, encoding="utf-8")


def prepare(item_id, source_path, workspace, expected_sha=None):
    if item_id not in CATALOG:
        raise ValueError("Unknown archive item.")
    entry = CATALOG[item_id]
    source = separate_from_repo(source_path)
    if not source.is_file() or source.is_symlink():
        raise ValueError("Source must be a regular local file, not a symlink.")
    if source.name != entry["expectedFilename"]:
        raise ValueError(f"Expected exactly {entry['expectedFilename']}; do not silently substitute editions.")
    size, digest = sha256_file(source)
    if expected_sha is not None and digest.lower() != expected_sha.lower():
        raise ValueError("Supplied source SHA-256 does not match the file.")
    duration, video_stream, audio_stream = probe(source)
    target = workspace / item_id
    if target.exists() or target.is_symlink():
        raise FileExistsError(f"Package already exists at {target}. Review it or use another workspace.")
    target.mkdir(mode=0o700)
    try:
        # Preserve audio: never add silence or mute original recordings. No publishing.
        ffmpeg_artifact(source, target / "review-with-sound.mp4", [
            "-map", "0:v:0", "-map", "0:a:0",
            "-vf", "scale=w='trunc(iw*min(1,480/ih)/2)*2':h='trunc(ih*min(1,480/ih)/2)*2',setsar=1",
            "-c:v", "libx264", "-threads", "2", "-preset", "veryfast",
            "-crf", "28", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "128k", "-ac", "2",
            "-movflags", "+faststart",
        ])
        ffmpeg_artifact(source, target / "contact-sheet.jpg", [
            "-map", "0:v:0",
            "-vf", f"fps=1/{max(duration/12,0.1):.6f},"
                   "scale=256:-2:flags=lanczos,tile=4x3:padding=4:margin=4",
            "-frames:v", "1", "-q:v", "4", "-update", "1",
        ])
        report = {
            "item": item_id, "title": entry["title"],
            "sourcePage": entry["sourcePage"], "sourceFilename": source.name,
            "sourceBytes": size, "sourceSha256": digest,
            "durationSeconds": round(duration, 3),
            "videoCodec": video_stream.get("codec_name"),
            "audioCodec": audio_stream.get("codec_name"),
            "audioTracksPresent": True,
            "originalAudioPreservedInPreview": True,
            "status": "PENDING", "approvedForPublicDemo": False,
            "visualAndSoundtrackRightsVerified": False,
            "reviewedBy": None, "reviewedAt": None,
            "rightsEvidence": "", "reviewNotes": "",
            "checks": {
                "watchedFullPreviewWithSound": False,
                "mappedExactFileRelativeAdTimestamps": False,
                "musicJinglesVoicesAndTalentCleared": False,
                "trademarksAndEndorsementConsidered": False,
                "publicPortfolioUseAndTerritoriesCleared": False,
                "attributionAndEditsRecorded": False,
            },
            "warning": "Human review notes only. NOT a version 2 publishing manifest.",
        }
        write_new(target / "rights-review.json", json.dumps(report, indent=2) + "\n")
        title = html.escape(entry["title"])
        page = f"""<!doctype html><html lang="en"><head><meta charset="utf-8">
<title>PRIVATE REVIEW — {title}</title>
<style>body{{font:16px/1.5 system-ui;margin:3rem auto;max-width:900px;padding:1rem}}
video,img{{width:100%;max-width:850px}}strong{{color:#a30}}</style></head><body>
<h1>{title}</h1>
<p><strong>PRIVATE — UNAPPROVED. Not for public streaming.</strong></p>
<p>Listen to the full original sound while watching. The contact sheet is only a
navigation aid; it is not a full visual or copyright review.</p>
<video controls src="review-with-sound.mp4"></video>
<h2>Sampled scenes</h2><img src="contact-sheet.jpg" alt="Twelve frame samples">
<h2>Rights checklist</h2><p>Open <code>rights-review.json</code> and record exact
ad boundaries, music, voices, performers, brands, source-license evidence,
territories and attribution. Archive item-level public-domain notices do not
automatically clear embedded sound, talent rights, trademarks or every country.</p>
</body></html>"""
        write_new(target / "index.html", page)
    except BaseException:
        # Never leave a confusing partly complete review package.
        shutil.rmtree(target)
        raise
    return report, target


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--item", required=True, choices=CATALOG.keys())
    parser.add_argument("--source", required=True, type=Path,
                        help="Manually downloaded source file, OUTSIDE the repo.")
    parser.add_argument("--workspace", required=True, type=Path,
                        help="Fresh private directory OUTSIDE the repo, mode 0700.")
    parser.add_argument("--expected-sha256", help="Optional SHA-256 to enforce for this edition.")
    args = parser.parse_args(argv)
    if not shutil.which("ffmpeg") or not shutil.which("ffprobe"):
        parser.error("Install ffmpeg and ffprobe.")
    before = os.umask(0o077)
    try:
        workspace = private_workspace(args.workspace)
        report, target = prepare(args.item, args.source, workspace, args.expected_sha256)
        print(f"Private review: {target / 'index.html'}")
        print(f"Source SHA-256: {report['sourceSha256']}")
        print("Rights status: PENDING. No content has been approved or deployed.")
        return 0
    except (OSError, ValueError, subprocess.CalledProcessError, subprocess.TimeoutExpired,
            json.JSONDecodeError) as exc:
        print(f"Unable to prepare private review: {exc}", file=sys.stderr)
        return 1
    finally:
        os.umask(before)


if __name__ == "__main__":
    sys.exit(main())
