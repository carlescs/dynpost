import { Directive, ElementRef, computed, inject } from '@angular/core';
import { CdkDrag, CdkDropList } from '@angular/cdk/drag-drop';
import { TreeItem, TreeState } from '../../services/tree-state';

/**
 * Makes an element the drop area of a folder (`''` for the workspace root) in a folder tree: a CDK
 * drop list, connected to the others through the tree's `cdkDropListGroup`, that only accepts
 * items when the pointer is over it rather than over a folder nested inside it. The tree provides
 * its `TreeState`.
 */
@Directive({
  selector: '[appFolderDrop]',
  hostDirectives: [{ directive: CdkDropList, inputs: ['cdkDropListData: appFolderDrop'] }],
  host: {
    '[class.bg-sky-100]': 'isTarget()',
    '[class.outline]': 'isTarget()',
    '[class.outline-1]': 'isTarget()',
    '[class.-outline-offset-1]': 'isTarget()',
    '[class.outline-sky-400]': 'isTarget()',
  },
})
export class FolderDrop {
  private readonly state = inject(TreeState);
  private readonly list = inject<CdkDropList<string>>(CdkDropList);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  protected readonly isTarget = computed(() => {
    const target = this.state.dropTarget();
    return target !== null && target === this.list.data;
  });

  constructor() {
    // Trees are sorted by name, so items are never reordered; only their folder changes.
    this.list.sortingDisabled = true;
    this.list.enterPredicate = (drag: CdkDrag<TreeItem>) =>
      this.state.canEnter(drag.data, this.list.data, this.element);
    this.state.registerDropList(this.element, () => this.list.data);
  }

  ngOnDestroy(): void {
    this.state.unregisterDropList(this.element);
  }
}
