#!/usr/bin/env python3
"""Inspect two specific NASA-origin videos in an ephemeral directory.

Explicit opt-in only: --download. This utility DOES NOT grant media rights,
approve clips, modify Rabbit Ears' private manifest, or stage anything for public
serving. No video frames or audio leave the temporary runner as artifacts.
"""
import argparse
import hashlib
import json
import math
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

SOURCES = (
    {
        "id": "S1-STS1",
        "title": "STS-1 Columbia launch (1981 footage, NASA Kennedy 2011 release)",
        "source_page": "https://commons.wikimedia.org/wiki/File:STS-1_Launch_6qMPLydUbuM.webm",
        "url": "https://upload.wikimedia.org/wikipedia/commons/0/03/STS-1_Launch_6qMPLydUbuM.webm",
        "filename": "STS-1_Launch_6qMPLydUbuM.webm",
        "limit_bytes": 25_000_000,
        "candidate_start": 0,
        "candidate_duration": 160,
    },
    {
        "id": "S2-HST-1990",
        "title": "STS-31 Hubble deployment (NASA SVS 10321 item 23; April 25, 1990)",
        "source_page": "https://svs.gsfc.nasa.gov/10321/",
        "url": "https://svs.gsfc.nasa.gov/vis/a010000/a010300/a010321/23-resource-hst_deployment-resourcereelreference_MPEG-1.webmhd.webm",
        "filename": "23-resource-hst_deployment-resourcereelreference_MPEG-1.webmhd.webm",
        "limit_bytes": 25_000_000,
        "candidate_start": 0,
        "candidate_duration": 160,
    },
)


def run(command):
    result = subprocess.run(command, capture_output=True, text=True, check=True, timeout=120)
    return result.stdout


def download(source, destination):
    """Allowlisted static NASA/Commons URLs only. Refuse HTML and oversized files."""
    last_error = None
    for attempt in range(3):
        digest = hashlib.sha256()
        amount = 0
        try:
            request = Request(source["url"], headers={
                "User-Agent": "RabbitEarsSourceAudit/1.0 (portfolio provenance check)",
                "Accept": "video/webm,application/octet-stream;q=0.8,*/*;q=0.1",
            })
            with urlopen(request, timeout=45) as response:
                mime = response.headers.get("Content-Type", "").split(";")[0].strip().lower()
                if mime in ("text/html", "application/json", "text/plain"):
                    raise ValueError("Source returned a non-video response: " + mime)
                declared = response.headers.get("Content-Length")
                if declared and int(declared) > source["limit_bytes"]:
                    raise ValueError("Source file exceeds safe inspection size cap.")
                with destination.open("wb") as out:
                    while True:
                        block = response.read(1024 * 1024)
                        if not block:
                            break
                        amount += len(block)
                        if amount > source["limit_bytes"]:
                            raise ValueError("Downloaded source exceeded inspection size cap.")
                        digest.update(block)
                        out.write(block)
            if amount < 1000:
                raise ValueError("Downloaded source was unexpectedly small.")
            return amount, digest.hexdigest(), mime
        except (HTTPError, URLError, OSError, ValueError, TimeoutError) as error:
            last_error = error
            destination.unlink(missing_ok=True)
            if attempt < 2:
                time.sleep(2 * (attempt + 1))
    raise RuntimeError(f"{source['id']}: source download unavailable: {last_error}")


