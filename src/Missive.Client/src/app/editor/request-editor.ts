import { Component, inject, input, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faBan, faFloppyDisk, faPaperPlane, faSpinner } from '@fortawesome/free-solid-svg-icons';
import {
  AUTH_TYPES,
  AuthType,
  BODY_TYPES,
  BodyType,
  HTTP_METHODS,
  KeyValue,
  METHOD_COLORS,
  RequestAuth,
  RequestContent,
  RequestDocument,
} from '../workspace/models';
import { EnvironmentStore } from '../environments/environment-store';
import { WorkspaceStore } from '../workspace/workspace-store';
import { CodeEditor } from './code/code-editor';
import { KeyValueTable } from './key-value-table';
import { ResponseViewer } from './response-viewer';
import { VariableInput } from './variable-input';

type RequestTab = 'params' | 'headers' | 'auth' | 'body';

export const AUTH_LABELS: Record<AuthType, string> = {
  none: 'No auth',
  basic: 'Basic',
  bearer: 'Bearer token',
  apiKey: 'API key',
};

export const BODY_LABELS: Record<BodyType, string> = {
  none: 'None',
  json: 'JSON',
  text: 'Raw text',
  form: 'Form data',
  urlencoded: 'x-www-form-urlencoded',
};

@Component({
  selector: 'app-request-editor',
  imports: [FaIconComponent, KeyValueTable, ResponseViewer, CodeEditor, VariableInput],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    @let doc = document();

    <!-- Name and save -->
    <div class="flex items-center gap-2 px-4 pt-3">
      <input
        class="min-w-0 flex-1 rounded px-1 text-lg font-semibold hover:bg-slate-100 focus:bg-white focus:ring-1 focus:ring-sky-500 focus:outline-none"
        aria-label="Request name"
        [value]="doc.name"
        (input)="patch({ name: $any($event.target).value })"
      />
      <span class="truncate text-xs text-slate-400" [title]="doc.path ?? ''">{{
        doc.path ?? 'New request — will be saved in ' + (doc.folder || 'the workspace root')
      }}</span>
      <button
        type="button"
        class="flex items-center gap-2 rounded border border-slate-300 px-3 py-1 text-sm hover:bg-slate-100 disabled:opacity-50"
        title="Save request (Ctrl+S)"
        [disabled]="!doc.dirty || doc.saving"
        (click)="save()"
      >
        <fa-icon
          [icon]="doc.saving ? faSpinner : faFloppyDisk"
          [animation]="doc.saving ? 'spin' : undefined"
        />
        Save
      </button>
    </div>

    <!-- Method, URL and send -->
    <div class="flex gap-2 px-4 py-3">
      <div class="flex min-w-0 flex-1 rounded border border-slate-300 focus-within:border-sky-500">
        <select
          class="rounded-l border-r border-slate-300 bg-slate-50 px-2 font-mono text-sm font-bold focus:outline-none"
          aria-label="HTTP method"
          [class]="methodColors[doc.method]"
          [value]="doc.method"
          (change)="patch({ method: $any($event.target).value })"
        >
          @for (method of methods; track method) {
            <option
              [value]="method"
              [selected]="method === doc.method"
              [class]="methodColors[method]"
            >
              {{ method }}
            </option>
          }
        </select>
        <app-variable-input
          class="flex-1"
          ariaLabel="URL"
          [placeholder]="urlPlaceholder"
          [variables]="environments.variables()"
          [value]="doc.url"
          (valueChange)="patch({ url: $event })"
        />
      </div>
      @if (doc.sendId) {
        <button
          type="button"
          class="flex items-center gap-2 rounded border border-slate-300 px-4 text-sm font-medium hover:bg-slate-100"
          title="Cancel the request"
          (click)="cancel()"
        >
          <fa-icon [icon]="faBan" /> Cancel
        </button>
      } @else {
        <button
          type="button"
          class="flex items-center gap-2 rounded bg-sky-600 px-4 text-sm font-medium text-white hover:bg-sky-700 disabled:opacity-50"
          title="Send request (Ctrl+Enter)"
          [disabled]="!doc.url.trim()"
          (click)="send()"
        >
          <fa-icon [icon]="faPaperPlane" /> Send
        </button>
      }
    </div>

    <!-- Request sections -->
    <div class="flex gap-4 border-b border-slate-200 px-4 text-sm" role="tablist">
      @for (tab of tabs; track tab.id) {
        <button
          type="button"
          role="tab"
          class="-mb-px border-b-2 py-1.5 text-slate-500 hover:text-slate-800"
          [class.border-sky-500]="activeTab() === tab.id"
          [class.text-slate-900]="activeTab() === tab.id"
          [class.border-transparent]="activeTab() !== tab.id"
          [attr.aria-selected]="activeTab() === tab.id"
          (click)="activeTab.set(tab.id)"
        >
          {{ tab.label }}
          @if (count(doc, tab.id); as n) {
            <span class="text-xs text-sky-600">({{ n }})</span>
          }
        </button>
      }
    </div>

    <section class="flex min-h-32 flex-1 flex-col overflow-y-auto px-4 py-2">
      @switch (activeTab()) {
        @case ('params') {
          <app-key-value-table
            keyLabel="Parameter"
            [rows]="doc.params"
            (rowsChange)="patch({ params: $event })"
          />
        }
        @case ('headers') {
          <app-key-value-table
            keyLabel="Header"
            [rows]="doc.headers"
            (rowsChange)="patch({ headers: $event })"
          />
        }
        @case ('auth') {
          <label class="flex items-center gap-2 text-sm">
            Type
            <select
              class="rounded border border-slate-300 px-2 py-1"
              [value]="doc.auth.type"
              (change)="patchAuth({ type: $any($event.target).value })"
            >
              @for (type of authTypes; track type) {
                <option [value]="type" [selected]="type === doc.auth.type">
                  {{ authLabels[type] }}
                </option>
              }
            </select>
          </label>
          @switch (doc.auth.type) {
            @case ('none') {
              <p class="mt-3 text-sm text-slate-500">This request does not use authorization.</p>
            }
            @case ('basic') {
              <div class="mt-3 grid max-w-lg grid-cols-[auto_1fr] items-center gap-2 text-sm">
                <label for="auth-username">Username</label>
                <input
                  id="auth-username"
                  [class]="fieldClass"
                  spellcheck="false"
                  [value]="doc.auth.username"
                  (input)="patchAuth({ username: $any($event.target).value })"
                />
                <label for="auth-password">Password</label>
                <input
                  id="auth-password"
                  [class]="fieldClass"
                  spellcheck="false"
                  [value]="doc.auth.password"
                  (input)="patchAuth({ password: $any($event.target).value })"
                />
              </div>
            }
            @case ('bearer') {
              <div class="mt-3 grid max-w-lg grid-cols-[auto_1fr] items-center gap-2 text-sm">
                <label for="auth-token">Token</label>
                <input
                  id="auth-token"
                  [class]="fieldClass"
                  spellcheck="false"
                  [value]="doc.auth.token"
                  (input)="patchAuth({ token: $any($event.target).value })"
                />
              </div>
            }
            @case ('apiKey') {
              <div class="mt-3 grid max-w-lg grid-cols-[auto_1fr] items-center gap-2 text-sm">
                <label for="auth-key">Key</label>
                <input
                  id="auth-key"
                  [class]="fieldClass"
                  spellcheck="false"
                  placeholder="X-API-Key"
                  [value]="doc.auth.key"
                  (input)="patchAuth({ key: $any($event.target).value })"
                />
                <label for="auth-value">Value</label>
                <input
                  id="auth-value"
                  [class]="fieldClass"
                  spellcheck="false"
                  [value]="doc.auth.value"
                  (input)="patchAuth({ value: $any($event.target).value })"
                />
                <label for="auth-in">Add to</label>
                <select
                  id="auth-in"
                  class="justify-self-start rounded border border-slate-300 px-2 py-1"
                  [value]="doc.auth.in"
                  (change)="patchAuth({ in: $any($event.target).value })"
                >
                  <option value="header" [selected]="doc.auth.in === 'header'">Header</option>
                  <option value="query" [selected]="doc.auth.in === 'query'">
                    Query parameters
                  </option>
                </select>
              </div>
            }
          }
          @if (doc.auth.type !== 'none') {
            <p class="mt-3 text-xs text-slate-500">
              These values are saved in the request file. Use a variable such as
              <code>{{ secretExample }}</code> to keep secrets out of it.
            </p>
          }
        }
        @case ('body') {
          <div class="flex flex-wrap gap-3 text-sm">
            @for (type of bodyTypes; track type) {
              <label class="flex items-center gap-1">
                <input
                  type="radio"
                  name="body-type"
                  [value]="type"
                  [checked]="doc.bodyType === type"
                  (change)="patch({ bodyType: type })"
                />
                {{ bodyLabels[type] }}
              </label>
            }
          </div>
          @if (doc.bodyType === 'none') {
            <p class="mt-3 text-sm text-slate-500">This request does not have a body.</p>
          } @else {
            @if (doc.bodyType === 'form' || doc.bodyType === 'urlencoded') {
              <p class="mt-2 text-xs text-slate-500">
                One <code>name=value</code> pair per line.
                @if (doc.bodyType === 'urlencoded') {
                  Values are sent as written, so percent-encode them.
                }
              </p>
            }
            <app-code-editor
              class="mt-2 min-h-24 flex-1 overflow-hidden rounded border border-slate-300 focus-within:border-sky-500"
              ariaLabel="Request body"
              [value]="doc.body"
              [language]="doc.bodyType === 'json' ? 'json' : 'plaintext'"
              [highlightVariables]="true"
              [variables]="environments.variables()"
              (valueChange)="patch({ body: $event })"
              (submit)="send()"
            />
          }
        }
      }
    </section>

    <!-- Response -->
    <app-response-viewer [document]="doc" />
  `,
})
export class RequestEditor {
  readonly document = input.required<RequestDocument>();

  private readonly store = inject(WorkspaceStore);
  protected readonly environments = inject(EnvironmentStore);

  protected readonly activeTab = signal<RequestTab>('params');
  protected readonly tabs: { id: RequestTab; label: string }[] = [
    { id: 'params', label: 'Params' },
    { id: 'headers', label: 'Headers' },
    { id: 'auth', label: 'Auth' },
    { id: 'body', label: 'Body' },
  ];

  protected readonly urlPlaceholder = 'https://api.example.com/users or {{baseUrl}}/users';
  protected readonly methods = HTTP_METHODS;
  protected readonly methodColors = METHOD_COLORS;
  protected readonly authTypes = AUTH_TYPES;
  protected readonly authLabels = AUTH_LABELS;
  protected readonly bodyTypes = BODY_TYPES;
  protected readonly bodyLabels = BODY_LABELS;
  protected readonly faPaperPlane = faPaperPlane;
  protected readonly faFloppyDisk = faFloppyDisk;
  protected readonly faSpinner = faSpinner;
  protected readonly faBan = faBan;
  protected readonly secretExample = '{{apiToken}}';
  protected readonly fieldClass =
    'min-w-0 rounded border border-slate-300 px-2 py-1 font-mono focus:border-sky-500 focus:outline-none';

  protected patch(patch: Partial<RequestContent>): void {
    this.store.update(this.document().id, patch);
  }

  protected patchAuth(patch: Partial<RequestAuth>): void {
    this.patch({ auth: { ...this.document().auth, ...patch } });
  }

  protected save(): void {
    void this.store.save(this.document().id);
  }

  protected send(): void {
    void this.store.send(this.document().id);
  }

  protected cancel(): void {
    void this.store.cancelSend(this.document().id);
  }

  protected count(doc: RequestDocument, tab: RequestTab): number {
    const enabled = (rows: KeyValue[]) => rows.filter((r) => r.enabled && r.key).length;
    if (tab === 'params') return enabled(doc.params);
    if (tab === 'headers') return enabled(doc.headers);
    return 0;
  }
}
