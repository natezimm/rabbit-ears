import { Injectable, OnDestroy, effect, inject, signal } from '@angular/core';

import { TapeShelf } from './models';
import { TvLink } from './tv-link';

import { FoundFile, clip, shelve, validateSelection, validateDuration } from './tape-policy';
export { FoundFile, shelve } from './tape-policy';
const TAPE_PREFIX = 'tape-';
const MAX_TITLE = 120;
const MAX_MESSAGE_BYTES = 28_000;

/** What the last load put on the TV, for the demo page to describe. */
export interface TapeSummary {
  channels: number;
  tapes: number;
  skipped: number;
  megabytes: number;
}

/**
 * The demo's VCR: a visitor's own videos, played straight from their disk.
 * Each file gets a blob URL that only this tab can open; the box hears an id
 * and a name for each, so it can put them on channels and keep the remote in
 * step, but never sees the video.
 */
@Injectable({ providedIn: 'root' })
export class TapeDeck implements OnDestroy {
  private readonly link = inject(TvLink);
  private urls = new Map<string, string>();
  private shelves: TapeShelf[] = [];
  private pending = new Map<string, string>();
  private attempt: AbortController | null = null;
  readonly progress = signal<string | null>(null);
  readonly error = signal<string | null>(null);
  readonly committing = signal(false);

  readonly loaded = signal<TapeSummary | null>(null);

  constructor() {
    // The box takes the tapes out when the connection that loaded them drops,
    // since only this tab can play them. After a reconnect, put them back
    // without changing the channel.
    let wasConnected = this.link.connected();
    effect(() => {
      const connected = this.link.connected();
      if (connected && !wasConnected && this.shelves.length && !this.attempt)
        void this.link.loadTapes(this.shelves, false).catch(() => this.error.set('Reconnect failed to restore your tapes. Please insert them again.'));
      wasConnected = connected;
    });
  }

  /** Where the TV should load an episode from, or null for a tape this tab no longer has. */
  source(episodeId: string): string | null {
    if (!episodeId.startsWith(TAPE_PREFIX)) return `/media/${episodeId}`;
    return this.pending.get(episodeId.slice(TAPE_PREFIX.length)) ?? this.urls.get(episodeId.slice(TAPE_PREFIX.length)) ?? null;
  }

  /** Validate without disturbing playback; only replace after the server accepts. */
  async insert(read: (cancel: AbortSignal) => FoundFile[] | Promise<FoundFile[]>): Promise<void> {
    if (this.attempt) return;
    const attempt = new AbortController();
    this.attempt = attempt;
    this.error.set(null);
    this.progress.set('Checking your selection…');
    try {
      const found = await read(attempt.signal);
      attempt.signal.throwIfAborted();
      const videos = validateSelection(found);
      const shelves = shelve(videos);
      const probe = document.createElement('video');
      for (let i = 0; i < videos.length; i++) {
        this.progress.set(`Checking video ${i + 1} of ${videos.length}…`);
        await probeVideo(probe, videos[i].file, attempt.signal);
      }
      attempt.signal.throwIfAborted();
      const sent: TapeShelf[] = shelves.map(shelf => ({
        name: shelf.name,
        tapes: shelf.files.map(file => {
          const id = newId();
          this.pending.set(id, URL.createObjectURL(file));
          return { id, title: clip(file.name.replace(/\.[^.]+$/, ''), MAX_TITLE) };
        }),
      }));
      if (new TextEncoder().encode(JSON.stringify(sent)).length > MAX_MESSAGE_BYTES)
        throw new Error('The video titles are too large to send. Please shorten them.');
      this.committing.set(true);
      this.progress.set('Inserting tapes…');
      await this.link.loadTapes(sent, true);
      this.release();
      this.urls = this.pending;
      this.pending = new Map();
      this.shelves = sent;
      this.loaded.set({ channels: sent.length, tapes: videos.length, skipped: found.length - videos.length,
        megabytes: Math.round(videos.reduce((sum, v) => sum + v.file.size, 0) / 1_000_000 * 10) / 10 });
    } catch (error) {
      this.error.set(attempt.signal.aborted ? 'Loading cancelled. Your previous tapes are unchanged.'
        : error instanceof Error ? error.message : 'Could not load these videos. Please try again.');
    } finally {
      for (const url of this.pending.values()) URL.revokeObjectURL(url);
      this.pending.clear();
      this.attempt = null;
      this.committing.set(false);
      this.progress.set(null);
    }
  }

