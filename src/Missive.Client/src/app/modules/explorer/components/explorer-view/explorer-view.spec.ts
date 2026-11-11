import { TestBed } from '@angular/core/testing';
import { Bridge } from '../../../../services/bridge';
import { WorkspaceStore } from '../../../../services/workspace-store';
import { ExplorerView } from './explorer-view';

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
});
