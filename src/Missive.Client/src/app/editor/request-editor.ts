import { Component, inject, input, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faFloppyDisk, faPaperPlane } from '@fortawesome/free-solid-svg-icons';
import {
  AUTH_TYPES,
  AuthType,
  BODY_TYPES,
  BodyType,
  HTTP_METHODS,
  HttpMethod,
  KeyValue,
  METHOD_COLORS,
  RequestDocument,
} from '../workspace/models';
import { WorkspaceStore } from '../workspace/workspace-store';
import { KeyValueTable } from './key-value-table';

type RequestTab = 'params' | 'headers' | 'auth' | 'body';

const AUTH_LABELS: Record<AuthType, string> = {
  none: 'No auth',
  basic: 'Basic',
  bearer: 'Bearer token',
  apiKey: 'API key',
};

const BODY_LABELS: Record<BodyType, string> = {
  none: 'None',
  json: 'JSON',
  text: 'Raw text',
  form: 'Form data',
  urlencoded: 'x-www-form-urlencoded',
};

@Component({
  selector: 'app-request-editor',
  imports: [FaIconComponent, KeyValueTable],
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
      <span class="truncate text-xs text-slate-400">{{ doc.path ?? 'Not saved yet' }}</span>
      <button
        type="button"
        class="flex items-center gap-2 rounded border border-slate-300 px-3 py-1 text-sm hover:bg-slate-100 disabled:opacity-50"
        title="Save request"
        [disabled]="!doc.dirty"
      >
        <fa-icon [icon]="faFloppyDisk" /> Save
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
            <option [value]="method">{{ method }}</option>
          }
        </select>
        <input
          class="min-w-0 flex-1 px-3 py-1.5 font-mono text-sm focus:outline-none"
          [placeholder]="urlPlaceholder"
          aria-label="URL"
          spellcheck="false"
          [value]="doc.url"
          (input)="patch({ url: $any($event.target).value })"
        />
      </div>
      <button
        type="button"
        class="flex items-center gap-2 rounded bg-sky-600 px-4 text-sm font-medium text-white hover:bg-sky-700 disabled:opacity-50"
        [disabled]="!doc.url.trim()"
      >
        <fa-icon [icon]="faPaperPlane" /> Send
      </button>
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

    <section class="min-h-32 flex-1 overflow-y-auto px-4 py-2">
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
              [value]="doc.auth"
              (change)="patch({ auth: $any($event.target).value })"
            >
              @for (type of authTypes; track type) {
                <option [value]="type">{{ authLabels[type] }}</option>
              }
            </select>
          </label>
          @if (doc.auth === 'none') {
            <p class="mt-3 text-sm text-slate-500">This request does not use authorization.</p>
          } @else {
            <p class="mt-3 text-sm text-slate-500">
              {{ authLabels[doc.auth] }} settings will appear here. Secret values are stored in
              <code>.missive/</code>, never in the request file.
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
            <textarea
              class="mt-2 block h-48 w-full rounded border border-slate-300 p-2 font-mono text-sm focus:border-sky-500 focus:outline-none"
              aria-label="Request body"
              spellcheck="false"
              [value]="doc.body"
              (input)="patch({ body: $any($event.target).value })"
            ></textarea>
          }
        }
      }
    </section>

    <!-- Response -->
    <section class="flex h-2/5 min-h-32 shrink-0 flex-col border-t border-slate-200 bg-slate-50">
      <header class="flex h-9 items-center px-4">
        <h2 class="text-xs font-semibold tracking-wide text-slate-500 uppercase">Response</h2>
      </header>
      <div class="flex flex-1 flex-col items-center justify-center gap-2 text-sm text-slate-400">
        <fa-icon class="text-2xl" [icon]="faPaperPlane" />
        Send the request to see the response here.
      </div>
    </section>
  `,
})
export class RequestEditor {
  readonly document = input.required<RequestDocument>();

  private readonly store = inject(WorkspaceStore);

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

  protected patch(
    patch: Partial<{
      name: string;
      method: HttpMethod;
      url: string;
      params: KeyValue[];
      headers: KeyValue[];
      auth: AuthType;
      bodyType: BodyType;
      body: string;
    }>,
  ): void {
    this.store.update(this.document().id, patch);
  }

  protected count(doc: RequestDocument, tab: RequestTab): number {
    const enabled = (rows: KeyValue[]) => rows.filter((r) => r.enabled && r.key).length;
    if (tab === 'params') return enabled(doc.params);
    if (tab === 'headers') return enabled(doc.headers);
    return 0;
  }
}
