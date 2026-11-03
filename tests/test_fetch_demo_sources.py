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



class Response(io.BytesIO):
    """A urlopen stand-in that reports its length like an HTTP response."""

    def __init__(self, data, length=None):
        super().__init__(data)
        self.headers = {'Content-Length': str(len(data) if length is None else length)}


class HlsTests(unittest.TestCase):
    MASTER = ('#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=900000,RESOLUTION=640x360\nlow/index.m3u8\n'
              '#EXT-X-STREAM-INF:BANDWIDTH=3000000\nmid/index.m3u8\n'
              '#EXT-X-STREAM-INF:BANDWIDTH=9000000\nhigh/index.m3u8\n')
    MEDIA = '#EXTM3U\n#EXT-X-TARGETDURATION:6\n#EXTINF:6,\na.ts\n#EXTINF:6,\nb.ts\n#EXT-X-ENDLIST\n'

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.candidate = {'identifier': 'nasa-plus-1', 'filename': 'show.ts', 'kind': 'hls',
                          'url': 'https://video.example/output/1.m3u8'}
        self.parts = {'https://video.example/output/mid/a.ts': b'first ',
                      'https://video.example/output/mid/b.ts': b'second'}

    def playlists(self, url):
        return {'https://video.example/output/1.m3u8': self.MASTER,
                'https://video.example/output/mid/index.m3u8': self.MEDIA}[url]

    def acquire(self, parts=None):
        parts = self.parts if parts is None else parts
        with patch.object(fetch, 'request_text', side_effect=self.playlists), \
             patch.object(fetch, 'urlopen', side_effect=lambda url, timeout: Response(parts[url])):
            return fetch.acquire(self.candidate, self.root)

    def test_picks_best_rendition_under_cap_and_joins_segments_in_order(self):
        lock = self.acquire()
        data = (self.root / 'nasa-plus-1/show.ts').read_bytes()
        self.assertEqual(data, b'first second')
        self.assertEqual(lock['sha256'], hashlib.sha256(data).hexdigest())
        self.assertEqual((lock['bandwidth'], lock['segments']), (3000000, 2))
        with patch.object(fetch, 'request_text', side_effect=AssertionError('network')):
            self.assertEqual(fetch.acquire(self.candidate, self.root), lock)

    def test_pinned_hash_mismatch_never_promoted(self):
        self.candidate['sha256'] = '0' * 64
        with self.assertRaises(ValueError):
            self.acquire()
        self.assertFalse((self.root / 'nasa-plus-1/show.ts').exists())
        self.assertFalse((self.root / 'nasa-plus-1/show.ts.part').exists())

    def test_short_segment_never_promoted(self):
        with patch.object(fetch, 'retry', side_effect=lambda operation: operation()), \
             patch.object(fetch, 'request_text', side_effect=self.playlists), \
             patch.object(fetch, 'urlopen', side_effect=lambda url, timeout: Response(b'x', length=5)):
            with self.assertRaises(OSError):
                fetch.acquire(self.candidate, self.root)
        self.assertFalse((self.root / 'nasa-plus-1/show.ts').exists())

    def test_fmp4_needs_mp4_name(self):
        media = '#EXTM3U\n#EXT-X-MAP:URI="init.mp4"\n#EXTINF:6,\na.m4s\n#EXT-X-ENDLIST\n'
        init, segments = fetch.media_segments(media, 'https://video.example/v/index.m3u8')
        self.assertEqual((init, segments), ('https://video.example/v/init.mp4', ['https://video.example/v/a.m4s']))
        self.MEDIA = media
        with self.assertRaises(ValueError):
            self.acquire()

    def test_unsupported_playlists_rejected(self):
        for media in ['#EXTM3U\n#EXTINF:6,\na.ts\n',
                      '#EXTM3U\n#EXT-X-KEY:METHOD=AES-128,URI="k"\n#EXTINF:6,\na.ts\n#EXT-X-ENDLIST\n',
                      '#EXTM3U\n#EXTINF:6,\nhttp://video.example/a.ts\n#EXT-X-ENDLIST\n',
                      '#EXTM3U\n#EXT-X-ENDLIST\n']:
            with self.subTest(media=media), self.assertRaises(ValueError):
                fetch.media_segments(media, 'https://video.example/index.m3u8')

    def test_no_rendition_under_cap(self):
        with self.assertRaises(ValueError):
            fetch.choose_variant(self.MASTER, 'https://video.example/1.m3u8', 500000)


class PeriodCatalogTests(unittest.TestCase):
    """The period catalog, its recorded locks and the manifest template must name the same files."""

    def test_catalog_locks_and_manifest_agree(self):
        demo = Path(__file__).resolve().parents[1] / 'demo'
        catalog = json.loads((demo / 'period-sources.json').read_text())['sources']
        locks = {s['identifier']: s for s in json.loads((demo / 'period-source-locks.json').read_text())['sources']}
        manifest = json.loads((demo / 'period-manifest.example.json').read_text())['videos']
        self.assertEqual(set(locks), {s['identifier'] for s in catalog})
        for source in catalog:
            lock = locks[source['identifier']]
            self.assertEqual((lock['filename'], lock['sha256']), (source['filename'], source['sha256']))
        self.assertEqual(sorted(v['source'] for v in manifest),
                         sorted(f"originals/{s['identifier']}/{s['filename']}" for s in catalog))
        self.assertTrue(all(v['approvedForPublicDemo'] is False for v in manifest))


class SpaceCatalogTests(unittest.TestCase):
    """The NASA catalog and its manifest template must name the same files and stay unapproved."""

    def test_catalog_and_manifest_agree(self):
        demo = Path(__file__).resolve().parents[1] / 'demo'
        catalog = json.loads((demo / 'space-sources.json').read_text())['sources']
        manifest = json.loads((demo / 'space-manifest.example.json').read_text())['videos']
        for source in catalog:
            fetch.safe_name(source['identifier'])
            fetch.safe_name(source['filename'])
            fetch.https_url(source['url'])
            self.assertEqual(source['kind'], 'hls')
        self.assertEqual(sorted(v['source'] for v in manifest),
                         sorted(f"originals/{s['identifier']}/{s['filename']}" for s in catalog))
        self.assertEqual({v['year'] for v in manifest}, {s['year'] for s in catalog})
        self.assertTrue(all(1980 <= v['year'] <= 1999 for v in manifest))
        self.assertTrue(all(v['approvedForPublicDemo'] is False for v in manifest))
