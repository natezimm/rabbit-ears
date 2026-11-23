import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { encode } from 'uqr';

import { BoxApi } from '../core/box-api';
import { LinkNotice } from '../core/link-notice';
import { DemoCredit } from '../core/models';
import { TapeDeck, fromDrop, fromPicker } from '../core/tape-deck';
import { TvLink } from '../core/tv-link';
import { RemotePad } from '../remote/remote-pad';
import { TvScreen } from '../tv/tv-screen';

/**
 * The public demo site: the TV, a remote beside it, and a QR code that turns a
 * visitor's phone into the remote for this same TV. Every visitor's browser
 * picks its own room, so strangers never change each other's channel.
 */
@Component({
  selector: 'ears-demo',
  imports: [TvScreen, RemotePad, LinkNotice],
  templateUrl: './demo.html',
  styleUrl: './demo.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Demo {
  protected readonly link = inject(TvLink);
  protected readonly credits = signal<DemoCredit[]>([]);
  protected readonly tapes = inject(TapeDeck);
  protected readonly dragging = signal(false);
  /**
   * Trouble with the link is told over the picture, where a visitor is looking.
   * The first moments of connecting are normal and stay quiet.
   */
  /** What the VCR holds, as one sentence; built here so template formatting cannot change its spacing. */
  protected readonly tapeSummary = computed(() => {
    const loaded = this.tapes.loaded();
    if (!loaded) return null;
    const skipped = loaded.skipped ? `; ${loaded.skipped} other files skipped` : '';
    return (
      `${loaded.tapes} ${loaded.tapes === 1 ? 'video' : 'videos'} on ${loaded.channels} ` +
      `${loaded.channels === 1 ? 'channel' : 'channels'}${skipped}. ${loaded.megabytes} MB loaded.`
    );
  });
  protected readonly screenNotice = computed(() => !!this.link.unavailable() || this.link.status() === 'reconnecting');

  constructor() {
    // Credits are a footnote: without them the footer says the demo plays test clips.
    inject(BoxApi)
      .credits()
      .then(
        credits => this.credits.set(credits),
        () => undefined,
      );
  }

  protected onDragOver(event: DragEvent): void {
    if (!event.dataTransfer?.types.includes('Files')) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = 'copy';
    this.dragging.set(true);
  }

  protected async onDrop(event: DragEvent): Promise<void> {
    if (!event.dataTransfer?.types.includes('Files')) return;
    event.preventDefault();
    this.dragging.set(false);
    const items = event.dataTransfer.items;
    await this.tapes.insert(cancel => fromDrop(items, cancel));
  }

  protected onPick(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files?.length) {
      const files = input.files;
      void this.tapes.insert(() => fromPicker(files));
    }
    // Picking the same files again should load them again.
    input.value = '';
  }

  protected readonly remoteUrl = `${location.origin}/remote?room=${encodeURIComponent(this.link.room)}`;
  protected readonly qr = toPath(this.remoteUrl);
}

/** The QR code as one SVG path, one unit square per dark module, inside a quiet border. */
function toPath(text: string): { size: number; d: string } {
  const code = encode(text, { ecc: 'M', border: 2 });
  let d = '';
  code.data.forEach((row, y) =>
    row.forEach((dark, x) => {
      if (dark) d += `M${x} ${y}h1v1h-1z`;
    }),
  );
  return { size: code.size, d };
}
