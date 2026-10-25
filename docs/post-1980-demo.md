# 1980–1999 color-and-sound demo

The public-demo brief is **original footage made from 1980 through 1999,
inclusive**, with color picture and audible original sound. Upload, restoration
or compilation dates do not establish the date of the underlying footage.
Modern films with retro filters do not meet this brief. Big Buck Bunny (2008)
and Tears of Steel (2012) are excluded; their earlier review pack is superseded
and must not be used to prepare this lineup. No candidate is approved or deployed.

## Period candidates to inspect

Archive metadata checked October 25, 2026. These are research leads, not cleared
selections or verified playback. Prefer native 4:3, period graphics, presenters,
sets and sound. Listen to and watch the exact selected file before approval.

| Candidate | Period evidence and archive record | Remaining checks |
| --- | --- | --- |
| Video Teletraining | [FAA / Public.Resource.Org](https://archive.org/details/gov.ntis.ava20236vnb1), 1994; listed color and sound with a public-domain notice. File `ava20236vnb1_512kb.mp4`, listed 320×240, 1109.08 seconds. Period communications/training programming. | Download, verify date in credits, inspect original audio and third-party material, choose an engaging excerpt. |
| Wake Turbulence Avoidance | [FAA / Public.Resource.Org](https://archive.org/details/gov.ntis.ava19661vnb1), 1995; listed color and sound with a public-domain notice. Re-enactments and animation about aviation. File `gov.ntis.ava19661vnb1_512kb.mp4`, listed 320×240, 1455.96 seconds. | Inspect exact producer/partner credits, soundtrack and reuse basis; verify color, audio and excerpt quality. |
| An Ax to Grind — Part 1 | [USDA Forest Service / Public.Resource.Org](https://archive.org/details/gov.usda.fs.mtdc.0823-2d01-mtdc.1.1), date/year fields say 1998 while description says 1999. Both fall within the brief; listed color, sound and CC0. File `gov.usda.fs.mtdc.0823-2d01-mtdc.1.1_512kb.mp4`, listed 320×240, 1776.61 seconds. Outdoor instructional programming. | Resolve the exact edition/year from the video, check original sound and credits, choose period live footage instead of historical inserts. |
| Man and Safety — Animated | [Department of Defense / Public.Resource.Org](https://archive.org/details/gov.dod.dimoc.600519), date field 1984, CC0 metadata. File `gov.dod.dimoc.600519_512kb.mp4`, listed 320×240, 2698.63 seconds. Potential period animation. | No color/sound fields in the inspected metadata. Check both in the actual file and establish the original production date before selecting it. |

All dimensions and durations in this table come from archive metadata and must
be measured from downloaded bytes before scheduling. An archive license label
is a starting point, not an automatic exact-file approval. Do not promise five
stations before five suitable recordings have passed review.

## Downloaded-file spot checks

On October 25, 2026, the three FAA/Forest Service MP4s above were downloaded
and probed. All contain H.264 video at 320×240 and AAC audio. Measured durations
are 1109.075742, 1455.955956 and 1776.609943 seconds respectively. Six frames
per recording were inspected (2, 15, 60, 180, 360 seconds and one minute before
the end). This is a spot check, not a full watch-through or soundtrack review.

- **Video Teletraining:** sampled frames show color conference-room footage,
  period title graphics, training slides and early computer animation.
- **Wake Turbulence Avoidance:** sampled frames show color cockpit/airport footage
  and period computer animation. End credits name Boeing, McDonnell Douglas and
  other partners; verify the reuse basis for their contributions before release.
- **An Ax to Grind:** workshop/presenter frames are color, but the introduction
  includes historical black-and-white imagery. Exclude historical inserts and
  resolve the exact edition date before selecting an excerpt.

Decoded audio from 03:00–03:30 has peak levels of -4.5, -2.1 and -0.2 dBFS
respectively. This establishes non-silence in those samples only; it does not
establish speech intelligibility, original soundtrack provenance or clearance.
No media has been deployed or approved by these checks.

Downloaded-source SHA-256 values (for reproducing the review):

| Recording | SHA-256 |
| --- | --- |
| teletraining | `b4a89aa4495363035aca65b729bf77dd9df3b8763543ca121ca6e1c608c84c8e` |
| aviation | `a64e66fb17bd3b754556f5412038cef88103bd734a51f4cb283f32d2bbcc81ca` |
| outdoors | `a34d184d02420513d79c1a47867aa32b148d0039a6826fdb15efef22c05ae868` |

## Excluded or held sources

- **Big Buck Bunny / Tears of Steel:** outside 1980–1999. Removed from the
  candidate manifest; do not ask for approval of these modern films again.
- **CNN Discovery STS-31 coverage:** the proposed “NASA” item
  [capture-a-6536](https://archive.org/metadata/capture-a-6536) explicitly identifies
  copyrighted CNN coverage. Do not use it or restore the removed silent NASA clips.
- **Computer Chronicles / Net Cafe:** strong visual matches, but check each exact
  episode. The supplied Radio Shack rip and `Portable1987` have no verified grant.
  [Net Cafe: Disinformation on the Web](https://archive.org/details/nc119_disinformation)
  identifies a 1997 broadcast but carries BY-NC-ND terms, not unrestricted CC BY.
  Keep these on hold for permission covering intended portfolio streaming and edits.
- **National Parks: An American Legacy (1992):** its checked 11:00–12:00 excerpt
  contains older black-and-white footage. The compilation date is insufficient;
  any different excerpt requires underlying-date, color, audio and rights review.

## Copyright and hosting corrections

- Government websites can contain third-party footage, music, logos and other
  restricted material. Follow [NASA's media rules](https://www.nasa.gov/nasa-brand-center/images-and-media/)
  for the exact asset, not a blanket “100% public domain” assertion.
- [Copyright Office Circular 3](https://www.copyright.gov/circs/circ03.pdf)
  distinguishes notice requirements through February 28, 1989 from later works.
  January 1, 1978 is not a shortcut for clearing a particular recording.
- License the movie and its existing soundtrack together. Do not separately
  download/remix a score assuming the movie license applies: the Blender-hosted
  [Tears of Steel soundtrack notice](https://download.blender.org/demo/movies/ToS/copyright.txt)
  specifies CC BY-ND 3.0 for **standalone original soundtrack files**. That notice
  does not by itself determine the complete movie's license.
- Pexels/Pixabay use their own licenses; do not label every clip CC0. AI output
  also requires checking provider terms, music rights and recognizable material.
- Download cleared media once and use the existing read-only local mount.
  Hotlinking does not guarantee availability, CORS/WebGL support, byte ranges,
  bandwidth terms, browser codecs or zero cost. The checked files themselves
  demonstrate why an `.mp4` extension is not sufficient codec evidence.

## Implemented broadcast behavior

`Box:Demo:SynchronizedBroadcast` defaults to true **in demo mode only**.
For a Live channel whose episode lengths are all known, the server sorts episodes
by stable file id, sums their measured lengths and selects the episode and offset
at `UTC seconds since Unix epoch modulo playlist length`. New rooms receive
known durations before their first tune. Returning, waking and ended-episode
tunes use the same schedule; client position reports do not move it, and browser
rounding cannot overwrite its known durations. Room controls remain independent.

Unknown/invalid durations fall back to existing behavior until the schedule can
be calculated on a later tune. Resume/Random and the home installation retain
their existing behavior. A new source/playlist changes the schedule. Rejoining
uses the server schedule; this is not frame-accurate synchronization of already
playing browsers under buffering/network delays. No browser `Date.now()` seek
is added beside the authoritative server state.

Prepared media validation now requires `1980 <= year <= 1999` and an original audio
stream for version 2. It also decodes the **selected output excerpt**, rejecting
near-silence at or below -50 dBFS peak, including cached output. These checks
cannot prove color, underlying date, meaningful sound or permission; review still
covers those. Version 1 remains a local compatibility path, not a public release.

## Next release and experience work

1. Download the period candidates above privately, inspect the actual video and
   soundtrack, and prepare short review clips with source hashes and accurate
   credits. Reject poor transfers, silence, uncertain dates and unsuitable rights.
2. Record approved selections in a private copy of
   [the generic manifest](../demo/media-manifest.example.json). The modern-film
   candidate manifest has been removed. Review footage date separately from the
   upload or compilation date; preserve native framing and original sound.
3. Prepare the approved files with `scripts/prepare-demo-media.py` and deploy a
   new library using [the existing guide](../demo/real-media.md). Verify credits,
   range requests, phone control and isolated rooms. Keep port **8620** and the
   generated rollback library. A merge alone does not publish any videos.
4. Expand the lineup using additional verified 1980–1999 recordings. Keep modern
   or AI-generated clips out of the period footage selection. Original interface
   effects and short Rabbit Ears station IDs can be considered separately after
   the actual programming fits the brief.
5. Add an optional antenna/fine-tuning interaction with keyboard controls and
   measure rapid surfing on phone/desktop. Existing buffering, CRT effects and
   green OSD already supply much of the requested presentation.
6. Compare gentle offline [ntsc-rs processing](https://ntsc.rs/docs/command-line-interface/)
   against the existing shader only after finding suitable period media. Preserve
   masters; avoid heavy degradation, double-baked scanlines and blind 4:3 crops.

Generated fixtures remain available for development/CI and rollback, clearly
separate from the curated period lineup. Test fixture years exercise validation;
they are not claims that synthetic test patterns were produced in the 1990s.
