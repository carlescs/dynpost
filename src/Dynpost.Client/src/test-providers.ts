import { Provider } from '@angular/core';
import { MONACO_LOADER, Monaco } from './app/components/code-editor/monaco-loader';

/**
 * Providers for every unit test (`providersFile` in `angular.json`). Monaco doesn't run in jsdom,
 * and a spec rendering an editor would otherwise start importing it and end before the import
 * does, so the editors' loader never resolves: they keep showing their plain-text fallback.
 */
const providers: Provider[] = [
  { provide: MONACO_LOADER, useValue: () => new Promise<Monaco>(() => {}) },
];

export default providers;
