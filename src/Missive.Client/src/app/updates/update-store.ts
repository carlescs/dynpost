import { Injectable, inject, signal } from '@angular/core';
import { Bridge } from '../bridge/bridge';
import { WorkspaceStore } from '../workspace/workspace-store';

/**
 * App updates. The host looks for a newer version once at startup and downloads it in the
 * background; it is only installed when the user chooses to restart.
 */
@Injectable({ providedIn: 'root' })
export class UpdateStore {
  private readonly bridge = inject(Bridge);
  private readonly workspace = inject(WorkspaceStore);

  /** The downloaded version waiting to be installed, if any. */
  readonly readyVersion = signal<string | null>(null);
  readonly restarting = signal(false);

  async check(): Promise<void> {
    if (!this.bridge.isDesktop) return;
    try {
      const status = await this.bridge.send('update.check');
      this.readyVersion.set(status.state === 'ready' ? (status.version ?? null) : null);
    } catch (error) {
      // Being offline or rate-limited is not worth interrupting the user for.
      console.warn('Update check failed', error);
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
}
