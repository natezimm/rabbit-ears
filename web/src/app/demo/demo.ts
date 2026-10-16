import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { encode } from 'uqr';

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
  private readonly link = inject(TvLink);

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
