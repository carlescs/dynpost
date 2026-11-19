import { TestBed } from '@angular/core/testing';
import { Bridge } from '../../services/bridge';
import { EnvironmentStore } from '../../services/environment-store';
import { Shortcuts } from '../../services/shortcuts';
import { UpdateStore } from '../../services/update-store';
import { WindowFrame } from '../../services/window-frame';
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

  it('marks the header and the picker with the selected environment’s color', async () => {
    const { fixture, store, send } = setup();
    let active: string | null = null;
    send.mockImplementation(async (type: string) => {
      switch (type) {
        case 'environment.list':
          return { active, environments: [{ id: 'prod.yaml', name: 'Prod', color: 'red' }] };
        case 'environment.load':
          return { name: 'Prod', variables: [], color: 'red' };
        case 'globals.load':
        case 'capture.list':
          return { variables: [] };
        default:
          return [];
      }
    });
    const root: HTMLElement = fixture.nativeElement;
    store.workspace.set({ name: 'api', root: '/home/me/api' } as never);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.querySelector('[data-environment-stripe]')).toBeNull();

    active = 'prod.yaml';
    await TestBed.inject(EnvironmentStore).refresh();
    fixture.detectChanges();

    expect(root.querySelector('[data-environment-stripe]')!.classList).toContain('bg-red-500');
    expect(root.querySelector('select[aria-label="Environment"]')!.classList).toContain(
      'border-red-500',
    );
  });

  it('shows the open workspace name with its path', () => {
    const { fixture, store } = setup();
    store.workspace.set({ name: 'api', root: '/home/me/api' });
    fixture.detectChanges();

    const name = fixture.nativeElement.querySelector('span[title="/home/me/api"]');
    expect(name.textContent).toContain('api');
    expect(fixture.nativeElement.querySelector('button[aria-label^="Show in"]')).toBeNull();
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

  it('draws window buttons when it is the title bar, and closes through quit', () => {
    const { fixture } = setup();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('[aria-label="Window"]')).toBeNull();
    const frame = TestBed.inject(WindowFrame);
    const run = vi.spyOn(TestBed.inject(Shortcuts), 'run').mockResolvedValue();
    const minimize = vi.spyOn(frame, 'minimize').mockReturnValue();

    frame.state.set({ frame: 'custom', platform: 'windows', maximized: true });
    fixture.detectChanges();

    root.querySelector<HTMLButtonElement>('[aria-label="Minimize"]')!.click();
    expect(minimize).toHaveBeenCalled();
    expect(root.querySelector('[aria-label="Restore"]')).not.toBeNull();
    root.querySelector<HTMLButtonElement>('[aria-label="Close"]')!.click();
    expect(run).toHaveBeenCalledWith('quit');
  });

  it('offers the system title bar in the settings menu of the desktop app', () => {
    const { fixture } = setup();
    const root: HTMLElement = fixture.nativeElement;
    const frame = TestBed.inject(WindowFrame);
    const setNative = vi.spyOn(frame, 'setNativeTitleBar').mockResolvedValue();
    frame.state.set({ frame: 'custom', platform: 'linux', maximized: false });
    root.querySelector<HTMLButtonElement>('[aria-label="Settings"]')!.click();
    fixture.detectChanges();

    const toggle = [...root.querySelectorAll<HTMLButtonElement>('[role="switch"]')].find((b) =>
      b.textContent?.includes('System title bar'),
    )!;
    expect(toggle.getAttribute('aria-checked')).toBe('false');
    toggle.click();

    expect(setNative).toHaveBeenCalledWith(true);
  });

  it('offers a ready update in the settings menu and marks the settings button', () => {
    const { fixture } = setup();
    const root: HTMLElement = fixture.nativeElement;
    const updates = TestBed.inject(UpdateStore);
    const restart = vi.spyOn(updates, 'restart').mockResolvedValue();
    const settings = root.querySelector<HTMLButtonElement>('[aria-label="Settings"]')!;
    expect(root.querySelector('[data-update-badge]')).toBeNull();
    expect(root.querySelector('[aria-label="Restart to update"]')).toBeNull();

    updates.readyVersion.set('1.2.3');
    fixture.detectChanges();

    expect(settings.querySelector('[data-update-badge]')).not.toBeNull();
    expect(settings.getAttribute('aria-describedby')).toBe('update-badge');
    settings.click();
    fixture.detectChanges();
    const entry = [...root.querySelectorAll<HTMLButtonElement>('#settings-panel button')].find(
      (b) => b.textContent?.includes('Restart to update'),
    )!;
    expect(entry.textContent).toContain('Dynpost 1.2.3 is ready');
    entry.click();
    fixture.detectChanges();

    expect(restart).toHaveBeenCalled();
    expect(root.querySelector('#settings-panel')).toBeNull();
  });
  function openSettings(fixture: { nativeElement: HTMLElement; detectChanges(): void }) {
    fixture.nativeElement.querySelector<HTMLButtonElement>('[aria-label="Settings"]')!.click();
    fixture.detectChanges();
  }

  const checkEntry = (root: HTMLElement) =>
    [...root.querySelectorAll<HTMLButtonElement>('#settings-panel button')].find((b) =>
      b.textContent?.includes('for updates'),
    );

  it('checks for updates on demand and shows the result', async () => {
    const { fixture, send } = setup();
    const root: HTMLElement = fixture.nativeElement;
    let answer!: (status: unknown) => void;
    send.mockImplementation((type: string) =>
      type === 'update.check' ? new Promise((resolve) => (answer = resolve)) : Promise.resolve([]),
    );
    openSettings(fixture);

    checkEntry(root)!.click();
    fixture.detectChanges();
    expect(send).toHaveBeenCalledWith('update.check');
    expect(checkEntry(root)!.disabled).toBe(true);
    expect(checkEntry(root)!.textContent).toContain('Checking for updates…');

    answer({ state: 'upToDate', currentVersion: '1.0.0' });
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();
    expect(checkEntry(root)!.disabled).toBe(false);
    expect(root.querySelector('[data-update-check-note]')!.textContent).toContain(
      'Dynpost 1.0.0 is up to date',
    );
  });

  it('offers the restart when a check on demand downloads an update', async () => {
    const { fixture, send } = setup();
    const root: HTMLElement = fixture.nativeElement;
    send.mockImplementation(async (type: string) =>
      type === 'update.check' ? { state: 'ready', currentVersion: '1.0.0', version: '1.1.0' } : [],
    );
    openSettings(fixture);

    checkEntry(root)!.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(checkEntry(root)).toBeUndefined();
    expect(root.querySelector('#settings-panel')!.textContent).toContain('Dynpost 1.1.0 is ready');
  });

  it('shows why a check on demand failed', async () => {
    const { fixture, send } = setup();
    const root: HTMLElement = fixture.nativeElement;
    send.mockImplementation(async (type: string) => {
      if (type === 'update.check') throw new Error('Could not check for updates: offline');
      return [];
    });
    openSettings(fixture);

    checkEntry(root)!.click();
    await fixture.whenStable();
    fixture.detectChanges();

    const note = root.querySelector('[data-update-check-note]')!;
    expect(note.textContent).toContain('offline');
    expect(note.classList).toContain('text-rose-700');
  });

  it('disables the check for copies that cannot update themselves', () => {
    const { fixture } = setup();
    const root: HTMLElement = fixture.nativeElement;
    TestBed.inject(UpdateStore).supported.set(false);
    openSettings(fixture);

    expect(checkEntry(root)!.disabled).toBe(true);
    expect(checkEntry(root)!.textContent).toContain('Only installed copies can update themselves');
  });
});
