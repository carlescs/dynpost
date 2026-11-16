import { Dialogs } from '../../services/dialogs';

/**
 * Opens the clone dialog over the shared backdrop. The dialog is loaded on first use, so it stays
 * out of the initial bundle.
 */
export async function openCloneDialog(dialogs: Dialogs): Promise<void> {
  const { CloneDialog } = await import('./clone-dialog');
  dialogs.open<boolean>(CloneDialog, { ariaLabelledBy: 'clone-title' });
}
