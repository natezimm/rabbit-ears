#!/usr/bin/env bash
# Convert a folder of shows into files the box can play: H.264 + AAC in MP4,
# with the index at the front so the TV can jump into the middle instantly.
#
#   bash scripts/convert-media.sh /path/to/originals /path/to/converted
#   bash scripts/convert-media.sh /path/to/originals /path/to/converted 480
#
# The folder layout is kept (Show/Season 1/S01E01.mkv -> Show/Season 1/S01E01.mp4)
# and files that are already converted are skipped, so it is safe to re-run after
# adding episodes. The optional third argument caps the picture height (720 by
# default; old 4:3 shows rarely have more than 480 lines anyway).
set -euo pipefail

src="${1:?Usage: convert-media.sh <originals> <converted> [max height]}"
dest="${2:?Usage: convert-media.sh <originals> <converted> [max height]}"
max_height="${3:-720}"

command -v ffmpeg >/dev/null || { echo "ffmpeg is required (sudo apt install ffmpeg / brew install ffmpeg)" >&2; exit 1; }
src="$(cd "$src" && pwd)"
mkdir -p "$dest"
dest="$(cd "$dest" && pwd)"
[[ "$dest" != "$src"* ]] || { echo "The converted folder must not be inside the originals." >&2; exit 1; }

# One property of the first video (v) or audio (a) stream, or nothing.
probe() {
  ffprobe -v error -select_streams "$2:0" -show_entries "stream=$3" -of default=nw=1:nk=1 "$1" 2>/dev/null | head -n 1 || true
}

converted=0 skipped=0 failed=0
while IFS= read -r -d '' file; do
  relative="${file#"$src"/}"
  out="$dest/${relative%.*}.mp4"
  # Already converted, unless the original has been replaced since.
  if [[ -f "$out" && ! "$file" -nt "$out" ]]; then
    skipped=$((skipped + 1))
    continue
  fi

  mkdir -p "$(dirname "$out")"
  partial="$out.partial"
  # Most rips are already H.264, so the picture can usually be copied as is: that
  # takes seconds, where re-encoding takes minutes on the box. Only re-encode when
  # the browser could not play it, it is taller than asked, or it is interlaced.
  vcodec="$(probe "$file" v codec_name)" pixfmt="$(probe "$file" v pix_fmt)"
  height="$(probe "$file" v height)" field="$(probe "$file" v field_order)"
  acodec="$(probe "$file" a codec_name)"
  if [[ "$vcodec" == h264 && "$pixfmt" == yuv420p && "$height" =~ ^[0-9]+$ && "$height" -le "$max_height" \
        && ( -z "$field" || "$field" == progressive || "$field" == unknown ) ]]; then
    echo "copying $relative"
    video=(-c:v copy)
  else
    echo "converting $relative"
    # yadif only touches frames flagged as interlaced, which old DVD rips often are.
    video=(-vf "yadif=deint=interlaced,scale=-2:'min($max_height,ih)',format=yuv420p"
      -c:v libx264 -preset medium -crf 20)
  fi
  if [[ "$acodec" == aac && "$(probe "$file" a channels)" =~ ^[12]$ ]]; then
    audio=(-c:a copy)
  else
    audio=(-c:a aac -b:a 160k -ac 2)
  fi
  if ffmpeg -hide_banner -loglevel error -nostdin -y -i "$file" \
      -map 0:v:0 -map 0:a:0? "${video[@]}" "${audio[@]}" \
      -movflags +faststart -f mp4 "$partial"; then
    mv "$partial" "$out"
    converted=$((converted + 1))
  else
    rm -f "$partial"
    echo "  FAILED: $relative" >&2
    failed=$((failed + 1))
  fi
done < <(find "$src" -type f \( -iname '*.mp4' -o -iname '*.m4v' -o -iname '*.mkv' -o -iname '*.avi' \
  -o -iname '*.mov' -o -iname '*.mpg' -o -iname '*.mpeg' -o -iname '*.wmv' -o -iname '*.ts' \
  -o -iname '*.webm' -o -iname '*.flv' -o -iname '*.divx' \) -not -name '.*' -print0 | sort -z)

echo "done: $converted converted, $skipped already up to date, $failed failed"
[[ "$failed" -eq 0 ]]
