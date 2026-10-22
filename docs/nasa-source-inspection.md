# Phase 5 — NASA source acquisition: technical inspection

**Status:** Investigation only. The source files are **not cleared or approved** for public Rabbit Ears streaming, and no production media release is created by this workflow.

## Exact candidates selected for ephemeral technical review

| ID | Candidate | Source page | Selected temporary rendition |
| --- | --- | --- | --- |
| S1 | STS-1 Columbia launch, historic April 1981 footage released by Kennedy Space Center in 2011 | [Wikimedia Commons](https://commons.wikimedia.org/wiki/File:STS-1_Launch_6qMPLydUbuM.webm) | `STS-1_Launch_6qMPLydUbuM.webm` (~7 MB) |
| S2 | STS-31 Hubble deployment, April 1990 | [NASA SVS 10321](https://svs.gsfc.nasa.gov/10321/) **item 23** | `23-resource-hst_deployment-resourcereelreference_MPEG-1.webmhd.webm` (~7.8 MB) |

**Measured 1981 technical result (GitHub Actions inspection, October 22, 2026):** The selected Wikimedia copy of NASA Kennedy's 2011 STS-1 launch release downloaded successfully on [the repeatable CI inspection run](https://github.com/natezimm/rabbit-ears/actions/runs/39872912173), **7,310,824 bytes**, **SHA-256 `332e958e0007ab981152b53703508d5ce32debcd7685276c779f0ea69bee8334`**, **168.781 seconds**, VP9 **1280×720 16:9 at 25 fps**, Opus 2-channel audio at 48 kHz, and video decode tests passing at two positions. A provisional clip start of 0 seconds with 160 seconds of footage fits the editorial length target but is **not approved** until every frame and sound is reviewed.

**Measured 1990 technical result (GitHub Actions inspection, October 22, 2026):** NASA's smaller S2 file downloaded successfully, **8,139,469 bytes**, **SHA-256 `4cca366cf0d9c7b76c64feb7ff9b5bdc36ad275d14b4f8afeaa55901af07d81c`**, **93.952 seconds**, VP8 **320×240 at 30000/1001 fps**, Vorbis 2-channel audio at 44.1 kHz, two video decode samples passing. NASA's web page lists 960×540, but the **actual downloaded rendition probed as 320×240**; trust the measured file properties. It cannot provide a full 2–4 minute excerpt without repetition, but it can work as a shorter interstitial if independently approved. Audio contents, rights and all frames remain unreviewed. No media binary was retained.

The first Wikimedia-hosted STS-1 request returned HTTP 429 (temporary rate limiting), but a later independent inspection run **downloaded and verified the original selected rendition**. The inspector includes an alternative lookup for NASA Kennedy's official [2021 archival-footage asset](https://images.nasa.gov/details-KSC-20210325-MH-JRS01_0001-STS-1_File_Footage_for_40th_Anniversary) if Commons throttles a future run; that is a **different edition** and must be separately reviewed and hashed if used. The published hashes above are for the actual Commons and NASA SVS files, **not** the fallback.

The lighter S2 candidate is an alternate to the 423.6 MB [original STS-31 mission highlights resource tape](https://svs.gsfc.nasa.gov/13081/). Its source description states that it is upconverted 16mm footage of the 1990 deployment. The two NASA presentations are not interchangeable: the original resource tape has different sections, timecodes and potential rights disclosures. Select and review the exact rendition before using any clip range.

## Verified outputs, but no editorial clearance

[GitHub Actions run 39872912173](https://github.com/natezimm/rabbit-ears/actions/runs/39872912173) downloaded and probed both source files without retaining either binary, and uploaded the metadata-only `nasa-source-inspection-metadata` artifact for seven days. The source SHA-256 values above are technically confirmed. Neither audio track has been listened to or licensed, and neither clip has been approved for public use.

## What the audit checks

Run `python3 scripts/inspect-nasa-sources.py --download --report nasa-source-inspection.json` in a **temporary environment** with `ffmpeg` and `ffprobe`. The script:

1. Downloads exactly two preselected NASA-origin source URLs, each with a 25 MB cap. It rejects HTML responses and unexpectedly short files.
2. Computes the actual downloaded file's SHA-256 and byte count.
3. Uses `ffprobe` to capture duration, pixel geometry, sample/display aspect ratio, codec and audio stream presence.
4. Attempts to decode video from two positions, validating basic technical readability.
5. Suggests a **provisional only** starting point of up to 160 seconds and records that full audiovisual, music, territory and endorsement review remain incomplete.
6. Immediately deletes both downloaded video files; only a metadata JSON report is retained.

The dedicated `.github/workflows/nasa-source-inspection.yml` job runs these checks in an ephemeral GitHub Actions runner. **Only the text JSON report is uploaded** as a short-lived artifact; no source video, extracted audio, frame, thumbnail, contact sheet, or playable preview is attached. The CI log also prints a one-line `NASA_AUDIT_RESULT=` per successful rendition.

The script deliberately never writes to the live demo mount, emits an approved private manifest, or switches `approvedForPublicDemo` to `true`.

## What the audit cannot establish

Technical decoding is **not** a content, copyright or human judgment review. Hashes and audio presence cannot distinguish NASA-original mission communications from later licensed music; neither prove worldwide publication permission. Likewise, isolated probe timestamps do not identify underlying footage, third-party clips, identifiable persons, trademarks, or implied endorsement.

These are mandatory next gates:

- Watch **and listen to every second** of the selected excerpt of the exact downloaded file.
- Inspect audio rights, restoration/upconversion additions, source provenance, person/likeness usage, official insignia and credit requirements.
- Consider access from non-U.S. jurisdictions. U.S. public-domain status is not a worldwide legal conclusion.
- Record an actual reviewer, date and supported decision for **each hashed exact rendition** in the **private** rights ledger, outside Git.
- Only then use the v2 approved source manifest, Phase 3 video preparation, and Phase 4 release verification to produce an immutable staged media library.
- Do not run the source audit as an implicit part of building the Docker image, serving a web request or deploying to the public VM.

If NASA or Wikimedia change their file hosting endpoints or block GitHub runners, the report will contain a failure, not a made-up hash, duration or clearance. Update the verified file link and rerun inspection or use the source on a private machine.

## Local verification

```bash
python3 -m unittest discover -s tests -p 'test_inspect_nasa_sources.py'
python3 scripts/inspect-nasa-sources.py --download --report nasa-source-inspection.json
```

Keep local inspection output private, as the SHA identifies the exact binary and could be referenced in the private ledger. No NASA clip has been cleared or released as a result of these commands.
