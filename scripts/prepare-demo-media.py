#!/usr/bin/env python3
"""Prepare approved, local source videos for the public demo. Never downloads media."""
import argparse
from datetime import date
import hashlib
import json
import math
import os
from pathlib import Path
import shutil
import subprocess
import sys
from urllib.parse import urlparse


# Changes to encoding filters, codec parameters or QA expectations must bump this.
ENCODING_VERSION = 2
VIDEO_HEIGHT = 480
FRAMINGS = ('preserve', 'letterbox-4:3')


def segment(value):
    if (not isinstance(value, str) or not value.strip() or value != value.strip()
            or value.startswith('.') or any(c in value for c in '/\\')
            or any(ord(c) < 32 for c in value)):
        raise ValueError('Channel and title must be plain, non-hidden file names without slashes.')
    return value


def http_url(value):
    parsed = urlparse(value) if isinstance(value, str) else None
    return bool(parsed and parsed.scheme in ('http', 'https') and parsed.netloc
                and not any(c.isspace() for c in value))


def approved(item):
    if item.get('approvedForPublicDemo') is not True:
        raise ValueError('Every version 2 clip requires explicit approvedForPublicDemo: true.')
    for field in ('reviewedBy', 'reviewedAt'):
        if not isinstance(item.get(field), str) or not item[field].strip():
            raise ValueError(f'{field} is required for version 2 approval.')
    try:
        date.fromisoformat(item['reviewedAt'])
    except ValueError as error:
        raise ValueError('reviewedAt must be an ISO date (YYYY-MM-DD).') from error


def nonnegative_seconds(item, field):
    value = item.get(field)
    if value is None:
        return None
    if type(value) not in (int, float) or not math.isfinite(value) or value < 0:
        raise ValueError(f'{field} must be finite and nonnegative.')
    if field == 'clipDurationSeconds' and value == 0:
        raise ValueError('clipDurationSeconds must be positive.')
    return float(value)


def edits(item):
    start = nonnegative_seconds(item, 'clipStartSeconds')
    length = nonnegative_seconds(item, 'clipDurationSeconds')
    framing = item.get('framing', 'preserve')
    if framing not in FRAMINGS:
        raise ValueError(f'framing must be one of {FRAMINGS}.')
    return (start if start is not None else 0.0), length, framing


def validate(manifest, output):
    document = json.loads(manifest.read_text())
    if document.get('version') not in (1, 2) or not isinstance(document.get('videos'), list) or not document['videos']:
        raise ValueError('Manifest must have version 1 or 2 and a nonempty videos array.')
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
        if not http_url(item['sourceUrl']):
            raise ValueError(f'{relative}: sourceUrl must be an HTTP(S) source page.')
        if document['version'] == 2:
            approved(item)
            for field in ('licenseUrl', 'modifications', 'attribution'):
                value = item.get(field)
                if value is not None and (not isinstance(value, str) or not value.strip()):
                    raise ValueError(f'{relative}: {field} must be nonempty text.')
            if item.get('licenseUrl') is not None and not http_url(item['licenseUrl']):
                raise ValueError(f'{relative}: licenseUrl must be HTTP(S).')
            if not isinstance(item.get('modifications'), str) or not item['modifications'].strip():
                raise ValueError(f'{relative}: modifications must describe edits/transcoding.')
            year = item.get('year')
            if year is not None and (type(year) is not int or year < 1800 or year > date.today().year):
                raise ValueError(f'{relative}: year must be a plausible release year.')
        edits(item)
        source = (manifest.parent / item['source']).resolve()
        if not source.is_file():
            raise ValueError(f'Source file not found: {source}')
        if source.is_relative_to(output):
            raise ValueError('Keep original sources outside the prepared library.')
        target = output / relative
        if not target.resolve().is_relative_to(output):
            raise ValueError(f'Destination escapes output directory: {relative}')
        videos.append((item, source, relative))
    return document['version'], videos


def fingerprint(source):
    digest = hashlib.sha256()
    with source.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()


def probe(path):
    result = subprocess.run(
        ['ffprobe', '-v', 'error', '-show_format', '-show_streams', '-of', 'json', str(path)],
        check=True, capture_output=True, text=True)
    data = json.loads(result.stdout)
    if not isinstance(data.get('streams'), list):
        raise ValueError(f'No readable streams in {path}')
    return data


def duration(info, path):
    try:
        seconds = float(info['format']['duration'])
    except (KeyError, ValueError, TypeError) as error:
        raise ValueError(f'No measurable duration for {path}') from error
    if not math.isfinite(seconds) or seconds <= 0:
        raise ValueError(f'Invalid duration for {path}')
    return seconds


