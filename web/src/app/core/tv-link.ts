import { Injectable, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';

import { TapeShelf, TvSnapshot } from './models';

const RETRY_MS = 2000;
const ROOM_KEY = 'rabbitears.room';

/**
 * A fresh room id. `randomUUID` exists only on https pages and localhost, and
 * the box is plain http on the home network, so this asks for raw random bytes.
 */
function newRoom(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(6));
  return Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('');
}

/**
 * Which television this browser belongs to. Only the public demo site uses it,
 * to give every visitor a TV of their own; the box at home ignores it. A phone
 * that scans the demo's QR code arrives with the TV's room in its address.
 */
function pickRoom(): string {
  const fromAddress = new URLSearchParams(location.search).get('room');
  let room = fromAddress;
  try {
    room ??= localStorage.getItem(ROOM_KEY);
  } catch {
    // Storage can be blocked; a fresh room per visit is fine.
  }
  room ??= newRoom();
  try {
    localStorage.setItem(ROOM_KEY, room);
  } catch {
    // As above.
  }
  return room;
}

/**
 * The live connection to the box. Every screen and remote shares the same
 * state: they send button presses and draw whatever comes back, so two remotes
 * can never disagree about what is on.
 */
@Injectable({ providedIn: 'root' })
export class TvLink {
  readonly state = signal<TvSnapshot | null>(null);
  readonly connected = signal(false);
  readonly room = pickRoom();

  private readonly hub: HubConnection = new HubConnectionBuilder()
    .withUrl(`/hub/tv?room=${encodeURIComponent(this.room)}`)
    // The box may be restarting; keep trying for as long as it takes.
    .withAutomaticReconnect({ nextRetryDelayInMilliseconds: () => RETRY_MS })
    .configureLogging(LogLevel.Warning)
    .build();

  constructor() {
    this.hub.on('State', (snapshot: TvSnapshot) => this.accept(snapshot));
    this.hub.onreconnecting(() => this.connected.set(false));
    this.hub.onreconnected(() => this.connected.set(true));
    this.hub.onclose(() => {
      this.connected.set(false);
      void this.connect();
    });
    void this.connect();
  }

  channelUp = () => this.send('ChannelUp');
  channelDown = () => this.send('ChannelDown');
  lastChannel = () => this.send('LastChannel');
  goToChannel = (number: number) => this.send('GoToChannel', number);
  digit = (digit: number) => this.send('Digit', digit);
  confirm = () => this.send('Confirm');
  info = () => this.send('Info');
  volumeUp = () => this.send('VolumeUp');
  volumeDown = () => this.send('VolumeDown');
  mute = () => this.send('Mute');
  power = () => this.send('Power');

  loadTapes = (shelves: TapeShelf[]) => this.send('LoadTapes', shelves);
  ejectTapes = () => this.send('EjectTapes');

  episodeEnded = (token: string) => this.send('EpisodeEnded', token);
  episodeFailed = (token: string) => this.send('EpisodeFailed', token);
  reportPosition = (token: string, seconds: number, duration: number | null) =>
    this.send('ReportPosition', token, seconds, duration);

  /**
   * Two presses made at the same instant are broadcast separately and can
   * arrive in either order, so never step back to an older snapshot. A new
   * boot id means the box restarted and its counter began again.
   */
  private accept(snapshot: TvSnapshot): void {
    const current = this.state();
    if (current && current.boot === snapshot.boot && snapshot.revision < current.revision) return;
    this.state.set(snapshot);
  }

  private async connect(): Promise<void> {
    while (this.hub.state === HubConnectionState.Disconnected) {
      try {
        await this.hub.start();
        this.connected.set(true);
      } catch {
        await new Promise(resolve => setTimeout(resolve, RETRY_MS));
      }
    }
  }

  private send(method: string, ...args: unknown[]): void {
    // A press made while the box is unreachable is dropped rather than queued:
    // a burst of stale channel changes on reconnect would be worse than nothing.
    if (this.hub.state !== HubConnectionState.Connected) return;
    this.hub.send(method, ...args).catch(() => undefined);
  }
}
