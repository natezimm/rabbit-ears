# 0006. OpenTelemetry is off unless an endpoint is set

**Status:** accepted

## Context

The box lives on a home network and must never phone home on its own. The public demo runs on a small shared
VM, where it is useful to see how many televisions are on, how many visitors are connected and what the
protections turn away. `/api/health` only said the process was up.

## Decision

- The API registers OpenTelemetry only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (in `demo/.env` or
  `/etc/rabbitears/box.env`). Without it nothing is loaded and nothing is sent.
- Metrics are gauges read on export (`rabbitears.televisions`, `rabbitears.connections`, `rabbitears.changes`)
  plus the existing `rabbitears.rejections`, with no room ids, addresses or file names.
- Traces leave out `/media` (many range requests a minute), `/hub` and `/health`, and sample a quarter of the
  rest. Logs go out with their trace ids; the demo's console writes JSON.
- `/health` reports each check (`library`: channels found, `player`: mpv's socket on the box), 200 while
  Degraded and 503 only when Unhealthy. `/api/health` stays the cheap liveness answer.

## Consequences

- A box owner who wants the numbers adds three lines to `box.env`; nobody else is affected.
- With it on, the SDK adds roughly 25 MB, inside the demo's 256 MB limit.
- The demo's uptime check can tell "running but showing colour bars" (Degraded) from "down".
