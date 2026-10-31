# Architecture

Rabbit Ears is one ASP.NET Core process that owns a television, and a handful
of screens that draw it. The server holds the only copy of the TV's state;
everything else sends button presses and renders what comes back. The
decisions behind that shape are recorded in [docs/adr/](adr/).

All diagrams use the same colour key as the other repositories in this
portfolio: people are grey, clients green, server processes orange, stored
data amber, delivery purple and outside programs red.

## System context

At home, the box is a Linux mini PC plugged into a TV. On the public demo the
same build runs in a container behind nginx, and a browser page plays the role
of the box's screen.

```mermaid
flowchart LR
  Viewer["Viewer on the sofa"] --> Flirc["Flirc USB receiver<br/>IR remote as key presses"]
  Viewer --> Phone["Phone remote<br/>/remote"]
  Flirc --> Screen
  Phone --> Box

  subgraph Home["Mini PC on the home network"]
    Box["Rabbit Ears service<br/>ASP.NET Core, .NET 10"]
    Screen["mpv full screen<br/>CRT shader + green OSD"]
    Media[("Media folders<br/>one per channel")]
    State[("state.json<br/>channels.json")]
    Box <-->|"JSON IPC socket"| Screen
    Box --> Media
    Box --> State
    Screen --> Media
  end

  Box --> Ffprobe["ffprobe<br/>episode lengths"]

  Visitor["Demo visitor"] --> Demo["rabbitears.nathanzimmerman.com<br/>/demo: TV, remote, QR code"]
  Demo --> Container["Same build in demo mode<br/>Docker behind nginx"]

  classDef user fill:#f8fafc,stroke:#475569,color:#0f172a
  classDef site fill:#e0f2fe,stroke:#0369a1,color:#0c4a6e
  classDef server fill:#ffedd5,stroke:#c2410c,color:#7c2d12
  classDef client fill:#dcfce7,stroke:#15803d,color:#14532d
  classDef data fill:#fef3c7,stroke:#b45309,color:#78350f
  classDef external fill:#fee2e2,stroke:#b91c1c,color:#7f1d1d
  class Viewer,Visitor user
  class Demo site
  class Box,Container server
  class Phone,Flirc client
  class Media,State data
  class Screen,Ffprobe external
```

## Containers and runtime

Inside the process, `Television` is a pure state machine with no disk, web or
threads; time and randomness are injected so tests can single-step it.
`TvService` wraps it with a lock, a background loop and SignalR. Either mpv
(on the box) or the Angular `/tv` page (laptop and demo) draws the picture.

```mermaid
flowchart LR
  subgraph Clients["Clients"]
    Remote["Phone remote<br/>web/src/app/remote"]
    TvPage["Browser TV page<br/>tube.ts + WebGL crt-shader.ts"]
    DemoPage["Demo page<br/>TV + remote + VCR"]
    Admin["Channel admin<br/>/admin"]
  end

  subgraph Api["RabbitEars.Api (one process)"]
    Hub["TvHub<br/>SignalR /hub/tv"]
    Service["TvService<br/>lock, rooms, broadcast"]
    Loop["Background loop<br/>tick, save, rescan"]
    AdminApi["AdminEndpoints<br/>same-origin writes"]
    Scanner["LibraryScanner<br/>folders to channels"]
    Catalog["ChannelCatalog<br/>names, numbers, tune-in"]
    MpvPlayer["MpvPlayer + MpvScreen<br/>+ MpvOsd"]
  end

  subgraph Core["RabbitEars.Core (pure)"]
    Television["Television<br/>state machine"]
    Deck["ShuffleDeck"]
  end

  Remote -->|"button presses"| Hub
  TvPage -->|"EpisodeEnded / Failed<br/>ReportPosition"| Hub
  DemoPage --> Hub
  Hub -->|"State snapshots"| Remote
  Hub -->|"State snapshots"| TvPage
  Hub --> Service
  Admin --> AdminApi --> Catalog
  Service --> Television --> Deck
  Loop --> Service
  Loop --> Scanner
  Service -->|"HomeChanged"| MpvPlayer
  MpvPlayer <-->|"IPC socket"| Mpv["mpv"]
  TvPage -->|"GET /media/{id}<br/>range requests"| Api
  Scanner --> Files[("Media root")]
  Service --> StateFile[("state.json")]
  Catalog --> CatalogFile[("channels.json")]

  classDef server fill:#ffedd5,stroke:#c2410c,color:#7c2d12
  classDef client fill:#dcfce7,stroke:#15803d,color:#14532d
  classDef data fill:#fef3c7,stroke:#b45309,color:#78350f
  classDef external fill:#fee2e2,stroke:#b91c1c,color:#7f1d1d
  class Remote,TvPage,DemoPage,Admin client
  class Hub,Service,Loop,AdminApi,Scanner,Catalog,MpvPlayer,Television,Deck server
  class Files,StateFile,CatalogFile data
  class Mpv external
```

