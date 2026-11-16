import { Dialogs } from '../../services/dialogs';
import { CloneDialog } from './clone-dialog';
import { openCloneDialog } from './open-clone-dialog';

describe('openCloneDialog', () => {
  it('opens the clone dialog labelled by its title', async () => {
    const open = vi.fn();

    await openCloneDialog({ open } as unknown as Dialogs);

    expect(open).toHaveBeenCalledWith(CloneDialog, { ariaLabelledBy: 'clone-title' });
  });
});
