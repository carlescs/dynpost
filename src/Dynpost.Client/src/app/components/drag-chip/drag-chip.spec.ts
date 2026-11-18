import { Component, Injectable, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TreeItem, TreeState } from '../../services/tree-state';
import { DragChip } from './drag-chip';

@Injectable()
class TestTreeState extends TreeState {
  readonly root = '';
  readonly items = signal<TreeItem[]>([]);
  protected createFolder = vi.fn();
  protected renameFolder = vi.fn();
  protected move = vi.fn();
}

@Component({
  imports: [DragChip],
  providers: [{ provide: TreeState, useClass: TestTreeState }],
  template: `<app-drag-chip rootLabel="top level">get-user</app-drag-chip>`,
})
class Host {}

describe('DragChip', () => {
  function setup() {
    const fixture = TestBed.createComponent(Host);
    const state = fixture.debugElement.injector.get(TreeState);
    state.dragged.set({ path: 'users/get-user.yaml', kind: 'request' });
    const hint = () => {
      fixture.detectChanges();
      return (fixture.nativeElement as HTMLElement)
        .querySelector('[data-testid="drag-hint"]')!
        .textContent!.replace(/\s+/g, ' ')
        .trim();
    };
    return { fixture, state, hint };
  }

  it('shows the dragged entry', () => {
    const { fixture } = setup();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('get-user');
  });

  it('names the folder the entry would move into', () => {
    const { state, hint } = setup();
    state.hovered.set('orders/archive');
    expect(hint()).toBe('Move to archive');
    state.hovered.set('');
    expect(hint()).toBe('Move to top level');
  });

  it('counts the other selected entries that come along', () => {
    const { fixture, state, hint } = setup();
    (state as TestTreeState).items.set([
      { path: 'users/get-user.yaml', kind: 'request' },
      { path: 'top.yaml', kind: 'request' },
      { path: 'orders', kind: 'folder', children: [] },
    ]);
    state.select({ path: 'users/get-user.yaml', kind: 'request' });
    state.select({ path: 'top.yaml', kind: 'request' }, { ctrlKey: true });
    state.hovered.set('orders');
    expect(hint()).toBe('Move to orders');
    const count = (fixture.nativeElement as HTMLElement).querySelector(
      '[data-testid="drag-count"]',
    );
    expect(count?.textContent?.trim()).toBe('+1');
    state.hovered.set('users');
    expect(hint()).toBe('Move to users (1 of 2)');
  });

  it('explains when dropping would not move the entry', () => {
    const { state, hint } = setup();
    expect(hint()).toBe('Drop on a folder');
    state.hovered.set('users');
    expect(hint()).toBe('Already here');
  });
});
