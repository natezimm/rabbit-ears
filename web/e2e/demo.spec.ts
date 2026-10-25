import { test, expect } from './fixtures';
import { copyFile, mkdir, readFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';

test.use({ demoMode: true });

// Headless Chromium policies differ across platforms. Enforce only the denial
// at the browser API boundary; metadata, decoding, seeking and playback are real.
test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    let activated = false;
    for (const type of ['pointerdown', 'keydown'])
      document.addEventListener(type, event => { if (event.isTrusted) activated = true; }, true);
    const play = HTMLMediaElement.prototype.play;
    HTMLMediaElement.prototype.play = function () {
      if (!this.muted && !activated)
        return Promise.reject(new DOMException('User gesture required', 'NotAllowedError'));
      return play.call(this);
    };
  });
});

test('first visit shows the live guide, sound action and responsive controls', async ({ page, box }, info) => {
  await page.goto(`${box.url}/demo`);
  const guide = page.getByRole('region', { name: "What's on / Jump to channel" });
  await expect(guide.getByRole('button', { name: /CH 02 Arthur/ })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Enable sound', exact: true })).toBeVisible();
  await expect.poll(() => page.locator('video.live').evaluateAll(videos =>
    videos.some(video => (video as HTMLVideoElement).videoWidth > 0 && !(video as HTMLVideoElement).paused))
  ).toBeTruthy();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
  if (info.project.name.startsWith('phone')) {
    await expect(page.getByRole('img', { name: 'QR code for the phone remote' })).toBeHidden();
    await expect(page.locator('.phone-prompt')).toBeHidden();
  } else {
    await expect(page.getByRole('img', { name: 'QR code for the phone remote' })).toBeVisible();
  }
  await info.attach('demo-first-visit', { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });
  await page.getByRole('button', { name: 'Enable sound', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Enable sound', exact: true })).toHaveCount(0);
  await expect.poll(() => page.locator('video.live').evaluateAll(videos =>
    videos.some(video => !(video as HTMLVideoElement).muted && !(video as HTMLVideoElement).paused))
  ).toBeTruthy();
  const movies = guide.getByRole('button', { name: /CH 03 Movies/ });
  await movies.click();
  await expect(movies).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('.lcd-big')).toContainText('CH 03');
});

test('phone remote updates this room without unlocking desktop audio or another room', async ({ page, browser, box }) => {
  await page.goto(`${box.url}/demo?room=desktop-room`);
  const sound = page.getByRole('button', { name: 'Enable sound', exact: true });
  await expect(sound).toBeVisible();
  const other = await browser.newContext();
  try {
    const remote = await other.newPage();
    await remote.goto(`${box.url}/remote?room=desktop-room`);
    const stranger = await other.newPage();
    await stranger.goto(`${box.url}/remote?room=separate-room`);
    await expect(stranger.locator('.lcd-big')).toContainText('CH 02');
    await remote.getByRole('button', { name: /03 Movies/ }).click();
    await expect(page.locator('.lineup button[aria-pressed="true"]')).toContainText('CH 03');
    await expect(sound).toBeVisible();
    await expect.poll(() => page.locator('video.live').evaluateAll(v => v.length === 1 && (v[0] as HTMLVideoElement).muted)).toBeTruthy();
    await expect(stranger.locator('.lcd-big')).toContainText('CH 02');
  } finally { await other.close(); }
});

test('keyboard activates focused controls and retains TV shortcuts', async ({ page, box }) => {
  await page.goto(`${box.url}/demo`);
  const sound = page.getByRole('button', { name: 'Enable sound', exact: true });
  await expect(sound).toBeVisible();
  await sound.focus();
  await page.keyboard.press('Enter');
  await expect(sound).toHaveCount(0);
  const movies = page.locator('.lineup').getByRole('button', { name: /CH 03 Movies/ });
  await movies.focus();
  await page.keyboard.press('Space');
  await expect(movies).toHaveAttribute('aria-pressed', 'true');
  await page.evaluate(() => (document.activeElement as HTMLElement)?.blur());
  await page.keyboard.press('ArrowDown');
  await expect(page.locator('.lineup button[aria-pressed="true"]')).toContainText('CH 02');
  await page.keyboard.press('p');
  await expect(page.locator('.lcd-big')).toContainText('STANDBY');
  await page.keyboard.press('p');
  await expect(page.locator('.lcd-big')).toContainText('CH 02');
});

test('unlock respects mute set by the phone remote', async ({ page, context, box }) => {
  await page.goto(`${box.url}/demo?room=muted-room`);
  await expect(page.getByRole('button', { name: 'Enable sound', exact: true })).toBeVisible();
  const remote = await context.newPage();
  await remote.goto(`${box.url}/remote?room=muted-room`);
  await remote.getByRole('button', { name: 'Mute', exact: true }).click();
  await expect(page.locator('.lcd-volume')).toHaveAttribute('aria-label', 'Muted');
  await page.getByRole('button', { name: 'Enable sound', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Enable sound', exact: true })).toHaveCount(0);
  await expect.poll(() => page.locator('video.live').evaluateAll(v => v.length === 1 && (v[0] as HTMLVideoElement).muted)).toBeTruthy();
  await remote.getByRole('button', { name: 'Mute', exact: true }).click();
  await expect.poll(() => page.locator('video.live').evaluateAll(v => v.length === 1 && !(v[0] as HTMLVideoElement).muted)).toBeTruthy();
});

test('shows a connection message when the TV cannot be reached', async ({ page, box }) => {
  await page.route('**/hub/tv**', route => route.abort());
  await page.goto(`${box.url}/demo`);
  await expect(page.locator('.lineup').getByRole('status')).toHaveText('Connecting to your TV…');
  await expect(page.locator('.lineup button')).toHaveCount(0);
});

test('HTTP range seeking decodes real H.264/AAC media before and after channel changes', async ({ page, box }) => {
  await page.goto(`${box.url}/demo?room=codec-check`);
  // A new demo room is registered by the SignalR handshake, not by page navigation.
  // Wait for the server-authoritative guide before probing this room's API state.
  await expect(page.locator('.lineup').getByRole('button', { name: /CH 02 Arthur/ })).toBeVisible();
  const initial = await box.api.get('/api/state?room=codec-check');
  expect(initial.ok()).toBeTruthy();
  const state = await initial.json();
  expect(state.playing?.episodeId).toBeTruthy();
  const range = await box.api.get(`/media/${state.playing.episodeId}`, {
    headers: { Range: 'bytes=0-63' },
  });
  expect(range.status()).toBe(206);
  expect(range.headers()['content-range']).toMatch(/^bytes 0-63\/\d+$/);
  expect((await range.body()).length).toBe(64);
  const video = page.locator('video.live');
  await expect.poll(() => video.evaluate((element: HTMLVideoElement) =>
    element.readyState >= 2 && element.videoWidth > 0 && !element.paused)).toBeTruthy();
  await video.evaluate((element: HTMLVideoElement) => { element.currentTime = 10; });
  await expect.poll(() => video.evaluate((element: HTMLVideoElement) => element.currentTime)).toBeGreaterThan(8);
  await page.locator('.lineup').getByRole('button', { name: /CH 03 Movies/ }).click();
  await expect(page.locator('.lcd-big')).toContainText('CH 03');
  await expect.poll(() => video.evaluateAll((videos: HTMLVideoElement[]) =>
    videos.some(v => v.videoWidth > 0 && v.readyState >= 2 && !v.paused))).toBeTruthy();
});

test('videos put in the VCR play from this browser on their own channel, and eject takes them out', async ({ page, box }) => {
  await page.goto(`${box.url}/demo?room=vcr-room`);
  const guide = page.getByRole('region', { name: "What's on / Jump to channel" });
  await expect(guide.getByRole('button', { name: /CH 02 Arthur/ })).toBeVisible();

  const uploads: string[] = [];
  page.on('request', request => { if (request.method() !== 'GET' || request.url().includes('Home')) uploads.push(request.url()); });
  await page.locator('input[type=file]:not([webkitdirectory])').setInputFiles({
    name: 'Home movie.mp4',
    mimeType: 'video/mp4',
    buffer: await readFile(join(box.root, 'fixture.mp4')),
  });

  const vcr = guide.getByRole('button', { name: /CH 00 VCR/ });
  await expect(vcr).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByRole('status').filter({ hasText: '1 video on 1 channel' })).toBeVisible();
  await expect.poll(() => page.locator('video.live').evaluateAll(videos =>
    videos.some(video => (video as HTMLVideoElement).src.startsWith('blob:') && (video as HTMLVideoElement).videoWidth > 0))
  ).toBeTruthy();
  expect(uploads.filter(url => !url.includes('/hub/'))).toEqual([]);

  await page.getByRole('button', { name: 'Eject' }).click();
  await expect(vcr).toHaveCount(0);
  await expect(guide.locator('button[aria-pressed="true"]')).not.toContainText('CH 00');
});

test('a folder of show folders loads as one channel per show, with loose files on the VCR', async ({ page, box }) => {
  const shows = join(box.root, 'Shows');
  for (const path of ['Cheers/Season 1/Pilot.mp4', 'Cheers/Season 2/Next.mp4', 'Home videos/Beach.mp4', 'Loose.mp4', 'notes.txt']) {
    await mkdir(dirname(join(shows, path)), { recursive: true });
    await copyFile(join(box.root, 'fixture.mp4'), join(shows, path));
  }
  await page.goto(`${box.url}/demo?room=folder-room`);
  const guide = page.getByRole('region', { name: "What's on / Jump to channel" });
  await expect(guide.getByRole('button', { name: /CH 02 Arthur/ })).toBeVisible();

  await page.locator('input[webkitdirectory]').setInputFiles(shows);

  await expect(guide.getByRole('button', { name: /CH 00 VCR/ })).toHaveAttribute('aria-pressed', 'true');
  await expect(guide.getByRole('button', { name: /Cheers.*2 episodes/ })).toBeVisible();
  await expect(guide.getByRole('button', { name: /Home videos.*1 episode/ })).toBeVisible();
  await expect(page.getByRole('status').filter({ hasText: '4 videos on 3 channels; 1 other files skipped' })).toBeVisible();
});
