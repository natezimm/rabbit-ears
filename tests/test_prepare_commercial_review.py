"""Offline sound-on commercial excerpt tests with ONLY generated audiovisual fixtures."""
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

PATH = Path(__file__).resolve().parents[1] / "scripts/prepare-commercial-review.py"
spec = importlib.util.spec_from_file_location("prepare_commercial_review", PATH)
review = importlib.util.module_from_spec(spec)
spec.loader.exec_module(review)


class CommercialReviewTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.dir = Path(temp.name)
        self.source = self.dir / "ClassicT1948_512kb.mp4"
        self.workspace = self.dir / "private"

    def fixture(self, audio="tone"):
        args = ["ffmpeg", "-loglevel", "error", "-y",
                "-f", "lavfi", "-i", "testsrc2=size=160x120:rate=15",
                "-f", "lavfi", "-i",
                ("sine=frequency=410:sample_rate=44100"
                 if audio == "tone" else "anullsrc=r=44100:cl=stereo")]
        args += ["-t", "5", "-shortest", "-map", "0:v:0", "-map", "1:a:0",
                 "-c:v", "libx264", "-threads", "1", "-pix_fmt", "yuv420p",
                 "-c:a", "aac", "-b:a", "96k", str(self.source)]
        subprocess.run(args, check=True, capture_output=True)

    def add_synthetic_catalog(self):
        size = self.source.stat().st_size
        digest = hashlib.sha256(self.source.read_bytes()).hexdigest()
        source_duration, _, _ = review.archive_review.probe(self.source)
        entry = {
            "fileName": self.source.name,
            "sha256": digest, "bytes": size,
            "durationSeconds": source_duration,
            "sourcePage": "https://archive.org/details/synthetic-fixture",
            "title": "Synthetic media (not real archival footage)",
            "candidates": [
                {"id": "first-scout", "title": "Fixture first",
                 "startSeconds": 0.5, "durationSeconds": 1.4,
                 "note": "A synthetic sample"},
                {"id": "second-scout", "title": "Fixture second",
                 "startSeconds": 2.0, "durationSeconds": 1.5,
                 "note": "Another synthetic sample"},
            ],
        }
        return patch.dict(review.SOURCES, {"classic-tv": entry})

    def test_prepares_audible_private_previews_with_pending_review(self):
        self.fixture()
        with self.add_synthetic_catalog():
            data, folder = review.make_review("classic-tv", self.source, self.workspace)
            self.assertFalse(data["approvedForPublicDemo"])
            self.assertFalse(data["soundtrackReviewed"])
            self.assertFalse(data["permissionToStreamPubliclyEstablished"])
            self.assertTrue(data["sourceAudioRetained"])
            self.assertEqual(len(data["candidates"]), 2)
            self.assertTrue((folder / "index.html").exists())
            record = json.loads((folder / "rights-review.json").read_text())
            self.assertFalse(record["candidates"][0]["approvedForPublicDemo"])
            for clip in data["candidates"]:
                self.assertGreater(clip["technicalQA"]["maxAudioVolumeDb"], -55)
                self.assertEqual(clip["technicalQA"]["audioCodec"], "aac")
                self.assertAlmostEqual(
                    clip["technicalQA"]["durationSeconds"], clip["durationSeconds"], delta=1,
                )
                self.assertGreater((folder / clip["previewFile"]).stat().st_size, 0)
            with self.assertRaises(FileExistsError):
                review.make_review("classic-tv", self.source, self.workspace)

    def test_source_hash_mismatch_rejected_before_output(self):
        self.fixture()
        with self.add_synthetic_catalog():
            self.source.write_bytes(self.source.read_bytes() + b"extra")
            with self.assertRaisesRegex(ValueError, "SHA-256"):
                review.make_review("classic-tv", self.source, self.workspace)
        self.assertFalse(self.workspace.exists())

    def test_silent_audio_is_rejected_and_partial_output_removed(self):
        self.fixture(audio="silence")
        with self.add_synthetic_catalog():
            with self.assertRaisesRegex(ValueError, "silent"):
                review.make_review("classic-tv", self.source, self.workspace)
        self.assertFalse((self.workspace / "classic-tv").exists())

    def test_wrong_filename_or_repo_path_fails(self):
        self.fixture()
        with self.add_synthetic_catalog():
            wrong = self.dir / "wrong-edition.mp4"
            wrong.write_bytes(self.source.read_bytes())
            with self.assertRaisesRegex(ValueError, "Wrong source file"):
                review.make_review("classic-tv", wrong, self.workspace)
            with self.assertRaisesRegex(ValueError, "outside"):
                review.make_review("classic-tv", review.REPO_ROOT / "README.md", self.workspace)

    def test_one_command_launcher_has_valid_bash_syntax(self):
        launcher = PATH.parents[1] / "scripts/run-commercial-review.sh"
        subprocess.run(["bash", "-n", str(launcher)], check=True)
        text = launcher.read_text()
        self.assertIn("shasum -a 256", text)
        self.assertIn("prepare-commercial-review.py", text)

    def test_outside_source_window_rejected(self):
        self.fixture()
        with self.add_synthetic_catalog():
            review.SOURCES["classic-tv"]["candidates"][1]["startSeconds"] = 4.0
            review.SOURCES["classic-tv"]["candidates"][1]["durationSeconds"] = 2.0
            with self.assertRaisesRegex(ValueError, "beyond"):
                review.make_review("classic-tv", self.source, self.workspace)
        self.assertFalse((self.workspace / "classic-tv").exists())


if __name__ == "__main__":
    unittest.main()
