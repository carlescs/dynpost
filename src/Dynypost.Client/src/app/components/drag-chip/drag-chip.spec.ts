import { Component, Injectable } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TreeState } from '../../services/tree-state';
import { DragChip } from './drag-chip';

@Injectable()
class TestTreeState extends TreeState {
  readonly root = '';
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

  it('explains when dropping would not move the entry', () => {
    const { state, hint } = setup();
    expect(hint()).toBe('Drop on a folder');
    state.hovered.set('users');
    expect(hint()).toBe('Already here');
  });
});
