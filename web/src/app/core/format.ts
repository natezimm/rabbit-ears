import { TvSnapshot } from './models';

/** A channel number as the set shows it: always two digits, like the dial. */
export function formatChannel(number: number): string {
  return String(number).padStart(2, '0');
}

/**
 * The volume as a row of lit and unlit segments. Muted shows none lit. Each
 * display picks its own count: the TV's on-screen meter has 25 steps like a
 * classic set's, and the remote's readout has 20 because it is phone-narrow.
 */
export function volumeSegments(state: TvSnapshot | null, count: number): boolean[] {
  const lit = !state || state.muted ? 0 : Math.round((state.volume / 100) * count);
  return Array.from({ length: count }, (_, index) => index < lit);
}
