# Phase 5 — batch 1 source/rights investigation (pending)

**Checked:** October 22, 2026. **Scope:** CH 05 NASA footage (2) and CH 06 archival commercials (3). **Disposition:** Source pages and specified downloadable renditions identified. **0 downloaded, 0 inspected locally, 0 approved for public streaming.** Do not set `approvedForPublicDemo: true` yet.

This is a publication-gate worksheet, not a legal opinion or the private rights ledger. Source page rights statements are not a substitute for inspecting the actual file and soundtrack. Keep copies, hashes, review attestations and any legal advice outside Git.

## Source files selected

| ID | Proposed channel/clip | Specific file to inspect | Evidence / risks |
| --- | --- | --- | --- |
| S1 | CH 05 — STS-1 Launch, 1981 | [`STS-1_Launch_6qMPLydUbuM.webm`](https://commons.wikimedia.org/wiki/File:STS-1_Launch_6qMPLydUbuM.webm) (7.31 MB, 168.781 sec, VP9/Opus, 1280×720) | NASA-Kennedy credited by Commons; U.S. PD federal work. **Confirm any added 2011 release audio, staff/astronaut likenesses, logos and nonendorsement** |
| S2 | CH 05 — STS-31 Hubble, 1990 | [NASA SVS 13081: `GSFC_1990xxxx_HST_m001.webm`](https://svs.gsfc.nasa.gov/13081/) (423.6 MB, 960×720) | NASA Goddard original mission resource tape. NASA's source notes launch **09:50**, bay doors **12:27** — provisional 157-second clip starting 590 seconds; check soundtrack and timeline first |
| A1 | CH 06 — Classic Television Commercials I | [`ClassicT1948_512kb.mp4`](https://archive.org/details/ClassicT1948), [file link](https://archive.org/download/ClassicT1948/ClassicT1948_512kb.mp4) | Item-level U.S. public-domain certification; **check each spot's soundtrack, brand, talent and territory**. Archive reel timecodes (01:00:11 etc.) are not MP4-relative offsets |
| A2 | CH 06 — 1955 Chevrolet Screen Ads | [`1955Chev1955_512kb.mp4`](https://archive.org/details/1955Chev1955), [file link](https://archive.org/download/1955Chev1955/1955Chev1955_512kb.mp4) (35.6 MB, 8:44) | Item-level U.S. public-domain certification; **check jingles, recognizable people and Chevrolet branding; no implied sponsorship** |
| A3 | CH 06 — Design for Dreaming | [`Designfo1956_512kb.mp4`](https://archive.org/details/Designfo1956), [file link](https://archive.org/download/Designfo1956/Designfo1956_512kb.mp4) (37.7 MB, 9:16) | Item-level U.S. public-domain certification, **higher music risk**: archive explicitly describes a musical with song lyrics and prominent GM promotion |

**Lighter 1990 NASA alternative:** [NASA SVS 10321](https://svs.gsfc.nasa.gov/10321) item **23**, `23-resource-hst_deployment-resourcereelreference_MPEG-1.webmhd.webm` (7.8 MB, 960×540), an upconverted 16mm film of the April 25, 1990 Hubble deployment. This is easier than the 423.6 MB tape but appears on a multi-contributor NASA/STScI resource page; inspect provenance, soundtrack and applicable conditions before replacing S2.

## Why item page rights are not sufficient

- All three archive entries explicitly show *Usage: Public Domain* linked to the retired [Creative Commons Copyright-Only Dedication/Public Domain Certification 1.0 United States](https://creativecommons.org/publicdomain/certification/1.0/us/). The instrument is based on **U.S. law** and is not a global rights warranty.
- [Prelinger Archives' May 27, 2026 FAQ](https://www.panix.com/~footage/prelarch.html) says not every film is PD, though earlier item-level dedications remain in effect. A separate song, new material, identifiable talent, or trademark can raise independent issues.
- [NASA's media-use guidelines](https://www.nasa.gov/nasa-brand-center/images-and-media/) generally permit factual acknowledgment/nonendorsing usage, but warn of third-party copyright, agency marks, and publicity rights especially for promotional material. [NASA SVS FAQ](https://svs.gsfc.nasa.gov/help/) expressly cautions that some NASA visualizations contain licensed music.
- A publicly accessible job-seeking portfolio should not be assumed noncommercial, nor should a U.S.-only status be treated as worldwide clearance.

## Editorial review order and cautions

1. **S1:** The whole 2:49 item fits. Listen for modern scores over the 1981 launch footage in the later NASA release; document absence/presence of recognizable people and any third-party inserts.
2. **S2:** Inspect the 09:50–12:27 window of the 1990 NASA resource tape. Distinguish historical mission control audio from added music, archival broadcast networks, or licensed inserts. NASA Goddard must be acknowledged in public credits.
3. **A1:** Choose quieter Band-Aid or Remington visuals; avoid the shotlisted musical Gillette parrot, named baseball players and tobacco/alcohol spots pending more specific proof. Identify **file-relative** trim offsets by watching the actual video.
4. **A2:** Select approximately 2–3 adjacent screen ads only after checking music/people and confirming brand images are presented as historical artifacts, not a partnership.
5. **A3:** Hold until musical-composition/performance rights are understood; if uncertain, replace with a more clearly licensed nonmusical archival film.

## Required gates before preparing and publishing clips

- [ ] Obtain the exact source file and save its SHA-256 + verified original URL in the **private** rights ledger (outside the repository).
- [ ] Run `ffprobe` for actual video/audio streams, duration, format and display aspect ratio. Match the recorded file to the page's stated rendition.
- [ ] Watch/listen to **every second of each proposed excerpt** for new music, third-party material, restoration credits, identifiable people, marks, safety and attribution constraints.
- [ ] Confirm permitted public streaming, promotional context, modifications, soundtrack and territories. Unresolved rights mean **do not publish**.
- [ ] Select correct file-relative `clipStartSeconds` and `clipDurationSeconds`; describe real edits in `modifications`.
- [ ] Have a human reviewer record the file-specific decision in a private ledger before setting v2 `approvedForPublicDemo: true`, `reviewedBy`, and `reviewedAt`.
- [ ] Prepare to a fresh versioned directory and run `scripts/prepare-demo-media.py` and `scripts/verify-demo-release.py`. Follow [rights](media-rights.md), [media preparation](media-preparation.md), and [GCP rollback](../demo/release-checklist.md) documentation.
- [ ] Never upload source films, the private ledger, or unapproved audio to GitHub or the running GCP site.

**No download or GCP deploy was performed for this review.** The actual source-file listening, hashing, territorial judgment and authorization are the next uncompleted operations.
