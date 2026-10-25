import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { encode } from 'uqr';

import { DemoCredit } from '../core/models';
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

  constructor() {
    void fetch('/api/demo/credits').then(async response => {
      if (response.ok) this.credits.set(await response.json());
    }).catch(() => undefined);
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
