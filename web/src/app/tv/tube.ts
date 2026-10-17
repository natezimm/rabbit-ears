import { NowPlaying, PictureConfig } from '../core/models';

/** What the tube needs to tell the box. */
export interface TubeListener {
  ended(token: string): void;
  failed(token: string): void;
  /** `duration` is the episode's length in seconds, or null while unknown. */
  position(token: string, seconds: number, duration: number | null): void;
  /** The browser is refusing to play sound until someone presses a key. */
  soundBlocked(blocked: boolean): void;
}

export interface TubeView {
  mode: 'video' | 'snow' | 'glitch' | 'blank';
  video: HTMLVideoElement | null;
  /** Which of the two players is on screen, or -1. */
  deck: number;
}

type Effect = 'none' | 'snow' | 'glitch';

interface Loading {
  request: NowPlaying;
  deck: number;
  cancel: AbortController;
  earliestCut: number;
}

const REPORT_MS = 2000;
const LOAD_TIMEOUT_MS = 12_000;
/** Playback that has not moved for this many reports is treated as a dead file. */
const STALLED_REPORTS = 6;
/** A resume point this close to the end starts here instead, so the episode simply ends. */
const END_GUARD_SECONDS = 0.5;
/** After a backend restart, how far apart "what is playing" may be and still count as the same thing. */
const ADOPT_WINDOW_SECONDS = 30;

/**
 * The picture tube: two video players, one on screen and one getting the next
 * thing ready, so a channel change cuts cleanly instead of freezing or going
 * black. It plays what the box tells it to and reports back what happened.
 */
export class Tube {
  private live = -1;
  private liveRequest: NowPlaying | null = null;
  private loading: Loading | null = null;
  private effect: Effect = 'none';
  private volume = 1;
  private muted = false;
  private blocked = false;
  private lastPosition = -1;
  private stalledReports = 0;
  private readonly reporter: number;

  constructor(
    private readonly decks: readonly HTMLVideoElement[],
    private picture: PictureConfig,
    private readonly listener: TubeListener,
  ) {
    for (const deck of decks) {
      deck.muted = true;
      deck.playsInline = true;
      deck.preload = 'auto';
    }
    this.reporter = window.setInterval(() => this.report(), REPORT_MS);
  }

  /** What the screen should be drawing right now. */
  get view(): TubeView {
    if (this.effect !== 'none') return { mode: this.effect, video: null, deck: -1 };
    if (this.live >= 0) return { mode: 'video', video: this.decks[this.live], deck: this.live };
    return { mode: 'blank', video: null, deck: -1 };
  }

  configure(picture: PictureConfig): void {
    this.picture = picture;
  }

  play(request: NowPlaying): void {
    if (this.adopt(request)) return;
    this.reportNow();
    this.cancelLoading();

    const cancel = new AbortController();
    const loading: Loading = {
      request,
      deck: this.live === 0 ? 1 : 0,
      cancel,
      earliestCut: performance.now(),
    };
    this.loading = loading;

    const changingChannel = request.reason === 'tune' && this.live >= 0;
    const effect: Effect =
      this.picture.transition === 'static' ? 'snow' : this.picture.transition === 'glitch' ? 'glitch' : 'none';

    if (changingChannel && effect !== 'none') {
      // The old channel goes quiet behind a burst of snow, like a real tuner.
      this.coverLive(effect);
      loading.earliestCut += this.picture.transitionSeconds * 1000;
    } else if (changingChannel) {
      // No effect: the old show keeps playing while the new one loads so the
      // picture never freezes. If that takes too long, cover it with snow.
      const bridge = window.setTimeout(() => this.coverLive('snow'), this.picture.bridgeSeconds * 1000);
      cancel.signal.addEventListener('abort', () => clearTimeout(bridge));
    }

    this.load(loading);
  }

  /** Standby, no signal, goodbye: stop everything and go dark. */
  stop(): void {
    this.reportNow();
    this.cancelLoading();
    if (this.live >= 0) this.unload(this.decks[this.live]);
    this.live = -1;
    this.liveRequest = null;
    this.effect = 'none';
  }

  setSound(level: number, muted: boolean): void {
    // Hearing is roughly logarithmic; squaring makes each step sound about even.
    this.volume = (Math.min(100, Math.max(0, level)) / 100) ** 2;
    this.muted = muted;
    this.applySound();
  }

  /** Call on any key press or tap: that is what browsers want before allowing sound. */
  unlockSound(): void {
    if (!this.blocked) return;
    this.blocked = false;
    this.listener.soundBlocked(false);
    this.applySound();
  }

  dispose(): void {
    clearInterval(this.reporter);
    this.stop();
  }

