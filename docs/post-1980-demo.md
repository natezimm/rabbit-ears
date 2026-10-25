# Post-1980 color-and-sound demo

Source checks: October 25, 2026. Goal: varied color programming with original
sound, fast channel surfing, and the feeling of tuning into an ongoing station.
Production years below identify the works; modern films are not presented as
actual 1980s/1990s broadcasts. No candidate is approved or deployed by this change.

## Source findings

| Proposed station | Exact source check | Decision |
| --- | --- | --- |
| NASA Select | [capture-a-6536 metadata](https://archive.org/metadata/capture-a-6536) identifies CNN coverage of Discovery STS-31, April 24–26, 1990. Description explicitly credits CNN and reserves rights. The actual file is `CaptureA_6536.mp4` (1697.07 seconds), not the proposed filename or duration. | Reject this source. NASA subject matter does not make CNN coverage a federal work. Do not restore the previously removed silent NASA clips. |
| Tech TV | The supplied [Radio Shack compilation](https://archive.org/details/radio-shack-video-radioshackcatalogs.org) is a third-party rip without a clear license grant. The original [Portable1987 item](https://archive.org/metadata/Portable1987) identifies March 3, 1987 but has no `licenseurl` or `rights` field. Its H.264 derivative is 1737.79 seconds, not 1680. | Hold for an explicit grant covering this episode, soundtrack, excerpts and portfolio streaming. “Creative Commons / Internet Archive” is not a license. Avoid PBS branding that could imply affiliation. |
| America Outdoors | [National Parks: An American Legacy](https://archive.org/details/gov.ntis.ava19377vnb1), NPS/Public.Resource.Org, is listed as 1992, color, sound and public domain. The existing derivative is [`gov.ntis.ava19377vnb1_512kb.mp4`](https://archive.org/download/gov.ntis.ava19377vnb1/gov.ntis.ava19377vnb1_512kb.mp4), measured 1773.273 seconds, 320×240 H.264/AAC. | Hold. The downloaded film contains extensive older black-and-white footage; the 1992 compilation date does not establish that the footage meets the 1980+ rule. The 11:00–12:00 sample fails the color/era brief. Modern-looking nature segments still need underlying-date and sound/rights review. |
| Omni Sci-Fi Theater | [Tears of Steel](https://mango.blender.org/), 2012. Downloaded the [Blender-hosted 720p movie](https://download.blender.org/demo/movies/ToS/tears_of_steel_720p.mov): 734.167 seconds, 1280×534 H.264 with MP3 audio. | First-wave candidate; prepare 02:00–03:00 for review, retaining the original movie mix. The indexed [movie sharing terms](https://mango.blender.org/sharing/) state CC BY 3.0; reconfirm the exact movie edition before publication. |
| Cartoon 13 | [Big Buck Bunny](https://peach.blender.org/about/), original work 2008, CC BY 3.0. Downloaded Blender's [Sunflower normal 1080p/30 edition](https://download.blender.org/demo/movies/BBB/bbb_sunflower_1080p_30fps_normal.mp4.zip): 634.600 seconds, 1920×1080 H.264 with MP3 and AC3 audio. | First-wave candidate; prepare 01:30–02:30 for review. Record the Sunflower edition credits as well as the original creator credit. The proposed 596-second duration belongs to a different edition. |

The checks used downloaded bytes/ffprobe for the three movies above; Archive
metadata for the rejected/held TV sources. No guessed durations enter playback.
The Google sample-bucket downloads returned HTTP 403 in this environment, so
this review makes no claim that those mirrors are universally broken or reliable.
The official Blender sharing pages returned access errors on direct reads;
search retrieved their CC BY 3.0 statements. Complete the exact-edition rights
review before marking either film approved. These are candidates, not five cleared stations.

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

Prepared media validation now requires `year >= 1980` and an original audio
stream for version 2. It also decodes the **selected output excerpt**, rejecting
near-silence at or below -50 dBFS peak, including cached output. These checks
cannot prove color, underlying date, meaningful sound or permission; review still
covers those. Version 1 remains a local compatibility path, not a public release.

## Next release and experience work

1. Review the two 60-second Blender previews, edition credits and exact rights.
   Use [the candidate manifest](../demo/media-manifest.post-1980.example.json)
   as a starting point; its approval fields deliberately fail validation. Record
   the private version 2 manifest, hashes and human decision. Complete
   the attribution and modifications notice (excerpting, fitting into 4:3,
   H.264/AAC conversion); keep reviewer details and evidence outside Git.
2. Prepare the approved files with `scripts/prepare-demo-media.py` and deploy a
   fresh library using [the existing guide](../demo/real-media.md). Verify credits,
   range requests, phone control and isolated rooms. Keep port **8620** and the
   generated rollback library. A merge alone does not publish these movies.
3. Fill the remaining stations with exact-edition licensed 1980s/1990s footage,
   or clearly labeled original productions. Do not fill a five-channel quota
   with the rejected CNN clip, uncertain Computer Chronicles rights or older
   footage hidden inside a 1992 compilation.
4. Add original weather programming and station IDs with original/licensed audio.
   Mark simulated weather as fictional; use Rabbit Ears branding. Ordered
   bumper/program scheduling needs a separate design because file-id order is
   stable but is not an editorial rundown.
5. Add an optional antenna/fine-tuning interaction and accessible keyboard
   controls, then measure rapid surfing on phone/desktop. Existing two-deck
   buffering, CRT shader, static transition and green OSD already cover much of
   the proposed tactile experience. Do not claim a 150–300ms load guarantee.
6. Compare gentle offline [ntsc-rs processing](https://ntsc.rs/docs/command-line-interface/)
   against the existing shader before adding degradation. Keep original masters
   and review crop composition; default to aspect-preserving letterbox, not a
   blind 4:3 center crop. Do not double-bake scanlines or heavily damage footage.

AI video is optional for later bumpers, not a prerequisite for real programming.
No AI video/music dependency, HLS service, third-party weather simulator or
production resource increase is introduced in this release.
