import { Injectable, effect, inject, signal } from '@angular/core';

import { Tape, TapeShelf } from './models';
import { TvLink } from './tv-link';

/** Matches `Tapes.EpisodePrefix` on the server. */
const TAPE_PREFIX = 'tape-';
// Mirror the server's limits so a big folder is trimmed here instead of refused there.
const MAX_SHELVES = 30;
const MAX_TAPES = 500;
const MAX_NAME = 80;
const MAX_TITLE = 120;
/** The box takes hub messages up to 32 KB; stay clear of that with room for the envelope. */
const MAX_MESSAGE_BYTES = 28_000;
const VIDEO_EXTENSIONS = /\.(mp4|m4v|webm|mkv|mov|ogv)$/i;

/** A file and the folders it was found in, outermost first, relative to what was dropped or picked. */
export interface FoundFile {
  file: File;
  folders: string[];
}

/** What the last load put on the TV, for the demo page to describe. */
export interface TapeSummary {
  channels: number;
  tapes: number;
  skipped: number;
}

/**
 * The demo's VCR: a visitor's own videos, played straight from their disk.
 * Each file gets a blob URL that only this tab can open; the box hears an id
 * and a name for each, so it can put them on channels and keep the remote in
 * step, but never sees the video.
 */
@Injectable({ providedIn: 'root' })
export class TapeDeck {
  private readonly link = inject(TvLink);
  private urls = new Map<string, string>();
  private shelves: TapeShelf[] = [];

  readonly loaded = signal<TapeSummary | null>(null);

  constructor() {
    // The box takes the tapes out when the connection that loaded them drops,
    // since only this tab can play them. After a reconnect, put them back
    // without changing the channel.
    let wasConnected = this.link.connected();
    effect(() => {
      const connected = this.link.connected();
      if (connected && !wasConnected && this.shelves.length) this.link.loadTapes(this.shelves, false);
      wasConnected = connected;
    });
  }

  /** Where the TV should load an episode from, or null for a tape this tab no longer has. */
  source(episodeId: string): string | null {
    if (!episodeId.startsWith(TAPE_PREFIX)) return `/media/${episodeId}`;
    return this.urls.get(episodeId.slice(TAPE_PREFIX.length)) ?? null;
  }

  /** Put these videos on the TV, replacing any tapes already in. */
  insert(found: FoundFile[]): TapeSummary | null {
    const videos = found.filter(f => VIDEO_EXTENSIONS.test(f.file.name) || f.file.type.startsWith('video/'));
    const shelves = shelve(videos);
    if (shelves.length === 0) return null;

    const files = new Map<string, File>();
    const sent = fitMessage(shelves.map(shelf => ({
      name: shelf.name,
      tapes: shelf.files.map(file => {
        const tape: Tape = { id: newId(), title: clip(file.name.replace(/\.[^.]+$/, ''), MAX_TITLE) };
        files.set(tape.id, file);
        return tape;
      }),
    })));

    this.release();
    for (const shelf of sent) {
      for (const tape of shelf.tapes) this.urls.set(tape.id, URL.createObjectURL(files.get(tape.id)!));
    }
    this.shelves = sent;
    const tapes = sent.reduce((sum, shelf) => sum + shelf.tapes.length, 0);
    const summary = { channels: sent.length, tapes, skipped: found.length - tapes };
    this.loaded.set(summary);
    this.link.loadTapes(sent, true);
    return summary;
  }

  eject(): void {
    this.release();
    this.shelves = [];
    this.loaded.set(null);
    this.link.ejectTapes();
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
  return Array.from(files, file => ({
    file,
    folders: (file.webkitRelativePath || file.name).split('/').slice(0, -1),
  }));
}

/**
 * Files and folders dropped on the page. Entries must be taken from the drop
 * event before anything awaits, or the browser empties the list.
 */
export async function fromDrop(items: DataTransferItemList): Promise<FoundFile[]> {
  const entries = Array.from(items, item => item.webkitGetAsEntry?.() ?? null).filter(e => e !== null);
  const found: FoundFile[] = [];
  for (const entry of entries) await walk(entry, [], found);
  return found;
}

async function walk(entry: FileSystemEntry, folders: string[], found: FoundFile[]): Promise<void> {
  if (found.length >= MAX_TAPES * 2) return;
  if (entry.isFile) {
    const file = await new Promise<File | null>(resolve => (entry as FileSystemFileEntry).file(resolve, () => resolve(null)));
    if (file) found.push({ file, folders });
    return;
  }
  const reader = (entry as FileSystemDirectoryEntry).createReader();
  const inside = [...folders, entry.name];
  // Directory readers hand entries over in batches until one comes back empty.
  for (;;) {
    const batch = await new Promise<FileSystemEntry[]>(resolve => reader.readEntries(resolve, () => resolve([])));
    if (batch.length === 0) return;
    for (const child of batch) await walk(child, inside, found);
  }
}

interface Shelf {
  name: string | null;
  files: File[];
}

/**
 * Arrange videos the way the box arranges its media folder. A folder of show
 * folders becomes one channel per show (seasons inside a show stay with it); a
 * folder holding only videos becomes one channel named after it; loose files
 * go on the VCR.
 */
export function shelve(videos: FoundFile[]): Shelf[] {
  const byTop = new Map<string, FoundFile[]>();
  const loose: File[] = [];
  for (const video of videos) {
    if (video.folders.length === 0) {
      loose.push(video.file);
      continue;
    }
    const top = video.folders[0];
    byTop.set(top, [...(byTop.get(top) ?? []), video]);
  }

  const named = new Map<string, File[]>();
  const add = (name: string, file: File) => {
    const key = clip(name, MAX_NAME);
    named.set(key, [...(named.get(key) ?? []), file]);
  };
  for (const [top, inside] of byTop) {
    const isLibrary = inside.some(v => v.folders.length > 1);
    for (const video of inside) {
      if (!isLibrary) add(top, video.file);
      else if (video.folders.length > 1) add(video.folders[1], video.file);
      else loose.push(video.file);
    }
  }

  const byName = (a: File, b: File) => a.name.localeCompare(b.name, undefined, { numeric: true });
  const shelves: Shelf[] = [...named]
    .sort(([a], [b]) => a.localeCompare(b, undefined, { sensitivity: 'base' }))
    .map(([name, files]) => ({ name, files: files.sort(byName) }));
  if (loose.length) shelves.unshift({ name: null, files: loose.sort(byName) });

  // Trim to the server's limits, keeping whole channels where possible.
  let room = MAX_TAPES;
  return shelves.slice(0, MAX_SHELVES).flatMap(shelf => {
    const files = shelf.files.slice(0, room);
    room -= files.length;
    return files.length ? [{ ...shelf, files }] : [];
  });
}

/** Drop tapes from the end until the whole list fits in one hub message. */
function fitMessage(shelves: TapeShelf[]): TapeShelf[] {
  const encoder = new TextEncoder();
  const size = () => encoder.encode(JSON.stringify(shelves)).length;
  while (shelves.length && size() > MAX_MESSAGE_BYTES) {
    const last = shelves[shelves.length - 1];
    last.tapes.pop();
    if (!last.tapes.length) shelves.pop();
  }
  return shelves;
}

function clip(text: string, max: number): string {
  // Control characters would be refused by the box; names are for display only.
  const plain = text.replace(/\p{Cc}/gu, ' ').trim() || 'Untitled';
  return plain.length > max ? plain.slice(0, max) : plain;
}

function newId(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(8));
  return Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('');
}
