# 1. The server is the only source of truth

Status: accepted

## Context

A TV has several things that can change what is on: an IR remote, a phone
remote, an episode ending, a restart. If the TV page and the phone remote each
kept their own idea of the channel and volume, a lost message or a second
remote would leave them disagreeing, and a restart would forget where every
channel was.

## Decision

One ASP.NET Core process owns the television. Clients send button presses only
and render whatever full `TvSnapshot` comes back over SignalR. The behaviour
itself lives in `RabbitEars.Core/Television.cs`, a pure state machine with
injected time and randomness and no disk, web or threads. `TvService` applies
each press under one lock, broadcasts only when `Revision` changed, and a
background loop ticks the machine, saves `state.json` when dirty and rescans
the media folder.

## Consequences

- The TV page and every remote always agree; a client that reconnects simply
  takes the next snapshot.
- TV behaviour is unit tested by single-stepping `Television`
  (`TelevisionTests.cs`) with no web host or real clock.
- Full snapshots cost a little more on the wire than deltas, which does not
  matter on a home network with a handful of clients.
- The server is a single point of failure, which is acceptable for one box in
  one living room.
