import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { EnvironmentDraft, EnvironmentStore } from '../../../../services/environment-store';
import { EnvironmentEditor } from './environment-editor';

describe('EnvironmentEditor', () => {
  // Unsaved, so the cookie, certificate and captured value sections (which read files) are hidden.
  const draft: EnvironmentDraft = {
    id: null,
    key: 'tab-1',
    name: 'Dev',
    variables: [],
    cookieJar: false,
    color: 'green',
    dirty: false,
    saving: false,
    revision: 0,
  };

  function render(value: EnvironmentDraft) {
    const store = { update: vi.fn(), activeId: signal<string | null>(null) };
    TestBed.configureTestingModule({
      imports: [EnvironmentEditor],
      providers: [{ provide: EnvironmentStore, useValue: store }],
    });
    const fixture = TestBed.createComponent(EnvironmentEditor);
    fixture.componentRef.setInput('draft', value);
    fixture.detectChanges();
    return { root: fixture.nativeElement as HTMLElement, store };
  }

  it('marks the chosen color and sets another one', () => {
    const { root, store } = render(draft);

    expect(root.querySelector('[aria-label="Green"]')!.getAttribute('aria-checked')).toBe('true');
    expect(root.querySelector('[aria-label="Red"]')!.getAttribute('aria-checked')).toBe('false');

    root.querySelector<HTMLButtonElement>('[aria-label="Red"]')!.click();
    expect(store.update).toHaveBeenCalledWith('tab-1', { color: 'red' });

    root.querySelector<HTMLButtonElement>('[aria-label="No color"]')!.click();
    expect(store.update).toHaveBeenCalledWith('tab-1', { color: null });
  });

  it('offers no color for the globals', () => {
    const { root } = render({ ...draft, id: ':globals', name: 'Globals' });

    expect(root.querySelector('[aria-label="Environment color"]')).toBeNull();
  });
});
