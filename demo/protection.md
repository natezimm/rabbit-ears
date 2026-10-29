# Public demo resource limits

The VCR accepts up to **5 videos on 3 local channels**, including CH 00. Each
video must be at most **30 minutes and 500 MB**, with **1 GB total** (decimal
units). The browser checks sizes before opening metadata and probes one video
at a time, with a 10-second timeout and Cancel. Folder drops stop after 1,000
entries (files and directories), 8 folder levels or 10 seconds. File-picker
results also have count/depth bounds; the browser's native picker itself is
outside application control. Oversized selections are rejected as a whole.

Video bytes never reach the server. Size/duration checks protect the visitor's
device, not the VM. The server independently validates IDs, titles, 5 files,
3 channels and the SignalR message-size limit. The existing tapes stay available
through preflight and server rejection. The two-player renderer still owns
seeking and playback; no transcoding is added. A codec can pass metadata checks
and fail during playback; the VCR shows a conversion/eject suggestion and the
existing Television failure limit prevents an endless loop.

## Server budgets

All of these apply in demo mode unless stated otherwise:

| Resource | Default |
| --- | --- |
| Rooms | 10, preserving connected rooms at capacity |
| Room cleanup | 5 minutes after the last connection leaves |
| Accepted hub connections | 30 global, 3 per room |
| Room creation | 5 attempts/minute/address; 1,024 expiring address entries |
| Tape replacement | 6 accepted replacements/minute/room, including restoration |
| Hosted-media responses | 20 global, 4 per address, no waiting queue |
| Aggregate hosted-media reads | 2,500,000 bytes/sec (20 Mbit/sec), 32 KiB chunks |
| HTTP/hub activity | 100/sec global, burst 500, plus configured per-address rate |
| Kestrel sockets | 100 ordinary, 30 upgraded; 10-second header timeout |
| Request bodies | 32 KiB |
| Container | 256 MB RAM, 0.5 CPU, 128 PIDs |
| Container logs | Three 10 MB files |

Room, connection and media defaults are `Box:Demo` settings. Ordinary rate
settings remain `Box:RateLimit`. Address rate partitions are hashed into 1,024
buckets (including at home); collisions share an allowance instead of allowing
unbounded memory growth. IPv6 clients share a /64 key. Loopback remains exempt
from the address limiter, but not the demo-wide budget. Startup room creation
has no HTTP path: `/api/state` only reads existing rooms; `TvHub.OnConnectedAsync`
is the creation path. Failed joins and disconnects release their accounting.

The media pacer is shared across all streams, not one limit per request. It
preserves single byte ranges and HEAD; multiple/oversized range headers get
416. Stream exhaustion gets 429 with Retry-After. Read pacing allows one chunk
of burst and normal network buffering; it is not a packet-level traffic shaper.
The preparation script targets at most 1,000 kbit/sec video plus 96 kbit/sec
audio, so 20 Mbit/sec is an initial budget for 10 viewers with seek headroom.
**Measure the actual mounted clips and staging playback before release.**

`RabbitEars.Protection` exposes `rabbitears.rejections` via .NET Metrics with
fixed reason labels. It records no IPs, room IDs, titles or payloads. Existing
health checks remain lightweight. No public diagnostics endpoint is added.

## Deployment verification (not performed by merging)

These are trial budgets. A passing test suite does not establish that a shared
VM can support the proposed load. Use a separate staging instance/container;
do not load-test the public site or build alongside another portfolio project.

1. Preserve the old image and effective Compose/proxy configuration. Ensure
   enough free disk for both images. Runtime CPU/RAM caps do not constrain image
   builds. Do not delete unrelated Docker volumes/images to make space.
2. Measure every mounted clip using `ffprobe -v error -show_entries
   format=duration,bit_rate -of json FILE`. Compare average bitrate and seek
   bursts to the 20 Mbit/sec total. Lower supported concurrency if needed.
3. Run the full backend and browser suites. On desktop and a real phone, try
   five tapes, corrupt files, cancellation, rejected replacement, eject/reinsert,
   reconnect and phone control. The automated 30-minute fixture seeks near the
   end and back; it does **not** replace a real 30-minute playback soak.
4. In staging, watch 10 rooms at once, surf channels, then exceed room/connection
   and direct-media caps, including aborted downloads. Existing rooms must keep
   working. Observe health latency, CPU, memory, PID count and transmitted bytes
   with `docker stats` and the host's existing tools. Target peak memory under
   200 MB inside the 256 MB cap. Record the measurements; lower concurrency or
   fix allocations if it fails. Do not silently raise the VM memory budget.
5. After deployment, inspect the running container: `docker inspect` must show
   Memory=268435456, NanoCpus=500000000, PidsLimit=128 and the configured log
   rotation. Verify Compose still publishes only 127.0.0.1:8620. Keep the
   backend inaccessible directly; nginx must overwrite X-Forwarded-For.
   `TrustForwardedFor` is only safe with this isolated proxy topology.
6. Check nginx log rotation with the host's existing logrotate setup; Docker
   rotation does not rotate nginx logs. Smoke-test Rabbit Ears, Know Ball and
   Wheelhouse. Record date, image ID, settings, measurements and results here
   or in the release record.
7. Roll back to the saved image/config if checks fail. For unexpected traffic,
   stop only the Rabbit Ears service using the same Compose files used to start
   it (`docker compose ... stop rabbit-ears`). Leave the other projects running.

Application caps cannot eliminate egress charges or network-level attacks.
No autoscaling, upload service, accounts or paid monitoring are introduced.
Production rollout, measured staging load and the manual soak remain pending.
