# Preparing reproducible public-demo clips

This workflow uses **only locally available, human-approved sources**. The script never downloads media. A video appearing in a public archive is not automatically licensed for the public demo; complete the [media rights review](media-rights.md) first.

## Private manifest: new editing fields

The existing private manifest version 2 is unchanged. Each video may supply:

| Field | Type | Meaning |
| --- | --- | --- |
| `clipStartSeconds` | optional finite number ≥ 0 (default 0) | Position into the **source** at which output begins |
| `clipDurationSeconds` | optional finite number > 0 | Output clip length, or the rest of the source when omitted |
| `framing` | optional `"preserve"` or `"letterbox-4:3"` | Maintain native display aspect ratio or fit content into a 640×480 canvas with black bars |

For example, a reviewed file might have `"clipStartSeconds": 36.25`, `"clipDurationSeconds": 120`, `"framing": "preserve"`. All rights/approval fields from Phase 2 remain mandatory. Excerpting, letterboxing, or other edits must be disclosed accurately in `modifications` (not just described as transcoding).

### Geometry and codec behavior

- The script uses source pixel dimensions **and the sample-aspect ratio** to calculate the actual visible picture shape. It does not stretch 4:3, anamorphic, or widescreen transfers.
- `preserve` converts to square pixels and keeps the original display aspect ratio. The output height does not exceed 480 or the source height. Width and height are rounded to even numbers for H.264.
- `letterbox-4:3` scales to fit a 640×480 frame, with bars added where needed. It never discards part of the picture and should not be confused with cropping. Never describe black bars as a native 4:3 source.
- The exact output is H.264 via libx264, 8-bit yuv420p, with AAC stereo when audio is present, a nominal 1 Mbit/s video ceiling, and `+faststart` for browser seeking. A silent input remains silent.
- Trim positions are decoded rather than relying on a fast keyframe-only input seek. Each selected range must fit inside the probed source; outputs shorter than 0.25 seconds are rejected. Format durations can differ by a frame, so measured output is accepted within 0.3 seconds of the requested duration.

### QA, cache, and publication safety

Each prepared clip is probed and checked for dimensions, square pixels, video codec/pixel format, optional AAC, duration tolerance and placement of the MP4 index before video bytes. The public `.demo-media.json` includes measured duration and a `qa` object with width/height, codecs, pixel format, sample aspect ratio and bitrate. That metadata feeds the existing real-time channel scheduler.

The preparation fingerprint includes SHA-256 of the source, actual start/duration, framing, output geometry, FFmpeg version, and an encoding-settings version. Editing any of those triggers a re-encode. An identical rerun skips encoding but **rechecks** the existing file. Attribution-only changes update public metadata without needlessly re-encoding.

The script writes to an unexposed `.mp4.partial` file, checks it, and only then replaces the final output. It publishes `.demo-media.json` after every clip completes. As an additional safeguard, **never prepare directly into a library currently serving the public demo**; use a fresh, versioned release directory so failures cannot leave mixed generations live.

New files are rejected if the directory still contains videos from an older lineup. This intentionally requires a clean staging location when an item is removed or replaced. A source-only change can be prepared in the same **offline** staging location and will be re-encoded.

## Run locally

```bash
python3 scripts/prepare-demo-media.py /private/media-manifest.json /private/prepared-demo --validate-only
python3 scripts/prepare-demo-media.py /private/media-manifest.json /private/prepared-demo
```

With FFmpeg installed, run the deterministic, generated-video tests:

```bash
python3 -m unittest discover -s tests -p 'test_*.py'
```

The Python suite generates short synthetic videos on demand. The Playwright suite also uses synthetic H.264/AAC fixtures to test browser video decoding and seeking through HTTP range responses. **No external film or soundtrack is required for these tests.**

The deployment and recovery procedure is tracked separately as Phase 4; do not deploy approved footage until that release gate is complete.
