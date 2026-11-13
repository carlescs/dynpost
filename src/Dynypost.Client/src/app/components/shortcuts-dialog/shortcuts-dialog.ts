import { DialogRef } from '@angular/cdk/dialog';
import { Component, inject } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faXmark } from '@fortawesome/free-solid-svg-icons';
import { SHORTCUTS, formatCombo } from '../../models/shortcuts';
import { Bridge } from '../../services/bridge';

/** Lists the keyboard shortcuts. Opened with `Dialogs.open` (F1 or the settings menu). */
@Component({
  selector: 'app-shortcuts-dialog',
  imports: [FaIconComponent],
  templateUrl: './shortcuts-dialog.html',
})
export class ShortcutsDialog {
  private readonly isDesktop = inject(Bridge).isDesktop;

  protected readonly ref = inject(DialogRef);
  protected readonly shortcuts = SHORTCUTS.filter((s) => !s.desktopOnly || this.isDesktop);
  protected readonly format = formatCombo;
  protected readonly faXmark = faXmark;
}
