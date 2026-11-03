#!/usr/bin/env python3
"""Acquire exact Archive.org or HLS candidates locally; never grants publishing approval."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import time
from urllib.parse import quote, urljoin, urlparse
from urllib.request import urlopen

FORMATS = {'512Kb MPEG4', 'h.264', 'MPEG4'}
MAX_SOURCE_BYTES = 2 * 1024 ** 3
MAX_PLAYLIST_BYTES = 1024 * 1024
# Highest HLS rendition to pick unless a source sets maxBandwidth; keeps an hour under 2 GB.
DEFAULT_MAX_BANDWIDTH = 4_000_000


def safe_name(value):
    if not isinstance(value, str) or not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9._ -]*', value) or '..' in value:
        raise ValueError('Unsafe identifier or filename')
    return value


def atomic_json(path, value):
    temporary = path.with_suffix(path.suffix + '.tmp')
    temporary.write_text(json.dumps(value, indent=2) + '\n')
    temporary.replace(path)


def hashes(path):
    sha, md5 = hashlib.sha256(), hashlib.md5()
    with path.open('rb') as source:
        for block in iter(lambda: source.read(1024 * 1024), b''):
            sha.update(block)
            md5.update(block)
    return sha.hexdigest(), md5.hexdigest()


def select_file(metadata, filename):
    matches = [f for f in metadata.get('files', []) if f.get('name') == filename]
    if len(matches) != 1 or matches[0].get('format') not in FORMATS or not filename.lower().endswith('.mp4'):
        raise ValueError('Exact MP4 candidate missing, ambiguous, or unsupported')
    entry = matches[0]
    size = int(entry.get('size', 0))
    if not 0 < size <= MAX_SOURCE_BYTES:
        raise ValueError('Missing or excessive source size')
    md5 = entry.get('md5')
    if md5 is not None and not re.fullmatch('[a-fA-F0-9]{32}', md5):
        raise ValueError('Invalid archive checksum')
    return entry


def request_json(url):
    with urlopen(url, timeout=30) as response:
        data = response.read(16 * 1024 * 1024 + 1)
    if len(data) > 16 * 1024 * 1024:
        raise ValueError('Metadata response too large')
    return json.loads(data)


def retry(operation):
    for attempt in range(3):
        try:
            return operation()
        except (OSError, TimeoutError):
            if attempt == 2:
                raise
            time.sleep(attempt + 1)


def https_url(value):
    parsed = urlparse(value) if isinstance(value, str) else None
    if not parsed or parsed.scheme != 'https' or not parsed.netloc or any(c.isspace() for c in value):
        raise ValueError('HLS addresses must be HTTPS')
    return value


def request_text(url):
    with urlopen(https_url(url), timeout=30) as response:
        data = response.read(MAX_PLAYLIST_BYTES + 1)
    if len(data) > MAX_PLAYLIST_BYTES:
        raise ValueError('Playlist response too large')
    return data.decode('utf-8')


def playlist_attributes(line):
    return dict(re.findall(r'([A-Z0-9-]+)=("[^"]*"|[^,]*)', line.split(':', 1)[1]))


def choose_variant(text, base, max_bandwidth):
    """Picks the best rendition at or under the bandwidth cap, so the same source resolves the same way."""
    lines = [line.strip() for line in text.splitlines() if line.strip()]
    variants = []
    for index, line in enumerate(lines):
        if line.startswith('#EXT-X-STREAM-INF:') and index + 1 < len(lines) and not lines[index + 1].startswith('#'):
            bandwidth = playlist_attributes(line).get('BANDWIDTH', '')
            if bandwidth.isdigit():
                variants.append((int(bandwidth), https_url(urljoin(base, lines[index + 1]))))
    fitting = [v for v in variants if v[0] <= max_bandwidth]
    if not fitting:
        raise ValueError('No HLS rendition fits maxBandwidth')
    return max(fitting)


def media_segments(text, base):
    """Lists a finished, unencrypted media playlist's segments in play order."""
    lines = [line.strip() for line in text.splitlines() if line.strip()]
    if not lines or lines[0] != '#EXTM3U' or '#EXT-X-ENDLIST' not in lines:
        raise ValueError('Expected a complete video-on-demand media playlist')
    init, segments = None, []
    for line in lines:
        if line.startswith('#EXT-X-KEY:') and playlist_attributes(line).get('METHOD') != 'NONE':
            raise ValueError('Encrypted HLS is not supported')
        if line.startswith(('#EXT-X-BYTERANGE', '#EXT-X-DISCONTINUITY')):
            raise ValueError('Byte-range or discontinuous HLS is not supported')
        if line.startswith('#EXT-X-MAP:'):
            if init is not None or segments:
                raise ValueError('Only one leading HLS initialization segment is supported')
            init = https_url(urljoin(base, playlist_attributes(line).get('URI', '').strip('"')))
        elif not line.startswith('#'):
            segments.append(https_url(urljoin(base, line)))
    if not segments:
        raise ValueError('Media playlist has no segments')
    return init, segments


