import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminCatalog, AdminChannel } from '../core/models';

/** Home lineup editor. The server rejects every management operation in demo mode. */
@Component({
  selector: 'ears-admin',
  imports: [FormsModule],
  templateUrl: './admin.html',
  styleUrl: './admin.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Admin {
  protected readonly catalog = signal<AdminCatalog | null>(null);
  protected readonly available = signal<boolean | null>(null);
  protected readonly busy = signal(false);
  protected readonly dirty = signal(false);
  protected readonly error = signal('');
  protected readonly notice = signal('');

  constructor() { void this.load(); }

  protected async load(): Promise<void> {
    await this.perform(async () => {
      const status = await this.request<{ enabled: boolean }>('/api/admin/status');
      this.available.set(status.enabled);
      if (status.enabled) {
        this.catalog.set(await this.request<AdminCatalog>('/api/admin/channels'));
        this.dirty.set(false);
      }
    });
  }

  protected async save(): Promise<void> {
    const catalog = this.catalog();
    if (!catalog) return;
    await this.perform(async () => {
      this.catalog.set(await this.request<AdminCatalog>('/api/admin/channels', 'PUT', {
        version: catalog.version, channels: catalog.channels.map(c => c.settings),
      }));
      this.dirty.set(false);
      this.notice.set('Lineup saved. The TV and remotes have been updated.');
    });
  }

  protected async rescan(): Promise<void> {
    await this.perform(async () => {
      this.catalog.set(await this.request<AdminCatalog>('/api/admin/rescan', 'POST'));
      this.notice.set('Media folders rescanned.');
    });
  }

  protected seasons(channel: AdminChannel): string[] {
    return [...new Set(channel.episodes.map(e => e.season).filter(Boolean))];
  }

  protected toggle(channel: AdminChannel, kind: 'excludedEpisodes' | 'excludedSeasons', id: string): void {
    const values = channel.settings[kind];
    channel.settings[kind] = values.includes(id) ? values.filter(value => value !== id) : [...values, id];
    this.dirty.set(true);
  }

  private async perform(action: () => Promise<void>): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    try { await action(); }
    catch (error) { this.error.set(error instanceof Error ? error.message : 'Could not reach the box.'); }
    finally { this.busy.set(false); }
  }

  private async request<T>(url: string, method = 'GET', body?: unknown): Promise<T> {
    const response = await fetch(url, {
      method, cache: 'no-store',
      headers: { 'Content-Type': 'application/json', 'X-RabbitEars-Admin': '1' },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    if (!response.ok) {
      const problem = await response.json().catch(() => ({}));
      throw new Error(problem.error ?? problem.detail ?? `Request failed (${response.status}). Your edits have been kept.`);
    }
    return response.json() as Promise<T>;
  }
}
