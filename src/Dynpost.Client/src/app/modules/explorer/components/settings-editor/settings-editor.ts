import { Component, computed, input, output } from '@angular/core';
import { RequestSettingsDto } from '../../../../models/messages';

/** The defaults the host applies when neither the request nor a folder sets a value. */
export const DEFAULT_SETTINGS: Required<{
  [K in keyof RequestSettingsDto]: NonNullable<RequestSettingsDto[K]>;
}> = {
  timeoutMs: 60_000,
  followRedirects: true,
  maxRedirects: 20,
  verifyTls: true,
  httpVersion: '1.1',
};

/** How many settings `settings` sets (the rest are inherited). */
export function settingsCount(settings: RequestSettingsDto | null | undefined): number {
  return settings ? Object.values(settings).filter((v) => v !== null && v !== undefined).length : 0;
}

/** A timeout for people: `60 s`, `500 ms`, or `no timeout`. */
export function describeTimeout(ms: number): string {
  if (ms === 0) return 'no timeout';
  return ms % 1000 === 0 || ms >= 10_000 ? `${+(ms / 1000).toFixed(1)} s` : `${ms} ms`;
}

/**
 * How a request (or every request below a folder) is sent: timeout, redirects, TLS certificate
 * checks and HTTP version. An empty value inherits the folders' setting, or the default; the
 * inherited value is shown next to each.
 */
@Component({
  selector: 'app-settings-editor',
  templateUrl: './settings-editor.html',
})
export class SettingsEditor {
  readonly settings = input.required<RequestSettingsDto>();
  /** The values the folders above set; `null` while unknown. */
  readonly inherited = input<RequestSettingsDto | null>(null);
  /** `request` or `folder`, for the texts. */
  readonly subject = input<'request' | 'folder'>('request');
  readonly settingsChange = output<RequestSettingsDto>();

  /** What applies when this level sets nothing: the folders' value, or the default. */
  protected readonly fallback = computed(() => {
    const inherited = this.inherited() ?? {};
    return {
      timeoutMs: inherited.timeoutMs ?? DEFAULT_SETTINGS.timeoutMs,
      followRedirects: inherited.followRedirects ?? DEFAULT_SETTINGS.followRedirects,
      maxRedirects: inherited.maxRedirects ?? DEFAULT_SETTINGS.maxRedirects,
      verifyTls: inherited.verifyTls ?? DEFAULT_SETTINGS.verifyTls,
      httpVersion: inherited.httpVersion ?? DEFAULT_SETTINGS.httpVersion,
    };
  });

  /** "Inherited" when a folder sets the value, else "Default". */
  protected source(key: keyof RequestSettingsDto): string {
    const value = this.inherited()?.[key];
    return value !== null && value !== undefined ? 'Inherited' : 'Default';
  }

  protected readonly describeTimeout = describeTimeout;

  protected timeoutSeconds(): string {
    const ms = this.settings().timeoutMs;
    return ms === null || ms === undefined ? '' : String(ms / 1000);
  }

  protected setTimeout(text: string): void {
    const seconds = text.trim() === '' ? null : Number(text);
    this.patch({
      timeoutMs:
        seconds === null || !Number.isFinite(seconds) || seconds < 0
          ? null
          : Math.round(seconds * 1000),
    });
  }

  protected setMaxRedirects(text: string): void {
    const value = text.trim() === '' ? null : Number(text);
    this.patch({
      maxRedirects: value === null || !Number.isInteger(value) || value < 0 ? null : value,
    });
  }

  /** `''` (inherit), `true` or `false` as a select's value. */
  protected flag(value: boolean | null | undefined): string {
    return value === null || value === undefined ? '' : String(value);
  }

  protected setFlag(key: 'followRedirects' | 'verifyTls', value: string): void {
    this.patch({ [key]: value === '' ? null : value === 'true' });
  }

  protected setHttpVersion(value: string): void {
    this.patch({ httpVersion: value === '' ? null : value });
  }

  protected reset(): void {
    this.settingsChange.emit({});
  }

  protected isEmpty(): boolean {
    return settingsCount(this.settings()) === 0;
  }

  private patch(patch: Partial<RequestSettingsDto>): void {
    const next: RequestSettingsDto = { ...this.settings(), ...patch };
    for (const key of Object.keys(next) as (keyof RequestSettingsDto)[])
      if (next[key] === null || next[key] === undefined) delete next[key];
    this.settingsChange.emit(next);
  }
}
