# Rabbit Ears demo — production release and rollback checklist

This is an **operator-run** procedure for the existing shared GCP VM, not an automated deployment. CI validates disposable Docker containers only; it never touches the public site. The production host port stays **127.0.0.1:8620**, behind nginx/HTTPS, with container port 8080 and a 256 MiB container memory cap.

## 1. Preflight — before changing anything

Run these on the **VM** from `~/rabbit-ears/demo`. Record command outputs and the currently running image/commit as your rollback point:

```bash
date -Iseconds
git rev-parse HEAD
docker compose -f compose.yaml ps
docker inspect --format '{{.Image}}' "$(docker compose -f compose.yaml ps -q rabbit-ears)"
docker stats --no-stream
df -h
free -h
sudo nginx -t
curl -fsS http://127.0.0.1:8620/api/health
curl -fsS http://127.0.0.1:8620/api/library
curl -fsS http://127.0.0.1:8620/api/demo/credits
```

Verify the real public HTTPS address as well, and verify nginx still proxies SignalR WebSockets and HTTP media byte-range responses. The repo supplies `demo/nginx.conf`, but it is only a template: do not overwrite a working host configuration blindly. **Do not** forward port 8620 publicly; it must bind to localhost.

Check that there is enough disk space for the new video directory **plus** at least one previous release. Video egress, not RAM, may dominate cost. Avoid rebuilding Node/.NET/Docker on the 1–2 GiB shared VM if RAM and swap are insufficient; build the release image elsewhere if needed.

## 2. Stage media immutably

On your development machine, use the [media rights review](../docs/media-rights.md) and [media preparation](../docs/media-preparation.md) workflows. Prepare a **new** versioned directory, not the existing production media root. The public release **must** contain approved version-2 `.demo-media.json` and only the clips referenced by it.

Transfer the complete prepared directory (including dotfiles) into a new VM directory, e.g. `/srv/rabbitears-demo/releases/library-2026-10-a/`. Do not transfer original unreviewed files, the private manifest, or the private rights ledger. Ensure directories are traversable and files readable by the Docker container's unprivileged user.

On the VM, before switching the container:

```bash
python3 scripts/verify-demo-release.py --help   # available after repository update
ls -la /srv/rabbitears-demo/releases/library-2026-10-a/
```

The release verifier checks both the staged directory and the *running* API, so run its full verification **after** mounting the intended new library. Keep the prior directory untouched until validation is complete.

## 3. Record configuration and deploy safely

In `~/rabbit-ears/demo/.env`, record the old values and set:

```dotenv
RABBIT_EARS_DEMO_PORT=8620
RABBIT_EARS_DEMO_MEDIA=/srv/rabbitears-demo/releases/library-2026-10-a
```

Confirm the path is a real directory. Inspect the composed configuration before applying it:

```bash
docker compose -f compose.yaml -f compose.media.yaml config --quiet
docker compose -f compose.yaml -f compose.media.yaml config
```

The combined configuration **must** have a read-only bind mount at `/srv/rabbitears/media` with `Box__Demo__RequireApprovedMedia=true`, and bind host port 8620 to `127.0.0.1`. The extra override is important: an ordinary `docker compose up` would lose the media mount. Use an image you've built and verified separately, then:

```bash
docker compose -f compose.yaml -f compose.media.yaml up -d --no-build
```

This recreates the container when the mount changes, but leaves nginx, other apps, and Docker volumes alone. No source-video download occurs on the VM.

## 4. Post-deploy smoke and manual verification

With `RABBIT_EARS_DEMO_MEDIA` pointing to the **active** release:

```bash
python3 scripts/verify-demo-release.py \
  --base-url http://127.0.0.1:8620 \
  --media-root "$RABBIT_EARS_DEMO_MEDIA" \
  --expect-channels 5 \
  --expect-clips 12
```

If you have fewer than 12 fully cleared clips, substitute the **actual** approved counts; it is better to have fewer channels than to publish uncertain media. The tool is read-only: it checks `/api/health`, demo mode, public admin isolation, channel counts, public credits, staged approval metadata, stale files and HTTP 206 Range responses. It does not pretend to verify rights independently.

Then visit the real HTTPS demo in a desktop browser and a phone. Verify playable picture and unlocked sound, QR controls the same room, channel changes, clip rollover, client reconnection, separate visitor rooms, credits, and no horizontal scrolling. Check a limited-bandwidth session if practical; the release verifier does **not** replace visual or audio checks. Review `docker stats --no-stream`, `docker logs`, disk space and nginx logs after deployment.

## 5. Recovery

If any critical check fails, stop using the new library and **restore the previous immutable release path** in `.env`, then run:

```bash
docker compose -f compose.yaml -f compose.media.yaml up -d --no-build --force-recreate
python3 scripts/verify-demo-release.py \
  --base-url http://127.0.0.1:8620 \
  --media-root /path/to/previous/approved/library
```

To fall all the way back to the generated **unlicensed test-pattern** default, explicitly omit the media override and recreate the container:

```bash
docker compose -f compose.yaml up -d --no-build --force-recreate
python3 scripts/verify-demo-release.py --base-url http://127.0.0.1:8620
```

The generated-only fallback must not require a mounted prepared library. Verify the actual image/port/proxy after rollback; do not delete either release until all checks pass.

To roll back code **as well as media**, restore the previously recorded Git commit or image tag and match the same Compose configuration to the version of metadata it supports. Do not run destructive `docker system prune` on the shared VM as part of recovery.

## 6. CI evidence and release status

Every PR should pass:

- `dotnet test`: deterministic TV/domain, contract and metadata checks
- Media preparation tests: generated videos, rights approval, trim/codec tests once Phase 3 merges
- Playwright Chromium: desktop and phone UX, actual browser decoding and room isolation
- Docker image smoke: generated default clips and redirect/API health
- **Prepared-media integration:** a disposable rights-free test pattern is converted by `prepare-demo-media.py`, mounted read-only with strict approval, and checked via the release verifier for credits and actual 206 byte ranges

CI does **not** assert successful real GCP VM deployment, physical mpv/Flirc, Safari/Firefox, legal clearance, or actual home-network behavior. Record those separately. Keep the code PR separate from any media-content publication.
