import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';

import { TvLink } from './tv-link';

/** What to tell a viewer about the connection, or null when there is nothing to say. */
export interface LinkWords {
  /** A short label in capitals, sized for the remote's readout and the TV's display. */
  title: string;
  detail: string;
  /** True when a button to try again now would actually start an attempt. */
  canRetry: boolean;
}

/**
 * The one wording for the connection's state, so the remote, the TV and the
 * demo page never describe the same moment differently.
 */
export function linkWords(link: TvLink): LinkWords | null {
  const refused = link.unavailable();
  if (refused) return { title: 'BUSY', detail: refused, canRetry: link.canRetry() };
  switch (link.status()) {
    case 'connecting':
      return { title: 'CONNECTING', detail: 'Tuning in to the TV…', canRetry: false };
    case 'reconnecting':
      return { title: 'NO LINK', detail: 'Cannot reach the TV. Trying again…', canRetry: link.canRetry() };
    default:
      return null;
  }
}

/** The connection's state in words, with a button to try again when that could help. */
@Component({
  selector: 'ears-link-notice',
  templateUrl: './link-notice.html',
  styleUrl: './link-notice.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LinkNotice {
  protected readonly link = inject(TvLink);
  /** False where another control on the page already offers the retry. */
  readonly retry = input(true);
  protected readonly words = computed(() => linkWords(this.link));
}
