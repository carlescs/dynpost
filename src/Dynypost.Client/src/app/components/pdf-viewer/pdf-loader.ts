export type PdfJs = typeof import('pdfjs-dist/legacy/build/pdf.mjs');

let loading: Promise<PdfJs> | null = null;

/** Loads pdf.js on first use, so its code stays out of the initial bundle. */
export function loadPdfJs(): Promise<PdfJs> {
  loading ??= import('./pdfjs').then((m) => m.pdfjs);
  return loading;
}

/**
 * Where the data files pdf.js may need are served (copied there by `angular.json`): character
 * maps for CJK fonts, the 14 standard fonts, ICC color profiles and image decoders.
 */
export function pdfAssetUrls(): {
  cMapUrl: string;
  standardFontDataUrl: string;
  iccUrl: string;
  wasmUrl: string;
} {
  const url = (path: string) => new URL(`pdfjs/${path}/`, document.baseURI).href;
  return {
    cMapUrl: url('cmaps'),
    standardFontDataUrl: url('standard_fonts'),
    iccUrl: url('iccs'),
    wasmUrl: url('wasm'),
  };
}
