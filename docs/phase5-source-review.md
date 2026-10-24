# Phase 5 — audio-first archival commercials: source research

**Status: candidates only.** No commercial has been downloaded, previewed in full, approved, encoded for deployment, or published. The previous Space Channel experiment has been dropped. Every final Rabbit Ears clip must have audible, specifically cleared sound.

## First source reels (one locally selected excerpt at a time)

| Candidate | Source item | Selected file to inspect | What still requires review |
| --- | --- | --- | --- |
| A1 | [Classic Television Commercials (Part I)](https://archive.org/details/ClassicT1948) | `ClassicT1948_512kb.mp4` ([direct archive download](https://archive.org/download/ClassicT1948/ClassicT1948_512kb.mp4)) | Individual commercial boundaries, music/jingles, announcers, talent, brand images, edition, and territories |
| A2 | [1955 Chevrolet Screen Ads](https://archive.org/details/1955Chev1955) | `1955Chev1955_512kb.mp4` ([direct archive download](https://archive.org/download/1955Chev1955/1955Chev1955_512kb.mp4)) | Each selected screen ad's original sound, featured people, logos, trademarks, endorser perceptions, and territories |
| A3 — on hold | [Design for Dreaming (1956)](https://archive.org/details/Designfo1956) | `Designfo1956_512kb.mp4` ([direct archive download](https://archive.org/download/Designfo1956/Designfo1956_512kb.mp4)) | Particularly prominent musical composition, soundtrack/performance, and GM promotional rights; skip unless documented permission or public-domain basis is confirmed |

All three Internet Archive item pages currently represent an item-level **U.S. public-domain certification**. That is a meaningful lead, **not** a worldwide guarantee and not evidence that every embedded musical composition or separately protected performance is cleared. An archive designation also does not automatically settle trademark, publicity, or endorsement claims. See [Prelinger FAQ](https://www.panix.com/~footage/prelarch.html) and the archive item pages for source-specific notices.

Technical verification before manual review is available through [the ephemeral commercial reel audit](archive-reel-audit.md), which downloads these two specific files only to produce metadata on their audio/video streams, scene-change candidates and silence windows. It does not grant rights or publish footage.

## How to run the new PRIVATE review workflow

Install FFmpeg on the Mac (`brew install ffmpeg`) and download **A1 and A2** through their source pages above. Do not convert or replace the original recording's sound. Put the originals somewhere private **outside the Git checkout**, e.g. `~/Movies/RabbitEars/Originals/`.

```bash
cd ~/rabbit-ears # Or wherever your Git checkout is
mkdir -p ~/Movies/RabbitEars/ArchiveReview
chmod 700 ~/Movies/RabbitEars/ArchiveReview
python3 scripts/review-archive-media.py \
  --item classic-tv \
  --source "$HOME/Movies/RabbitEars/Originals/ClassicT1948_512kb.mp4" \
  --workspace "$HOME/Movies/RabbitEars/ArchiveReview"
python3 scripts/review-archive-media.py \
  --item chevrolet-1955 \
  --source "$HOME/Movies/RabbitEars/Originals/1955Chev1955_512kb.mp4" \
  --workspace "$HOME/Movies/RabbitEars/ArchiveReview"
```

The script produces private `index.html`, a **sound-on** H.264/AAC video preview, a 12-frame contact sheet, and `rights-review.json` with **PENDING** status and review questions. It will not create a public-ready manifest, discard a soundtrack, write into the repository, or modify the demo. It checks the expected filename and records the exact SHA-256 measured locally; to pin a known checksum, provide `--expected-sha256`. It fails on missing audio, unrecognized source titles, existing output, symlinked input, or paths inside the repository.

When inspecting, note the **file-relative** start/end time of *each individual advertisement*. Archive program listings sometimes use broadcast-style 01:00:00 timecode; this is **not** an FFmpeg time offset. Watch and listen to the full proposed segment for copyrighted songs, performers and broadcast inserts. If the soundtrack is not reusable, **reject that candidate** and choose different, audibly cleared material—do not mute it.

Only after complete manual assessment and recorded permission for public portfolio streaming, adapted excerpt, sound, credits and territories should a selected source enter the existing [private version 2 preparation manifest](../demo/media-manifest.example.json). Use `scripts/prepare-demo-media.py` and the existing release checklist when approved. Do not imply approval simply because a private preview plays.

## Selection criteria

- Prioritize an engaging original spoken advertisement or period sound effects and clear source evidence; screen only the most compelling 1–3 minutes from a reel.
- Keep original 4:3 proportions, audio and any license-required notices.
- Prefer two validated, varied ads from the first two reels over a larger selection with questionable music.
- If licensing remains ambiguous, stay on generated demo footage and research licensed Creative Commons BY/CC0 sound films with a verifiable rights grant.
