import { Dialog } from '@angular/cdk/dialog';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { ConfirmDialog } from '../components/confirm-dialog/confirm-dialog';
import { DIALOG_BACKDROP_CLASSES, Dialogs } from './dialogs';

describe('Dialogs', () => {
  it('opens a confirmation over a blurred backdrop and resolves to the answer', async () => {
    const closed = new Subject<boolean | undefined>();
    const open = vi.fn().mockReturnValue({ closed });
    TestBed.configureTestingModule({ providers: [{ provide: Dialog, useValue: { open } }] });
    const dialogs = TestBed.inject(Dialogs);

    const answer = dialogs.confirm('Delete it?', { danger: true });
    expect(open).toHaveBeenCalledWith(
      ConfirmDialog,
      expect.objectContaining({
        backdropClass: DIALOG_BACKDROP_CLASSES,
        data: { message: 'Delete it?', danger: true },
        role: 'alertdialog',
      }),
    );
    expect(DIALOG_BACKDROP_CLASSES).toContain('backdrop-blur-sm');

    closed.next(true);
    expect(await answer).toBe(true);
  });

  it('treats a dialog dismissed with Escape or the backdrop as cancelled', async () => {
    const closed = new Subject<boolean | undefined>();
    TestBed.configureTestingModule({
      providers: [{ provide: Dialog, useValue: { open: () => ({ closed }) } }],
    });

    const answer = TestBed.inject(Dialogs).confirm('Sure?');
    closed.next(undefined);
    expect(await answer).toBe(false);
  });
});
