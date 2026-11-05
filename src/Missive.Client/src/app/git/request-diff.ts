import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, input, linkedSignal } from '@angular/core';
import { CodeDiffEditor } from '../editor/code/code-diff-editor';
import { AUTH_LABELS, BODY_LABELS } from '../editor/request-editor';
import { METHOD_COLORS, RequestContent } from '../workspace/models';
import {
  EMPTY_REQUEST,
  RequestChanges,
  RowChange,
  authFields,
  authValue,
  diffRows,
  requestChanges,
  textChange,
} from './request-diff-model';
import { TextChange } from './text-change';

type RequestTab = 'params' | 'headers' | 'auth' | 'body';

const ROW_STYLES: Record<RowChange['kind'], { marker: string; row: string; label: string }> = {
  same: { marker: '', row: '', label: 'Unchanged' },
  added: { marker: '+', row: 'bg-emerald-50 text-emerald-900', label: 'Added' },
  removed: { marker: '−', row: 'bg-rose-50 text-rose-900 line-through', label: 'Removed' },
  changed: { marker: '~', row: 'bg-amber-50', label: 'Changed' },
};

/**
 * Two versions of a request laid out like the request editor, read-only, with what changed
 * marked: the name, the method and URL, and each section's rows and fields.
 */
@Component({
  selector: 'app-request-diff',
  imports: [NgTemplateOutlet, TextChange, CodeDiffEditor],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    @let a = before() ?? empty;
    @let b = after() ?? empty;
    @let c = changes();

    <!-- Name -->
    <div class="flex items-center gap-2 px-4 pt-3 text-lg font-semibold">
      <app-text-change
        [before]="before() ? a.name : b.name"
        [after]="after() ? b.name : a.name"
        placeholder="(unnamed)"
      />
      @if (!before()) {
        <span class="rounded bg-emerald-100 px-1.5 text-xs font-medium text-emerald-800">New</span>
      } @else if (!after()) {
        <span class="rounded bg-rose-100 px-1.5 text-xs font-medium text-rose-800">Deleted</span>
      }
    </div>

    <!-- Method and URL -->
    <div class="space-y-1 px-4 py-3">
      @if (!c.line) {
        <ng-container
          *ngTemplateOutlet="line; context: { $implicit: b, other: null, side: 'same' }"
        />
      } @else {
        @if (before()) {
          <ng-container
            *ngTemplateOutlet="
              line;
              context: { $implicit: a, other: after() ? b : null, side: 'before' }
            "
          />
        }
        @if (after()) {
          <ng-container
            *ngTemplateOutlet="
              line;
              context: { $implicit: b, other: before() ? a : null, side: 'after' }
            "
          />
        }
      }
    </div>

    <ng-template #line let-request let-other="other" let-side="side">
      <div
        class="flex min-w-0 items-stretch rounded border font-mono text-sm"
        [class]="
          side === 'before'
            ? 'border-rose-200 bg-rose-50'
            : side === 'after'
              ? 'border-emerald-200 bg-emerald-50'
              : 'border-slate-300'
        "
        [attr.aria-label]="side === 'before' ? 'Before' : side === 'after' ? 'After' : null"
      >
        @if (side !== 'same') {
          <span
            class="flex w-6 shrink-0 items-center justify-center font-bold"
            [class]="side === 'before' ? 'text-rose-600' : 'text-emerald-600'"
            aria-hidden="true"
            >{{ side === 'before' ? '−' : '+' }}</span
          >
        }
        <span
          class="flex shrink-0 items-center border-r border-slate-300 bg-slate-50 px-2 font-bold"
          [class]="methodColor(request)"
          [class.underline]="other && other.method !== request.method"
          >{{ request.method }}</span
        >
        <span class="min-w-0 flex-1 px-2 py-1 break-all">
          @if (other && other.url !== request.url) {
            @let t =
              side === 'before' ? split(request.url, other.url) : split(other.url, request.url);
            {{ t.prefix
            }}<span [class]="side === 'before' ? 'bg-rose-200' : 'bg-emerald-200'">{{
              side === 'before' ? t.removed : t.added
            }}</span
            >{{ t.suffix }}
          } @else {
            {{ request.url }}
          }
          @if (!request.url) {
            <span class="text-slate-400 italic">(no URL)</span>
          }
        </span>
      </div>
    </ng-template>

    <!-- Sections -->
    <div class="flex gap-4 border-b border-slate-200 px-4 text-sm" role="tablist">
      @for (tab of tabs; track tab.id) {
        <button
          type="button"
          role="tab"
          class="-mb-px flex items-center gap-1 border-b-2 py-1.5 text-slate-500 hover:text-slate-800"
          [class.border-sky-500]="activeTab() === tab.id"
          [class.text-slate-900]="activeTab() === tab.id"
          [class.border-transparent]="activeTab() !== tab.id"
          [attr.aria-selected]="activeTab() === tab.id"
          (click)="activeTab.set(tab.id)"
        >
          {{ tab.label }}
          @if (changeCount(c, tab.id); as n) {
            <span
              class="rounded-full bg-amber-100 px-1.5 text-xs text-amber-800"
              [title]="n === true ? 'Changed' : n + ' changed'"
              >{{ n === true ? '•' : n }}</span
            >
          }
        </button>
      }
    </div>

    <section class="flex min-h-32 flex-1 flex-col overflow-y-auto px-4 py-2">
      @switch (activeTab()) {
        @case ('params') {
          <ng-container
            *ngTemplateOutlet="table; context: { $implicit: paramRows(), label: 'Parameter' }"
          />
        }
        @case ('headers') {
          <ng-container
            *ngTemplateOutlet="table; context: { $implicit: headerRows(), label: 'Header' }"
          />
        }
        @case ('auth') {
          <div class="grid max-w-2xl grid-cols-[auto_1fr] items-baseline gap-x-4 gap-y-2 text-sm">
            <span class="text-slate-500">Type</span>
            <app-text-change
              [before]="authLabels[before() ? a.auth.type : b.auth.type]"
              [after]="authLabels[after() ? b.auth.type : a.auth.type]"
            />
            @for (f of auth(); track f.field) {
              <span class="text-slate-500">{{ f.label }}</span>
              <app-text-change
                class="font-mono"
                [before]="f.before"
                [after]="f.after"
                placeholder="(empty)"
              />
            }
          </div>
          @if (a.auth.type === 'none' && b.auth.type === 'none') {
            <p class="mt-3 text-sm text-slate-500">This request does not use authorization.</p>
          }
        }
        @case ('body') {
          <div class="flex items-baseline gap-4 text-sm">
            <span class="text-slate-500">Type</span>
            <app-text-change
              [before]="bodyLabels[before() ? a.bodyType : b.bodyType]"
              [after]="bodyLabels[after() ? b.bodyType : a.bodyType]"
            />
          </div>
          @if (a.bodyType === 'none' && b.bodyType === 'none') {
            <p class="mt-3 text-sm text-slate-500">This request does not have a body.</p>
          } @else {
            <app-code-diff-editor
              class="mt-2 min-h-48 flex-1 overflow-hidden rounded border border-slate-300"
              ariaLabel="Request body"
              [original]="a.bodyType === 'none' ? '' : a.body"
              [modified]="b.bodyType === 'none' ? '' : b.body"
              [language]="a.bodyType === 'json' || b.bodyType === 'json' ? 'json' : 'plaintext'"
              [sideBySide]="sideBySide()"
            />
          }
        }
      }
    </section>

    <ng-template #table let-rows let-label="label">
      <table class="w-full table-fixed border-collapse text-sm">
        <thead>
          <tr class="text-left text-xs text-slate-500">
            <th class="w-6"><span class="sr-only">Change</span></th>
            <th class="w-8"><span class="sr-only">Enabled</span></th>
            <th class="px-2 py-1 font-medium">{{ label }}</th>
            <th class="px-2 py-1 font-medium">Value</th>
          </tr>
        </thead>
        <tbody>
          @for (change of rows; track $index) {
            @let style = rowStyle(change);
            <tr class="border-t border-slate-200 align-top" [class]="style.row">
              <td
                class="py-1 text-center font-mono font-bold"
                [title]="style.label"
                [class.text-emerald-600]="change.kind === 'added'"
                [class.text-rose-600]="change.kind === 'removed'"
                [class.text-amber-600]="change.kind === 'changed'"
              >
                {{ style.marker }}
              </td>
              <td class="py-1 text-center">
                <input
                  type="checkbox"
                  disabled
                  [checked]="change.row.enabled"
                  [attr.aria-label]="
                    (change.row.enabled ? 'Enabled: ' : 'Disabled: ') + change.row.key
                  "
                />
                @if (change.kind === 'changed' && change.before.enabled !== change.row.enabled) {
                  <span class="block text-[10px] leading-3 text-amber-700">{{
                    change.row.enabled ? 'enabled' : 'disabled'
                  }}</span>
                }
              </td>
              <td class="px-2 py-1 font-mono">
                @if (change.kind === 'changed') {
                  <app-text-change [before]="change.before.key" [after]="change.row.key" />
                } @else {
                  <span class="break-all">{{ change.row.key }}</span>
                }
              </td>
              <td class="px-2 py-1 font-mono">
                @if (change.kind === 'changed') {
                  <app-text-change
                    [before]="change.before.value"
                    [after]="change.row.value"
                    placeholder="(empty)"
                  />
                } @else {
                  <span class="break-all">{{ change.row.value }}</span>
                }
              </td>
            </tr>
          } @empty {
            <tr class="border-t border-slate-200">
              <td colspan="4" class="px-2 py-2 text-slate-500">None.</td>
            </tr>
          }
        </tbody>
      </table>
    </ng-template>
  `,
})
export class RequestDiff {
  /** The committed request; `null` for a new request file. */
  readonly before = input.required<RequestContent | null>();
  /** The request in the working tree; `null` for a deleted request file. */
  readonly after = input.required<RequestContent | null>();
  /** How the body differences are shown. */
  readonly sideBySide = input(true);

  protected readonly empty = EMPTY_REQUEST;
  protected readonly authLabels = AUTH_LABELS;
  protected readonly bodyLabels = BODY_LABELS;
  protected readonly tabs: { id: RequestTab; label: string }[] = [
    { id: 'params', label: 'Params' },
    { id: 'headers', label: 'Headers' },
    { id: 'auth', label: 'Auth' },
    { id: 'body', label: 'Body' },
  ];

  private readonly a = computed(() => this.before() ?? EMPTY_REQUEST);
  private readonly b = computed(() => this.after() ?? EMPTY_REQUEST);

  // Compared by value, so re-reading the same diff (e.g. on window focus) keeps the open tab.
  protected readonly changes = computed(() => requestChanges(this.a(), this.b()), {
    equal: (x, y) => JSON.stringify(x) === JSON.stringify(y),
  });
  // For an added or deleted request every row shows as added or removed.
  protected readonly paramRows = computed(() => diffRows(this.a().params, this.b().params));
  protected readonly headerRows = computed(() => diffRows(this.a().headers, this.b().headers));
  protected readonly auth = computed(() => {
    const a = this.a().auth;
    const b = this.b().auth;
    return authFields(a, b).map((f) => ({
      ...f,
      before: this.before() ? authValue(a, f.field) : authValue(b, f.field),
      after: this.after() ? authValue(b, f.field) : authValue(a, f.field),
    }));
  });

  /** Opens on the first section that changed, again whenever the compared requests change. */
  protected readonly activeTab = linkedSignal<RequestTab>(() => {
    const c = this.changes();
    return this.tabs.find((t) => this.changeCount(c, t.id))?.id ?? 'params';
  });

  protected split = textChange;

  protected methodColor(request: RequestContent): string {
    return METHOD_COLORS[request.method];
  }

  protected rowStyle(change: RowChange): (typeof ROW_STYLES)[RowChange['kind']] {
    return ROW_STYLES[change.kind];
  }

  protected changeCount(c: RequestChanges, tab: RequestTab): number | boolean {
    return c[tab];
  }
}
