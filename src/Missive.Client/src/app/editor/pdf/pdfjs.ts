// The legacy build includes polyfills for the newer JavaScript APIs pdf.js uses, which
// older system webviews (WebKitGTK, WKWebView) lack.
import * as pdfjs from 'pdfjs-dist/legacy/build/pdf.mjs';

// The worker is bundled from a local entry point; Angular resolves `new URL(…, import.meta.url)`.
// One worker is shared by every document.
pdfjs.GlobalWorkerOptions.workerPort = new Worker(new URL('./pdf.worker', import.meta.url), {
  type: 'module',
});

export { pdfjs };
