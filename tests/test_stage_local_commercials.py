"""End-to-end local staging tests: synthetic commercials only, no archival media."""
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

PATH = Path(__file__).resolve().parents[1] / "scripts/stage-local-commercials.py"
spec = importlib.util.spec_from_file_location("stage_local_commercials", PATH)
stage = importlib.util.module_from_spec(spec)
spec.loader.exec_module(stage)


class LocalCommercialPlaybackTests(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.base = Path(tmp.name)
        self.originals = self.base / "originals"
        self.reviewed = self.base / "reviewed"
        self.output = self.base / "local-preview"
        self.originals.mkdir()
        self.reviewed.mkdir()

    def fixture(self):
        catalog = {}
        for idx, (item, source_name, freq) in enumerate([
            ("classic-tv", "ClassicT1948_512kb.mp4", 420),
            ("chevrolet-1955", "1955Chev1955_512kb.mp4", 560),
        ]):
            source = self.originals / source_name
            subprocess.run([
                "ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
                "-f", "lavfi", "-i", "testsrc2=size=160x120:rate=12",
                "-f", "lavfi", "-i", f"sine=frequency={freq}:sample_rate=44100",
                "-t", "5", "-c:v", "libx264", "-threads", "1",
                "-pix_fmt", "yuv420p", "-c:a", "aac", str(source)
            ], check=True, capture_output=True)
            dur, _, _ = stage.preview.archive_review.probe(source)
            candidates = [
                {"id": f"{item}-one", "title": "First synthetic ad — scouting window",
                 "startSeconds": 0.5, "durationSeconds": 1.5},
                {"id": f"{item}-two", "title": "Second synthetic ad — scouting window",
                 "startSeconds": 2.5, "durationSeconds": 1.4},
            ]
            catalog[item] = {
                "fileName": source_name,
                "sha256": hashlib.sha256(source.read_bytes()).hexdigest(),
                "bytes": source.stat().st_size,
                "durationSeconds": dur,
                "sourcePage": "https://archive.org/details/test-source",
                "title": f"Synthetic item {idx}",
                "candidates": candidates,
            }
            folder = self.reviewed / item
            folder.mkdir()
            reviewed = []
            for candidate in candidates:
                file_name = f"{candidate['id']}.mp4"
                stage.preview.render_candidate(source, folder / file_name, candidate)
                reviewed.append({
                    **candidate, "previewFile": file_name,
                    "soundtrackAndRightsReviewed": False,
                    "approvedForPublicDemo": False,
                })
            (folder / "rights-review.json").write_text(json.dumps({
                "sourceItem": item,
                "sourceSha256": catalog[item]["sha256"],
                "sourceFileName": source_name,
                "sourceAudioRetained": True,
                "approvedForPublicDemo": False,
                "candidates": reviewed,
            }))
        return catalog

    def test_stages_four_audible_candidates_with_unapproved_v1_metadata(self):
        catalog = self.fixture()
        with patch.dict(stage.preview.SOURCES, catalog, clear=True):
            result = stage.stage(self.originals, self.reviewed, self.output)
        self.assertEqual(result["version"], 1)
        self.assertEqual(len(result["videos"]), 4)
        self.assertTrue((self.output / "LOCAL_ONLY_DO_NOT_DEPLOY.txt").exists())
        public = json.loads((self.output / ".demo-media.json").read_text())
        self.assertEqual(len(public["videos"]), 4)
        for entry in public["videos"]:
            self.assertTrue(entry["path"].startswith("Retro Commercials/"))
            self.assertFalse(entry["approvedForPublicDemo"])
            self.assertIn("UNREVIEWED", entry["license"])
            self.assertNotIn("sourceSha256", entry)
            self.assertNotIn("rightsEvidence", entry)
            path = self.output / entry["path"]
            duration, video, audio = stage.preview.archive_review.probe(path)
            self.assertGreater(duration, 1)
            self.assertEqual(video["codec_name"], "h264")
            self.assertEqual(audio["codec_name"], "aac")
            self.assertGreater(stage.preview.max_audio_volume(path), -55.0)
        with self.assertRaisesRegex(ValueError, "NEW private"):
            stage.stage(self.originals, self.reviewed, self.output)

    def test_rejects_modified_original_without_leaving_staged_media(self):
        catalog = self.fixture()
        source = self.originals / catalog["classic-tv"]["fileName"]
        with source.open("ab") as stream:
            stream.write(b"forged")
        with patch.dict(stage.preview.SOURCES, catalog, clear=True):
            with self.assertRaisesRegex(ValueError, "SHA-256"):
                stage.stage(self.originals, self.reviewed, self.output)
        self.assertFalse(self.output.exists())

    def test_rejects_review_with_unexpected_times(self):
        catalog = self.fixture()
        ledger_path = self.reviewed / "chevrolet-1955/rights-review.json"
        ledger = json.loads(ledger_path.read_text())
        ledger["candidates"][0]["startSeconds"] = 999.0
        ledger_path.write_text(json.dumps(ledger))
        with patch.dict(stage.preview.SOURCES, catalog, clear=True):
            with self.assertRaisesRegex(ValueError, "pinned"):
                stage.stage(self.originals, self.reviewed, self.output)
        self.assertFalse(self.output.exists())

    def test_launcher_and_compose_are_isolated_from_public_demo(self):
        repo = PATH.parents[1]
        for name in ("scripts/start-local-commercials.sh", "scripts/run-commercial-review.sh"):
            subprocess.run(["bash", "-n", str(repo / name)], check=True)
        script = (repo / "scripts/start-local-commercials.sh").read_text()
        compose = (repo / "demo/compose.local-commercials.yaml").read_text()
        self.assertIn('uname -s', script)
        self.assertIn("127.0.0.1:8760:8080", compose)
        self.assertIn('Box__FirstChannelNumber: "6"', compose)
        self.assertIn('Box__Demo__RequireApprovedMedia: "false"', compose)
        self.assertIn("RABBIT_EARS_LOCAL_UID", compose)
        self.assertIn("commercial-review", script)
        # Production rights gate must remain fail-closed.
        public_compose = (repo / "demo/compose.media.yaml").read_text()
        self.assertIn("Box__Demo__RequireApprovedMedia: 'true'", public_compose)


if __name__ == "__main__":
    unittest.main()
