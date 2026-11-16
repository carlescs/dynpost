import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faCertificate,
  faFloppyDisk,
  faFolderOpen,
  faPlus,
  faTrash,
} from '@fortawesome/free-solid-svg-icons';
import { ClientCertificateDto } from '../../../../models/messages';
import { Bridge } from '../../../../services/bridge';
import { WorkspaceStore } from '../../../../services/workspace-store';

/** A certificate row as edited: the passphrase typed (`null` keeps the stored one). */
interface CertificateRow {
  host: string;
  certificate: string;
  key: string;
  hasPassphrase: boolean;
  passphrase: string | null;
}

/**
 * The client certificates (mutual TLS) of an environment: for each host, a PKCS#12 or PEM
 * certificate (and key) presented when the server asks for one. They stay on this computer, in
 * `.dynpost/certificates.json`, and are saved on their own, apart from the environment file.
 */
@Component({
  selector: 'app-client-certificates',
  imports: [FaIconComponent],
  templateUrl: './client-certificates.html',
})
export class ClientCertificates {
  private readonly bridge = inject(Bridge);
  private readonly workspace = inject(WorkspaceStore);

  /** The environment's id (its file name). */
  readonly environmentId = input.required<string>();

  protected readonly rows = signal<CertificateRow[]>([]);
  protected readonly dirty = signal(false);
  protected readonly saving = signal(false);

  protected readonly faCertificate = faCertificate;
  protected readonly faFloppyDisk = faFloppyDisk;
  protected readonly faFolderOpen = faFolderOpen;
  protected readonly faPlus = faPlus;
  protected readonly faTrash = faTrash;

  private loadCount = 0;

  constructor() {
    effect(() => {
      const id = this.environmentId();
      untracked(() => void this.load(id));
    });
  }

  protected add(): void {
    this.rows.update((rows) => [
      ...rows,
      { host: '', certificate: '', key: '', hasPassphrase: false, passphrase: null },
    ]);
    this.dirty.set(true);
  }

  protected remove(index: number): void {
    this.rows.update((rows) => rows.filter((_, i) => i !== index));
    this.dirty.set(true);
  }

  protected patch(index: number, patch: Partial<CertificateRow>): void {
    this.rows.update((rows) => rows.map((row, i) => (i === index ? { ...row, ...patch } : row)));
    this.dirty.set(true);
  }

  protected async pick(index: number, field: 'certificate' | 'key'): Promise<void> {
    try {
      const file = await this.bridge.send('request.pickFile');
      if (file) this.patch(index, { [field]: file.path });
    } catch (error) {
      this.report(error);
    }
  }

  protected async save(): Promise<void> {
    const id = this.environmentId();
    this.saving.set(true);
    try {
      const saved = await this.bridge.send('certificate.save', {
        environment: id,
        certificates: this.rows().map((row) => ({
          host: row.host,
          certificate: row.certificate,
          key: row.key || null,
          passphrase: row.passphrase,
        })),
      });
      if (id === this.environmentId()) {
        this.rows.set(saved.map(toRow));
        this.dirty.set(false);
      }
    } catch (error) {
      this.report(error);
    } finally {
      this.saving.set(false);
    }
  }

  private async load(id: string): Promise<void> {
    const load = ++this.loadCount;
    try {
      const certificates = await this.bridge.send('certificate.list', { environment: id });
      if (load !== this.loadCount) return;
      this.rows.set(certificates.map(toRow));
      this.dirty.set(false);
    } catch (error) {
      this.report(error);
    }
  }

  private report(error: unknown): void {
    this.workspace.error.set(error instanceof Error ? error.message : String(error));
  }
}

function toRow(dto: ClientCertificateDto): CertificateRow {
  return {
    host: dto.host,
    certificate: dto.certificate,
    key: dto.key ?? '',
    hasPassphrase: dto.hasPassphrase ?? false,
    passphrase: null,
  };
}
