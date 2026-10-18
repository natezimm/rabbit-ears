import { spawn, type ChildProcess } from 'node:child_process';
import { once } from 'node:events';
import { mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { test as base, expect, type APIRequestContext, type Page, type PlaywrightWorkerArgs } from '@playwright/test';

type Playwright = PlaywrightWorkerArgs['playwright'];

const apiRoot = fileURLToPath(new URL('../../src/RabbitEars.Api/', import.meta.url));
const apiDll = join(apiRoot, 'bin', 'Release', 'net10.0', 'RabbitEars.Api.dll');
export const longTitle = 'The extraordinarily long episode title that used to push the phone remote beyond the edge of the screen';

/** Each test owns an actual application process and its disposable library/state. */
export class TestBox {
  url = '';
  api!: APIRequestContext;
  log = '';
  private child?: ChildProcess;

  constructor(readonly root: string, private readonly playwright: Playwright, private readonly demo: boolean) {}

  get mediaRoot(): string { return join(this.root, 'media'); }

  async addMedia(relative: string): Promise<void> {
    const path = join(this.mediaRoot, relative);
    await mkdir(join(path, '..'), { recursive: true });
    // These tests exercise admin/remote behavior, not decoding. Disable ffprobe
    // and mpv rather than installing a real personal media library.
    await writeFile(path, 'Browser test fixture; not a video.');
  }

  async start(): Promise<void> {
    this.child = spawn('dotnet', [apiDll, '--urls', 'http://127.0.0.1:0'], {
      cwd: apiRoot,
      env: {
        ...process.env,
        ASPNETCORE_ENVIRONMENT: 'Production',
        Box__MediaRoot: this.mediaRoot,
        Box__StateDir: join(this.root, 'state'),
        Box__Demo__Enabled: String(this.demo),
        Box__Player__Kind: 'browser',
        Box__Ffprobe: '',
        Box__RescanMinutes: '0',
        Box__Tv__PowerOffAtZeroVolume: 'false',
      },
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    await new Promise<void>((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error(`App startup timed out.\n${this.log}`)), 20_000);
      const finish = (error?: Error): void => {
        clearTimeout(timeout);
        if (error) reject(error); else resolve();
      };
      this.child!.on('error', error => finish(error));
      this.child!.on('exit', (code, signal) => finish(new Error(`App exited (${code ?? signal}).\n${this.log}`)));
      this.child!.stderr!.on('data', chunk => { this.log += chunk.toString(); });
      this.child!.stdout!.on('data', chunk => {
        this.log += chunk.toString();
        const address = this.log.match(/Now listening on: (http:\/\/127\.0\.0\.1:\d+)/);
        if (address) {
          this.url = address[1];
          finish();
        }
      });
    });
    this.api = await this.playwright.request.newContext({ baseURL: this.url });
    expect((await this.api.get('/api/health')).ok()).toBeTruthy();
  }

  async stop(): Promise<void> {
    await this.api?.dispose();
    const child = this.child;
    if (!child?.pid || child.exitCode !== null || child.signalCode !== null) return;
    const exited = once(child, 'exit');
    child.kill('SIGTERM');
    const timeout = setTimeout(() => child.kill('SIGKILL'), 5_000);
    try { await exited; } finally { clearTimeout(timeout); }
    this.child = undefined;
  }

  async restart(): Promise<void> {
    await this.stop();
    this.log = '';
    await this.start();
  }
}

export const test = base.extend<{ box: TestBox; demoMode: boolean; runtimeErrors: void }>({
  demoMode: [false, { option: true }],
  box: async ({ playwright, demoMode }, use, testInfo) => {
    const root = await mkdtemp(join(tmpdir(), 'rabbit-ears-browser-'));
    const box = new TestBox(root, playwright, demoMode);
    try {
      for (const path of [
        'Arthur/Season 1/First episode.mp4',
        'Arthur/Season 2/Second episode.mp4',
        `Movies/${longTitle}.mp4`,
        'Old Rips/Needs converting.mkv',
      ]) await box.addMedia(path);
      await box.start();
      await use(box);
    } finally {
      if (testInfo.status !== testInfo.expectedStatus)
        await testInfo.attach('application-log', { body: box.log, contentType: 'text/plain' });
      await box.stop();
      await rm(root, { recursive: true, force: true });
    }
  },
  runtimeErrors: [async ({ context }, use, testInfo) => {
    const errors: string[] = [];
    const watch = (page: Page): void => { page.on('pageerror', error => errors.push(error.message)); };
    context.pages().forEach(watch);
    context.on('page', watch);
    await use();
    context.off('page', watch);
    if (testInfo.status === testInfo.expectedStatus) expect(errors, 'Uncaught browser errors').toEqual([]);
  }, { auto: true }],
});

export { expect };
