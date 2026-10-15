// Mirrors the records in RabbitEars.Core/Models.cs and the options in BoxOptions.cs.
// ModelSyncTests.cs checks this file against them, so `dotnet test` catches drift.

export interface ChannelInfo {
  number: number;
  name: string;
}

export interface NowPlaying {
  /** Unique per play request; echoed back in reports so stale ones are ignored. */
  token: string;
  episodeId: string;
  title: string;
  /** Seconds into the episode to start at. */
  start: number;
  reason: 'tune' | 'advance' | 'wake';
}

export interface OsdMessage {
  seq: number;
  text: string;
  /** 0 means stay up until replaced. */
  seconds: number;
}

export interface ChannelListing {
  number: number;
  name: string;
  episodes: number;
  title: string | null;
}

export interface TvSnapshot {
  boot: string;
  revision: number;
  standby: boolean;
  poweredOff: boolean;
  channel: ChannelInfo | null;
  playing: NowPlaying | null;
  volume: number;
  muted: boolean;
  /** Ticks whenever the channel banner should flash. */
  bannerSeq: number;
  /** Ticks whenever the volume bar should flash. */
  volumeSeq: number;
  message: OsdMessage | null;
  channels: ChannelListing[];
}

export interface CrtConfig {
  enabled: boolean;
  curvature: number;
  cornerRadius: number;
  vignette: number;
  scanlines: boolean;
  scanlineIntensity: number;
}

export interface PictureConfig {
  transition: 'none' | 'static' | 'glitch';
  transitionSeconds: number;
  bridgeSeconds: number;
  bannerSeconds: number;
  osdSeconds: number;
  crt: CrtConfig;
  osd: { color: string; glow: boolean };
}

/** Used until /api/picture answers; same values as the server defaults. */
export const DEFAULT_PICTURE: PictureConfig = {
  transition: 'none',
  transitionSeconds: 0.4,
  bridgeSeconds: 0.8,
  bannerSeconds: 4,
  osdSeconds: 2,
  crt: {
    enabled: true,
    curvature: 0.12,
    cornerRadius: 0.065,
    vignette: 0.25,
    scanlines: true,
    scanlineIntensity: 0.12,
  },
  osd: { color: '#4DFF5A', glow: true },
};
