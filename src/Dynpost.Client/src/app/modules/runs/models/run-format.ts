import { RunState } from '../../../models/messages';

/** Reads the short durations of run plans (`500ms`, `10s`, `2m`, `1h`; a bare number is seconds). */
export function parseDuration(text: string): number | null {
  const match = /^(\d+(?:\.\d+)?)\s*(ms|s|m|h)?$/.exec(text.trim().toLowerCase());
  if (!match) return null;
  const scale = { ms: 1, s: 1000, m: 60_000, h: 3_600_000 }[match[2] ?? 's']!;
  return Number(match[1]) * scale;
}

/** A latency: `85 ms`, `1.25 s`. */
export function formatMs(ms: number): string {
  if (ms < 1000) return `${Math.round(ms)} ms`;
  return `${(ms / 1000).toFixed(ms < 10_000 ? 2 : 1)} s`;
}

/** How long something took: `6.0 s`, `2m 05s`, `1h 03m`. */
export function formatElapsed(ms: number): string {
  if (ms < 60_000) return `${(ms / 1000).toFixed(1)} s`;
  const seconds = Math.floor(ms / 1000);
  const pad = (value: number) => String(value).padStart(2, '0');
  return seconds < 3600
    ? `${Math.floor(seconds / 60)}m ${pad(seconds % 60)}s`
    : `${Math.floor(seconds / 3600)}h ${pad(Math.floor((seconds % 3600) / 60))}m`;
}

export function formatBytes(bytes: number): string {
  const units = ['B', 'KB', 'MB', 'GB'];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }
  return `${unit === 0 ? Math.round(value) : value.toFixed(1)} ${units[unit]}`;
}

/** The smallest "round" number (1, 2, 2.5, 5 × a power of ten) at or above the value. */
export function niceMax(value: number): number {
  if (value <= 0) return 1;
  const power = 10 ** Math.floor(Math.log10(value));
  return [1, 2, 2.5, 5, 10].map((step) => step * power).find((step) => step >= value)!;
}

/** The fill of a status code by its class: 2xx, 3xx, 4xx, 5xx or anything else. */
export function statusFill(code: string | number | null): string {
  switch (String(code ?? '')[0]) {
    case '2':
      return 'bg-emerald-500';
    case '3':
      return 'bg-sky-500';
    case '4':
      return 'bg-amber-500';
    case '5':
      return 'bg-rose-500';
    default:
      return 'bg-slate-400';
  }
}

export const RUN_STATE_LABELS: Record<RunState, string> = {
  running: 'Running',
  completed: 'Completed',
  cancelled: 'Stopped',
  failed: 'Failed',
};

/** Badge colors (tint, border and text) of each run state. */
export const RUN_STATE_COLORS: Record<RunState, string> = {
  running: 'border-sky-200 bg-sky-50 text-sky-700',
  completed: 'border-emerald-200 bg-emerald-50 text-emerald-700',
  cancelled: 'border-amber-200 bg-amber-50 text-amber-700',
  failed: 'border-rose-200 bg-rose-50 text-rose-700',
};
