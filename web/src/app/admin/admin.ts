import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';

import { BoxApi } from '../core/box-api';
import { AdminCatalog, AdminChannel } from '../core/models';

/** Home lineup editor. The server rejects every management operation in demo mode. */
@Component({
  selector: 'ears-admin',
  imports: [FormsModule, RouterLink],
  templateUrl: './admin.html',
  styleUrl: './admin.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(window:beforeunload)': 'onBeforeUnload($event)' },
})
export class Admin {
  private readonly api = inject(BoxApi);

  protected readonly catalog = signal<AdminCatalog | null>(null);
  protected readonly available = signal<boolean | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly notice = signal('');

  /** The settings as the server last confirmed them, to compare edits against. */
  private readonly saved = signal('');
  /**
   * The form edits the settings objects in place, which a signal cannot see,
   * so each edit bumps this and `dirty` looks again.
   */
  private readonly edits = signal(0);
  /** True only while the lineup differs from what is saved, so undoing an edit clears it. */
  protected readonly dirty = computed(() => {
    this.edits();
    const catalog = this.catalog();
    return !!catalog && settingsOf(catalog) !== this.saved();
  });

  constructor() {
    void this.load();
  }

  /** Asked by the router before leaving for another page of the app. */
  canLeave(): boolean {
    return !this.dirty() || confirm('Leave without saving your lineup changes?');
  }

  /** Closing or reloading the tab with unsaved edits gets the browser's own warning. */
  protected onBeforeUnload(event: BeforeUnloadEvent): void {
    if (this.dirty()) event.preventDefault();
  }

  protected edited(): void {
    this.edits.update(count => count + 1);
  }

  protected async load(): Promise<void> {
    await this.perform(async () => {
      const enabled = await this.api.adminEnabled();
      this.available.set(enabled);
      if (enabled) this.accept(await this.api.channels());
    });
  }

  protected async save(): Promise<void> {
    const catalog = this.catalog();
    if (!catalog) return;
    await this.perform(async () => {
      try {
        this.accept(
          await this.api.saveChannels(
            catalog.version,
            catalog.channels.map(c => c.settings),
          ),
        );
      } catch (error) {
        throw new Error(`${message(error)} Your edits have been kept.`, { cause: error });
      }
      this.notice.set('Lineup saved. The TV and remotes have been updated.');
    });
  }

  protected async rescan(): Promise<void> {
    await this.perform(async () => {
      this.accept(await this.api.rescan());
      this.notice.set('Media folders rescanned.');
    });
  }

  protected seasons(channel: AdminChannel): string[] {
    return [...new Set(channel.episodes.map(e => e.season).filter(Boolean))];
  }

  protected toggle(channel: AdminChannel, kind: 'excludedEpisodes' | 'excludedSeasons', id: string): void {
    const values = channel.settings[kind];
    channel.settings[kind] = values.includes(id) ? values.filter(value => value !== id) : [...values, id];
    this.edited();
  }

  private accept(catalog: AdminCatalog): void {
    this.saved.set(settingsOf(catalog));
    this.catalog.set(catalog);
  }

  private async perform(action: () => Promise<void>): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    try {
      await action();
    } catch (error) {
      this.error.set(message(error));
    } finally {
      this.busy.set(false);
    }
  }
}

/** The editable part of a lineup, in a form two lineups can be compared by. */
function settingsOf(catalog: AdminCatalog): string {
  return JSON.stringify(catalog.channels.map(channel => channel.settings));
}

function message(error: unknown): string {
  return error instanceof Error ? error.message : 'Could not reach the box.';
}
