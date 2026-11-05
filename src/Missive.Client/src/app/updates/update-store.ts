import { Injectable, inject, signal } from '@angular/core';
import { Bridge } from '../bridge/bridge';
import { BridgeErrorShape } from '../bridge/messages';

/**
 * App updates. The host looks for a newer version once at startup and downloads it in the
 * background; it is only installed when the user chooses to restart.
 */
@Injectable({ providedIn: 'root' })
export class UpdateStore {
  private readonly bridge = inject(Bridge);

  /** The downloaded version waiting to be installed, if any. */
  readonly readyVersion = signal<string | null>(null);
  readonly restarting = signal(false);
  /** Why the last restart failed, until the next attempt. */
  readonly error = signal<BridgeErrorShape | null>(null);

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
    this.error.set(null);
    try {
      // On success the app exits and restarts into the new version.
      await this.bridge.send('update.restart');
    } catch (error) {
      this.error.set(error as BridgeErrorShape);
    } finally {
      this.restarting.set(false);
    }
  }
}
