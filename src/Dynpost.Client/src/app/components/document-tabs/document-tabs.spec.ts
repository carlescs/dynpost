import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { RequestDocument } from '../../models/workspace';
import { Dialogs } from '../../services/dialogs';
import { EnvironmentStore } from '../../services/environment-store';
import { FolderSettingsStore } from '../../services/folder-settings-store';
import { RunStore } from '../../services/run-store';
import { WorkspaceStore } from '../../services/workspace-store';
import { DocumentTabs } from './document-tabs';

describe('DocumentTabs', () => {
  const doc = (key: string, dirty = false) =>
    ({
      key,
      id: key,
      name: key.toUpperCase(),
      method: 'GET',
      path: `requests/${key}.yaml`,
      dirty,
    }) as RequestDocument;

  let tabs: ReturnType<typeof signal<string[]>>;
  let activeTab: ReturnType<typeof signal<string | null>>;
  let close: ReturnType<typeof vi.fn>;
  let activateTab: ReturnType<typeof vi.fn>;
  let confirm: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    tabs = signal(['a', 'b', 'c', 'd']);
    activeTab = signal<string | null>('a');
    // Closing a dirty request with a question is refused, as if it was cancelled.
    const documents = signal([doc('a'), doc('b', true), doc('c'), doc('d')]);
    close = vi.fn(async (id: string, ask = true) => {
      if (ask && documents().find((d) => d.id === id)?.dirty) return;
      tabs.update((t) => t.filter((k) => k !== id));
    });
    confirm = vi.fn(async () => true);
    activateTab = vi.fn((key: string) => activeTab.set(key));
    const empty = { drafts: signal([]), close: vi.fn(), nameOf: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        {
          provide: WorkspaceStore,
          useValue: { tabs, activeTab, documents, close, activateTab, newRequest: vi.fn() },
        },
        { provide: EnvironmentStore, useValue: empty },
        { provide: RunStore, useValue: empty },
        { provide: FolderSettingsStore, useValue: empty },
        { provide: Dialogs, useValue: { confirm } },
      ],
    });
    fixture = TestBed.createComponent(DocumentTabs);
    await fixture.whenStable();
  });

  let fixture: ReturnType<typeof TestBed.createComponent<DocumentTabs>>;
  const element = () => fixture.nativeElement as HTMLElement;
  const tabNames = () =>
    Array.from(element().querySelectorAll('[role=tab]')).map((t) => t.textContent!.trim());
  const rightClick = async (key: string) => {
    element()
      .querySelector(`[data-tab-key="${key}"]`)!
      .dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }));
    await fixture.whenStable();
  };
  const menuItem = (label: string) =>
    Array.from(element().querySelectorAll<HTMLButtonElement>('[role=menuitem]')).find(
      (b) => b.textContent!.trim() === label,
    )!;
  const choose = async (label: string) => {
    menuItem(label).click();
    await fixture.whenStable();
  };

  it('shows the open tabs', () => {
    expect(tabNames()).toEqual(['GETA', 'GETB', 'GETC', 'GETD']);
    expect(element().querySelector('[role=menu]')).toBeNull();
  });

  it('opens a menu with the close commands on a right click', async () => {
    await rightClick('d');
    expect(
      Array.from(element().querySelectorAll('[role=menuitem]')).map((b) => b.textContent!.trim()),
    ).toEqual(['Close', 'Close others', 'Close to the right', 'Close saved', 'Close all']);
    expect(menuItem('Close to the right').disabled).toBe(true);
  });

  it('closes the other tabs and shows the one the menu was opened on', async () => {
    await rightClick('c');
    await choose('Close others');
    expect(confirm).toHaveBeenCalledWith('Discard unsaved changes to "B"?', expect.anything());
    expect(close.mock.calls).toEqual([
      ['a', false],
      ['b', false],
      ['d', false],
    ]);
    expect(tabs()).toEqual(['c']);
    expect(activateTab).toHaveBeenLastCalledWith('c');
    expect(element().querySelector('[role=menu]')).toBeNull();
  });

  it('closes nothing when discarding the unsaved changes is cancelled', async () => {
    confirm.mockResolvedValue(false);
    await rightClick('a');
    await choose('Close all');
    expect(close).not.toHaveBeenCalled();
    expect(tabs()).toEqual(['a', 'b', 'c', 'd']);
  });

  it('closes the tabs to the right without asking when they are saved', async () => {
    await rightClick('b');
    await choose('Close to the right');
    expect(confirm).not.toHaveBeenCalled();
    expect(tabs()).toEqual(['a', 'b']);
  });

  it('closes only the tabs without unsaved changes', async () => {
    await rightClick('a');
    await choose('Close saved');
    expect(close.mock.calls.map(([id]) => id)).toEqual(['a', 'c', 'd']);
    expect(confirm).not.toHaveBeenCalled();
    expect(tabs()).toEqual(['b']);
  });

  it('closes every tab after one question about the unsaved ones', async () => {
    await rightClick('a');
    await choose('Close all');
    expect(confirm).toHaveBeenCalledTimes(1);
    expect(tabs()).toEqual([]);
  });

  it('closes a tab with a middle click', async () => {
    element()
      .querySelector('[data-tab-key="c"]')!
      .dispatchEvent(new MouseEvent('auxclick', { button: 1, bubbles: true, cancelable: true }));
    await fixture.whenStable();
    expect(close).toHaveBeenCalledWith('c', true);
  });

  it('closes the menu on Escape', async () => {
    await rightClick('a');
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    await fixture.whenStable();
    expect(element().querySelector('[role=menu]')).toBeNull();
  });
});
