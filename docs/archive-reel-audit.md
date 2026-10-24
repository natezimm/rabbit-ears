# Phase 5.4 — Technical audit of commercials with original audio

The first two Internet Archive sound-film reels are tracked in [source research](phase5-source-review.md). This workflow is **investigation only**, not a public release, music license, or media approval.

## Why the next step is an audit

The former Space Channel was discarded because the user wants original, rights-cleared **sound** on every channel. Neither archive reel has been viewed/listened to in full or approved. Their item pages claim a U.S. public-domain basis, but rights for individual songs, performances, brands, later edits, and non-U.S. streaming require separate investigation.

## Two exact renditions

| Reel | Official archive page | Candidate original file |
| --- | --- | --- |
| Classic Television Commercials, Part I | https://archive.org/details/ClassicT1948 | `ClassicT1948_512kb.mp4` |
| 1955 Chevrolet Screen Ads | https://archive.org/details/1955Chev1955 | `1955Chev1955_512kb.mp4` |

Run the **Archive Commercial Reel Audit** GitHub Action manually (Actions → Archive Commercial Reel Audit → Run workflow). Alternatively, on a temporary local machine with internet access and FFmpeg installed:

```bash
python3 -m unittest discover -s tests -p 'test_probe_archive_reels.py'
python3 scripts/probe-archive-reels.py --download --report /tmp/archive-reel-audit.json
```

The script downloads **only these two fixed URLs**, limits file size to 110 MB, refuses non-Archive redirects, checks audio and video presence, computes SHA-256, probes codecs/duration and attempts full audio and video decoding, and records sampled **scene-change candidate timestamps** and **silence intervals**. These timestamps are **technical hints**, not verified commercial boundaries or recommended publishing cuts. Any fetch/codec failure is reported as a blocked candidate rather than being treated as cleared media.

Everything runs in a temporary directory and deletes the source files. The workflow uploads **JSON metadata only** for seven days; it never uploads or commits original footage, previews, audio, contact sheets or any playable media. The current GCP demo, port 8620, synthetic fallback, and media rights gate remain unchanged.

### After the technical report

1. Verify downloaded hashes for the exact renditions you plan to review on a private machine.
2. Play every proposed excerpt **with original sound**, checking jingles/music/compositions, narration, performers, brand use, insert footage and any audible station material.
3. Record actual start/end offsets **relative to the file**, not historical broadcaster timecodes.
4. Document item-level licensing evidence and the separate applicable worldwide/portfolio-streaming analysis. An Archive U.S. public-domain label alone is not enough.
5. Once specific sound-bearing clips are independently approved, add them to a private version 2 manifest and use `scripts/prepare-demo-media.py`. Unapproved source files must never go into the demo.

If Archive denies download or redirects to an unsupported host, do not bypass access controls. Download the source manually under the archive's permitted access conditions and use the private review tool already in `scripts/review-archive-media.py`.
