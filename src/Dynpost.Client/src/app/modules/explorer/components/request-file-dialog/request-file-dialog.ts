import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { Component, inject, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faCheck, faCopy, faXmark } from '@fortawesome/free-solid-svg-icons';
import { CodeEditor } from '../../../../components/code-editor/code-editor';
import { RequestDto } from '../../../../models/messages';
import { copyText } from '../../../../models/clipboard';
import { Bridge } from '../../../../services/bridge';

/** The request whose file to show. */
export interface RequestFileData {
  name: string;
  request: RequestDto;
  /** The saved file to show as on disk; `null` shows what saving the request would write. */
  path: string | null;
  /** The file the request is (or will be) saved in, for the caption. */
  location: string;
}

/**
 * Shows the YAML of a request's file, read-only: the file on disk for a saved request without
 * unsaved changes, otherwise the text saving it would write. Opened with `Dialogs.open`.
 */
@Component({
  selector: 'app-request-file-dialog',
  imports: [FaIconComponent, CodeEditor],
  templateUrl: './request-file-dialog.html',
})
export class RequestFileDialog {
  private readonly bridge = inject(Bridge);
  protected readonly data = inject<RequestFileData>(DIALOG_DATA);
  protected readonly ref = inject(DialogRef<void>);

  protected readonly text = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly copied = signal(false);
  protected readonly faXmark = faXmark;
  protected readonly faCopy = faCopy;
  protected readonly faCheck = faCheck;

  constructor() {
    this.bridge.send('request.viewFile', { request: this.data.request, path: this.data.path }).then(
      (text) => this.text.set(text),
      (error: unknown) => this.error.set(error instanceof Error ? error.message : String(error)),
    );
  }

  protected async copy(): Promise<void> {
    const text = this.text();
    if (text !== null && (await copyText(text))) this.copied.set(true);
  }
}
