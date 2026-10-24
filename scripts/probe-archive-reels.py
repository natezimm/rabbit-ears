#!/usr/bin/env python3
"""Inspect two *specific* sound-bearing Internet Archive reels in ephemeral storage.

This program DOES NOT license, approve, excerpt for publication, or upload media.
It preserves only technical metadata: SHA-256, ffprobe details, scene-change
candidates and silence intervals. Audio rights require human review.
"""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import time
from urllib.error import HTTPError, URLError
from urllib.parse import urlparse
from urllib.request import Request, urlopen

SOURCES = (
    {
        "id": "classic-tv",
        "title": "Classic Television Commercials, Part I (1948–1969)",
        "sourcePage": "https://archive.org/details/ClassicT1948",
        "url": "https://archive.org/download/ClassicT1948/ClassicT1948_512kb.mp4",
        "filename": "ClassicT1948_512kb.mp4",
    },
    {
        "id": "chevrolet-1955",
        "title": "1955 Chevrolet Screen Ads",
        "sourcePage": "https://archive.org/details/1955Chev1955",
        "url": "https://archive.org/download/1955Chev1955/1955Chev1955_512kb.mp4",
        "filename": "1955Chev1955_512kb.mp4",
    },
)
MAX_BYTES = 110_000_000
SCENE_THRESHOLD = 0.42


def is_archive_url(url):
    parsed = urlparse(url)
    host = (parsed.hostname or "").lower()
    return parsed.scheme == "https" and (
        host == "archive.org" or host.endswith(".archive.org")
    )


def fetch_exact(source, destination):
    """Fetch only a pinned public archive URL; no arbitrary URL arguments."""
    if not is_archive_url(source["url"]):
        raise ValueError("Source URL must be HTTPS on archive.org.")
    last = None
    for attempt in range(2):
        try:
            request = Request(source["url"], headers={
                "User-Agent": "RabbitEarsArchivalSourceReview/1.0",
                "Accept": "video/mp4,application/octet-stream;q=0.8,*/*;q=0.2",
            })
            with urlopen(request, timeout=40) as response:
                final = response.geturl()
                if not is_archive_url(final):
                    raise ValueError("Refusing redirect away from the Internet Archive.")
                mime = response.headers.get("Content-Type", "").split(";")[0].lower()
                if mime.startswith("text/") or mime in ("application/json", "text/html"):
                    raise ValueError("Download response was not video (" + mime + ").")
                declared = response.headers.get("Content-Length")
                if declared and int(declared) > MAX_BYTES:
                    raise ValueError("Declared file size exceeds 110 MB cap.")
                size = 0
                digest = hashlib.sha256()
                with destination.open("wb") as output:
                    while True:
                        chunk = response.read(1024 * 1024)
                        if not chunk:
                            break
                        size += len(chunk)
                        if size > MAX_BYTES:
                            raise ValueError("File exceeded the 110 MB cap.")
                        output.write(chunk)
                        digest.update(chunk)
                if size < 1024:
                    raise ValueError("Downloaded file was too small to be video.")
                # A misconfigured mirror may respond as application/octet-stream
                # with an HTML error; refuse to treat HTML as media.
                if destination.open("rb").read(32).lstrip().lower().startswith((
                    b"<!doctype", b"<html", b"<?xml"
                )):
                    raise ValueError("Download contained a non-video error page.")
                return {
                    "downloadedBytes": size,
                    "sha256": digest.hexdigest(),
                    "reportedMime": mime,
                }
        except (HTTPError, URLError, OSError, ValueError, TimeoutError) as exc:
            last = str(exc)
            destination.unlink(missing_ok=True)
            if attempt == 0:
                time.sleep(2)
    raise RuntimeError("Archive download failed: " + str(last))


def process(args, timeout=180):
    return subprocess.run(args, text=True, capture_output=True, check=True,
                          timeout=timeout)


