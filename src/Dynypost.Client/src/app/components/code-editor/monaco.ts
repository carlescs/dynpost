/**
 * The Monaco editor with the languages Dynypost highlights. Import it only through
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
// Services that editor contributions need. The JSON language's lazily loaded worker code
// registers every contribution, after Monaco has initialized its services; editors created after
// that (such as diff editors) would otherwise fail to find these services.
import 'monaco-editor/editor/common/services/treeViewsDndService';
import 'monaco-editor/editor/contrib/codelens/browser/codeLensCache';
import 'monaco-editor/editor/contrib/documentSymbols/browser/outlineModel';
import 'monaco-editor/editor/contrib/inlayHints/browser/inlayHintsController';
import 'monaco-editor/editor/contrib/suggest/browser/suggestMemory';
import 'monaco-editor/platform/actionWidget/browser/actionWidget';

// Workers are bundled from local entry points; Angular resolves `new URL(…, import.meta.url)`.
self.MonacoEnvironment = {
  getWorker: (_workerId: string, label: string) =>
    label === 'json'
      ? new Worker(new URL('./json.worker', import.meta.url), { type: 'module' })
      : new Worker(new URL('./editor.worker', import.meta.url), { type: 'module' }),
};

export { monaco };
