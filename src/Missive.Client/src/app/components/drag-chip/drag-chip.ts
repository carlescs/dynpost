import { Component, computed, inject, input } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faArrowRight, faBan } from '@fortawesome/free-solid-svg-icons';
import { TreeState, folderName } from '../../services/tree-state';

/**
 * The chip that follows the pointer while an entry of a folder tree is dragged: the entry (projected
 * content) and, under it, where dropping it now would move it — or why it would not move.
 */
@Component({
  selector: 'app-drag-chip',
  imports: [FaIconComponent],
  templateUrl: './drag-chip.html',
})
export class DragChip {
  /** How the workspace root is called in this tree's hint. */
  readonly rootLabel = input('workspace root');

  private readonly state = inject(TreeState);

  protected readonly faArrowRight = faArrowRight;
  protected readonly faBan = faBan;

  /** Whether dropping now moves the entry. */
  protected readonly moves = computed(() => this.state.dropHint()?.kind === 'move');

  protected readonly hint = computed(() => {
    const hint = this.state.dropHint();
    if (!hint) return '';
    if (hint.kind === 'move') return hint.folder ? folderName(hint.folder) : this.rootLabel();
    switch (hint.reason) {
      case 'same':
        return 'Already here';
      case 'inside':
        return "Can't move into itself";
      default:
        return 'Drop on a folder';
    }
  });
}
