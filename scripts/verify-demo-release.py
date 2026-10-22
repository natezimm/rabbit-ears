#!/usr/bin/env python3
"""Non-destructive public-demo release checks. Requires only the Python standard library.

Synthetic fallback:
  python3 scripts/verify-demo-release.py --base-url http://127.0.0.1:8620
Mounted approved library:
  python3 scripts/verify-demo-release.py --base-url http://127.0.0.1:8620 --media-root /path/to/staged/library
"""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import sys
from urllib.error import HTTPError, URLError
from urllib.parse import urljoin
from urllib.request import Request, urlopen


def require(condition, message):
    if not condition:
        raise ValueError(message)


def valid_source_url(url):
    from urllib.parse import urlparse
    try:
        value = urlparse(url)
    except (TypeError, ValueError):
        return False
    return value.scheme in ('http', 'https') and bool(value.netloc)


def stage_inventory(root):
    """Verify that one immutable staged mount exactly matches approved v2 metadata."""
    require(root.is_dir() and not root.is_symlink(), 'Staged media directory is missing or symlinked.')
    metadata = root / '.demo-media.json'
    require(metadata.is_file() and not metadata.is_symlink(), 'Prepared metadata is missing.')
    data = json.loads(metadata.read_text())
    require(isinstance(data, dict) and data.get('version') == 2
            and isinstance(data.get('videos'), list) and data['videos'],
            'A public media release requires nonempty approved v2 prepared metadata.')
    recorded = set()
    channels = Counter()
    credits = set()
    for clip in data['videos']:
        require(isinstance(clip, dict), 'Invalid prepared media entry.')
        path = clip.get('path')
        require(isinstance(path, str) and not path.startswith('/')
                and len(path.split('/')) == 2
                and path.endswith('.mp4')
                and all(part not in ('', '.', '..') and not part.startswith('.') for part in path.split('/')),
                'Unsafe prepared media path.')
        require(path.casefold() not in recorded, 'Duplicate prepared media file.')
        recorded.add(path.casefold())
        file = root / path
        require(file.is_file() and not file.is_symlink() and file.resolve().is_relative_to(root.resolve()),
                'A listed prepared media file is missing or symlinked.')
        require(file.stat().st_size > 0, 'Empty prepared video.')
        require(clip.get('approvedForPublicDemo') is True, 'Unapproved media in prepared library.')
        for field in ('title', 'creator', 'license', 'sourceUrl', 'modifications'):
            require(isinstance(clip.get(field), str) and clip[field].strip(),
                    f'Prepared video is missing public {field}.')
        require(valid_source_url(clip['sourceUrl']), 'Invalid source link in prepared media.')
        if clip.get('licenseUrl') is not None:
            require(valid_source_url(clip['licenseUrl']), 'Invalid license link.')
        duration = clip.get('duration')
        require(type(duration) in (int, float) and 0 < duration < float('inf'),
                'Invalid prepared duration.')
        # Private evidence and review records must never be in the public asset manifest.
        for key in ('rightsEvidence', 'reviewedBy', 'reviewedAt', 'source'):
            require(key not in clip, 'Private rights details have leaked into prepared metadata.')
        channel = path.split('/')[0]
        channels[channel] += 1
        key = (channel, clip['title'])
        require(key not in credits, 'Duplicate channel and title in prepared metadata.')
        credits.add(key)
    actual = set()
    for file in root.rglob('*'):
        require(not file.is_symlink(), 'No symlinks permitted in staged media.')
        if file.is_file() and file != metadata:
            relative = file.relative_to(root).as_posix()
            require(relative.endswith('.mp4'), 'Unexpected non-video file in media staging.')
            actual.add(relative.casefold())
    require(actual == recorded, 'Stale or unapproved files found in the staged release.')
    return data, channels


def read_response(base, path, headers=None):
    request = Request(urljoin(base.rstrip('/') + '/', path.lstrip('/')), headers=headers or {})
    try:
        with urlopen(request, timeout=10) as response:
            return response.status, dict(response.headers), response.read()
    except HTTPError as error:
        try:
            return error.code, dict(error.headers), error.read()
        finally:
            error.close()