  /**
   * After the backend restarts it asks again for what was already playing.
   * If that is still on screen, carry on rather than reload it.
   */
  private adopt(request: NowPlaying): boolean {
    if (request.reason !== 'wake' || this.live < 0 || this.loading) return false;
    if (this.liveRequest?.episodeId !== request.episodeId) return false;
    const deck = this.decks[this.live];
    if (deck.paused || deck.ended) return false;
    if (Math.abs(deck.currentTime - request.start) > ADOPT_WINDOW_SECONDS) return false;
    this.liveRequest = request;
    return true;
  }

  private load(loading: Loading): void {
    const { request, cancel } = loading;
    const { signal } = cancel;
    const deck = this.decks[loading.deck];
    let seekIssued = false;

    const fail = () => {
      if (this.loading !== loading) return;
      this.cancelLoading();
      this.listener.failed(request.token);
    };
    const tryCut = () => {
      if (this.loading !== loading || !seekIssued || deck.seeking) return;
      if (deck.readyState < HTMLMediaElement.HAVE_FUTURE_DATA) return;
      const wait = loading.earliestCut - performance.now();
      if (wait > 0) {
        const timer = window.setTimeout(tryCut, wait);
        signal.addEventListener('abort', () => clearTimeout(timer));
        return;
      }
      this.cut(loading);
    };

    const timeout = window.setTimeout(fail, LOAD_TIMEOUT_MS);
    signal.addEventListener('abort', () => clearTimeout(timeout));
    deck.addEventListener('error', fail, { signal });
    deck.addEventListener(
      'loadedmetadata',
      () => {
        const latest = Math.max(0, deck.duration - END_GUARD_SECONDS);
        deck.currentTime = Math.min(request.start, latest);
        seekIssued = true;
        tryCut();
      },
      { signal, once: true },
    );
    for (const event of ['seeked', 'canplay', 'loadeddata']) {
      deck.addEventListener(event, tryCut, { signal });
    }

    deck.muted = true;
    deck.src = `/media/${request.episodeId}`;
    deck.load();
  }

  /** The new picture is ready: put it on screen and let the old one go. */
  private cut(loading: Loading): void {
    const previous = this.live;
    this.loading = null;
    loading.cancel.abort();

    this.live = loading.deck;
    this.liveRequest = loading.request;
    this.effect = 'none';
    this.lastPosition = -1;
    this.stalledReports = 0;
    if (previous >= 0) this.unload(this.decks[previous]);

    const deck = this.decks[this.live];
    deck.onended = () => this.liveRequest && this.listener.ended(this.liveRequest.token);
    deck.onerror = () => this.liveRequest && this.listener.failed(this.liveRequest.token);
    this.applySound();
    void this.start(deck);
  }

  private async start(deck: HTMLVideoElement): Promise<void> {
    try {
      await deck.play();
    } catch (error) {
      // AbortError just means a newer request got in first.
      if (!(error instanceof DOMException) || error.name !== 'NotAllowedError') return;
      // No key has been pressed yet, so the browser will not play sound. Play
      // silently until one is. The kiosk launches with autoplay allowed and
      // never comes through here.
      this.blocked = true;
      this.listener.soundBlocked(true);
      this.applySound();
      await deck.play().catch(() => undefined);
    }
  }

  /** Hide and silence the live picture behind an effect while the next one loads. */
  private coverLive(effect: Effect): void {
    this.reportNow();
    if (this.live >= 0) this.decks[this.live].pause();
    this.effect = effect;
  }

  private cancelLoading(): void {
    const loading = this.loading;
    if (!loading) return;
    this.loading = null;
    loading.cancel.abort();
    this.unload(this.decks[loading.deck]);
  }

  private unload(deck: HTMLVideoElement): void {
    deck.onended = null;
    deck.onerror = null;
    deck.pause();
    deck.muted = true;
    deck.removeAttribute('src');
    deck.load();
  }

  private applySound(): void {
    this.decks.forEach((deck, index) => {
      deck.muted = index !== this.live || this.muted || this.blocked;
      deck.volume = this.volume;
    });
  }

  private reportNow(): void {
    if (this.live < 0 || !this.liveRequest) return;
    const deck = this.decks[this.live];
    this.listener.position(this.liveRequest.token, deck.currentTime, lengthOf(deck));
  }

  private report(): void {
    if (this.live < 0 || !this.liveRequest) return;
    const deck = this.decks[this.live];
    if (deck.paused || deck.ended) return;

    if (deck.currentTime === this.lastPosition) {
      if (++this.stalledReports >= STALLED_REPORTS) {
        this.stalledReports = 0;
        this.listener.failed(this.liveRequest.token);
      }
      return;
    }
    this.lastPosition = deck.currentTime;
    this.stalledReports = 0;
    this.listener.position(this.liveRequest.token, deck.currentTime, lengthOf(deck));
  }
}

function lengthOf(deck: HTMLVideoElement): number | null {
  return Number.isFinite(deck.duration) && deck.duration > 0 ? deck.duration : null;
}
