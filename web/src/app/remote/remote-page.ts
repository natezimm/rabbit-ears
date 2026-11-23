import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { BoxApi } from '../core/box-api';
import { RemotePad } from './remote-pad';

/** The phone remote's page: the remote filling the screen, and the way to the lineup editor at home. */
@Component({
  selector: 'ears-remote-page',
  imports: [RemotePad, RouterLink],
  templateUrl: './remote-page.html',
  styleUrl: './remote-page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { role: 'main' },
})
export class RemotePage {
  protected readonly canAdmin = signal(false);

  constructor() {
    // Without the answer the link simply stays hidden, which is right for the demo too.
    inject(BoxApi).adminEnabled().then(enabled => this.canAdmin.set(enabled), () => undefined);
  }
}
