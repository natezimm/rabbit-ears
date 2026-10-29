# 4. Demo rooms isolate visitors but are not authentication

Status: accepted

## Context

At home there is one TV and, by design, no accounts, logins or pairing: it is
for the home network only. A public demo needs every visitor to get a TV
nobody else can change, without adding sign-in to a project whose point is
that it has none.

## Decision

In demo mode (`Box:Demo:Enabled`) `TvService` runs one `Television` per room.
The browser makes up a room id (kept in `localStorage`, or taken from
`?room=` when a phone scans the QR code), the hub joins the matching SignalR
group, and snapshots go to that group only. Nothing is saved, idle rooms are
switched off after 15 minutes, `/api/rescan` and the admin APIs are off, and
requests and hub calls are rate limited per address. A visitor's own videos
in the VCR stay in their browser as blob URLs; the hub sees only random ids
and names.

At home the room id is ignored.

## Consequences

- Visitors are isolated in practice, and the home build keeps zero auth code.
- A room id is a capability anyone holding it can use, which is fine for a
  toy TV and is why it must never guard anything that matters. Demo mode must
  not grow into authentication; if something ever needs protecting, that is a
  new decision.
- Room count and memory are bounded by idle cleanup and rate limits rather
  than by accounts.
