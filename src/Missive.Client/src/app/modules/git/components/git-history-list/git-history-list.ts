import { Component, inject } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faCircleExclamation, faRotate, faSpinner } from '@fortawesome/free-solid-svg-icons';
import { relativeTime } from '../../models/git-format';
import { GitHistoryStore } from '../../../../services/git-history-store';

/** The commits that changed the workspace, newest first; choosing one opens it for review. */
@Component({
  selector: 'app-git-history-list',
  imports: [FaIconComponent],
  host: { class: 'flex flex-col' },
  templateUrl: './git-history-list.html',
})
export class GitHistoryList {
  protected readonly history = inject(GitHistoryStore);
  protected readonly relativeTime = relativeTime;
  protected readonly faCircleExclamation = faCircleExclamation;
  protected readonly faRotate = faRotate;
  protected readonly faSpinner = faSpinner;

  protected isSelected(id: string): boolean {
    return (this.history.commit()?.commit.id ?? this.history.opening()) === id;
  }
}