def resolve_hls(url, max_bandwidth):
    text = request_text(url)
    if '#EXT-X-STREAM-INF:' in text:
        bandwidth, url = choose_variant(text, url, max_bandwidth)
        text = request_text(url)
    else:
        bandwidth = None
    init, segments = media_segments(text, url)
    return {'variantUrl': url, 'bandwidth': bandwidth, 'init': init, 'segments': segments}


def download_parts(urls, temporary):
    """Concatenates segments; TS segments join directly and fMP4 segments follow their init segment."""
    try:
        count = 0
        with temporary.open('wb') as output:
            for url in urls:
                with urlopen(url, timeout=30) as response:
                    declared = response.headers.get('Content-Length') if getattr(response, 'headers', None) else None
                    received = 0
                    while block := response.read(1024 * 1024):
                        received += len(block)
                        count += len(block)
                        if count > MAX_SOURCE_BYTES:
                            raise ValueError('HLS source exceeds the size limit')
                        output.write(block)
                if declared is not None and declared.isdigit() and int(declared) != received:
                    raise OSError('Incomplete segment')
        if count == 0:
            raise ValueError('HLS source is empty')
        return count
    except BaseException:
        temporary.unlink(missing_ok=True)
        raise


def acquire(candidate, root):
    identifier = safe_name(candidate['identifier'])
    filename = safe_name(candidate['filename'])
    pinned_sha = candidate.get('sha256')
    if pinned_sha is not None and not re.fullmatch('[a-f0-9]{64}', pinned_sha):
        raise ValueError('Invalid catalog SHA-256')
    directory = root / identifier
    if directory.is_symlink():
        raise ValueError('Source directory must not be a symlink')
    directory.mkdir(parents=True, exist_ok=True)
    target, lock_path = directory / filename, directory / 'source-lock.json'
    for path in (target, lock_path, directory / 'metadata.json', directory / 'metadata.json.tmp',
                 directory / 'playlist.json', directory / 'playlist.json.tmp',
                 directory / 'source-lock.json.tmp', directory / (filename + '.part')):
        if path.is_symlink():
            raise ValueError('Source paths must not be symlinks')
    if lock_path.exists():
        lock = json.loads(lock_path.read_text())
        if lock.get('identifier') != identifier or lock.get('filename') != filename:
            raise ValueError('Pinned source differs from catalog; use a new source directory')
        if not target.is_file() or target.stat().st_size != lock['size'] or hashes(target)[0] != lock['sha256']:
            raise ValueError('Pinned source missing or changed; restore it or use a new source directory')
        if pinned_sha and lock['sha256'] != pinned_sha:
            raise ValueError('Cached source differs from catalog SHA-256')
        return lock
    if target.exists():
        raise ValueError('Unpinned source exists; use a clean source directory')
    if candidate.get('kind', 'archive') == 'hls':
        return acquire_hls(candidate, directory, target, lock_path, pinned_sha)
    if candidate.get('kind', 'archive') != 'archive':
        raise ValueError('Unknown source kind')
    metadata = retry(lambda: request_json('https://archive.org/metadata/' + quote(identifier, safe='')))
    if metadata.get('metadata', {}).get('identifier') != identifier:
        raise ValueError('Archive response identifier mismatch')
    entry = select_file(metadata, filename)
    expected = int(entry['size'])
    temporary = directory / (filename + '.part')
    url = 'https://archive.org/download/' + quote(identifier, safe='') + '/' + quote(filename, safe='')

    def download():
        try:
            count = 0
            with urlopen(url, timeout=30) as response, temporary.open('wb') as output:
                while block := response.read(1024 * 1024):
                    count += len(block)
                    if count > expected:
                        raise ValueError('Download exceeds declared size')
                    output.write(block)
            if count != expected:
                raise OSError('Incomplete download')
            sha, md5 = hashes(temporary)
            if entry.get('md5') and md5 != entry['md5'].lower():
                raise ValueError('Archive checksum mismatch')
            if pinned_sha and sha != pinned_sha:
                raise ValueError('Catalog SHA-256 mismatch')
            return sha
        except BaseException:
            temporary.unlink(missing_ok=True)
            raise

    sha = retry(download)
    lock = {'identifier': identifier, 'filename': filename, 'size': expected, 'sha256': sha,
            'archiveMd5': entry.get('md5'), 'downloadUrl': url,
            'retrievedAt': datetime.now(timezone.utc).isoformat()}
    atomic_json(directory / 'metadata.json', metadata)
    temporary.replace(target)
    atomic_json(lock_path, lock)
    return lock