## Signature flow: a button press becomes a picture

This is the path every remote button takes, and the reason the TV and the
phone can never disagree. Play tokens make late reports harmless.

```mermaid
sequenceDiagram
  autonumber
  actor Viewer
  participant Remote as Phone remote
  participant Hub as TvHub
  participant Service as TvService
  participant TV as Television
  participant Screen as TV page or mpv

  Viewer->>Remote: presses CH up
  Remote->>Hub: ChannelUp()
  Hub->>Service: Press(t => t.ChannelUp())
  Service->>Service: take the lock
  Service->>TV: ChannelUp()
  TV->>TV: next channel, new play token,<br/>Revision++, bannerSeq++
  Service->>TV: Snapshot()
  Service-->>Hub: State(snapshot) to every client
  Hub-->>Remote: snapshot (channel, volume, banner)
  Hub-->>Screen: snapshot (playing: token, episode, start)
  Note over Service,Screen: On the box, MpvScreen gets the same snapshot in-process
  Screen->>Screen: drop it if revision is older (same boot)
  Screen->>Screen: preload on the hidden player,<br/>seek to start, cut when it can play
  Screen->>Screen: flash the banner because bannerSeq moved

  Note over Screen,Hub: Minutes later the episode ends
  Screen->>Hub: EpisodeEnded(token)
  Hub->>Service: Press(t => t.EpisodeEnded(token))
  alt token is still current
    TV->>TV: roll to the next episode, new token
    Service-->>Screen: new snapshot
  else viewer already changed channel
    TV->>TV: stale token, nothing changes
    Note over Service: Revision unchanged, so nothing is broadcast
  end
```

The details that make this work:

- **One lock, one writer.** `TvService.Press` applies the command under a lock
  and broadcasts only when `Television.Revision` changed, so a no-op press
  costs nothing on the wire.
- **Play tokens.** Every "play this" instruction carries a unique token that
  the screen echoes in `EpisodeEnded`, `EpisodeFailed` and `ReportPosition`.
  A stale "ended" cannot skip an episode on a channel the viewer has left,
  while a stale position report still lands, which is how `Resume` works.
  On the box, mpv's playlist entry ids play the same part.
- **Sequence counters.** `bannerSeq`, `volumeSeq` and `message.seq` tick when
  that piece of the on-screen display should flash. Clients compare numbers;
  there are no separate "show the banner" events to lose.
- **Snapshot ordering.** Snapshots carry `boot` and `revision`;
  `web/src/app/core/tv-link.ts` drops an older revision from the same boot,
  and a new boot id resets the counter after a restart.
- **No dead frames.** `tube.ts` keeps two `<video>` elements: the hidden one
  loads the next episode muted and seeks to its start point, and the cut
  happens only once it can play.

## Demo mode: one TV per visitor

With `Box:Demo:Enabled`, `TvService` runs a `Television` per room instead of
one for the house. A room id is made up by the browser, so this isolates
visitors without being authentication (see
[ADR 0004](adr/0004-demo-rooms-are-not-authentication.md)).

```mermaid
flowchart LR
  VisitorA["Visitor A browser<br/>room id in localStorage"] --> HubD["TvHub<br/>joins SignalR group = room"]
  PhoneA["A's phone<br/>/remote?room=… from QR"] --> HubD
  VisitorB["Visitor B browser"] --> HubD
  HubD --> Rooms["TvService rooms<br/>one Television each"]
  Rooms --> RoomA["Room A TV"]
  Rooms --> RoomB["Room B TV"]
  RoomA -->|"snapshots to group A only"| VisitorA
  RoomA --> PhoneA
  RoomB -->|"group B only"| VisitorB
  Tapes["VCR tapes<br/>random ids + names only"] --> RoomA
  Clock["UTC broadcast clock<br/>shared schedule"] --> Rooms
  Idle["Idle after 15 min<br/>room switched off, nothing saved"] -.-> Rooms

  classDef user fill:#f8fafc,stroke:#475569,color:#0f172a
  classDef server fill:#ffedd5,stroke:#c2410c,color:#7c2d12
  classDef client fill:#dcfce7,stroke:#15803d,color:#14532d
  classDef data fill:#fef3c7,stroke:#b45309,color:#78350f
  class VisitorA,VisitorB user
  class PhoneA client
  class HubD,Rooms,RoomA,RoomB,Clock,Idle server
  class Tapes data
```

