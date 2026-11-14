import { Component, computed, inject, input } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faFolderOpen } from '@fortawesome/free-solid-svg-icons';
import { InheritedVariableDto } from '../../../../models/messages';
import { KeyValue } from '../../../../models/workspace';
import { FolderSettingsStore } from '../../../../services/folder-settings-store';

/**
 * The variables a folder inherits from the folders above it, read-only, each with the folder that
 * sets it. Those it sets itself (enabled, same name) are struck through.
 */
@Component({
  selector: 'app-inherited-variables',
  imports: [FaIconComponent],
  templateUrl: './inherited-variables.html',
})
export class InheritedVariables {
  readonly variables = input.required<InheritedVariableDto[]>();
  /** The variables set at this level, which replace inherited ones of the same name. */
  readonly own = input<KeyValue[]>([]);

  protected readonly folders = inject(FolderSettingsStore);
  protected readonly faFolderOpen = faFolderOpen;

  protected readonly rows = computed(() => {
    const own = new Set(
      this.own()
        .filter((v) => v.enabled && v.key)
        .map((v) => v.key.trim()),
    );
    return this.variables().map((variable) => ({ ...variable, replaced: own.has(variable.name) }));
  });
}
