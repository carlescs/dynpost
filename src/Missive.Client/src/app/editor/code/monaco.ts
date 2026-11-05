/**
 * The Monaco editor with the languages Missive highlights. Import it only through
 * `loadMonaco()` so it stays in a lazy chunk.
 */
import * as monaco from 'monaco-editor/editor/editor.api';
import 'monaco-editor/language/json/monaco.contribution';
import 'monaco-editor/languages/definitions/css/register';
import 'monaco-editor/languages/definitions/graphql/register';
import 'monaco-editor/languages/definitions/html/register';
import 'monaco-editor/languages/definitions/javascript/register';
import 'monaco-editor/languages/definitions/markdown/register';
import 'monaco-editor/languages/definitions/xml/register';
import 'monaco-editor/languages/definitions/yaml/register';

// Workers are bundled from local entry points; Angular resolves `new URL(…, import.meta.url)`.
self.MonacoEnvironment = {
  getWorker: (_workerId: string, label: string) =>
    label === 'json'
      ? new Worker(new URL('./json.worker', import.meta.url), { type: 'module' })
      : new Worker(new URL('./editor.worker', import.meta.url), { type: 'module' }),
};

export { monaco };
