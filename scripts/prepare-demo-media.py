#!/usr/bin/env python3
"""Prepare cleared, local source videos for the public demo. Never downloads media."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import shutil
import subprocess
import sys
from urllib.parse import urlparse


def segment(value):
    if (not isinstance(value, str) or not value.strip() or value != value.strip()
            or value.startswith('.') or any(c in value for c in '/\\')
            or any(ord(c) < 32 for c in value)):
        raise ValueError('Channel and title must be plain, non-hidden file names without slashes.')
    return value


def validate(manifest, output):
    document = json.loads(manifest.read_text())
    if document.get('version') != 1 or not isinstance(document.get('videos'), list) or not document['videos']:
        raise ValueError('Manifest must have version 1 and a nonempty videos array.')
    videos, destinations = [], set()
    for item in document['videos']:
        channel, title = segment(item['channel']), segment(item['title'])
        relative = f'{channel}/{title}.mp4'
        if relative.casefold() in destinations:
            raise ValueError(f'Duplicate destination: {relative}')
        destinations.add(relative.casefold())
        for field in ('source', 'creator', 'license', 'sourceUrl', 'rightsEvidence'):
            if not isinstance(item.get(field), str) or not item[field].strip():
                raise ValueError(f'{relative}: {field} is required.')
        if urlparse(item['sourceUrl']).scheme not in ('http', 'https'):
            raise ValueError(f'{relative}: sourceUrl must be an HTTP(S) source page.')
        source = (manifest.parent / item['source']).resolve()
        if not source.is_file():
            raise ValueError(f'Source file not found: {source}')
        # A rerun must never replace its own source or consume previous outputs.
        if source.is_relative_to(output):
            raise ValueError('Keep original sources outside the prepared library.')
        target = output / relative
        if not target.resolve().is_relative_to(output):
            raise ValueError(f'Destination escapes output directory: {relative}')
        videos.append((item, source, relative))
    return videos


def fingerprint(source):
    digest = hashlib.sha256()
    with source.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()


def duration(path):
    result = subprocess.run(['ffprobe', '-v', 'error', '-show_entries', 'format=duration',
                             '-of', 'default=nw=1:nk=1', str(path)], check=True, capture_output=True, text=True)
    seconds = float(result.stdout.strip())
    if not math.isfinite(seconds) or seconds <= 0:
        raise ValueError(f'Invalid duration: {path}')
    return seconds


def atomic_json(path, value):
    temporary = path.with_suffix(path.suffix + '.tmp')
    temporary.write_text(json.dumps(value, indent=2) + '\n')
    os.replace(temporary, path)


def prepare(manifest, output):
    videos = validate(manifest, output)
    for command in ('ffmpeg', 'ffprobe'):
        if not shutil.which(command):
            raise ValueError(f'{command} is required.')
    output.mkdir(parents=True, exist_ok=True)
    metadata = output / '.demo-media.json'
    old = json.loads(metadata.read_text()) if metadata.exists() else {'videos': []}
    previous = {video['path']: video for video in old['videos']}
    credits = []
    # Detect stale videos rather than silently publishing files absent from the manifest.
    expected = {relative for _, _, relative in videos}
    existing = {str(p.relative_to(output)) for p in output.rglob('*') if p.is_file() and not p.name.startswith('.')}
    if existing - expected:
        raise ValueError('Use a clean output directory or remove stale files first: ' + ', '.join(sorted(existing - expected)))
    for item, source, relative in videos:
        target = output / relative
        digest = fingerprint(source)
        prior = previous.get(relative, {})
        if target.exists() and prior.get('sourceSha256') == digest and prior.get('encoding') == 1:
            seconds = duration(target)
            print(f'Already prepared: {relative}')
        else:
            target.parent.mkdir(parents=True, exist_ok=True)
            temporary = target.with_suffix('.mp4.partial')
            try:
                subprocess.run([
                    'ffmpeg', '-hide_banner', '-loglevel', 'error', '-nostdin', '-y', '-i', str(source),
                    '-map', '0:v:0', '-map', '0:a:0?',
                    '-vf', "yadif=deint=interlaced,scale=-2:'min(480,ih)',format=yuv420p",
                    '-c:v', 'libx264', '-threads', '2', '-preset', 'medium', '-crf', '25',
                    '-maxrate', '1000k', '-bufsize', '2000k', '-c:a', 'aac', '-b:a', '96k', '-ac', '2',
                    '-map_metadata', '-1', '-movflags', '+faststart', '-f', 'mp4', str(temporary),
                ], check=True)
                seconds = duration(temporary)
                os.replace(temporary, target)
            finally:
                temporary.unlink(missing_ok=True)
            print(f'Prepared: {relative}')
        credits.append({
            'path': relative, 'title': item['title'], 'creator': item['creator'],
            'license': item['license'], 'sourceUrl': item['sourceUrl'], 'duration': seconds,
            'sourceSha256': digest, 'encoding': 1,
        })
    atomic_json(metadata, {'version': 1, 'videos': credits})
    print(f'{len(credits)} videos ready in {output}. Deploy this directory only after preparation succeeds.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('manifest', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--validate-only', action='store_true')
    args = parser.parse_args()
    try:
        if args.validate_only:
            validate(args.manifest.resolve(), args.output.resolve())
            print('Manifest valid; no files written.')
        else:
            prepare(args.manifest.resolve(), args.output.resolve())
    except (ValueError, KeyError, TypeError, OSError, subprocess.CalledProcessError) as error:
        print(f'Preparation failed: {error}', file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
