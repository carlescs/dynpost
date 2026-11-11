import { DialogRef } from '@angular/cdk/dialog';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faXmark } from '@fortawesome/free-solid-svg-icons';
import { Bridge, BridgeError } from '../../services/bridge';

/** The most entries and the largest body (in KB) the host accepts. */
export const MAX_HISTORY_ENTRIES = 500;
export const MAX_HISTORY_BODY_KB = 50 * 1024;

/**
 * Settings of the response history: whether responses are kept, how many per request and how
 * large a body may be. They are per user (the host's settings file). Opened with `Dialogs.open`.
 */
@Component({
  selector: 'app-history-settings-dialog',
  imports: [FaIconComponent],
  templateUrl: './history-settings-dialog.html',
})
export class HistorySettingsDialog implements OnInit {
  private readonly bridge = inject(Bridge);

  protected readonly ref = inject(DialogRef);
  protected readonly loaded = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly enabled = signal(true);
  protected readonly maxEntries = signal(20);
  protected readonly maxBodyKb = signal(1024);
  protected readonly maxEntriesLimit = MAX_HISTORY_ENTRIES;
  protected readonly maxBodyKbLimit = MAX_HISTORY_BODY_KB;
  protected readonly faXmark = faXmark;

  async ngOnInit(): Promise<void> {
    try {
      const { history } = await this.bridge.send('settings.get');
      this.enabled.set(history.enabled);
      this.maxEntries.set(history.maxEntries);
      this.maxBodyKb.set(Math.round(history.maxBodyBytes / 1024));
      this.loaded.set(true);
    } catch (error) {
      this.error.set(error instanceof BridgeError ? error.message : String(error));
    }
  }

  protected async save(): Promise<void> {
    const maxEntries = Math.trunc(this.maxEntries());
    const maxBodyKb = Math.trunc(this.maxBodyKb());
    if (!(maxEntries >= 1 && maxEntries <= MAX_HISTORY_ENTRIES)) {
      this.error.set(`Keep between 1 and ${MAX_HISTORY_ENTRIES} responses per request.`);
      return;
    }
    if (!(maxBodyKb >= 0 && maxBodyKb <= MAX_HISTORY_BODY_KB)) {
      this.error.set(`The body size limit must be between 0 and ${MAX_HISTORY_BODY_KB} KB.`);
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    try {
      await this.bridge.send('settings.setHistory', {
        enabled: this.enabled(),
        maxEntries,
        maxBodyBytes: maxBodyKb * 1024,
      });
      this.ref.close();
    } catch (error) {
      this.error.set(error instanceof BridgeError ? error.message : String(error));
    } finally {
      this.saving.set(false);
    }
  }
}
