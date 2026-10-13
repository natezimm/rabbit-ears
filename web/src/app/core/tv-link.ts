import { Injectable, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';

import { TvSnapshot } from './models';

const RETRY_MS = 2000;

/**
 * The live connection to the box. Every screen and remote shares the same
 * state: they send button presses and draw whatever comes back, so two remotes
 * can never disagree about what is on.
 */
@Injectable({ providedIn: 'root' })
export class TvLink {
  readonly state = signal<TvSnapshot | null>(null);
  readonly connected = signal(false);

  private readonly hub: HubConnection = new HubConnectionBuilder()
    .withUrl('/hub/tv')
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

  episodeEnded = (token: string) => this.send('EpisodeEnded', token);
  episodeFailed = (token: string) => this.send('EpisodeFailed', token);
  reportPosition = (token: string, seconds: number) => this.send('ReportPosition', token, seconds);

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
