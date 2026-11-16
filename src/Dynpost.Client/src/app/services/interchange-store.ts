import { Injectable, inject, signal } from '@angular/core';
import { Bridge, BridgeError } from './bridge';
import { CurlShell, ExportFormatDto } from '../models/messages';
import { fromRequestDto, toRequestDto } from '../models/request-mapping';
import { copyText } from '../models/clipboard';
import { REQUESTS_FOLDER, RequestDocument } from '../models/workspace';
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
  { id: 'openapi', name: 'OpenAPI 3.0', extension: '.openapi.json' },
  { id: 'har', name: 'HAR 1.2', extension: '.har' },
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

  /** The shell cURL commands are written for: PowerShell on Windows, else bash/zsh, until changed. */
  readonly curlShell = signal<CurlShell>(
    typeof navigator !== 'undefined' && /windows/i.test(navigator.userAgent)
      ? 'powershell'
      : 'posix',
  );

  /**
   * Replaces the method, URL, parameters, headers, auth, body and settings of the open request
   * `id` with those of a pasted cURL command; its name, docs and captures stay.
   */
  async importCurl(id: string, command: string): Promise<boolean> {
    try {
      const parsed = await this.bridge.send('request.parseCurl', { command });
      const content = fromRequestDto(parsed.request);
      this.workspace.update(id, {
        method: content.method,
        url: content.url,
        pathParams: content.pathParams,
        params: content.params,
        headers: content.headers,
        auth: content.auth,
        bodyType: content.bodyType,
        body: content.body,
        bodyVariables: content.bodyVariables,
        bodyFields: content.bodyFields,
        settings: content.settings,
      });
      this.notice.set({
        message: 'Filled in the request from the cURL command.',
        warnings: parsed.warnings,
      });
      this.workspace.dismissError();
      return true;
    } catch (error) {
      this.report(error);
      return false;
    }
  }

  /** Opens a new request filled in from a cURL command. */
  async newRequestFromCurl(command: string, folder = REQUESTS_FOLDER): Promise<boolean> {
    this.workspace.newRequest(folder);
    const doc = this.workspace.documents().at(-1);
    return doc ? this.importCurl(doc.id, command) : false;
  }

  /** Copies the request in the editor as a cURL command; the result says whether it was copied. */
  async copyAsCurl(
    doc: RequestDocument,
    options: { resolve: boolean; includeSecrets: boolean },
  ): Promise<boolean> {
    try {
      const command = await this.bridge.send('request.copyAsCurl', {
        request: toRequestDto(doc),
        path: doc.path,
        folder: doc.folder,
        resolve: options.resolve,
        includeSecrets: options.includeSecrets,
        shell: this.curlShell(),
      });
      if (!(await copyText(command))) throw new Error('Could not copy to the clipboard.');
      this.notice.set({
        message: options.resolve
          ? options.includeSecrets
            ? 'Copied the request as a cURL command, with its secret values.'
            : 'Copied the request as a cURL command (secret values and credentials are masked).'
          : 'Copied the request as a cURL command, with its {{variables}} as written.',
        warnings: [],
      });
      return true;
    } catch (error) {
      this.report(error);
      return false;
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
