import { TestBed } from '@angular/core/testing';
import { Bridge } from '../../../../services/bridge';
import { WorkspaceStore } from '../../../../services/workspace-store';
import { ExplorerView, placeMenu } from './explorer-view';

describe('ExplorerView', () => {
  function setup() {
    const send = vi.fn().mockResolvedValue(undefined);
    TestBed.configureTestingModule({
      imports: [ExplorerView],
      providers: [{ provide: Bridge, useValue: { isDesktop: true, send } }],
    });
    const store = TestBed.inject(WorkspaceStore);
    store.workspace.set({ name: 'api', root: '/home/me/api' });
    const fixture = TestBed.createComponent(ExplorerView);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    const more = root.querySelector<HTMLButtonElement>('button[aria-label="More actions"]')!;
    return { fixture, store, root, more };
  }

  it('shows the workspace in the file manager from the more actions menu', () => {
    const { fixture, store, root, more } = setup();
    const reveal = vi.spyOn(store, 'revealFolder').mockResolvedValue();
    expect(root.querySelector('#explorer-more-menu')).toBeNull();

    more.click();
    fixture.detectChanges();
    const item = root.querySelector<HTMLButtonElement>('#explorer-more-menu [role="menuitem"]')!;
    expect(item.textContent).toContain('Show in');
    expect(item.title).toContain('/home/me/api');
    item.click();
    fixture.detectChanges();

    expect(reveal).toHaveBeenCalled();
    expect(root.querySelector('#explorer-more-menu')).toBeNull();
  });

  it('closes the more actions menu on a click elsewhere', () => {
    const { fixture, root, more } = setup();

    more.click();
    fixture.detectChanges();
    expect(more.getAttribute('aria-expanded')).toBe('true');
    document.body.click();
    fixture.detectChanges();

    expect(root.querySelector('#explorer-more-menu')).toBeNull();
  });

  it('keeps a folder row to a few buttons and puts the rest in its menu', () => {
    const { fixture, store, root } = setup();
    store.tree.set([{ path: 'requests/users', name: 'users', kind: 'folder', children: [] }]);
    fixture.detectChanges();
    const rowButtons = [
      ...root.querySelectorAll<HTMLButtonElement>('[role="tree"] button[aria-label]'),
    ];
    expect(rowButtons.map((b) => b.getAttribute('aria-label'))).toEqual([
      'New request in users',
      'New folder in users',
      'More actions for users',
    ]);

    rowButtons[2].click();
    fixture.detectChanges();
    const menu = root.querySelector<HTMLElement>('[role="menu"][aria-label="users"]')!;
    const items = [...menu.querySelectorAll('[role="menuitem"]')].map((i) => i.textContent!.trim());
    expect(items).toEqual([
      'New request',
      'New folder',
      'Import into this folder…',
      'Folder settings',
      'RenameF2',
      'Duplicate',
      'DeleteDel',
    ]);
    expect(rowButtons[2].getAttribute('aria-expanded')).toBe('true');

    const rename = menu.querySelectorAll<HTMLButtonElement>('[role="menuitem"]')[4];
    rename.click();
    fixture.detectChanges();
    expect(root.querySelector('[role="menu"][aria-label="users"]')).toBeNull();
    expect(root.querySelector('input[aria-label="Folder name"]')).not.toBeNull();
  });

  it('closes a folder menu when its button is clicked again', () => {
    const { fixture, store, root } = setup();
    store.tree.set([{ path: 'requests/users', name: 'users', kind: 'folder', children: [] }]);
    fixture.detectChanges();
    const more = root.querySelector<HTMLButtonElement>('[aria-label="More actions for users"]')!;

    more.click();
    fixture.detectChanges();
    more.click();
    fixture.detectChanges();

    expect(root.querySelector('[role="menu"][aria-label="users"]')).toBeNull();
  });
});

describe('placeMenu', () => {
  const viewport = { width: 800, height: 600 };

  it('opens below the point when the menu fits', () => {
    expect(placeMenu({ x: 100, y: 200, above: 180 }, 240, 200, viewport)).toEqual({
      x: 100,
      y: 200,
    });
  });

  it('opens upwards when it does not fit below', () => {
    expect(placeMenu({ x: 100, y: 500, above: 480 }, 240, 200, viewport)).toEqual({
      x: 100,
      y: 280,
    });
  });

  it('stays inside the window', () => {
    expect(placeMenu({ x: 700, y: 100, above: 100 }, 240, 900, viewport)).toEqual({ x: 556, y: 4 });
  });
});
