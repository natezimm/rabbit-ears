import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('fetch', Path(__file__).resolve().parents[1] / 'scripts/fetch-demo-sources.py')
fetch = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fetch)


class SourceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.candidate = {'identifier': 'gov.example', 'filename': 'movie.mp4'}
        self.data = b'a test movie'
        self.metadata = {'metadata': {'identifier': 'gov.example'}, 'files': [
            {'name': 'movie.mp4', 'format': '512Kb MPEG4', 'size': str(len(self.data)),
             'md5': hashlib.md5(self.data).hexdigest()}]}

    def acquire(self, data=None):
        with patch.object(fetch, 'request_json', return_value=self.metadata), \
             patch.object(fetch, 'urlopen', return_value=io.BytesIO(self.data if data is None else data)):
            return fetch.acquire(self.candidate, self.root)

    def test_download_pins_bytes_and_cache_never_uses_network(self):
        lock = self.acquire()
        self.assertEqual(lock['sha256'], hashlib.sha256(self.data).hexdigest())
        with patch.object(fetch, 'request_json', side_effect=AssertionError('network')):
            self.assertEqual(fetch.acquire(self.candidate, self.root), lock)
        self.assertEqual(json.loads((self.root / 'gov.example/metadata.json').read_text()), self.metadata)

    def test_modified_cache_rejected(self):
        self.acquire()
        (self.root / 'gov.example/movie.mp4').write_bytes(b'changed')
        with self.assertRaises(ValueError):
            fetch.acquire(self.candidate, self.root)

    def test_checksum_mismatch_never_promoted(self):
        with self.assertRaises(ValueError):
            self.acquire(b'x' * len(self.data))
        self.assertFalse((self.root / 'gov.example/movie.mp4').exists())
        self.assertFalse((self.root / 'gov.example/movie.mp4.part').exists())

    def test_truncated_download_never_promoted(self):
        with patch.object(fetch, 'retry', side_effect=lambda operation: operation()):
            with self.assertRaises(OSError):
                self.acquire(b'x')
        self.assertFalse((self.root / 'gov.example/movie.mp4').exists())

    def test_unsafe_names(self):
        for name in ['../evil', '/tmp/file', 'a/b', 'a\\b', '.hidden', 'a\nfile']:
            with self.subTest(name=name), self.assertRaises(ValueError):
                fetch.safe_name(name)

    def test_ambiguous_missing_and_bad_size(self):
        for files in [[], self.metadata['files'] * 2,
                      [dict(self.metadata['files'][0], size='0')]]:
            with self.subTest(files=files), self.assertRaises(ValueError):
                fetch.select_file({'files': files}, 'movie.mp4')

    def test_symlink_rejected(self):
        (self.root / 'gov.example').symlink_to(self.root, target_is_directory=True)
        with self.assertRaises(ValueError):
            self.acquire()

    def test_wrong_identifier_rejected(self):
        self.metadata['metadata']['identifier'] = 'other'
        with self.assertRaises(ValueError):
            self.acquire()

    def test_retry_is_bounded(self):
        with patch.object(fetch.time, 'sleep'), patch.object(fetch, 'request_json', side_effect=OSError('offline')) as request:
            with self.assertRaises(OSError):
                fetch.retry(lambda: fetch.request_json('url'))
            self.assertEqual(request.call_count, 3)

    def test_catalog_hash_rejected_even_with_valid_archive_checksum(self):
        self.candidate['sha256'] = '0' * 64
        with self.assertRaises(ValueError):
            self.acquire()
        self.assertFalse((self.root / 'gov.example/movie.mp4').exists())
