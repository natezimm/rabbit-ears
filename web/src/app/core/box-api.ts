import { Injectable } from '@angular/core';

import { AdminCatalog, ChannelEdit, DemoCredit, PictureConfig } from './models';

/** A request to the box that did not succeed, with words fit to show the viewer. */
export class BoxApiError extends Error {
  constructor(
    message: string,
    readonly status: number | null,
  ) {
    super(message);
  }
}

/**
 * The box's plain HTTP endpoints, as typed calls. Live TV state does not come
 * through here: that arrives over the hub (`TvLink`). Every call either
 * returns its result or throws a `BoxApiError`; callers decide whether a
 * failure is worth showing or whether a default will do.
 */
@Injectable({ providedIn: 'root' })
export class BoxApi {
  private pictureRequest: Promise<PictureConfig> | null = null;

  /** The picture settings. Asked once per page load and shared, since they change only on a restart. */
  picture(): Promise<PictureConfig> {
    this.pictureRequest ??= this.request<PictureConfig>('/api/picture').catch(error => {
      this.pictureRequest = null;
      throw error;
    });
    return this.pictureRequest;
  }

  credits(): Promise<DemoCredit[]> {
    return this.request<DemoCredit[]>('/api/demo/credits');
  }

  /** Whether this box allows managing channels; the public demo never does. */
  async adminEnabled(): Promise<boolean> {
    return (await this.admin<{ enabled: boolean }>('/api/admin/status')).enabled;
  }

  channels(): Promise<AdminCatalog> {
    return this.admin<AdminCatalog>('/api/admin/channels');
  }

  saveChannels(version: string, channels: ChannelEdit[]): Promise<AdminCatalog> {
    return this.admin<AdminCatalog>('/api/admin/channels', 'PUT', { version, channels });
  }

  rescan(): Promise<AdminCatalog> {
    return this.admin<AdminCatalog>('/api/admin/rescan', 'POST');
  }

  /** Admin calls carry the header the server checks, and are never answered from a cache. */
  private admin<T>(url: string, method = 'GET', body?: unknown): Promise<T> {
    return this.request<T>(url, {
      method,
      cache: 'no-store',
      headers: { 'Content-Type': 'application/json', 'X-RabbitEars-Admin': '1' },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  }

  private async request<T>(url: string, init?: RequestInit): Promise<T> {
    let response: Response;
    try {
      response = await fetch(url, init);
    } catch {
      throw new BoxApiError('Could not reach the box.', null);
    }
    if (!response.ok) {
      const problem = await response.json().catch(() => ({}));
      throw new BoxApiError(problem.error ?? problem.detail ?? `Request failed (${response.status}).`, response.status);
    }
    return response.json() as Promise<T>;
  }
}
