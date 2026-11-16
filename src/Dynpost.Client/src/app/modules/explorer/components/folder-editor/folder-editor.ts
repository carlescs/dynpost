import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faFloppyDisk, faFolder, faSpinner } from '@fortawesome/free-solid-svg-icons';
import { InheritedSettingsDto } from '../../../../models/messages';
import { KeyValue, REQUESTS_FOLDER, RequestAuth } from '../../../../models/workspace';
import { FolderDraft, FolderSettingsStore } from '../../../../services/folder-settings-store';
import { KeyValueTable } from '../../../../components/key-value-table/key-value-table';
import { AuthEditor } from '../auth-editor/auth-editor';
import { InheritedHeaders } from '../inherited-headers/inherited-headers';
import { InheritedVariables } from '../inherited-variables/inherited-variables';
import { SettingsEditor, settingsCount } from '../settings-editor/settings-editor';
import { RequestSettingsDto } from '../../../../models/messages';

type FolderTab = 'headers' | 'auth' | 'variables' | 'settings';

/** Nothing inherited: what the top `requests` folder gets. */
const NOTHING_INHERITED: InheritedSettingsDto = {
  headers: [],
  auth: { type: 'none' },
  authFolder: null,
  variables: [],
  settings: {},
};

/**
 * The headers, auth and variables a collection folder sets for every request and sub-folder in it
 * (its `_folder.yaml`). Closer levels win: a sub-folder over its parent, a request over its
 * folders. Folder variables are defaults: the selected environment's variables win over them.
 */
@Component({
  selector: 'app-folder-editor',
  imports: [
    FaIconComponent,
    KeyValueTable,
    AuthEditor,
    InheritedHeaders,
    InheritedVariables,
    SettingsEditor,
  ],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  templateUrl: './folder-editor.html',
})
export class FolderEditor {
  readonly draft = input.required<FolderDraft>();

  protected readonly store = inject(FolderSettingsStore);
  protected readonly activeTab = signal<FolderTab>('headers');
  protected readonly tabs: { id: FolderTab; label: string }[] = [
    { id: 'headers', label: 'Headers' },
    { id: 'auth', label: 'Auth' },
    { id: 'variables', label: 'Variables' },
    { id: 'settings', label: 'Settings' },
  ];
  protected readonly faFloppyDisk = faFloppyDisk;
  protected readonly faSpinner = faSpinner;
  protected readonly faFolder = faFolder;

  /** What the folder itself inherits from the folders above it. */
  protected readonly inherited = signal<InheritedSettingsDto | null>(null);
  private readonly parent = computed(() => {
    const path = this.draft().path;
    return path === REQUESTS_FOLDER ? null : path.slice(0, path.lastIndexOf('/'));
  });
  private inheritedLoad = 0;

  constructor() {
    effect(() => {
      const parent = this.parent();
      this.store.changed();
      const load = ++this.inheritedLoad;
      if (!parent) {
        this.inherited.set(NOTHING_INHERITED);
        return;
      }
      void this.store.inherited(parent).then((inherited) => {
        if (load === this.inheritedLoad) this.inherited.set(inherited);
      });
    });
  }

  protected patchHeaders(headers: KeyValue[]): void {
    this.store.update(this.draft().key, { headers });
  }

  protected patchVariables(variables: KeyValue[]): void {
    this.store.update(this.draft().key, { variables });
  }

  protected patchSettings(settings: RequestSettingsDto): void {
    this.store.update(this.draft().key, { settings });
  }

  protected patchAuth(patch: Partial<RequestAuth>): void {
    this.store.update(this.draft().key, { auth: { ...this.draft().auth, ...patch } });
  }

  protected save(): void {
    void this.store.save(this.draft().key);
  }

  protected count(draft: FolderDraft, tab: FolderTab): number {
    if (tab === 'settings') return settingsCount(draft.settings);
    const rows = tab === 'headers' ? draft.headers : tab === 'variables' ? draft.variables : [];
    return rows.filter((r) => r.enabled && r.key).length;
  }
}
