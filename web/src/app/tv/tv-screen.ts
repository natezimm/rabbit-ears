import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';

import { ChannelInfo, DEFAULT_PICTURE, PictureConfig, TvSnapshot } from '../core/models';
import { TapeDeck } from '../core/tape-deck';
import { TvLink } from '../core/tv-link';
import { CrtRenderer, ScreenMode } from './crt-renderer';
import { Tube } from './tube';

const VOLUME_SEGMENTS = 25;
const POWER_OFF_SECONDS = 0.45;
const POWER_ON_SECONDS = 0.6;

/**
 * The remote, as the Flirc sends it: a plain keyboard. Arrows are the
 * documented layout (up/down channel, right/left volume, m mute, p power);
 * the rest are extras for other remotes and ordinary keyboards.
 */
const KEYS: Record<string, (link: TvLink) => void> = {
  ArrowUp: link => link.channelUp(),
  PageUp: link => link.channelUp(),
  ChannelUp: link => link.channelUp(),
  ArrowDown: link => link.channelDown(),
  PageDown: link => link.channelDown(),
  ChannelDown: link => link.channelDown(),
  ArrowRight: link => link.volumeUp(),
  '=': link => link.volumeUp(),
  '+': link => link.volumeUp(),
  AudioVolumeUp: link => link.volumeUp(),
  ArrowLeft: link => link.volumeDown(),
  '-': link => link.volumeDown(),
  AudioVolumeDown: link => link.volumeDown(),
  m: link => link.mute(),
  AudioVolumeMute: link => link.mute(),
  p: link => link.power(),
  Power: link => link.power(),
  i: link => link.info(),
  Info: link => link.info(),
  l: link => link.lastChannel(),
  Backspace: link => link.lastChannel(),
  Enter: link => link.confirm(),
  ' ': link => link.confirm(),
};

/** The television: a curved picture with the green on-screen display over it. */
@Component({
  selector: 'ears-tv-screen',
  templateUrl: './tv-screen.html',
  styleUrl: './tv-screen.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '(document:keydown)': 'onKey($event)',
    '(document:pointerdown)': 'onTouch()',
  },
})
export class TvScreen {
  private readonly link = inject(TvLink);
  private readonly tapes = inject(TapeDeck);
  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('glass');
  private readonly deckA = viewChild.required<ElementRef<HTMLVideoElement>>('deckA');
  private readonly deckB = viewChild.required<ElementRef<HTMLVideoElement>>('deckB');

  protected readonly picture = signal<PictureConfig>(DEFAULT_PICTURE);
  protected readonly banner = signal<ChannelInfo | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly volumeShown = signal(false);
  protected readonly soundBlocked = signal(false);
  private readonly webgl = signal(true);
  /**
   * False when the tube effect is switched off or WebGL is unavailable. The
   * plain video is shown instead, which asks almost nothing of the graphics chip.
   */
  protected readonly curved = computed(() => this.webgl() && this.picture().crt.enabled);
  protected readonly liveDeck = signal(-1);

  protected readonly standby = computed(() => this.link.state()?.standby ?? false);
  protected readonly muted = computed(() => this.link.state()?.muted ?? false);
  /** Solid bars for the level, dots for the rest, like a classic TV volume display. */
  protected readonly segments = computed(() => {
    const state = this.link.state();
    const lit = !state || state.muted ? 0 : Math.round((state.volume / 100) * VOLUME_SEGMENTS);
    return Array.from({ length: VOLUME_SEGMENTS }, (_, index) => index < lit);
  });

  private tube: Tube | null = null;
  private renderer: CrtRenderer | null = null;
  private readonly ready = signal(false);

  // What has already been acted on, so each snapshot only triggers what changed.
  private seenBoot: string | null = null;
  private seenToken: string | null = null;
  private seenBanner = -1;
  private seenVolume = -1;
  private seenMessage = -1;
  private bannerTimer = 0;
  private volumeTimer = 0;
  private messageTimer = 0;

  private power = 0;
  private lastFrameAt = 0;
  private animation = 0;

  constructor() {
    void this.loadPicture();

    afterNextRender(() => {
      const decks = [this.deckA().nativeElement, this.deckB().nativeElement];
      this.tube = new Tube(decks, this.picture(), {
        ended: token => this.link.episodeEnded(token),
        failed: token => this.link.episodeFailed(token),
        position: (token, seconds, duration) => this.link.reportPosition(token, seconds, duration),
        soundBlocked: blocked => this.soundBlocked.set(blocked),
      }, id => this.tapes.source(id));
      this.renderer = CrtRenderer.create(this.canvas().nativeElement, this.picture().crt);
      this.webgl.set(this.renderer !== null);
      this.ready.set(true);
      this.animation = requestAnimationFrame(now => this.frame(now));
    });

    effect(() => {
      const state = this.link.state();
      if (this.ready() && state) this.follow(state);
    });

    effect(() => {
      const picture = this.picture();
      if (!this.ready()) return;
      this.tube?.configure(picture);
      this.renderer?.configure(picture.crt);
    });

    inject(DestroyRef).onDestroy(() => {
      cancelAnimationFrame(this.animation);
      clearTimeout(this.bannerTimer);
      clearTimeout(this.volumeTimer);
      clearTimeout(this.messageTimer);
      this.tube?.dispose();
    });
  }

