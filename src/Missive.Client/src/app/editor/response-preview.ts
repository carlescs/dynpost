import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { DomSanitizer, SafeHtml, SafeResourceUrl } from '@angular/platform-browser';
import { ResponseDto } from '../bridge/messages';

/** The kind of preview a response body can be shown as, or `null` when it has none. */
export type PreviewKind = 'html' | 'pdf';

/** Whether (and how) a response body can be previewed rendered instead of as code. */
export function previewKind(r: ResponseDto): PreviewKind | null {
  const type = r.contentType ?? '';
  if (
    r.bodyEncoding === 'text' &&
    r.body &&
    (type === 'text/html' || type === 'application/xhtml+xml')
  ) {
    return 'html';
  }
  // A cut-off PDF can't be rendered, so it gets no preview.
  if (r.bodyEncoding === 'base64' && type === 'application/pdf' && !r.truncated) return 'pdf';
  return null;
}

/**
 * Renders an HTML or PDF response body. HTML goes into a fully sandboxed frame (no scripts,
 * forms, popups or access to the app); a PDF is shown by the webview's built-in viewer.
 */
@Component({
  selector: 'app-response-preview',
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    @if (kind() === 'html') {
      <iframe
        class="min-h-0 flex-1 border-0 bg-white"
        title="Response preview"
        sandbox=""
        referrerpolicy="no-referrer"
        [srcdoc]="html()"
      ></iframe>
    } @else if (pdfUrl(); as url) {
      <object class="min-h-0 flex-1 bg-white" type="application/pdf" [data]="url">
        <p class="flex h-full items-center justify-center text-sm text-slate-500">
          This view can't display PDF documents.
        </p>
      </object>
    }
  `,
})
export class ResponsePreview {
  readonly response = input.required<ResponseDto>();

  private readonly sanitizer = inject(DomSanitizer);

  protected readonly kind = computed(() => previewKind(this.response()));

  // Trusting the markup is safe because the frame is sandboxed: it can't run scripts or reach
  // the app, so the body is only displayed.
  protected readonly html = computed<SafeHtml>(() =>
    this.sanitizer.bypassSecurityTrustHtml(this.kind() === 'html' ? this.response().body : ''),
  );

  protected readonly pdfUrl = signal<SafeResourceUrl | null>(null);

  constructor() {
    // A blob URL keeps large PDFs out of the DOM; it is revoked when the response changes.
    effect((onCleanup) => {
      const r = this.response();
      if (this.kind() !== 'pdf') {
        this.pdfUrl.set(null);
        return;
      }
      const url = URL.createObjectURL(
        new Blob([decodeBase64(r.body)], { type: 'application/pdf' }),
      );
      this.pdfUrl.set(this.sanitizer.bypassSecurityTrustResourceUrl(url));
      onCleanup(() => URL.revokeObjectURL(url));
    });
  }
}

function decodeBase64(base64: string): Uint8Array<ArrayBuffer> {
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes;
}