def video_stream(info, path):
    videos = [stream for stream in info['streams'] if stream.get('codec_type') == 'video']
    if not videos:
        raise ValueError(f'No video stream in {path}')
    video = videos[0]
    if (type(video.get('width')) is not int or type(video.get('height')) is not int
            or video['width'] < 2 or video['height'] < 2):
        raise ValueError(f'Invalid source video dimensions in {path}')
    return video


def pixel_ratio(value):
    try:
        numerator, denominator = (int(v) for v in value.split(':', 1))
        if numerator > 0 and denominator > 0:
            return numerator / denominator
    except (AttributeError, ValueError):
        pass
    return 1.0


def even(value):
    return max(2, 2 * round(value / 2))


def dimensions(video, framing):
    ratio = video['width'] * pixel_ratio(video.get('sample_aspect_ratio')) / video['height']
    if not math.isfinite(ratio) or ratio <= 0:
        raise ValueError('Invalid display aspect ratio.')
    height = max(2, 2 * (min(VIDEO_HEIGHT, video['height']) // 2))
    width = even(height * ratio)
    if framing == 'preserve':
        return width, height, f'scale={width}:{height}:flags=lanczos,setsar=1'
    # Fit the image inside a 4:3 canvas, preserving its display ratio.
    # A wide frame receives black bars instead of being stretched or cropped.
    canvas_w, canvas_h = 640, 480
    if ratio >= canvas_w / canvas_h:
        inner_w, inner_h = canvas_w, max(2, 2 * int((canvas_w / ratio) // 2))
    else:
        inner_w, inner_h = max(2, 2 * int((canvas_h * ratio) // 2)), canvas_h
    return canvas_w, canvas_h, (
        f'scale={inner_w}:{inner_h}:flags=lanczos,setsar=1,'
        f'pad={canvas_w}:{canvas_h}:(ow-iw)/2:(oh-ih)/2:color=black')


def trim(info, item, path):
    source_seconds = duration(info, path)
    start, requested, framing = edits(item)
    if start >= source_seconds:
        raise ValueError(f'Clip starts beyond the end of {path}')
    length = (source_seconds - start) if requested is None else requested
    if length < 0.25 or start + length > source_seconds + 0.05:
        raise ValueError(f'Clip range exceeds the source or is under 0.25 seconds: {path}')
    # Clamp a tiny source-duration rounding discrepancy.
    return start, min(length, source_seconds - start), framing


def encoding_fingerprint(source_hash, start, length, framing, width, height, ffmpeg_version):
    settings = {
        'sourceSha256': source_hash,
        'clipStartSeconds': start,
        'clipDurationSeconds': length,
        'framing': framing,
        'width': width,
        'height': height,
        'encoding': ENCODING_VERSION,
        'ffmpeg': ffmpeg_version,
        'video': 'h264 libx264 preset=medium crf=25 maxrate=1000k bufsize=2000k yuv420p yadif',
        'audio': 'aac stereo 96k optional; faststart; accurate decoded seek',
    }
    return hashlib.sha256(json.dumps(settings, sort_keys=True).encode()).hexdigest()


def inspect_output(path, expected_length, expected_ratio, source_audio):
    info = probe(path)
    actual = duration(info, path)
    video = video_stream(info, path)
    if (video.get('codec_name') != 'h264' or video.get('pix_fmt') != 'yuv420p'
            or video['height'] > VIDEO_HEIGHT or video['width'] % 2 or video['height'] % 2
            or abs(pixel_ratio(video.get('sample_aspect_ratio')) - 1) > 0.001):
        raise ValueError(f'Output is not a 480p-or-smaller square-pixel H.264/yuv420p MP4: {path}')
    ratio = video['width'] / video['height']
    if abs(ratio / expected_ratio - 1) > 0.025:
        raise ValueError(f'Output aspect ratio changed unexpectedly: {path}')
    audio = [stream for stream in info['streams'] if stream.get('codec_type') == 'audio']
    if source_audio and (not audio or audio[0].get('codec_name') != 'aac'):
        raise ValueError(f'AAC audio missing in output {path}')
    if not source_audio and audio:
        raise ValueError(f'Unexpected audio on silent source: {path}')
    if abs(actual - expected_length) > 0.3:
        raise ValueError(f'Output duration {actual:.3f}s differs from requested {expected_length:.3f}s: {path}')
    with path.open('rb') as stream:
        head = stream.read(1024 * 1024)
    if b'moov' not in head or b'mdat' not in head or head.index(b'moov') > head.index(b'mdat'):
        raise ValueError(f'Output is not faststart MP4: {path}')
    bitrate = info.get('format', {}).get('bit_rate')
    return {
        'width': video['width'], 'height': video['height'], 'videoCodec': 'h264',
        'pixelFormat': 'yuv420p', 'sampleAspectRatio': '1:1',
        'audioCodec': 'aac' if audio else None, 'duration': round(actual, 3),
        'bitrateKbps': round(int(bitrate) / 1000) if bitrate and bitrate.isdigit() else None,
    }


def atomic_json(path, value):
    temporary = path.with_suffix(path.suffix + '.tmp')
    temporary.write_text(json.dumps(value, indent=2) + '\n')
    os.replace(temporary, path)


def prepare(manifest, output):
    version, videos = validate(manifest, output)
    for command in ('ffmpeg', 'ffprobe'):
        if not shutil.which(command):
            raise ValueError(f'{command} is required.')
    ffmpeg_version = subprocess.run(['ffmpeg', '-version'], check=True, capture_output=True, text=True).stdout.splitlines()[0]
    output.mkdir(parents=True, exist_ok=True)
    metadata = output / '.demo-media.json'
    old = json.loads(metadata.read_text()) if metadata.exists() else {'videos': []}
    previous = {video['path']: video for video in old['videos']}
    credits = []
    expected = {relative for _, _, relative in videos}
    existing = {str(path.relative_to(output)) for path in output.rglob('*')
                if path.is_file() and not path.name.startswith('.')}
    if existing - expected:
        raise ValueError('Use a clean output directory or remove stale files first: ' + ', '.join(sorted(existing - expected)))

    for item, source, relative in videos:
        target = output / relative
        source_hash = fingerprint(source)
        source_info = probe(source)
        source_video = video_stream(source_info, source)
        source_audio = any(stream.get('codec_type') == 'audio' for stream in source_info['streams'])
        start, length, framing = trim(source_info, item, source)
        width, height, framing_filter = dimensions(source_video, framing)
        aspect = width / height
        cache_key = encoding_fingerprint(source_hash, start, length, framing, width, height, ffmpeg_version)
        prior = previous.get(relative, {})
        if target.exists() and prior.get('preparationSha256') == cache_key and prior.get('encoding') == ENCODING_VERSION:
            qa = inspect_output(target, length, aspect, source_audio)
            print(f'Already prepared: {relative}')
        else:
            target.parent.mkdir(parents=True, exist_ok=True)
            temporary = target.with_suffix('.mp4.partial')
            try:
                command = ['ffmpeg', '-hide_banner', '-loglevel', 'error', '-nostdin', '-y', '-i', str(source)]
                if start:
                    command += ['-ss', f'{start:.6f}']
                if item.get('clipDurationSeconds') is not None or start:
                    command += ['-t', f'{length:.6f}']
                command += [
                    '-map', '0:v:0', '-map', '0:a:0?',
                    '-vf', 'yadif=deint=interlaced,' + framing_filter + ',format=yuv420p',
                    '-c:v', 'libx264', '-threads', '2', '-preset', 'medium', '-crf', '25',
                    '-maxrate', '1000k', '-bufsize', '2000k',
                    '-c:a', 'aac', '-b:a', '96k', '-ac', '2',
                    '-map_metadata', '-1', '-movflags', '+faststart',
                    '-f', 'mp4', str(temporary),
                ]
                subprocess.run(command, check=True)
                qa = inspect_output(temporary, length, aspect, source_audio)
                os.replace(temporary, target)
            finally:
                temporary.unlink(missing_ok=True)
            print(f'Prepared: {relative}')

        credit = {
            'path': relative, 'title': item['title'], 'creator': item['creator'],
            'license': item['license'], 'sourceUrl': item['sourceUrl'],
            'duration': qa['duration'], 'sourceSha256': source_hash,
            'preparationSha256': cache_key, 'encoding': ENCODING_VERSION,
            'qa': qa,
        }
        if version == 2:
            credit.update({
                'approvedForPublicDemo': True,
                'licenseUrl': item.get('licenseUrl'), 'modifications': item['modifications'],
                'year': item.get('year'), 'attribution': item.get('attribution'),
            })
        credits.append(credit)
        print(f"  QA: {qa['width']}x{qa['height']} H.264/yuv420p; "
              f"{qa['audioCodec'] or 'silent'}; {qa['duration']:.2f}s")

    atomic_json(metadata, {'version': version, 'videos': credits})
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