def get_json(base, path, expected=200):
    status, _, body = read_response(base, path)
    require(status == expected, f'{path}: HTTP {status}, expected {expected}.')
    return json.loads(body)


def verify_release(base, media_root=None, expect_channels=None, expect_clips=None):
    health = get_json(base, '/api/health')
    require(health.get('status') == 'on the air', 'Health endpoint is not ready.')
    info = get_json(base, '/api/demo')
    require(info.get('enabled') is True, 'The target is not running in demo mode.')
    library = get_json(base, '/api/library')
    credits = get_json(base, '/api/demo/credits')
    require(isinstance(credits, list), 'Expected an array of credits.')
    require(isinstance(library.get('channels'), list) and isinstance(library.get('episodes'), int),
            'Library endpoint returned unexpected data.')
    require(sum(ch['episodes'] for ch in library['channels']) == library['episodes'],
            'Reported channels and episode counts disagree.')
    require(library['episodes'] > 0, 'No playable demo clips available.')
    if expect_channels is not None:
        require(len(library['channels']) == expect_channels,
                f'Expected {expect_channels} channels, got {len(library["channels"])}.')
    if expect_clips is not None:
        require(library['episodes'] == expect_clips,
                f'Expected {expect_clips} clips, got {library["episodes"]}.')
    require(read_response(base, '/api/admin/channels')[0] == 404,
            'Public demo unexpectedly exposes channel administration.')
    require(read_response(base, '/api/rescan', {'Accept': 'application/json'})[0] in (404, 405),
            'Public demo unexpectedly exposes GET rescan.')
    require(read_response(base, '/media/0000000000000000')[0] == 404,
            'An unregistered media identifier unexpectedly resolves.')

    if media_root is not None:
        prepared, channel_counts = stage_inventory(media_root)
        require(library['episodes'] == len(prepared['videos']),
                'Published episode count does not match staged approved media.')
        require({ch['name']: ch['episodes'] for ch in library['channels']} == dict(channel_counts),
                'Published channel lineup does not match approved staged media.')
        require(len(credits) == len(prepared['videos']),
                'Credits do not cover every approved clip.')
        actual_credits = {(c['channel'], c['title'], c['creator'], c['license'], c['sourceUrl'])
                          for c in credits}
        expected_credits = {(path['path'].split('/')[0], path['title'], path['creator'],
                             path['license'], path['sourceUrl']) for path in prepared['videos']}
        require(actual_credits == expected_credits, 'Published attribution differs from approved metadata.')
        require(all(all(k not in clip for k in ('rightsEvidence', 'reviewedBy', 'reviewedAt', 'source'))
                    for clip in credits), 'Private rights metadata leaked into the credits API.')
        first = prepared['videos'][0]['path']
        episode_id = hashlib.sha256(first.encode()).hexdigest()[:16]
        status, headers, body = read_response(base, '/media/' + episode_id, {'Range': 'bytes=0-63'})
        require(status == 206 and len(body) == 64,
                'Approved video does not support HTTP byte-range playback.')
        require(headers.get('Content-Range', '').startswith('bytes 0-63/'),
                'Invalid HTTP Content-Range returned from video endpoint.')
    else:
        # No prepared library was supplied. The ordinary synthetic demo does not carry credits.
        require(not credits, 'Unexpected prepared media credits; supply --media-root to verify the mounted library.')
    return len(library['channels']), library['episodes'], len(credits)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base-url', required=True, help='Localhost or public HTTPS URL of Rabbit Ears.')
    parser.add_argument('--media-root', type=Path,
                        help='The staged approved media directory; checks API, attribution and Range against it.')
    parser.add_argument('--expect-channels', type=int)
    parser.add_argument('--expect-clips', type=int)
    args = parser.parse_args()
    try:
        channels, clips, credits = verify_release(args.base_url, args.media_root,
                                                   args.expect_channels, args.expect_clips)
    except (ValueError, OSError, URLError, json.JSONDecodeError) as error:
        print(f'Demo release verification failed: {error}', file=sys.stderr)
        return 1
    print(f'Demo verified: {channels} channels, {clips} playable clips, {credits} credited clips; '
          'public admin blocked; approved media range-seekable when mounted.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
