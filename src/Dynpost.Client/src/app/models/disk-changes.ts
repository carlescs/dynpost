/**
 * Helpers for following files that change on disk while they are open: the host reports the
 * workspace paths of the files and folders that changed (see the `workspace.filesChanged` event),
 * and the stores re-read what they show of them.
 */

/**
 * Whether a change to `paths` can have changed the file or folder at `path`: `path` itself or a
 * folder holding it changed (a renamed or deleted folder is reported, not each file inside it).
 * Without `paths`, anything may have changed.
 */
export function touches(paths: readonly string[] | undefined, path: string): boolean {
  return !paths || paths.some((changed) => path === changed || path.startsWith(`${changed}/`));
}

/** Whether any of `paths` (all when missing) is `folder` or inside it, or a top-level file named `files`. */
export function touchesArea(
  paths: readonly string[] | undefined,
  folder: string,
  ...files: string[]
): boolean {
  return (
    !paths || paths.some((p) => p === folder || p.startsWith(`${folder}/`) || files.includes(p))
  );
}

/**
 * The content of open files as last read from or written to disk, by tab key, so a file reported
 * as changed can be told apart from the same content written back (above all, the app's own
 * saves), and the question about unsaved edits is asked once at a time per tab.
 */
export class DiskSnapshots {
  private readonly content = new Map<string, string>();
  private readonly asking = new Set<string>();

  /** Records `value` (any JSON-serializable DTO) as what is on disk for the tab `key`. */
  remember(key: string, value: unknown): void {
    this.content.set(key, JSON.stringify(value));
  }

  /** Whether `value`, just read from disk, differs from what was last recorded for `key`. */
  differs(key: string, value: unknown): boolean {
    return this.content.get(key) !== JSON.stringify(value);
  }

  forget(key: string): void {
    this.content.delete(key);
  }

  clear(): void {
    this.content.clear();
  }

  /**
   * Asks `question` for the tab `key` unless it is already being asked; resolves to its answer, or
   * `null` when the question was already open.
   */
  async ask(key: string, question: () => Promise<boolean>): Promise<boolean | null> {
    if (this.asking.has(key)) return null;
    this.asking.add(key);
    try {
      return await question();
    } finally {
      this.asking.delete(key);
    }
  }
}
