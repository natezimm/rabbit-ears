import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';

import { TvLink } from '../core/tv-link';

const VOLUME_SEGMENTS = 20;

/** The phone remote: the same buttons as the handheld one, plus a channel list. */
@Component({
  selector: 'ears-remote',
  templateUrl: './remote.html',
  styleUrl: './remote.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Remote {
  protected readonly link = inject(TvLink);
  protected readonly state = this.link.state;
  protected readonly keypad = [1, 2, 3, 4, 5, 6, 7, 8, 9];

  /** What the little display at the top reads. */
  protected readonly readout = computed(() => {
    const state = this.state();
    if (!this.link.connected() || !state) return { big: 'NO LINK', small: 'Looking for the box…' };
    if (state.poweredOff) return { big: 'GOODBYE', small: 'Safe to unplug in a moment' };
    if (state.standby) return { big: 'STANDBY', small: 'Press power to wake' };
    if (!state.channel) return { big: 'NO CHANNELS', small: 'Add a show folder' };

    const title = state.channels.find(channel => channel.number === state.channel?.number)?.title;
    return {
      big: `CH ${this.pad(state.channel.number)}  ${state.channel.name}`,
      small: state.playing ? (title ?? '') : 'No signal',
    };
  });

  protected readonly segments = computed(() => {
    const state = this.state();
    const lit = !state || state.muted ? 0 : Math.round((state.volume / 100) * VOLUME_SEGMENTS);
    return Array.from({ length: VOLUME_SEGMENTS }, (_, index) => index < lit);
  });

  protected pad(number: number): string {
    return String(number).padStart(2, '0');
  }

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
