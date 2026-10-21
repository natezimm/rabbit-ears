# Browser regression tests

The Playwright suite exercises the built Angular app and the real ASP.NET Core
API in Chromium at desktop and 390px phone widths. Every test starts its own
application on an automatically allocated localhost port, with temporary media
and state directories. Servers are stopped and directories removed after each
test, including failures. Tests never connect to the home box or public site.

## Run locally

Install .NET 10 (the SDK in `global.json`) and the Node version in `.nvmrc`.
Then, from `web/`:

```bash
npm ci
npx playwright install chromium
npm run test:browser
```

On Linux, use `npx playwright install --with-deps chromium` if browser system
libraries are missing. `test:browser` builds the web app and Release API,
type-checks the tests, and runs both browser projects. Install ffmpeg for demo
fixtures: they generate a small H.264/AAC video locally, without downloading
media. Management tests still use placeholder files. All fixtures disable
probing and select the browser player; mpv is not required.

To limit a run or see the browser:

```bash
npm run test:browser -- --project=desktop-chromium
npm run test:browser -- --project=phone-chromium --headed
```

After building, `npx playwright test` runs only the tests. Use
`npx playwright test --list` to see the test cases, and
`npx playwright show-report` to open the latest report.

## Coverage

Each scenario runs at both screen widths:

- Rename, assign a channel number, change a tune-in mode, and verify persistence
  after restarting the actual server.
- Update a connected phone remote's lineup through SignalR without reloading.
- Exclude seasons/episodes, remove all eligible episodes, and restore a channel.
- Hide/restore the full lineup without deleting media.
- Keep edits after a real stale-version conflict in another tab; discard/reload
  explicitly to recover the saved lineup.
- Show server validation errors and successfully save a corrected lineup.
- Preserve a draft after a simulated network failure, then retry against the
  actual API. This is the only scenario that intercepts a management request.
- Block rescanning while edits are pending, then discover newly added media.
- Keep long channel and episode names within the editor/remote viewport.
- Show the public-demo unavailable state, omit the management link from its
  remote, and verify read/write management requests return 404.

Demo onboarding also covers real generated video playback, blocked audio and
sound activation, server mute, keyboard activation/shortcuts, direct channel
selection, phone-first room control, room isolation, unavailable connections,
and desktop/phone layout. Audio denial is simulated at the media play API until a trusted local gesture;
decoding and playback still use the real generated video. Layout screenshots are attached for both widths.

Unexpected page errors fail the test. There are no fixed sleeps or automatic
retries; state changes are awaited through visible UI or real responses.

## CI artifacts

The `web` job installs Chromium and its system dependencies and runs the full
suite on pull requests and main. It uploads `browser-test-results` for seven
days with the HTML report, captured layout screenshots, and (on failure)
screenshots, video, trace and application log attachments. Open the HTML report
or run `npx playwright show-trace <path/to/trace.zip>` to inspect a failure.

This catches behavioral regressions and horizontal overflow. It does not claim
Safari/Firefox coverage, pixel-perfect screenshot comparisons, all media codecs,
or testing the actual IR remote and mini PC hardware.
