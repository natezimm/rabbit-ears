# The demo site

The public demo runs the [`Dockerfile`](../Dockerfile) at the repository root:
the whole app in one container, in demo mode, with clips generated at build
time. Every visitor gets a TV of their own and nothing is saved, so it needs no
volume and no settings. CI builds and smoke-tests the image on every change, including a disposable read-only mount containing an approved **generated** fixture. No third-party video is downloaded or added to the image.

## Run it on a server

These steps assume a Linux VM with Docker (with the compose plugin), git and
nginx, and a domain or subdomain pointed at it.

1. Build and start it from a checkout. The build installs Node, .NET and ffmpeg
   inside Docker, so nothing else is needed on the server:

   ```bash
   git clone https://github.com/natezimm/rabbit-ears.git ~/rabbit-ears
   cd ~/rabbit-ears/demo
   docker compose up -d --build
   curl http://127.0.0.1:8620/api/health    # {"status":"on the air"}
   ```

2. Add [`nginx.conf`](nginx.conf) as a site, with your domain as `server_name`,
   then reload nginx and turn on HTTPS:

   ```bash
   sudo nginx -t && sudo systemctl reload nginx
   sudo certbot --nginx -d rabbitears.example.com
   ```

   The two `Upgrade`/`Connection` lines matter: the TV and remote use a
   WebSocket. With Caddy instead of nginx, `reverse_proxy 127.0.0.1:8620`
   handles WebSockets and HTTPS by itself.
3. Make sure the VM's firewall allows HTTP and HTTPS (on Google Cloud, the
   instance's "Allow HTTP/HTTPS traffic" boxes).

To update after a merge: `git pull && docker compose up -d --build` in `demo/`.

Compose defaults to host port **8620**; set `RABBIT_EARS_DEMO_PORT` to change it
and update nginx's upstream to match. The container still listens on 8080.

## Use real content

Select footage produced in **1980–1999 (inclusive)**, with clear picture quality and
audible original sound. Verify the exact video and soundtrack before release.

[real-media.md](real-media.md) covers the source/rights manifest, laptop-side
preparation, read-only library mount, credits, and rollback. No application
rebuild is needed to replace media once this version is installed. When using
the media override, include both Compose files in update commands as documented
there. Use the [release checklist](release-checklist.md) to stage a new immutable library, run the [release verifier](../scripts/verify-demo-release.py) and roll back without disturbing the other apps on the VM. The [implementation plan](../docs/archive/channel-and-demo-plan.md) covers this
and the home channel editor. The rights workflow is in
[media rights](../docs/media-rights.md).

## Shared broadcast schedule

Demo Live channels with known durations use a repeating, UTC-anchored playlist:
new visitors and returning viewers join the same scheduled program position.
Channel, volume, mute and power controls remain isolated per visitor room.
`Box__Demo__SynchronizedBroadcast=false` restores the per-room shuffle. Home
playback is unchanged.

## Size

Running, the container idles at about 25 MB of memory and measured about 45 MB
with 30 visitors each on their own TV; `compose.yaml` caps it at 256 MB. Idle
TVs are switched off after 15 minutes and at most 100 run at once. The image is
about 450 MB on disk. Each visitor streams video at up to about 1.2 Mbit/s,
which is the cost worth watching on a metered VM.

Building is the heavy part: the Angular and .NET builds can briefly want a
gigabyte or more. On a small VM that already runs other sites, add swap first
if it has none (`sudo fallocate -l 2G /swapfile && sudo chmod 600 /swapfile &&
sudo mkswap /swapfile && sudo swapon /swapfile`).

## Try it locally

```bash
docker build -t rabbit-ears . && docker run --rm -p 8080:8080 rabbit-ears
```

Then open <http://localhost:8080>.

## Resource protection

The demo has bounded local tape loading, room/connection admission and hosted-media
traffic. See [limits and deployment checks](protection.md) before rolling out the
trial CPU/PID budgets on a shared VM.
