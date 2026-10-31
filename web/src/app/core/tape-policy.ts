// Contract-tested against the server. Reject whole selections rather than truncating.
export const MAX_SHELVES = 3;
export const MAX_TAPES = 5;
const MAX_NAME = 80;
const VIDEO_EXTENSIONS = /\.(mp4|m4v|webm|mkv|mov|ogv)$/i;

/** A file and the folders it was found in, outermost first, relative to what was dropped or picked. */
export interface FoundFile {
  file: File;
  folders: string[];
}

export function validateSelection(found: FoundFile[]): FoundFile[] {
  if (found.length > 1000 || found.some(f => f.folders.length > 8))
    throw new Error('Folder scan limit reached. Choose a smaller or less deeply nested folder.');
  const videos = found.filter(f => VIDEO_EXTENSIONS.test(f.file.name) || f.file.type.startsWith('video/'));
  if (!videos.length) throw new Error('No videos found. Try an MP4 with H.264 video and AAC audio.');
  if (videos.length > MAX_TAPES) throw new Error('Choose up to 5 videos. Your previous tapes are unchanged.');
  if (videos.some(v => v.file.size > 500_000_000)) throw new Error('Each video must be 500 MB or smaller.');
  if (videos.reduce((sum, v) => sum + v.file.size, 0) > 1_000_000_000) throw new Error('Choose videos totalling 1 GB or less.');
  if (shelve(videos).length > MAX_SHELVES) throw new Error('Choose up to 3 channels, including loose files on CH 00.');
  return videos;
}

export function validateDuration(duration: number): void {
  if (!Number.isFinite(duration) || duration <= 0 || duration > 1800)
    throw new Error('Each video must have a readable duration of 30 minutes or less.');
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

  return shelves;
}

export function clip(text: string, max: number): string {
  // Control characters would be refused by the box; names are for display only.
  const plain = text.replace(/\p{Cc}/gu, ' ').trim() || 'Untitled';
  return plain.length > max ? plain.slice(0, max) : plain;
}

