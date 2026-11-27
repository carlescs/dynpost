import { InjectionToken } from '@angular/core';

export type Monaco = typeof import('monaco-editor/editor/editor.api');

let loading: Promise<Monaco> | null = null;

/**
 * Loads Monaco on first use: its code chunk and its stylesheet (the `monaco` style bundle in
 * `angular.json`, which is not injected into the page up front).
 */
export function loadMonaco(): Promise<Monaco> {
  loading ??= (async () => {
    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = 'monaco.css';
    document.head.appendChild(link);
    const { monaco } = await import('./monaco');
    return monaco;
  })();
  return loading;
}

/**
 * How the editors load Monaco: `loadMonaco` in the app. Unit tests provide a loader that never
 * resolves (`src/test-providers.ts`), because jsdom can't run Monaco and an import still pending
 * when a spec file's environment is torn down fails the run.
 */
export const MONACO_LOADER = new InjectionToken<() => Promise<Monaco>>('MONACO_LOADER', {
  providedIn: 'root',
  factory: () => loadMonaco,
});

/** The Monaco language for a response's media type (without parameters). */
export function languageForMediaType(mediaType: string | null | undefined): string {
  const type = (mediaType ?? '').toLowerCase();
  if (type === 'application/json' || type.endsWith('+json')) return 'json';
  if (type === 'application/xml' || type === 'text/xml' || type.endsWith('+xml')) return 'xml';
  if (type === 'text/html') return 'html';
  if (type === 'text/css') return 'css';
  if (type.includes('javascript') || type.includes('ecmascript')) return 'javascript';
  if (type.includes('yaml')) return 'yaml';
  if (type === 'application/graphql') return 'graphql';
  if (type === 'text/markdown') return 'markdown';
  return 'plaintext';
}
