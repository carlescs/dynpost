import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { Component, inject, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faXmark } from '@fortawesome/free-solid-svg-icons';
import { InterchangeStore } from '../../../../services/interchange-store';
import { REQUESTS_FOLDER } from '../../../../models/workspace';

/** Where the new request goes. */
export interface CurlImportData {
  folder?: string;
}

/**
 * Asks for a `curl …` command and opens it as a new (unsaved) request. Opened with
 * `Dialogs.open`; closes with `true` when the request was opened.
 */
@Component({
  selector: 'app-curl-import-dialog',
  imports: [FaIconComponent],
  templateUrl: './curl-import-dialog.html',
})
export class CurlImportDialog {
  private readonly interchange = inject(InterchangeStore);
  private readonly data = inject<CurlImportData | null>(DIALOG_DATA, { optional: true });

  protected readonly ref = inject(DialogRef<boolean>);
  protected readonly command = signal('');
  protected readonly busy = signal(false);
  protected readonly faXmark = faXmark;

  protected async open(): Promise<void> {
    if (!this.command().trim() || this.busy()) return;
    this.busy.set(true);
    const opened = await this.interchange.newRequestFromCurl(
      this.command(),
      this.data?.folder ?? REQUESTS_FOLDER,
    );
    this.busy.set(false);
    if (opened) this.ref.close(true);
  }
}
