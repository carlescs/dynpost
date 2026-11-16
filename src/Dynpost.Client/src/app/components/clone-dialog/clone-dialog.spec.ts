import { DialogRef } from '@angular/cdk/dialog';
import { TestBed } from '@angular/core/testing';
import { WorkspaceStore } from '../../services/workspace-store';
import { CloneDialog } from './clone-dialog';

describe('CloneDialog', () => {
  async function setup(cloneResult: string | false | null = null) {
    const store = {
      pickCloneFolder: vi.fn().mockResolvedValue('/home/me/src'),
      cloneRepository: vi.fn().mockResolvedValue(cloneResult),
    };
    const close = vi.fn();
    TestBed.configureTestingModule({
      imports: [CloneDialog],
      providers: [
        { provide: WorkspaceStore, useValue: store },
        { provide: DialogRef, useValue: { close } },
      ],
    });
    const fixture = TestBed.createComponent(CloneDialog);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    const settle = async () => {
      await fixture.whenStable();
      fixture.detectChanges();
    };
    const input = (name: string) => root.querySelector<HTMLInputElement>(`input[name="${name}"]`)!;
    const type = (name: string, value: string) => {
      input(name).value = value;
      input(name).dispatchEvent(new Event('input'));
      fixture.detectChanges();
    };
    const button = (label: string) =>
      [...root.querySelectorAll('button')].find((b) => b.textContent?.trim() === label)!;
    return { root, store, close, settle, input, type, button };
  }

  it('names the folder after the repository and clones into the chosen folder', async () => {
    const { root, store, close, settle, input, type, button } = await setup();
    type('url', 'git@github.com:team/my-api.git');
    expect(input('name').value).toBe('my-api');
    expect(button('Clone').disabled).toBe(true);

    button('Browse…').click();
    await settle();
    expect(root.textContent).toContain('/home/me/src/my-api');

    button('Clone').click();
    await settle();

    expect(store.cloneRepository).toHaveBeenCalledWith(
      'git@github.com:team/my-api.git',
      '/home/me/src',
      'my-api',
    );
    expect(close).toHaveBeenCalledWith(true);
  });

  it('keeps a folder name the user typed', async () => {
    const { store, settle, input, type, button } = await setup();
    type('url', 'https://example.com/a.git');
    type('name', 'mine');
    type('url', 'https://example.com/b.git');
    expect(input('name').value).toBe('mine');

    button('Browse…').click();
    await settle();
    button('Clone').click();
    await settle();

    expect(store.cloneRepository).toHaveBeenCalledWith(
      'https://example.com/b.git',
      '/home/me/src',
      'mine',
    );
  });

  it('stays open and shows why cloning failed', async () => {
    const { root, close, settle, type, button } = await setup('Authentication failed.');
    type('url', 'https://example.com/private.git');
    button('Browse…').click();
    await settle();
    button('Clone').click();
    await settle();

    expect(root.querySelector('[role="alert"]')!.textContent).toContain('Authentication failed.');
    expect(close).not.toHaveBeenCalled();
  });
});
