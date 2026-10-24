"""Offline smoke and safety tests using generated footage with audible synthetic audio."""
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

FILE = Path(__file__).resolve().parents[1] / "scripts/review-archive-media.py"
spec = importlib.util.spec_from_file_location("archive_media_review", FILE)
review = importlib.util.module_from_spec(spec)
spec.loader.exec_module(review)


class ArchiveReviewTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.base = Path(self.tmp.name)
        self.workspace = review.private_workspace(self.base / "private")
        self.source = self.base / "ClassicT1948_512kb.mp4"

    def synthetic(self, with_sound=True):
        args = [
            "ffmpeg", "-v", "error", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=160x120:rate=10",
        ]
        if with_sound:
            args += ["-f", "lavfi", "-i", "sine=frequency=480:sample_rate=44100"]
        args += ["-t", "4", "-c:v", "libx264", "-threads", "1", "-pix_fmt", "yuv420p"]
        if with_sound:
            args += ["-c:a", "aac"]
        args += [str(self.source)]
        subprocess.run(args, check=True)

    def test_previews_preserve_sound_and_pending_rights(self):
        self.synthetic()
        record, target = review.prepare("classic-tv", self.source, self.workspace)
        self.assertEqual(record["sourceSha256"], hashlib.sha256(self.source.read_bytes()).hexdigest())
        self.assertTrue(record["audioTracksPresent"])
        self.assertTrue(record["originalAudioPreservedInPreview"])
        self.assertEqual(record["status"], "PENDING")
        self.assertIs(record["approvedForPublicDemo"], False)
        self.assertTrue((target / "contact-sheet.jpg").stat().st_size)
        self.assertTrue((target / "review-with-sound.mp4").stat().st_size)
        result = subprocess.run([
            "ffprobe", "-v", "error", "-select_streams", "a",
            "-show_entries", "stream=codec_name", "-of", "json",
            str(target / "review-with-sound.mp4"),
        ], capture_output=True, text=True, check=True)
        self.assertEqual(json.loads(result.stdout)["streams"][0]["codec_name"], "aac")
        self.assertEqual(json.loads((target / "rights-review.json").read_text())["status"], "PENDING")
        with self.assertRaises(FileExistsError):
            review.prepare("classic-tv", self.source, self.workspace)

    def test_silent_input_fails_closed(self):
        self.synthetic(False)
        with self.assertRaisesRegex(ValueError, "no audio"):
            review.prepare("classic-tv", self.source, self.workspace)
        self.assertFalse((self.workspace / "classic-tv").exists())

    def test_wrong_file_and_checksum_refused(self):
        self.synthetic()
        with self.assertRaisesRegex(ValueError, "SHA-256"):
            review.prepare("classic-tv", self.source, self.workspace, expected_sha="0"*64)
        bad = self.base / "other-file.mp4"
        bad.write_bytes(self.source.read_bytes())
        with self.assertRaisesRegex(ValueError, "Expected exactly"):
            review.prepare("classic-tv", bad, self.workspace)

    def test_repo_boundaries_are_enforced(self):
        with self.assertRaisesRegex(ValueError, "outside"):
            review.separate_from_repo(review.ROOT / "README.md")
        with self.assertRaisesRegex(ValueError, "outside"):
            review.separate_from_repo(review.ROOT.parent)
        with self.assertRaisesRegex(ValueError, "outside"):
            review.private_workspace(review.ROOT / "private-review")
        with self.assertRaisesRegex(ValueError, "outside"):
            review.private_workspace(review.ROOT.parent)


if __name__ == "__main__":
    unittest.main()
