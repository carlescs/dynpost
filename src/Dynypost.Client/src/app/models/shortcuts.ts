/** What a keyboard shortcut does; `Shortcuts.run` carries each one out. */
export type ShortcutAction =
  | 'save'
  | 'send'
  | 'newRequest'
  | 'openFolder'
  | 'close'
  | 'nextTab'
  | 'previousTab'
  | 'toggleSidebar'
  | 'explorer'
  | 'environments'
  | 'runs'
  | 'git'
  | 'help'
  | 'quit';

/** A key combination. `mod` is Ctrl, or Cmd on macOS (either is accepted everywhere). */
export interface KeyCombo {
  key: string;
  mod?: boolean;
  shift?: boolean;
}

export interface Shortcut {
  action: ShortcutAction;
  label: string;
  keys: KeyCombo[];
  /** Only offered by the desktop app (the browser preview can't, e.g., quit). */
  desktopOnly?: boolean;
}

/** Every shortcut, in the order the shortcuts dialog lists them. */
export const SHORTCUTS: readonly Shortcut[] = [
  { action: 'send', label: 'Send the request', keys: [{ key: 'Enter', mod: true }] },
  { action: 'save', label: 'Save', keys: [{ key: 's', mod: true }] },
  { action: 'newRequest', label: 'New request', keys: [{ key: 'n', mod: true }] },
  { action: 'openFolder', label: 'Open a workspace folder', keys: [{ key: 'o', mod: true }] },
  {
    action: 'close',
    label: 'Close the tab, or the open diff or commit',
    keys: [
      { key: 'w', mod: true },
      { key: 'F4', mod: true },
    ],
  },
  {
    action: 'nextTab',
    label: 'Next tab',
    keys: [
      { key: 'Tab', mod: true },
      { key: 'PageDown', mod: true },
    ],
  },
  {
    action: 'previousTab',
    label: 'Previous tab',
    keys: [
      { key: 'Tab', mod: true, shift: true },
      { key: 'PageUp', mod: true },
    ],
  },
  { action: 'toggleSidebar', label: 'Show or hide the sidebar', keys: [{ key: 'b', mod: true }] },
  { action: 'explorer', label: 'Explorer', keys: [{ key: '1', mod: true }] },
  { action: 'environments', label: 'Environments', keys: [{ key: '2', mod: true }] },
  { action: 'runs', label: 'Load runs', keys: [{ key: '3', mod: true }] },
  { action: 'git', label: 'Source control', keys: [{ key: '4', mod: true }] },
  { action: 'help', label: 'Keyboard shortcuts', keys: [{ key: 'F1' }] },
  { action: 'quit', label: 'Quit Dynypost', keys: [{ key: 'q', mod: true }], desktopOnly: true },
];

/** Whether the app runs on macOS, where shortcuts are shown with ⌘ instead of Ctrl. */
export const IS_MAC =
  typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.userAgent);

/** A combination as the user reads it, e.g. `Ctrl+Shift+Tab` (`⌘⇧Tab` on macOS). */
export function formatCombo(combo: KeyCombo, mac = IS_MAC): string {
  const key = combo.key.length === 1 ? combo.key.toUpperCase() : combo.key;
  if (mac) return (combo.mod ? '⌘' : '') + (combo.shift ? '⇧' : '') + key;
  return [combo.mod && 'Ctrl', combo.shift && 'Shift', key].filter(Boolean).join('+');
}

/** Whether `event` presses `combo`. */
export function matches(combo: KeyCombo, event: KeyboardEvent): boolean {
  const mod = event.ctrlKey || event.metaKey;
  return (
    !event.altKey &&
    mod === !!combo.mod &&
    event.shiftKey === !!combo.shift &&
    event.key.toLowerCase() === combo.key.toLowerCase()
  );
}
