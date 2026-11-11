# Observability

Rabbit Ears answers two health endpoints and, when told where to send them, OpenTelemetry metrics, traces and
logs ([ADR 0006](adr/0006-opentelemetry-off-unless-asked.md)). Nothing is sent by default: a box on a home network
stays quiet unless its owner asks.

## Health

| Endpoint | Answers |
| --- | --- |
| `GET /api/health` | `{"status":"on the air"}` while the process is up. Cheap; the kiosk and smoke tests use it. |
| `GET /health` | Each check and what it measured. 200 for Healthy and Degraded, 503 for Unhealthy. |

`/health`'s checks:

- `library`: Healthy with at least one channel; Degraded with none, when the TV shows colour bars. Its data
  gives the channel, episode and needs-conversion counts, and on the demo the televisions and connections.
- `player`: on the box, whether mpv's control socket is open (Degraded while the kiosk restarts it); with the
  browser as the screen, always Healthy.

It never names the media folder, a show, a room or an address.

## Metrics, traces and logs

| Metric | Type | What |
| --- | --- | --- |
| `rabbitears.televisions` | gauge | Televisions on: one at home, one per visitor room on the demo |
| `rabbitears.connections` | gauge | Screens and remotes connected to the hub |
| `rabbitears.changes` | counter | State changes broadcast since the server started |
| `rabbitears.rejections` | counter | Requests the demo's protections refused, by `reason` |

ASP.NET Core's request metrics, SignalR's connection metrics and the .NET runtime's come too. Traces cover API
calls except `/media`, `/hub` and `/health`, a quarter of them (`Telemetry:TraceSampleRatio`). Logs carry their
trace ids.

## Turning it on

For the demo, add to `demo/.env` beside `compose.yaml` and run `docker compose up -d` there:

```sh
OTEL_EXPORTER_OTLP_ENDPOINT=https://otlp-gateway-<region>.grafana.net/otlp
OTEL_EXPORTER_OTLP_HEADERS=Authorization=Basic%20<base64 of instance id:token>
```

Grafana Cloud shows both under **Connections → OpenTelemetry (OTLP)**; its free tier is far more than the demo
needs. For a box, uncomment the same lines in `/etc/rabbitears/box.env` and `sudo systemctl restart rabbitears`.
Any OTLP receiver works, such as a local .NET Aspire dashboard
(`docker run --rm -p 18888:18888 -p 4318:18890 mcr.microsoft.com/dotnet/aspire-dashboard:13.6.5`, then
`OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4318`).
