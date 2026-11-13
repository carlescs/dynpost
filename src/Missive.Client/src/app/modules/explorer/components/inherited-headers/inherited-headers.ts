import { Component, computed, inject, input } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faFolderOpen } from '@fortawesome/free-solid-svg-icons';
import { InheritedHeaderDto } from '../../../../models/messages';
import { KeyValue } from '../../../../models/workspace';
import { FolderSettingsStore } from '../../../../services/folder-settings-store';

/**
 * The headers a request or folder inherits from the folders above it, read-only, each with the
 * folder that sets it. Those it sets itself (enabled, same name ignoring case) are struck through.
 */
@Component({
  selector: 'app-inherited-headers',
  imports: [FaIconComponent],
  templateUrl: './inherited-headers.html',
})
export class InheritedHeaders {
  readonly headers = input.required<InheritedHeaderDto[]>();
  /** The headers set at this level, which replace inherited ones of the same name. */
  readonly own = input<KeyValue[]>([]);

  protected readonly folders = inject(FolderSettingsStore);
  protected readonly faFolderOpen = faFolderOpen;

  protected readonly rows = computed(() => {
    const own = new Set(
      this.own()
        .filter((h) => h.enabled && h.key)
        .map((h) => h.key.trim().toLowerCase()),
    );
    return this.headers().map((header) => ({
      ...header,
      replaced: own.has(header.key.toLowerCase()),
    }));
  });
}