  cancel(): void { if (!this.committing()) this.attempt?.abort(); }

  async eject(): Promise<void> {
    if (this.attempt) return;
    this.attempt = new AbortController();
    this.committing.set(true);
    this.progress.set('Ejecting tapes…');
    try {
      await this.link.ejectTapes();
      this.release();
      this.shelves = [];
      this.loaded.set(null);
      this.error.set(null);
    } catch { this.error.set('Could not eject tapes. Please reconnect and try again.'); }
    finally {
      this.attempt = null;
      this.committing.set(false);
      this.progress.set(null);
    }
  }

  ngOnDestroy(): void {
    this.attempt?.abort();
    this.release();
  }

  private release(): void {
    for (const url of this.urls.values()) URL.revokeObjectURL(url);
    this.urls.clear();
  }
}

/**
 * Files picked with the folder chooser, which knows each one's path inside it.
 * Loose files from the file chooser have no folders.
 */
export function fromPicker(files: FileList): FoundFile[] {
  if (files.length > 1000) throw new Error('This folder has too many entries. Choose a smaller folder.');
  return Array.from(files, file => ({
    file,
    folders: (file.webkitRelativePath || file.name).split('/').slice(0, -1),
  }));
}

/**
 * Files and folders dropped on the page. Entries must be taken from the drop
 * event before anything awaits, or the browser empties the list.
 */
export async function fromDrop(items: DataTransferItemList, cancel: AbortSignal): Promise<FoundFile[]> {
  // Capture entries synchronously while the drop event still owns them.
  const entries = Array.from(items, item => item.webkitGetAsEntry?.() ?? null).filter(e => e !== null);
  const found: FoundFile[] = [];
  const scan = { count: 0, deadline: performance.now() + 10_000 };
  for (const entry of entries) await walk(entry, [], found, scan, cancel);
  return found;
}

async function walk(entry: FileSystemEntry, folders: string[], found: FoundFile[],
  scan: { count: number; deadline: number }, cancel: AbortSignal): Promise<void> {
  cancel.throwIfAborted();
  if (++scan.count > 1000 || folders.length > 8 || performance.now() >= scan.deadline)
    throw new Error('Folder scan limit reached. Choose a smaller or less deeply nested folder.');
  const remaining = () => Math.max(1, scan.deadline - performance.now());
  if (entry.isFile) {
    const file = await bounded<File>((resolve, reject) => (entry as FileSystemFileEntry).file(resolve, reject), cancel, remaining());
    found.push({ file, folders });
    return;
  }
  const reader = (entry as FileSystemDirectoryEntry).createReader();
  for (;;) {
    const batch = await bounded<FileSystemEntry[]>((resolve, reject) => reader.readEntries(resolve, reject), cancel, remaining());
    if (!batch.length) return;
    for (const child of batch) await walk(child, [...folders, entry.name], found, scan, cancel);
  }
}

/** All temporary event handlers and timers leave with the operation. */
function bounded<T>(start: (resolve: (value: T) => void, reject: (reason: unknown) => void) => void,
  cancel: AbortSignal, timeout: number): Promise<T> {
  return new Promise((resolve, reject) => {
    const finish = (action: () => void) => { clearTimeout(timer); cancel.removeEventListener('abort', abort); action(); };
    const abort = () => finish(() => reject(new Error('Loading cancelled.')));
    const timer = setTimeout(() => finish(() => reject(new Error('Reading the video or folder took too long. Try a smaller selection.'))), timeout);
    cancel.addEventListener('abort', abort, { once: true });
    if (cancel.aborted) { abort(); return; }
    try { start(value => finish(() => resolve(value)), error => finish(() => reject(error))); }
    catch (error) { finish(() => reject(error)); }
  });
}

async function probeVideo(video: HTMLVideoElement, file: File, cancel: AbortSignal): Promise<void> {
  const url = URL.createObjectURL(file);
  try {
    video.preload = 'metadata';
    await bounded<void>((resolve, reject) => {
      video.onloadedmetadata = () => {
        try { validateDuration(video.duration); resolve(); } catch (error) { reject(error); }
      };
      video.onerror = () => reject(new Error(`Cannot read “${file.name}”. Try MP4 with H.264 video and AAC audio.`));
      video.src = url;
      video.load();
    }, cancel, 10_000);
  } finally {
    video.onloadedmetadata = null;
    video.onerror = null;
    video.removeAttribute('src');
    video.load();
    URL.revokeObjectURL(url);
  }
}

function newId(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(8));
  return Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('');
}
