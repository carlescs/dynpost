import { TestBed } from '@angular/core/testing';
import { Bridge } from '../../services/bridge';
import { WorkspaceStore } from '../../services/workspace-store';
import { AppHeader } from './app-header';

describe('AppHeader', () => {
  function setup() {
    const recent = [{ name: 'other', path: '/home/me/other' }];
    const send = vi.fn().mockResolvedValue(recent);
    TestBed.configureTestingModule({
      imports: [AppHeader],
      providers: [{ provide: Bridge, useValue: { isDesktop: true, send } }],
    });
    const store = TestBed.inject(WorkspaceStore);
    store.recent.set(recent);
    const fixture = TestBed.createComponent(AppHeader);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    return { fixture, store, send };
  }

  const toggle = (root: HTMLElement) =>
    root.querySelector<HTMLButtonElement>('button[aria-controls="recent-panel"]')!;

  it('shows the open folder button next to the toggle without opening the panel', () => {
    const { fixture, store } = setup();
    const root: HTMLElement = fixture.nativeElement;
    const openFolder = vi.spyOn(store, 'openFolder').mockResolvedValue();

    const button = root.querySelector<HTMLButtonElement>('[aria-label="Open workspace folder"]')!;
    expect(button.parentElement).toBe(toggle(root).parentElement);
    button.click();

    expect(openFolder).toHaveBeenCalled();
    expect(root.querySelector('#recent-panel')).toBeNull();
  });

  it('opens the recent workspaces when the toggle is clicked', async () => {
    const { fixture } = setup();
    const root: HTMLElement = fixture.nativeElement;

    toggle(root).click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(root.querySelector('#recent-panel')?.textContent).toContain('/home/me/other');
  });

  it('opens the chosen workspace and closes the panel', async () => {
    const { fixture, store } = setup();
    const root: HTMLElement = fixture.nativeElement;
    const open = vi.spyOn(store, 'openRecent').mockResolvedValue();

    toggle(root).click();
    fixture.detectChanges();
    root.querySelector<HTMLButtonElement>('#recent-panel [role="menuitem"]')!.click();
    fixture.detectChanges();

    expect(open).toHaveBeenCalledWith('/home/me/other');
    expect(root.querySelector('#recent-panel')).toBeNull();
  });

  it('closes the panel on a click elsewhere', () => {
    const { fixture } = setup();
    const root: HTMLElement = fixture.nativeElement;

    toggle(root).click();
    fixture.detectChanges();
    document.body.click();
    fixture.detectChanges();

    expect(root.querySelector('#recent-panel')).toBeNull();
  });

  it('closes the panel when the last recent workspace is removed', async () => {
    const { fixture, send } = setup();
    const root: HTMLElement = fixture.nativeElement;

    toggle(root).click();
    await fixture.whenStable();
    fixture.detectChanges();
    send.mockResolvedValue(undefined);
    root.querySelector<HTMLButtonElement>('#recent-panel [aria-label^="Remove"]')!.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(send).toHaveBeenCalledWith('workspace.recent.remove', { path: '/home/me/other' });
    expect(root.querySelector('#recent-panel')).toBeNull();
    expect(root.querySelector('button[aria-controls="recent-panel"]')).toBeNull();
    expect(document.activeElement?.getAttribute('aria-label')).toBe('Open workspace folder');
  });
});
