# 2. Play tokens and sequence counters instead of events

Status: accepted

## Context

The screen reports back to the server: an episode ended, failed, or reached a
position. Those reports race with the viewer's presses. A late "episode ended"
for a channel the viewer has already left must not skip an episode on the new
one. The on-screen display also needs to flash the channel banner or volume
bar, and a one-off "show the banner" event can be lost or replayed on
reconnect.

## Decision

- Every "play this" instruction in a snapshot carries a unique **play token**
  that the screen echoes in `EpisodeEnded`, `EpisodeFailed` and
  `ReportPosition`. `Television` ignores an end or failure with a stale token.
  Position reports for a channel just left still land, which is how `Resume`
  picks up where the viewer left off. On the box, mpv's playlist entry ids
  play the same part.
- `bannerSeq`, `volumeSeq` and `message.seq` in the snapshot tick when that
  piece of display should flash. Clients compare numbers.
- Snapshots carry `boot` and `revision`; clients drop an older revision from
  the same boot (`web/src/app/core/tv-link.ts`).

## Consequences

- Reports are idempotent and safe to deliver late, twice or out of order.
- The whole display can be rebuilt from any one snapshot, so a reconnecting
  client never misses or repeats a flash.
- Every screen implementation (`tube.ts`, `MpvScreen.cs`) has to echo tokens
  faithfully; tests cover stale tokens in `TelevisionTests.cs`.
