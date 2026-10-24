# Phase 5 — audio-first archival commercials: source research

**Status: source files technically inspected in ephemeral CI on October 24, 2026, but no commercial watched/listened to in full, approved, encoded for deployment, or published.** The previous Space Channel experiment has been dropped. Every final Rabbit Ears clip must have audible, specifically cleared sound.

## First source reels (one locally selected excerpt at a time)

| Candidate | Source item | Selected file to inspect | What still requires review |
| --- | --- | --- | --- |
| A1 | [Classic Television Commercials (Part I)](https://archive.org/details/ClassicT1948) | `ClassicT1948_512kb.mp4` ([direct archive download](https://archive.org/download/ClassicT1948/ClassicT1948_512kb.mp4)) | Individual commercial boundaries, music/jingles, announcers, talent, brand images, edition, and territories |
| A2 | [1955 Chevrolet Screen Ads](https://archive.org/details/1955Chev1955) | `1955Chev1955_512kb.mp4` ([direct archive download](https://archive.org/download/1955Chev1955/1955Chev1955_512kb.mp4)) | Each selected screen ad's original sound, featured people, logos, trademarks, endorser perceptions, and territories |
| A3 — on hold | [Design for Dreaming (1956)](https://archive.org/details/Designfo1956) | `Designfo1956_512kb.mp4` ([direct archive download](https://archive.org/download/Designfo1956/Designfo1956_512kb.mp4)) | Particularly prominent musical composition, soundtrack/performance, and GM promotional rights; skip unless documented permission or public-domain basis is confirmed |

All three Internet Archive item pages currently represent an item-level **U.S. public-domain certification**. That is a meaningful lead, **not** a worldwide guarantee and not evidence that every embedded musical composition or separately protected performance is cleared. An archive designation also does not automatically settle trademark, publicity, or endorsement claims. See [Prelinger FAQ](https://www.panix.com/~footage/prelarch.html) and the archive item pages for source-specific notices.

Technical verification before manual review is available through [the ephemeral commercial reel audit](archive-reel-audit.md), which downloads these two specific files only to produce metadata on their audio/video streams, scene-change candidates and silence windows. It does not grant rights or publish footage.

**Working local TV preview:** [Watch the unapproved sound-on candidate clips on CH 06, localhost only](local-commercials-preview.md). This does not clear, publish or deploy any media.

## October 24 source verification (GitHub Actions [run #40057053268](https://github.com/natezimm/rabbit-ears/actions/runs/40057053268))

Both selected 320×240 H.264/AAC stereo source renditions downloaded and fully decoded successfully. Technical evidence pinned to exact local filenames:

| Item | Bytes | Duration | SHA-256 |
| --- | ---: | ---: | --- |
| Classic TV | 38,145,570 | 536.503s | `d7d2cf8b1e75a7abde8d150cac768f19123f82b4453e0ba1914667734aca497e` |
| Chevrolet | 37,344,324 | 527.327s | `60fcbb2496c602bf7f8a0b5fe4efb25a0e2acd9f93488081c46ca6fe9cce5b2a` |

The GitHub Action discarded the actual recordings and uploaded only metadata. Source audio streams **exist and decode**; that alone does not establish audible content quality or reuse permissions.

### Private sound-on scouting previews — proposed windows, not approved edits

Use `scripts/prepare-commercial-review.py` with the exact source files. It SHA-256-checks the video, retains and checks audible source sound, prepares H.264/AAC local previews and pending rights records, and **never exports a public media manifest**.

| Reel | Scouting label | File-relative window | Editorial status |
| --- | --- | --- | --- |
| Classic TV | Remington | 2:30–3:54 | Broad exploratory window; shotlist places the ad nearby; verify exact boundaries |
| Classic TV | Mum deodorant | 4:20–5:45 | Broad exploratory window; verify beginning, end, soundtrack and adjacent spots |
| Chevrolet | Opening reel | 0:00–1:04 | Unknown individual ad boundaries; compare visuals and audio |
| Chevrolet | Middle reel | 4:10–5:38 | Unknown individual ad boundaries; compare visuals and audio |

**Do not treat a scene-change timestamp as an ad boundary.** The full source and all proposed preview soundtracks require human viewing/listening plus license, embedded music, performer, brand, and territory review before they may be published.

**Recommended: one-command Mac workflow after merging the preview PR.** From the Rabbit Ears checkout, run the following. The launcher downloads only the two audited files via HTTPS if absent, verifies size and SHA-256, builds sound-on previews privately outside Git, and opens both review pages. Requires FFmpeg (`brew install ffmpeg`).

```bash
bash scripts/run-commercial-review.sh
```

If you prefer manual downloads or are unable to access the archive through curl, place the exact source files in the Originals folder yourself and run these equivalent commands:

```bash
mkdir -p "$HOME/Movies/RabbitEars/Originals" "$HOME/Movies/RabbitEars/CommercialReview"
chmod 700 "$HOME/Movies/RabbitEars/CommercialReview"

python3 scripts/prepare-commercial-review.py \
  --item classic-tv \
  --source "$HOME/Movies/RabbitEars/Originals/ClassicT1948_512kb.mp4" \
  --workspace "$HOME/Movies/RabbitEars/CommercialReview"

python3 scripts/prepare-commercial-review.py \
  --item chevrolet-1955 \
  --source "$HOME/Movies/RabbitEars/Originals/1955Chev1955_512kb.mp4" \
  --workspace "$HOME/Movies/RabbitEars/CommercialReview"

open "$HOME/Movies/RabbitEars/CommercialReview/classic-tv/index.html"
open "$HOME/Movies/RabbitEars/CommercialReview/chevrolet-1955/index.html"
```

Do not rename source files. A new output directory is required for reruns to preserve existing rights-review notes. Generated content remains outside Git, and every `rights-review.json` stays pending (`approvedForPublicDemo: false`). 


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
