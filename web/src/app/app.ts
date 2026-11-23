import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { BoxApi } from './core/box-api';

@Component({
  selector: 'ears-root',
  imports: [RouterOutlet],
  template: '<router-outlet />',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  constructor() {
    // The TV's display colour is a box setting; the remote and demo readouts
    // take it too, so changing it never leaves them a different green.
    inject(BoxApi).picture().then(
      picture => document.documentElement.style.setProperty('--phosphor', picture.osd.color),
      () => undefined, // The stylesheet's default is the server's default.
    );
  }
}
