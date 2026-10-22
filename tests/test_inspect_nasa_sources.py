"""Technical NASA audit tests; use generated local clips, never external media."""
import hashlib
import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

MODULE = Path(__file__).parents[1] / "scripts/inspect-nasa-sources.py"
spec = importlib.util.spec_from_file_location("nasa_inspection", MODULE)
inspection = importlib.util.module_from_spec(spec)
spec.loader.exec_module(inspection)


class NasaInspectionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name) / "fixture.mp4"
        self.fixture = {
            "id": "CI-generated-only",
            "title": "Synthetic fixture",
            "source_page": "https://example.com/test",
            "url": "https://example.com/synthetic-test.mp4",
            "filename": "fixture.mp4",
            "limit_bytes": 1_000_000,
            "candidate_start": 0,
            "candidate_duration": 160,
        }

    def generate(self, audio):
        command = [
            "ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=160x120:rate=15",
        ]
        if audio:
            command += ["-f", "lavfi", "-i", "sine=frequency=330:sample_rate=44100"]
        command += [
            "-t", "3", "-c:v", "libx264", "-threads", "1",
            "-pix_fmt", "yuv420p",
        ]
        if audio:
            command += ["-c:a", "aac"]
        command.append(str(self.path))
        subprocess.run(command, check=True)

    def test_video_and_audio_are_probed_but_never_approved(self):
        self.generate(audio=True)
        actual = hashlib.sha256(self.path.read_bytes()).hexdigest()
        result = inspection.inspect(self.fixture, self.path, self.path.stat().st_size,
                                    actual, "video/mp4")
        self.assertEqual(result["sha256"], actual)
        self.assertEqual(result["video"]["codec"], "h264")
        self.assertEqual(result["audio"][0]["codec"], "aac")
        self.assertTrue(result["candidateClip"]["provisionalOnly"])
        self.assertFalse(result["approvedForPublicDemo"])
        self.assertFalse(result["soundtrackReviewedByListening"])
        self.assertLess(result["candidateClip"]["clipDurationSeconds"], 4)

    def test_silent_source_is_reported_without_invented_audio(self):
        self.generate(audio=False)
        result = inspection.inspect(self.fixture, self.path, self.path.stat().st_size,
                                    "placeholder-sha", "video/mp4")
        self.assertEqual(result["audio"], [])
        self.assertFalse(result["visualContentsReviewedInFull"])

    def test_audio_only_is_rejected(self):
        subprocess.run([
            "ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i",
            "sine=frequency=440:sample_rate=44100", "-t", "2", str(self.path),
        ], check=True)
        with self.assertRaisesRegex(ValueError, "video stream"):
            inspection.inspect(self.fixture, self.path, self.path.stat().st_size,
                               "placeholder-sha", "audio/mp4")

    def test_download_enforces_content_type_and_size(self):
        class FakeResponse:
            def __init__(self, mime, payload):
                self.headers = {"Content-Type": mime, "Content-Length": str(len(payload))}
                self.payload = payload
                self.reads = 0
            def __enter__(self):
                return self
            def __exit__(self, *args):
                pass
            def read(self, _):
                self.reads += 1
                return self.payload if self.reads == 1 else b""

        with patch.object(inspection, "urlopen", return_value=FakeResponse("text/html", b"<html/>")), \
             patch.object(inspection.time, "sleep", return_value=None):
            with self.assertRaisesRegex(RuntimeError, "non-video"):
                inspection.download(self.fixture, self.path)
        self.assertFalse(self.path.exists())

        with patch.object(inspection, "urlopen", return_value=FakeResponse("video/webm", b"x" * 5000)), \
             patch.object(inspection.time, "sleep", return_value=None):
            size, digest, mime = inspection.download(self.fixture, self.path)
            self.assertEqual(size, 5000)
            self.assertEqual(digest, hashlib.sha256(b"x" * 5000).hexdigest())
            self.assertEqual(mime, "video/webm")

        self.fixture["limit_bytes"] = 2000
        with patch.object(inspection, "urlopen", return_value=FakeResponse("video/webm", b"x" * 5000)), \
             patch.object(inspection.time, "sleep", return_value=None):
            with self.assertRaisesRegex(RuntimeError, "size cap"):
                inspection.download(self.fixture, self.path)
        self.assertFalse(self.path.exists())


if __name__ == "__main__":
    unittest.main()
