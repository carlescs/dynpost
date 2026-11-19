import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, input, linkedSignal } from '@angular/core';
import { CodeDiffEditor } from '../code-diff-editor/code-diff-editor';
import { AUTH_LABELS, BODY_LABELS, METHOD_COLORS, RequestContent } from '../../models/workspace';
import {
  EMPTY_REQUEST,
  RequestChanges,
  authFields,
  authValue,
  captureRows,
  diffRows,
  paramRows,
  requestChanges,
  settingsRows,
  textChange,
} from '../../models/request-diff-model';
import { TextChange } from '../text-change/text-change';
import { RowDiffTable } from '../row-diff-table/row-diff-table';
import { bodyText } from '../../models/form-fields';

type RequestTab = 'params' | 'headers' | 'auth' | 'body' | 'settings' | 'capture' | 'docs';

/**
 * Two versions of a request laid out like the request editor, read-only, with what changed
 * marked: the name, the method and URL, and each section's rows and fields.
 */
@Component({
  selector: 'app-request-diff',
  imports: [NgTemplateOutlet, TextChange, CodeDiffEditor, RowDiffTable],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  templateUrl: './request-diff.html',
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
    { id: 'settings', label: 'Settings' },
    { id: 'capture', label: 'Capture' },
    { id: 'docs', label: 'Docs' },
  ];

  private readonly a = computed(() => this.before() ?? EMPTY_REQUEST);
  private readonly b = computed(() => this.after() ?? EMPTY_REQUEST);

  // Compared by value, so re-reading the same diff (e.g. on window focus) keeps the open tab.
  protected readonly changes = computed(() => requestChanges(this.a(), this.b()), {
    equal: (x, y) => JSON.stringify(x) === JSON.stringify(y),
  });
  // For an added or deleted request every row shows as added or removed.
  protected readonly paramRows = computed(() => diffRows(paramRows(this.a()), paramRows(this.b())));
  protected readonly headerRows = computed(() => diffRows(this.a().headers, this.b().headers));
  protected readonly settingChanges = computed(() =>
    diffRows(settingsRows(this.a()), settingsRows(this.b())),
  );
  protected readonly captureChanges = computed(() =>
    diffRows(captureRows(this.a()), captureRows(this.b())),
  );
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
  protected readonly bodyText = bodyText;

  protected methodColor(request: RequestContent): string {
    return METHOD_COLORS[request.method];
  }

  protected changeCount(c: RequestChanges, tab: RequestTab): number | boolean {
    return c[tab];
  }
}
