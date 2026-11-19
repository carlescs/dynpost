import { Injectable, inject, signal } from '@angular/core';
import { UpdateStatus } from '../models/messages';
import { Bridge } from './bridge';
import { WorkspaceStore } from './workspace-store';

/** What the last check the user asked for found, shown in the settings menu. */
export interface UpdateCheckResult {
  ok: boolean;
  message: string;
}

/**
 * App updates. The host looks for a newer version once at startup and downloads it in the
 * background; the user can also check again from the settings menu (`checkNow`). An update is
 * only installed when the user chooses to restart.
 */
@Injectable({ providedIn: 'root' })
export class UpdateStore {
  private readonly bridge = inject(Bridge);
  private readonly workspace = inject(WorkspaceStore);
  /** The check in progress, shared so a second check never runs alongside it. */
  private pending: Promise<UpdateStatus> | null = null;

  /** Only the desktop host can look for updates; the browser preview cannot. */
  readonly canCheck = this.bridge.isDesktop;
  /** The downloaded version waiting to be installed, if any. */
  readonly readyVersion = signal<string | null>(null);
  readonly restarting = signal(false);
  readonly checking = signal(false);
  /** Whether this copy can update itself; `null` until a check has answered. */
  readonly supported = signal<boolean | null>(null);
  /** The outcome of the last `checkNow`, cleared when another starts. */
  readonly lastCheck = signal<UpdateCheckResult | null>(null);

  /** The check at startup: failures are only logged. */
  async check(): Promise<void> {
    if (!this.bridge.isDesktop) return;
    try {
      await this.request();
    } catch (error) {
      // Being offline or rate-limited is not worth interrupting the user for.
      console.warn('Update check failed', error);
    }
  }

  /** A check the user asked for: its outcome, failures included, goes to `lastCheck`. */
  async checkNow(): Promise<void> {
    if (!this.bridge.isDesktop) return;
    this.lastCheck.set(null);
    try {
      const status = await this.request();
      this.lastCheck.set(
        status.state === 'upToDate'
          ? {
              ok: true,
              message: status.currentVersion
                ? `Dynpost ${status.currentVersion} is up to date`
                : 'Dynpost is up to date',
            }
          : null, // Ready shows as "Restart to update"; unsupported disables the entry.
      );
    } catch (error) {
      this.lastCheck.set({
        ok: false,
        message: error instanceof Error ? error.message : 'Could not check for updates',
      });
    }
  }

  async restart(): Promise<void> {
    this.restarting.set(true);
    try {
      // On success the app exits and restarts into the new version.
      await this.bridge.send('update.restart');
    } catch (error) {
      this.workspace.error.set(`Could not install the update: ${(error as Error).message}`);
    } finally {
      this.restarting.set(false);
    }
  }

  private request(): Promise<UpdateStatus> {
    if (!this.pending) {
      this.checking.set(true);
      this.pending = this.bridge
        .send('update.check')
        .then((status) => {
          this.supported.set(status.state !== 'unsupported');
          this.readyVersion.set(status.state === 'ready' ? (status.version ?? null) : null);
          return status;
        })
        .finally(() => {
          this.pending = null;
          this.checking.set(false);
        });
    }
    return this.pending;
  }
}
