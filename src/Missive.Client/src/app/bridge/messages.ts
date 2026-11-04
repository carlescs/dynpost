/**
 * Bridge message contract between the UI and the .NET host. Keep in sync with
 * `Missive.Desktop/Bridge/BridgeMessages.cs` and the contracts next to the handlers
 * (`Missive.Desktop/Bridge/**`).
 */

/** An expected failure reported by the host. */
export interface BridgeError {
  code: string;
  message: string;
}

/** UI → host. */
export interface BridgeRequestEnvelope {
  id: string;
  type: MessageType;
  payload: unknown;
}

/** Host → UI, correlated by `id`. */
export type BridgeResponseEnvelope =
  { id: string; ok: true; payload?: unknown } | { id: string; ok: false; error: BridgeError };

export type GitFileStatus =
  'modified' | 'added' | 'deleted' | 'renamed' | 'untracked' | 'conflicted';

export interface GitChange {
  /** Path relative to the workspace folder, with `/` separators. */
  path: string;
  status: GitFileStatus;
  /** For a rename, the path the file was renamed from. */
  originalPath?: string;
}

export interface GitStatus {
  /** The checked-out branch; absent when HEAD is detached. */
  branch?: string;
  /** The branch's upstream, e.g. `origin/main`; absent when none is set. */
  upstream?: string;
  ahead: number;
  behind: number;
  /** Whether a merge is in progress. */
  merging: boolean;
  changes: GitChange[];
}

export type PullOutcome = 'upToDate' | 'updated' | 'conflicted';

export interface WorkspaceRequest {
  workspacePath: string;
}

/** Request and response payloads, by message type. */
export interface BridgeMessages {
  'workspace.pickFolder': {
    request: Record<string, never>;
    /** Both absent when the user cancelled. */
    response: { path?: string; name?: string };
  };
  'git.status': { request: WorkspaceRequest; response: GitStatus };
  'git.commit': {
    /** Without `paths`, every change in the workspace is committed. */
    request: WorkspaceRequest & { message: string; paths?: string[] };
    response: { commitId: string; status: GitStatus };
  };
  'git.pull': { request: WorkspaceRequest; response: { outcome: PullOutcome; status: GitStatus } };
  'git.push': { request: WorkspaceRequest; response: GitStatus };
}

export type MessageType = keyof BridgeMessages;
export type RequestOf<T extends MessageType> = BridgeMessages[T]['request'];
export type ResponseOf<T extends MessageType> = BridgeMessages[T]['response'];

/** The outcome of a bridge call: the response payload or the reported error. */
export type BridgeResult<T> = { ok: true; value: T } | { ok: false; error: BridgeError };

/** Error codes produced on the UI side of the bridge. */
export const BRIDGE_UNAVAILABLE = 'bridge.unavailable';