def probe(source):
    result = json.loads(process([
        "ffprobe", "-v", "error", "-show_streams", "-show_format",
        "-of", "json", str(source)
    ]).stdout)
    videos = [s for s in result.get("streams", []) if s.get("codec_type") == "video"]
    audios = [s for s in result.get("streams", []) if s.get("codec_type") == "audio"]
    if not videos or not audios:
        raise ValueError("Source requires both decodable video and audio streams.")
    duration = float(result["format"]["duration"])
    if not math.isfinite(duration) or duration <= 0 or duration > 3600:
        raise ValueError("Unexpected source duration.")
    # Full decode, but not a substitute for looking/listening to the footage.
    video_scan = process([
        "ffmpeg", "-nostdin", "-hide_banner", "-loglevel", "info",
        "-i", str(source), "-map", "0:v:0",
        "-vf", f"select='gt(scene,{SCENE_THRESHOLD})',showinfo",
        "-an", "-f", "null", "-"
    ], timeout=240)
    detected = re.findall(r"\bpts_time:\s*([0-9]+(?:\.[0-9]+)?)", video_scan.stderr)
    scene_times = sorted(set(round(float(x), 2) for x in detected))

    # Silence spans are editing *hints*, not a trustworthy ad segmentation.
    audio_scan = process([
        "ffmpeg", "-nostdin", "-hide_banner", "-loglevel", "info",
        "-i", str(source), "-map", "0:a:0", "-vn",
        "-af", "silencedetect=noise=-38dB:d=0.7",
        "-f", "null", "-"
    ], timeout=240)
    start = None
    spans = []
    for line in audio_scan.stderr.splitlines():
        st = re.search(r"silence_start:\s*([0-9]+(?:\.[0-9]+)?)", line)
        en = re.search(r"silence_end:\s*([0-9]+(?:\.[0-9]+)?)", line)
        if st:
            start = float(st.group(1))
        elif en and start is not None:
            spans.append([round(start, 2), round(float(en.group(1)), 2)])
            start = None
    return {
        "durationSeconds": round(duration, 3),
        "video": {
            "codec": videos[0].get("codec_name"),
            "width": videos[0].get("width"),
            "height": videos[0].get("height"),
            "displayAspectRatio": videos[0].get("display_aspect_ratio"),
            "frameRate": videos[0].get("avg_frame_rate"),
        },
        "audio": [{
            "codec": stream.get("codec_name"),
            "channels": stream.get("channels"),
            "sampleRate": stream.get("sample_rate"),
        } for stream in audios],
        "sceneChangeCandidatesSeconds": scene_times[:100],
        "sceneCandidatesTruncated": len(scene_times) > 100,
        "silenceWindowsSeconds": spans[:80],
        "silenceWindowsTruncated": len(spans) > 80,
        "videoAndAudioStreamDecode": "passed",
        "soundtrackRightsReviewed": False,
        "contentReviewedByWatching": False,
        "adBoundariesVerified": False,
        "approvedForPublicDemo": False,
    }


def inspect_one(source, folder):
    destination = folder / source["filename"]
    try:
        print(f"ARCHIVE_AUDIT_START={source['id']}", flush=True)
        fetched = fetch_exact(source, destination)
        media = probe(destination)
        result = {
            "id": source["id"], "title": source["title"],
            "sourcePage": source["sourcePage"],
            "downloadUrl": source["url"],
            "fileName": source["filename"],
            **fetched, **media,
        }
        print("ARCHIVE_AUDIT_RESULT=" + json.dumps(result, sort_keys=True), flush=True)
        return result
    finally:
        destination.unlink(missing_ok=True)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--download", action="store_true",
                        help="Explicitly allow downloading only the two pinned files.")
    parser.add_argument("--report", type=Path, required=True,
                        help="JSON-only technical report, no footage or audio.")
    args = parser.parse_args(argv)
    if not args.download:
        parser.error("Source acquisition requires explicit --download.")
    if not shutil_available("ffmpeg") or not shutil_available("ffprobe"):
        parser.error("ffmpeg and ffprobe are required.")
    reports, failures = [], []
    with tempfile.TemporaryDirectory(prefix="rabbit-ears-archive-audit-") as directory:
        folder = Path(directory)
        for source in SOURCES:
            try:
                reports.append(inspect_one(source, folder))
            except (RuntimeError, ValueError, subprocess.CalledProcessError,
                    subprocess.TimeoutExpired, KeyError, OSError) as exc:
                issue = {"id": source["id"], "error": str(exc)}
                failures.append(issue)
                print("ARCHIVE_AUDIT_ERROR=" + json.dumps(issue), flush=True)
    result = {
        "schemaVersion": 1,
        "purpose": "Preliminary technical inspection; NOT rights approval",
        "inspected": reports,
        "blocked": failures,
        "approvedForPublicDemo": False,
        "sourceBinaryRetained": False,
        "publicMediaGenerated": False,
    }
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(result, indent=2) + "\n")
    print("ARCHIVE_AUDIT_SUMMARY=" + json.dumps({
        "inspected": len(reports), "blocked": len(failures),
        "report": str(args.report), "approved": 0,
    }), flush=True)
    return 1 if failures else 0


def shutil_available(name):
    import shutil
    return shutil.which(name) is not None


if __name__ == "__main__":
    sys.exit(main())
