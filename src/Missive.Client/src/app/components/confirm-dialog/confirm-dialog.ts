import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { Component, inject } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faTriangleExclamation } from '@fortawesome/free-solid-svg-icons';

/** What a confirmation dialog asks. */
export interface ConfirmOptions {
  message: string;
  /** Heading above the message; defaults to "Are you sure?". */
  title?: string;
  /** Label of the confirming button; defaults to "OK". */
  confirmLabel?: string;
  /** Label of the cancelling button; defaults to "Cancel". `null` leaves the button out. */
  cancelLabel?: string | null;
  /** Styles the confirming button as destructive. */
  danger?: boolean;
}

/** A yes/no question, opened by `Dialogs.confirm`; closes with `true` when confirmed. */
@Component({
  selector: 'app-confirm-dialog',
  imports: [FaIconComponent],
  templateUrl: './confirm-dialog.html',
})
export class ConfirmDialog {
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
  protected readonly data = inject<ConfirmOptions>(DIALOG_DATA);
  protected readonly faTriangleExclamation = faTriangleExclamation;
}
