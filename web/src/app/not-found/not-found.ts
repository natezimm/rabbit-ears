import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { BoxApi } from '../core/box-api';

/** An address the app does not have. Points the way back, to the demo on the public site. */
@Component({
  selector: 'ears-not-found',
  imports: [RouterLink],
  templateUrl: './not-found.html',
  styleUrl: './not-found.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { role: 'main' },
})
export class NotFound {
  protected readonly demo = signal<boolean | null>(null);

  constructor() {
    inject(BoxApi)
      .demoEnabled()
      .then(
        enabled => this.demo.set(enabled),
        () => this.demo.set(false),
      );
  }
}
