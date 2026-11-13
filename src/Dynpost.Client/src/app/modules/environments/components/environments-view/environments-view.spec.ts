import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { EnvironmentStore } from '../../../../services/environment-store';
import { WorkspaceStore } from '../../../../services/workspace-store';
import { EnvironmentsView } from './environments-view';

describe('EnvironmentsView', () => {
  let store: any;

  function render() {
    TestBed.configureTestingModule({
      providers: [
        { provide: EnvironmentStore, useValue: store },
        { provide: WorkspaceStore, useValue: { workspace: signal({ path: '/ws' }) } },
      ],
    });
    const fixture = TestBed.createComponent(EnvironmentsView);
    fixture.detectChanges();
    return fixture;
  }

  function button(fixture: { nativeElement: HTMLElement }, label: string): HTMLButtonElement {
    return fixture.nativeElement.querySelector(`[aria-label="${label}"]`)!;
  }

  beforeEach(() => {
    store = {
      environments: signal([
        { id: 'dev.yaml', name: 'Dev', error: null },
        { id: 'broken.yaml', name: 'Broken', error: 'Invalid YAML' },
      ]),
      activeId: signal<string | null>(null),
      draft: signal(null),
      select: vi.fn(),
      edit: vi.fn(),
      create: vi.fn(),
      refresh: vi.fn(),
    };
  });

  it('selects an environment when its name is clicked, without opening it', () => {
    const fixture = render();
    button(fixture, 'Use Dev').click();
    expect(store.select).toHaveBeenCalledWith('dev.yaml');
    expect(store.edit).not.toHaveBeenCalled();
  });

  it('opens an environment for editing from its edit button, without selecting it', () => {
    const fixture = render();
    button(fixture, 'Edit Dev').click();
    expect(store.edit).toHaveBeenCalledWith('dev.yaml');
    expect(store.select).not.toHaveBeenCalled();
  });

  it('disables both actions for an environment that can’t be read', () => {
    const fixture = render();
    expect(button(fixture, 'Use Broken').disabled).toBe(true);
    expect(button(fixture, 'Edit Broken').disabled).toBe(true);
  });
});
