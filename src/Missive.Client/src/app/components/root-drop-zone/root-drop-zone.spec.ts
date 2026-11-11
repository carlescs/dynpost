import { Component, Injectable } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TreeState } from '../../services/tree-state';
import { RootDropZone } from './root-drop-zone';

@Injectable()
class TestTreeState extends TreeState {
  protected createFolder = vi.fn();
  protected renameFolder = vi.fn();
  protected move = vi.fn();
}

@Component({
  imports: [RootDropZone],
  providers: [{ provide: TreeState, useClass: TestTreeState }],
  template: `<ul>
    <li app-root-drop-zone label="top level"></li>
  </ul>`,
})
class Host {}

describe('RootDropZone', () => {
  it('lights up while the root is the drop target', () => {
    const fixture = TestBed.createComponent(Host);
    const state = fixture.debugElement.injector.get(TreeState);
    state.dragged.set({ path: 'users/get-user.yaml', kind: 'request' });
    fixture.detectChanges();
    const zone = (fixture.nativeElement as HTMLElement).querySelector('li > div')!;
    expect(zone.textContent).toContain('Drop here to move to the top level');
    expect(zone.classList).not.toContain('bg-sky-100');

    state.hovered.set('');
    fixture.detectChanges();
    expect(zone.classList).toContain('bg-sky-100');
  });
});
