/**
 * The Monaco editor with the languages Dynpost highlights. Import it only through
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
// The right-click menu (Prettify, Copy JSONPath, and cut/copy/paste in it).
import 'monaco-editor/editor/contrib/clipboard/browser/clipboard';
import 'monaco-editor/editor/contrib/contextmenu/browser/contextmenu';
import 'monaco-editor/editor/contrib/codelens/browser/codeLensCache';
import 'monaco-editor/editor/contrib/documentSymbols/browser/outlineModel';
import 'monaco-editor/editor/contrib/inlayHints/browser/inlayHintsController';
import 'monaco-editor/editor/contrib/suggest/browser/suggestMemory';
import 'monaco-editor/platform/actionWidget/browser/actionWidget';
import { MenuId, MenuRegistry } from 'monaco-editor/platform/actions/common/actions';

// Workers are bundled from local entry points; Angular resolves `new URL(…, import.meta.url)`.
self.MonacoEnvironment = {
  getWorker: (_workerId: string, label: string) =>
    label === 'json'
      ? new Worker(new URL('./json.worker', import.meta.url), { type: 'module' })
      : new Worker(new URL('./editor.worker', import.meta.url), { type: 'module' }),
};

/**
 * The right-click menu offers only Dynpost's own actions (Prettify, Copy JSONPath) and cut, copy
 * and paste. The contributions Monaco brings would add Go to Symbol, Change All Occurrences, a
 * Format Document beside Prettify and a Command Palette on F1, the app's shortcuts key.
 */
const CLIPBOARD_COMMANDS = new Set([
  'editor.action.clipboardCutAction',
  'editor.action.clipboardCopyAction',
  'editor.action.clipboardPasteAction',
]);
const menuItems = MenuRegistry.getMenuItems.bind(MenuRegistry);
MenuRegistry.getMenuItems = (id) => {
  const items = menuItems(id);
  if (id !== MenuId.EditorContext) return items;
  return items.filter((item) => {
    const command = item.command?.id ?? '';
    // Actions added with `editor.addAction` are named `<editor id>:<action id>`.
    return CLIPBOARD_COMMANDS.has(command) || command.includes(':dynpost.');
  });
};

export { monaco };