def inspect(source, path, size, sha, mime):
    info = json.loads(run([
        "ffprobe", "-v", "error", "-show_format", "-show_streams", "-of", "json", str(path)
    ]))
    video = [s for s in info.get("streams", []) if s.get("codec_type") == "video"]
    audio = [s for s in info.get("streams", []) if s.get("codec_type") == "audio"]
    if not video:
        raise ValueError("Expected a decodable video stream.")
    seconds = float(info["format"]["duration"])
    if not math.isfinite(seconds) or seconds < 2:
        raise ValueError("Invalid media duration.")
    video0 = video[0]
    if video0.get("width", 0) < 2 or video0.get("height", 0) < 2:
        raise ValueError("Invalid media dimensions.")
    # Decode at two real positions. This tests video decodability but is NOT
    # a visual editorial or music-rights inspection.
    for position in (0.0, max(0.0, min(seconds - 2, seconds * 0.55))):
        subprocess.run([
            "ffmpeg", "-hide_banner", "-loglevel", "error", "-nostdin",
            "-ss", str(position), "-i", str(path), "-t", "2",
            "-map", "0:v:0", "-an", "-f", "null", "-"
        ], check=True, timeout=90, capture_output=True)
    chosen_seconds = min(source["candidate_duration"], seconds - source["candidate_start"])
    return {
        "id": source["id"],
        "title": source["title"],
        "sourcePage": source["source_page"],
        "exactDownloadUrl": source["url"],
        "filename": source["filename"],
        "downloadedBytes": size,
        "sha256": sha,
        "responseMime": mime,
        "durationSeconds": round(seconds, 3),
        "video": {
            "codec": video0.get("codec_name"),
            "width": video0.get("width"),
            "height": video0.get("height"),
            "sampleAspectRatio": video0.get("sample_aspect_ratio"),
            "displayAspectRatio": video0.get("display_aspect_ratio"),
            "frameRate": video0.get("avg_frame_rate"),
        },
        "audio": [{
            "codec": s.get("codec_name"),
            "channels": s.get("channels"),
            "sampleRate": s.get("sample_rate"),
        } for s in audio],
        "candidateClip": {
            "clipStartSeconds": source["candidate_start"],
            "clipDurationSeconds": round(chosen_seconds, 3),
            "framing": "preserve",
            "provisionalOnly": True,
        },
        "decodeAtTwoPositions": "passed",
        "soundtrackReviewedByListening": False,
        "visualContentsReviewedInFull": False,
        "territorialRightsConfirmed": False,
        "approvedForPublicDemo": False,
        "next": "Human must watch/listen to proposed excerpt, confirm the exact edition and every right, then sign private rights ledger.",
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--download", action="store_true", help="Explicitly fetch the two allowlisted sources.")
    parser.add_argument("--report", type=Path, required=True, help="Metadata-only report path, never source binaries.")
    args = parser.parse_args()
    if not args.download:
        parser.error("Refusing implicit downloads. Supply --download.")
    for executable in ("ffmpeg", "ffprobe"):
        if not shutil.which(executable):
            parser.error(executable + " is required.")
    results = []
    failures = []
    with tempfile.TemporaryDirectory(prefix="rabbit-ears-nasa-review-") as directory:
        scratch = Path(directory)
        for source in SOURCES:
            filename = scratch / source["filename"]
            try:
                print("Inspecting " + source["id"] + " (ephemeral download, no media artifact)", flush=True)
                size, digest, mime = download(source, filename)
                result = inspect(source, filename, size, digest, mime)
                results.append(result)
                print("NASA_AUDIT_RESULT=" + json.dumps(result, sort_keys=True), flush=True)
            except (RuntimeError, ValueError, KeyError, subprocess.CalledProcessError,
                    subprocess.TimeoutExpired, OSError) as error:
                failures.append({"id": source["id"], "error": str(error)})
                print("NASA_AUDIT_ERROR=" + json.dumps(failures[-1]), flush=True)
            finally:
                filename.unlink(missing_ok=True)
    report = {
        "schemaVersion": 1,
        "purpose": "Temporary file-level provenance / codec inspection, not copyright approval",
        "results": results,
        "failures": failures,
        "approvedForPublicDemo": False,
        "sourcesPersisted": False,
        "noRightsDecision": True,
    }
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + "\n")
    print("NASA_AUDIT_SUMMARY=" + json.dumps({
        "inspected": len(results), "failed": len(failures), "unapproved": len(results),
        "report": str(args.report),
    }), flush=True)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