def acquire_hls(candidate, directory, target, lock_path, pinned_sha):
    url = https_url(candidate.get('url'))
    max_bandwidth = candidate.get('maxBandwidth', DEFAULT_MAX_BANDWIDTH)
    if type(max_bandwidth) is not int or max_bandwidth <= 0:
        raise ValueError('maxBandwidth must be a positive integer')
    playlist = retry(lambda: resolve_hls(url, max_bandwidth))
    expected_suffix = '.mp4' if playlist['init'] else '.ts'
    if not target.name.lower().endswith(expected_suffix):
        raise ValueError(f'HLS source filename must end with {expected_suffix} for these segments')
    temporary = directory / (target.name + '.part')
    parts = ([playlist['init']] if playlist['init'] else []) + playlist['segments']

    def download():
        size = download_parts(parts, temporary)
        sha = hashes(temporary)[0]
        if pinned_sha and sha != pinned_sha:
            temporary.unlink(missing_ok=True)
            raise ValueError('Catalog SHA-256 mismatch')
        return size, sha

    size, sha = retry(download)
    lock = {'identifier': candidate['identifier'], 'filename': target.name, 'size': size, 'sha256': sha,
            'kind': 'hls', 'playlistUrl': url, 'variantUrl': playlist['variantUrl'],
            'bandwidth': playlist['bandwidth'], 'segments': len(playlist['segments']),
            'retrievedAt': datetime.now(timezone.utc).isoformat()}
    atomic_json(directory / 'playlist.json', playlist)
    temporary.replace(target)
    atomic_json(lock_path, lock)
    return lock


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('catalog', type=Path)
    parser.add_argument('destination', type=Path)
    args = parser.parse_args()
    catalog = json.loads(args.catalog.read_text())
    if catalog.get('version') != 1 or not catalog.get('sources'):
        raise ValueError('Expected version 1 source catalog')
    seen = set()
    for item in catalog['sources']:
        identifier = safe_name(item['identifier'])
        safe_name(item['filename'])
        if identifier.casefold() in seen:
            raise ValueError('Duplicate source identifier')
        seen.add(identifier.casefold())
    root = args.destination.resolve()
    root.mkdir(parents=True, exist_ok=True)
    for item in catalog['sources']:
        lock = acquire(item, root)
        print(f"{lock['identifier']}: {lock['sha256']}", flush=True)


if __name__ == '__main__':
    main()
