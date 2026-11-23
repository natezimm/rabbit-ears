import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';

import { formatChannel, volumeSegments } from '../core/format';
import { linkWords } from '../core/link-notice';
import { TvLink } from '../core/tv-link';

/** The remote's readout is phone-narrow, so its meter has fewer steps than the TV's. */
const VOLUME_SEGMENTS = 20;

/**
 * The remote itself: readout, buttons and channel list, with no page around
 * it. The phone remote page and the demo both show this one.
 *
 * It talks to `TvLink` directly rather than raising events for a parent to
 * forward: every press goes to the same one TV, and the snapshot that comes
 * back is the only state it draws.
 */
@Component({
  selector: 'ears-remote-pad',
  templateUrl: './remote-pad.html',
  styleUrl: './remote-pad.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RemotePad {
  protected readonly link = inject(TvLink);
  protected readonly state = this.link.state;
  protected readonly keypad = [1, 2, 3, 4, 5, 6, 7, 8, 9];
  protected readonly formatChannel = formatChannel;

  /** What the little display at the top reads. */
  protected readonly readout = computed(() => {
    const state = this.state();
    const words = linkWords(this.link);
    if (words) return { big: words.title, small: words.detail };
    if (!state) return { big: 'CONNECTING', small: 'Tuning in to the TV…' };
    if (state.poweredOff) return { big: 'GOODBYE', small: 'Safe to unplug in a moment' };
    if (state.standby) return { big: 'STANDBY', small: 'Press power to wake' };
    if (!state.channel) return { big: 'NO CHANNELS', small: 'Add a show folder' };

    const title = state.channels.find(channel => channel.number === state.channel?.number)?.title;
    return {
      big: `CH ${formatChannel(state.channel.number)}  ${state.channel.name}`,
      small: state.playing ? (title ?? '') : 'No signal',
    };
  });

  protected readonly segments = computed(() => volumeSegments(this.state(), VOLUME_SEGMENTS));

  protected press(action: () => void): void {
    navigator.vibrate?.(8);
    action();
  }

  protected tune(number: number): void {
    this.press(() => this.link.goToChannel(number));
  }

  protected digit(digit: number): void {
    this.press(() => this.link.digit(digit));
  }
}
