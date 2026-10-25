import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { encode } from 'uqr';

import { DemoCredit } from '../core/models';
import { FoundFile, TapeDeck, fromDrop, fromPicker } from '../core/tape-deck';
import { TvLink } from '../core/tv-link';
import { Remote } from '../remote/remote';
import { TvScreen } from '../tv/tv-screen';

/**
 * The public demo site: the TV, a remote beside it, and a QR code that turns a
 * visitor's phone into the remote for this same TV. Every visitor's browser
 * picks its own room, so strangers never change each other's channel.
 */
@Component({
  selector: 'ears-demo',
  imports: [TvScreen, Remote],
  templateUrl: './demo.html',
  styleUrl: './demo.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Demo {
  protected readonly link = inject(TvLink);
  protected readonly state = this.link.state;
  protected readonly credits = signal<DemoCredit[]>([]);
  protected readonly tapes = inject(TapeDeck);
  protected readonly dragging = signal(false);
  protected readonly vcrNote = signal<string | null>(null);

  constructor() {
    void fetch('/api/demo/credits').then(async response => {
      if (response.ok) this.credits.set(await response.json());
    }).catch(() => undefined);
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
    this.insert(await fromDrop(event.dataTransfer.items));
  }

  protected onPick(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files?.length) this.insert(fromPicker(input.files));
    // Picking the same files again should load them again.
    input.value = '';
  }

  private insert(found: FoundFile[]): void {
    this.vcrNote.set(this.tapes.insert(found) ? null : 'No videos there. Try MP4 or WebM files.');
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
