import { Injectable, inject, signal } from '@angular/core';
import { Bridge, BridgeError } from './bridge';
import { ExportFormatDto } from '../models/messages';
import { REQUESTS_FOLDER } from '../models/workspace';
import { EnvironmentStore } from './environment-store';
import { WorkspaceStore } from './workspace-store';

/** The outcome of an import or export, shown to the user until dismissed. */
export interface InterchangeNotice {
  message: string;
  /** What was skipped or not carried over exactly. */
  warnings: string[];
}

/** Used until the host lists its formats (and if it can't). */
const DEFAULT_FORMATS: ExportFormatDto[] = [
  { id: 'postman', name: 'Postman collection v2.1', extension: '.postman_collection.json' },
  { id: 'insomnia', name: 'Insomnia v4', extension: '.insomnia.json' },
];

/**
 * Imports files from other API clients and exports the workspace for them. The host shows the
 * file dialogs and does all the reading and writing; this store reports what happened.
 */
@Injectable({ providedIn: 'root' })
export class InterchangeStore {
  private readonly bridge = inject(Bridge);
  private readonly workspace = inject(WorkspaceStore);
  private readonly environments = inject(EnvironmentStore);

  readonly formats = signal<ExportFormatDto[]>(DEFAULT_FORMATS);
  readonly busy = signal(false);
  readonly notice = signal<InterchangeNotice | null>(null);

  private formatsLoaded = false;

  async loadFormats(): Promise<void> {
    if (this.formatsLoaded) return;
    try {
      const formats = await this.bridge.send('workspace.exportFormats');
      if (formats.length) this.formats.set(formats);
      this.formatsLoaded = true;
    } catch {
      // Keep the defaults.
    }
  }

  /** Asks for a file and imports it into a new folder inside `folder` (by default the top of `requests`). */
  async importInto(folder = REQUESTS_FOLDER): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    try {
      const result = await this.bridge.send('workspace.import', { folder });
      if (!result) return;
      const parts = [plural(result.requests, 'request')];
      if (result.environments.length) {
        parts.push(plural(result.environments.length, 'environment'));
      }
      const where = result.folder ? ` into "${result.folder}"` : '';
      this.notice.set({
        message: `Imported ${parts.join(' and ')}${where} (${result.format}).`,
        warnings: result.warnings,
      });
      this.workspace.dismissError();
      await this.workspace.refresh();
      if (result.environments.length) await this.environments.refresh();
    } catch (error) {
      this.report(error);
    } finally {
      this.busy.set(false);
    }
  }

  /** Asks where to save and exports the workspace in `format`. */
  async export(format: string, includeEnvironments: boolean): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    try {
      const result = await this.bridge.send('workspace.export', { format, includeEnvironments });
      if (!result) return;
      const extra = result.files.length - 1;
      const parts = [plural(result.requests, 'request')];
      if (includeEnvironments) parts.push(plural(result.environments, 'environment'));
      this.notice.set({
        message:
          `Exported ${parts.join(' and ')} to ${result.path}` +
          (extra > 0 ? ` (and ${plural(extra, 'environment file')} next to it).` : '.'),
        warnings: result.warnings,
      });
      this.workspace.dismissError();
    } catch (error) {
      this.report(error);
    } finally {
      this.busy.set(false);
    }
  }

  dismiss(): void {
    this.notice.set(null);
  }

  private report(error: unknown): void {
    this.notice.set(null);
    this.workspace.error.set(
      error instanceof BridgeError || error instanceof Error ? error.message : String(error),
    );
  }
}

function plural(count: number, noun: string): string {
  return `${count} ${noun}${count === 1 ? '' : 's'}`;
}
