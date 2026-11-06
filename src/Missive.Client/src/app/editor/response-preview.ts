import { Component, computed, inject, input } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { ResponseDto } from '../bridge/messages';
import { PdfViewer } from './pdf/pdf-viewer';

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
 * forms, popups or access to the app); a PDF is rendered
 * with pdf.js.
 */
@Component({
  selector: 'app-response-preview',
  imports: [PdfViewer],
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
    } @else if (pdf(); as data) {
      <app-pdf-viewer [data]="data" />
    }
  `,
})
export class ResponsePreview {
  readonly response = input.required<ResponseDto>();

  private readonly sanitizer = inject(DomSanitizer);

  protected readonly kind = computed(() => previewKind(this.response()));

  // Trusting the markup is safe because the frame is sandboxed (it can't run scripts or reach
  // the app) and the policy stops it from loading anything, so the body is only displayed.
  protected readonly html = computed<SafeHtml>(() =>
    this.sanitizer.bypassSecurityTrustHtml(
      this.kind() === 'html' ? withPreviewPolicy(this.response().body) : '',
    ),
  );

  /** The PDF's bytes, decoded once per response. */
  protected readonly pdf = computed(() =>
    this.kind() === 'pdf' ? decodeBase64(this.response().body) : null,
  );
}

/**
 * Blocks every request the previewed page could make (images, styles, fonts, frames, forms):
 * all network work belongs to the backend, and a response must not reach other services from
 * the webview. Inline styles and `data:` images and fonts still work.
 */
const PREVIEW_POLICY =
  "default-src 'none'; style-src 'unsafe-inline'; img-src data:; font-src data:; form-action 'none'";

/**
 * Puts the policy first in the document so it applies before any of the markup. It goes after a
 * doctype, if there is one, so the page keeps its rendering mode; the parser places the `<meta>`
 * in the head even when the markup has its own `<html>` and `<head>`.
 */
export function withPreviewPolicy(html: string): string {
  const meta = `<meta http-equiv="Content-Security-Policy" content="${PREVIEW_POLICY}">`;
  const doctype = /^\s*<!doctype[^>]*>/i.exec(html);
  return doctype
    ? doctype[0] + meta + html.slice(doctype[0].length)
    : meta + html.replace(/^\uFEFF/, '');
}

function decodeBase64(base64: string): Uint8Array<ArrayBuffer> {
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes;
}