The demo page's VCR (`core/tape-deck.ts`) plays a visitor's own files from
blob URLs. The hub receives only random ids and names, `Tapes.cs` turns them
into extra channels for that room, and the files never leave the visitor's
browser. Channels with known lengths follow a repeating, UTC-anchored
playlist, so everyone tuned to a channel sees the same programme while channel,
volume and power stay per room.

## Delivery

```mermaid
flowchart LR
  PR["Pull request"] --> CI["GitHub Actions ci.yml"]
  CI --> Backend["backend<br/>dotnet test (xUnit)"]
  CI --> Web["web<br/>Playwright: real app + API,<br/>desktop and phone"]
  CI --> MediaPrep["media-preparation<br/>Python unittest + ffmpeg"]
  CI --> Image["demo-image<br/>build container, smoke test,<br/>verify release and byte ranges"]
  Backend & Web & MediaPrep & Image --> Main["merge to main"]
  Main --> Operator["Operator release on the VM<br/>demo/release-checklist.md"]
  Operator --> Site["rabbitears.nathanzimmerman.com<br/>nginx + HTTPS to 127.0.0.1:8620"]
  Main --> BoxDeploy["scripts/deploy.sh YOU@box<br/>self-contained publish + install.sh"]
  BoxDeploy --> Box["Mini PC<br/>systemd service + kiosk mpv"]

  classDef site fill:#e0f2fe,stroke:#0369a1,color:#0c4a6e
  classDef delivery fill:#f3e8ff,stroke:#7e22ce,color:#581c87
  classDef server fill:#ffedd5,stroke:#c2410c,color:#7c2d12
  class PR,CI,Backend,Web,MediaPrep,Image,Main,Operator,BoxDeploy delivery
  class Site site
  class Box server
```

Both releases are deliberate, manual steps. The demo is released by an operator
following [the release checklist](../demo/release-checklist.md), which keeps a
rollback point; the box is updated from a laptop with `scripts/deploy.sh`.

## Source boundaries

| Path | Owns |
| --- | --- |
| `src/RabbitEars.Core/` | TV behaviour with no I/O: `Television`, `ShuffleDeck`, `Tapes`, the snapshot records in `Models.cs` |
| `src/RabbitEars.Api/` | The process: hub, `TvService`, scanning, probing, channel catalog, admin endpoints, rate limiting, mpv driver |
| `src/RabbitEars.Api/mpv/` | mpv's config, remote keys and `crt.glsl`, the tube maths as an mpv hook shader |
| `web/src/app/` | Angular 22 app: `tv/`, `remote/`, `demo/`, `admin/`, and `core/` for the SignalR link and models |
| `tests/` | xUnit tests for Core and Api, and Python tests for the media tooling |
| `deploy/` | Runs on the box: systemd unit, kiosk launcher, installer |
| `scripts/` | Runs on a laptop: dev server, publish, deploy, media conversion and demo-media tooling |
| `demo/` | Hosting the public demo: compose files, nginx, release checklist |

`web/src/app/core/models.ts` mirrors `Models.cs` and the picture options by
hand; `ModelSyncTests` fails when they drift. `MpvOsd.cs` and `tv-screen.css`
lay out the green display on the same 4:3 frame and are kept in step the same
way.


## Public-demo resource boundaries

Local tape preflight reads metadata sequentially with cancellation; only IDs and
names cross SignalR. `LoadTapes` returns acceptance, allowing `TapeDeck` to keep
previous URLs until replacement succeeds. `tape-policy.ts` holds browser limits;
a backend contract test checks file/channel limits against `Tapes`.

`TvService.Join` admits demo rooms and connections under its existing lock.
Room creation has a bounded expiring address budget, and replacement has a room
budget. Direct `/media` requests have independent concurrency limits and a
shared read pacer (`DemoMediaBudget`), preserving standard single-range seeking.
The global HTTP/hub budget, bounded address partitions, and container caps limit
other work. See [operator limits and rollout checks](../demo/protection.md).
