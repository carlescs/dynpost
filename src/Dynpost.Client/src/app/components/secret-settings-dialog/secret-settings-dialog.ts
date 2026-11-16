import { DialogRef } from '@angular/cdk/dialog';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faXmark } from '@fortawesome/free-solid-svg-icons';
import { Bridge, BridgeError } from '../../services/bridge';
import { SecretStorage } from '../../models/messages';

/**
 * Where secret variable values are kept — the workspace's `.dynpost/secrets.yaml` or the
 * operating system's credential store — and whether secrets without a value take one from the
 * workspace's `.env` file. Per user (the host's settings file). Opened with `Dialogs.open`.
 */
@Component({
  selector: 'app-secret-settings-dialog',
  imports: [FaIconComponent],
  templateUrl: './secret-settings-dialog.html',
})
export class SecretSettingsDialog implements OnInit {
  private readonly bridge = inject(Bridge);

  protected readonly ref = inject(DialogRef);
  protected readonly loaded = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly storage = signal<SecretStorage>('file');
  protected readonly useDotEnv = signal(false);
  protected readonly keychainAvailable = signal(false);
  protected readonly keychainName = signal('the system keychain');
  protected readonly faXmark = faXmark;

  async ngOnInit(): Promise<void> {
    try {
      const settings = await this.bridge.send('secrets.getSettings');
      this.storage.set(settings.storage);
      this.useDotEnv.set(settings.useDotEnv);
      this.keychainAvailable.set(settings.keychainAvailable);
      this.keychainName.set(settings.keychainName);
      this.loaded.set(true);
    } catch (error) {
      this.error.set(error instanceof BridgeError ? error.message : String(error));
    }
  }

  protected async save(): Promise<void> {
    this.saving.set(true);
    this.error.set(null);
    try {
      await this.bridge.send('secrets.setSettings', {
        storage: this.storage(),
        useDotEnv: this.useDotEnv(),
      });
      this.ref.close();
    } catch (error) {
      this.error.set(error instanceof BridgeError ? error.message : String(error));
    } finally {
      this.saving.set(false);
    }
  }
}
