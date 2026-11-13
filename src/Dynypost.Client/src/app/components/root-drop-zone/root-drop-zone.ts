import { Component, computed, inject, input } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faArrowTurnUp } from '@fortawesome/free-solid-svg-icons';
import { TreeState } from '../../services/tree-state';

/**
 * A drop zone shown at the bottom of a folder tree while an entry that is inside a folder is
 * dragged, for moving it to the tree's top level. It is the last item of the root level, which is the
 * root's drop list, so dropping on it (or anywhere else outside the folders) targets the root; it
 * stays in view while the tree scrolls and lights up whenever the root is the drop target.
 */
@Component({
  selector: 'li[app-root-drop-zone]',
  imports: [FaIconComponent],
  host: { class: 'sticky bottom-0 block bg-white px-2 pt-1.5 pb-1', role: 'presentation' },
  templateUrl: './root-drop-zone.html',
})
export class RootDropZone {
  /** How the top level is called in this tree. */
  readonly label = input('top level');

  private readonly state = inject(TreeState);

  protected readonly faArrowTurnUp = faArrowTurnUp;
  protected readonly active = computed(() => this.state.dropTarget() === this.state.root);
}
