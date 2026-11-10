import { Dialog, DialogConfig, DialogRef } from '@angular/cdk/dialog';
import { ComponentType } from '@angular/cdk/portal';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfirmDialog, ConfirmOptions } from '../components/confirm-dialog/confirm-dialog';

/** Tailwind classes of every dialog's backdrop: dims and blurs the app behind it. */
export const DIALOG_BACKDROP_CLASSES = ['bg-black/40', 'backdrop-blur-sm'];

/**
 * Opens modal dialogs (Angular CDK `Dialog`) over a dimmed, blurred backdrop. Use it instead of
 * `window.confirm`/`alert`, which block the webview and can't be styled.
 */
@Injectable({ providedIn: 'root' })
export class Dialogs {
  private readonly dialog = inject(Dialog);

  /** Opens `component` as a modal dialog. Escape and a click on the backdrop close it. */
  open<R, D = unknown, C = unknown>(
    component: ComponentType<C>,
    config: DialogConfig<D, DialogRef<R, C>> = {},
  ): DialogRef<R, C> {
    return this.dialog.open<R, D, C>(component, {
      backdropClass: DIALOG_BACKDROP_CLASSES,
      ...config,
    });
  }

  /** Asks a yes/no question; resolves to whether the user confirmed. */
  async confirm(message: string, options: Omit<ConfirmOptions, 'message'> = {}): Promise<boolean> {
    const ref = this.open<boolean, ConfirmOptions>(ConfirmDialog, {
      data: { ...options, message },
      role: 'alertdialog',
      ariaLabelledBy: 'confirm-dialog-title',
      ariaDescribedBy: 'confirm-dialog-message',
    });
    return (await firstValueFrom(ref.closed)) === true;
  }
}

/** Options of the questions that discard unsaved edits. */
export const DISCARD: Omit<ConfirmOptions, 'message'> = {
  title: 'Unsaved changes',
  confirmLabel: 'Discard',
  danger: true,
};
