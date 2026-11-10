import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { TestBed } from '@angular/core/testing';
import { ConfirmDialog, ConfirmOptions } from './confirm-dialog';

describe('ConfirmDialog', () => {
  function render(data: ConfirmOptions) {
    const close = vi.fn();
    TestBed.configureTestingModule({
      imports: [ConfirmDialog],
      providers: [
        { provide: DIALOG_DATA, useValue: data },
        { provide: DialogRef, useValue: { close } },
      ],
    });
    const fixture = TestBed.createComponent(ConfirmDialog);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    return { el, close };
  }

  it('shows the question and closes with the answer', () => {
    const { el, close } = render({ message: 'Delete it?', title: 'Delete', confirmLabel: 'Yes' });

    expect(el.querySelector('h2')!.textContent).toContain('Delete');
    expect(el.querySelector('[data-testid="confirm-message"]')!.textContent).toContain(
      'Delete it?',
    );
    const ok = el.querySelector<HTMLButtonElement>('[data-testid="confirm-ok"]')!;
    expect(ok.textContent).toContain('Yes');
    ok.click();
    expect(close).toHaveBeenLastCalledWith(true);

    el.querySelector<HTMLButtonElement>('[data-testid="confirm-cancel"]')!.click();
    expect(close).toHaveBeenLastCalledWith(false);
  });

  it('uses default labels and a destructive style when asked', () => {
    const { el } = render({ message: 'Sure?', danger: true });

    expect(el.querySelector('h2')!.textContent).toContain('Are you sure?');
    const ok = el.querySelector<HTMLButtonElement>('[data-testid="confirm-ok"]')!;
    expect(ok.textContent).toContain('OK');
    expect(ok.classList).toContain('bg-rose-600');
  });
});
