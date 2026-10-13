#!/usr/bin/env bash
# Generates a few short 4:3 test "shows" in dev-media/ so the TV can be run on a
# laptop without any real episodes. Needs ffmpeg. Safe to re-run.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="$root/dev-media"

# make <show folder> <episode name> <seconds> <lavfi video source> <tone Hz>
make() {
  local show="$1" episode="$2" seconds="$3" source="$4" tone="$5"
  local file="$out/$show/$episode.mp4"
  [[ -f "$file" ]] && return
  mkdir -p "$out/$show"
  ffmpeg -hide_banner -loglevel error -y \
    -f lavfi -i "$source" \
    -f lavfi -i "sine=frequency=$tone:sample_rate=48000" \
    -t "$seconds" -vf "format=yuv420p" \
    -c:v libx264 -preset veryfast -crf 23 -g 30 \
    -c:a aac -b:a 96k -af "volume=0.08" \
    -movflags +faststart "$file"
  echo "made $show/$episode.mp4"
}

size="640x480"

make "Bars and Tone"   "S01E01" 40 "smptebars=size=$size:rate=30"  440
make "Bars and Tone"   "S01E02" 40 "pal75bars=size=$size:rate=30"  523

make "Mandelbrot Hour" "S01E01" 60 "mandelbrot=size=$size:rate=30" 330
make "Mandelbrot Hour" "S01E02" 60 "mandelbrot=size=$size:rate=30:start_scale=1.5:end_pts=200:inner=convergence" 392

# testsrc2 burns in a running clock, which makes start offsets easy to check.
make "Test Card"       "S01E01" 60 "testsrc2=size=$size:rate=30"   262
make "Test Card"       "S01E02" 60 "testsrc=size=$size:rate=30"    294
make "Test Card"       "S01E03" 60 "rgbtestsrc=size=$size:rate=30" 349

# One widescreen show, to see how 16:9 sits in the tube.
make "Wide Load"       "S01E01" 40 "testsrc2=size=854x480:rate=30" 196

echo "dev media ready in $out"