  protected channelNumber(channel: ChannelInfo): string {
    return String(channel.number).padStart(2, '0');
  }

  protected onKey(event: KeyboardEvent): void {
    this.tube?.unlockSound();
    if (event.ctrlKey || event.metaKey || event.altKey) return;
    // Leave native activation and editing keys to the focused control.
    if (event.target instanceof Element &&
        event.target.closest('button, a, input, select, textarea, summary, [contenteditable]')) return;

    const key = event.key.length === 1 ? event.key.toLowerCase() : event.key;
    const digit = /^[0-9]$/.test(key) ? Number(key) : null;
    const press = KEYS[key];
    if (!press && digit === null && key !== 'f') return;

    event.preventDefault();
    // One press, one step: a held button must not run the volume or channels away.
    if (event.repeat) return;

    if (press) press(this.link);
    else if (digit !== null) this.link.digit(digit);
    else void document.documentElement.requestFullscreen?.().catch(() => undefined);
  }

  protected enableSound(): void {
    this.tube?.unlockSound();
  }

  protected onTouch(): void {
    this.tube?.unlockSound();
  }

  /** React to a new snapshot from the box. */
  private follow(state: TvSnapshot): void {
    const tube = this.tube!;
    const firstFromThisBoot = state.boot !== this.seenBoot;
    this.seenBoot = state.boot;

    const token = state.playing?.token ?? null;
    if (token !== this.seenToken) {
      this.seenToken = token;
      if (state.playing) tube.play(state.playing);
      else tube.stop();
    }
    tube.setSound(state.volume, state.muted);

    if (state.bannerSeq !== this.seenBanner) {
      this.seenBanner = state.bannerSeq;
      this.flashBanner(state.channel);
    }

    if (state.volumeSeq !== this.seenVolume) {
      // A fresh connection is not somebody pressing volume.
      if (!firstFromThisBoot && this.seenVolume !== -1) this.flashVolume();
      this.seenVolume = state.volumeSeq;
    }

    const messageSeq = state.message?.seq ?? 0;
    if (messageSeq !== this.seenMessage) {
      this.seenMessage = messageSeq;
      this.showMessage(state.message?.text ?? null, state.message?.seconds ?? 0);
    }

    if (state.standby || state.poweredOff) {
      this.banner.set(null);
      this.volumeShown.set(false);
    }
  }

  private flashBanner(channel: ChannelInfo | null): void {
    clearTimeout(this.bannerTimer);
    this.banner.set(channel);
    if (channel) {
      this.bannerTimer = window.setTimeout(() => this.banner.set(null), this.picture().bannerSeconds * 1000);
    }
  }

  private flashVolume(): void {
    clearTimeout(this.volumeTimer);
    this.volumeShown.set(true);
    this.volumeTimer = window.setTimeout(() => this.volumeShown.set(false), this.picture().osdSeconds * 1000);
  }

  private showMessage(text: string | null, seconds: number): void {
    clearTimeout(this.messageTimer);
    this.message.set(text);
    if (text && seconds > 0) {
      this.messageTimer = window.setTimeout(() => this.message.set(null), seconds * 1000);
    }
  }

  /** One animation frame: ease the power level and redraw the glass. */
  private frame(now: number): void {
    this.animation = requestAnimationFrame(next => this.frame(next));
    const elapsed = Math.min(0.1, (now - (this.lastFrameAt || now)) / 1000);
    this.lastFrameAt = now;

    const state = this.link.state();
    const on = !!state && !state.standby && !state.poweredOff;
    this.power = on
      ? Math.min(1, this.power + elapsed / POWER_ON_SECONDS)
      : Math.max(0, this.power - elapsed / POWER_OFF_SECONDS);

    const view = this.tube!.view;
    if (view.deck !== this.liveDeck()) this.liveDeck.set(view.deck);

    let mode: ScreenMode = view.mode;
    if (!on) {
      // Going dark: the last picture stays on the glass while it collapses.
      mode = this.power > 0 ? 'video' : 'blank';
    } else if (!state?.playing) {
      mode = 'bars';
    }
    if (this.curved()) this.renderer?.draw(mode, on ? view.video : null, this.power, now / 1000);
  }

  private async loadPicture(): Promise<void> {
    try {
      const response = await fetch('/api/picture');
      if (response.ok) this.picture.set(await response.json());
    } catch {
      // The defaults are the same as the server's; carry on with those.
    }
  }
}
