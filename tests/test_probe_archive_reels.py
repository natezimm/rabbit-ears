"""Offline unit tests for archive reel audit: no archive media needed."""
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

PATH = Path(__file__).resolve().parents[1] / "scripts/probe-archive-reels.py"
spec = importlib.util.spec_from_file_location("archive_reel_probe", PATH)
probe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)


class ArchiveReelAuditTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.video = self.root / "fixture.mp4"

    def make_fixture(self, with_audio=True):
        args = ["ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i",
                "testsrc2=size=160x120:rate=12"]
        if with_audio:
            args += ["-f", "lavfi", "-i", "sine=frequency=340:sample_rate=44100"]
        args += ["-t", "3", "-c:v", "libx264", "-threads", "1",
                 "-pix_fmt", "yuv420p"]
        if with_audio:
            args += ["-c:a", "aac"]
        args.append(str(self.video))
        subprocess.run(args, check=True)

    def test_decodes_audio_and_video_but_never_approves(self):
        self.make_fixture()
        details = probe.probe(self.video)
        self.assertGreater(details["durationSeconds"], 2)
        self.assertEqual(details["video"]["codec"], "h264")
        self.assertEqual(details["audio"][0]["codec"], "aac")
        self.assertFalse(details["approvedForPublicDemo"])
        self.assertFalse(details["soundtrackRightsReviewed"])
        self.assertEqual(details["videoAndAudioStreamDecode"], "passed")
        self.assertIsInstance(details["silenceWindowsSeconds"], list)
        self.assertIsInstance(details["sceneChangeCandidatesSeconds"], list)

    def test_silent_source_rejected(self):
        self.make_fixture(with_audio=False)
        with self.assertRaisesRegex(ValueError, "audio"):
            probe.probe(self.video)

    def test_archive_host_allowlist(self):
        self.assertTrue(probe.is_archive_url("https://archive.org/download/ClassicT1948/example.mp4"))
        self.assertTrue(probe.is_archive_url("https://ia801234.us.archive.org/item/file.mp4"))
        for url in ("https://archive.org.evil.example/file.mp4",
                    "http://archive.org/file.mp4",
                    "https://evil.example/file.mp4"):
            self.assertFalse(probe.is_archive_url(url))

    def test_file_is_deleted_after_review(self):
        self.make_fixture()
        source = {
            "id": "synthetic", "title": "fixture",
            "filename": "fixture.mp4", "sourcePage": "https://archive.org/details/example",
            "url": "https://archive.org/download/example/example.mp4",
        }
        with patch.object(probe, "fetch_exact", return_value={
            "sha256": hashlib.sha256(self.video.read_bytes()).hexdigest(),
            "downloadedBytes": self.video.stat().st_size, "reportedMime": "video/mp4",
        }):
            target = self.root / "audit"
            target.mkdir()
            # Simulate downloader writing source at target path.
            self.video.rename(target / "fixture.mp4")
            outcome = probe.inspect_one(source, target)
        self.assertFalse((target / "fixture.mp4").exists())
        self.assertFalse(outcome["approvedForPublicDemo"])
        self.assertTrue(outcome["audio"])

    def test_html_and_redirects_refused(self):
        class FakeResponse:
            headers = {"Content-Type": "text/html", "Content-Length": "12000"}
            def geturl(self):
                return "https://evil.example/reel"
            def __enter__(self):
                return self
            def __exit__(self, *args):
                pass
        with patch.object(probe, "urlopen", return_value=FakeResponse()), \
             patch.object(probe.time, "sleep", return_value=None):
            with self.assertRaisesRegex(RuntimeError, "redirect"):
                probe.fetch_exact(probe.SOURCES[0], self.root / "download.mp4")
        self.assertFalse((self.root / "download.mp4").exists())


if __name__ == "__main__":
    unittest.main()
