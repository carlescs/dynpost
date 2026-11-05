/**
 * Messages exchanged with the .NET host over the Photino bridge. Keep in sync with
 * `src/Missive.Desktop/Bridge/BridgeMessages.cs`.
 */

export interface KeyValueDto {
  key: string;
  value: string;
  enabled: boolean;
}

export interface AuthDto {
  type: 'none' | 'basic' | 'bearer' | 'apiKey';
  username?: string | null;
  password?: string | null;
  token?: string | null;
  key?: string | null;
  value?: string | null;
  in?: 'header' | 'query' | null;
}

export interface BodyDto {
  type: 'none' | 'json' | 'text' | 'form' | 'urlencoded';
  content: string;
}

export interface RequestDto {
  name: string;
  method: string;
  url: string;
  params: KeyValueDto[];
  headers: KeyValueDto[];
  auth: AuthDto;
  body: BodyDto;
}

export interface SavedRequestDto {
  path: string;
  request: RequestDto;
}

export interface FolderDto {
  path: string;
}

export interface WorkspaceNodeDto {
  path: string;
  name: string;
  kind: 'folder' | 'request';
  method?: string | null;
  error?: string | null;
  children?: WorkspaceNodeDto[] | null;
}

export interface WorkspaceDto {
  name: string;
  root: string;
  nodes: WorkspaceNodeDto[];
}

export type GitFileStatus =
  'modified' | 'added' | 'deleted' | 'renamed' | 'untracked' | 'conflicted';

export interface GitChange {
  /** Path relative to the workspace folder, with `/` separators. */
  path: string;
  status: GitFileStatus;
  /** For a rename, the path the file was renamed from. */
  originalPath?: string | null;
}

export interface GitStatus {
  /** The checked-out branch; `null` when HEAD is detached. */
  branch?: string | null;
  /** The branch's upstream, e.g. `origin/main`; `null` when none is set. */
  upstream?: string | null;
  ahead: number;
  behind: number;
  /** Whether a merge is in progress. */
  merging: boolean;
  changes: GitChange[];
  /** During a merge, git's proposed commit message. */
  mergeMessage?: string | null;
}

/** Both sides of a conflicted file; a side is `null` when it deleted the file. */
export interface GitConflict {
  path: string;
  /** The common ancestor; `null` when both sides added the file. */
  base: string | null;
  /** The current branch's version. */
  ours: string | null;
  /** The incoming version. */
  theirs: string | null;
  /** The file on disk, usually with conflict markers; `null` if it was deleted. */
  working: string | null;
  hasConflictMarkers: boolean;
}

/** How the user resolves a conflicted file: take one side, or keep their own edit. */
export type ConflictResolution = 'ours' | 'theirs' | 'edited';

export type PullOutcome = 'upToDate' | 'updated' | 'conflicted';

/** Every message type with its payload and result. */
export interface BridgeMessages {
  /** Shows a folder picker and opens the chosen folder; `null` when cancelled. */
  'workspace.pick': { payload: void; result: WorkspaceDto | null };
  'workspace.open': { payload: { path: string }; result: WorkspaceDto };
  /** Reopens the workspace from the previous session; `null` when there is none. */
  'workspace.openLast': { payload: void; result: WorkspaceDto | null };
  /** Re-reads the open workspace from disk. */
  'workspace.tree': { payload: void; result: WorkspaceDto };
  'request.load': { payload: { path: string }; result: RequestDto };
  /** Saves a new request in a folder (`''` is the workspace root). */
  'request.create': { payload: { folder: string; request: RequestDto }; result: SavedRequestDto };
  /** Saves an existing request file; the result path differs if the file was renamed. */
  'request.save': { payload: { path: string; request: RequestDto }; result: SavedRequestDto };
  /** Moves a request file into `folder` (`''` is the root), keeping its file name. */
  'request.move': { payload: { path: string; folder: string }; result: { path: string } };
  /** Creates a folder inside `parent` (`''` is the workspace root). */
  'folder.create': { payload: { parent: string; name: string }; result: FolderDto };
  /** Renames a folder within its parent; the result is its new path. */
  'folder.rename': { payload: { path: string; name: string }; result: FolderDto };
  /** Moves a folder into `parent` (`''` is the root), keeping its name. */
  'folder.move': { payload: { path: string; parent: string }; result: FolderDto };
  /** Deletes a folder with its requests; refused if it holds other files. */
  'folder.delete': { payload: { path: string }; result: null };
  /** The open workspace's git status. */
  'git.status': { payload: void; result: GitStatus };
  /** Commits `paths`, or every change in the workspace when `paths` is omitted. */
  'git.commit': {
    payload: { message: string; paths?: string[] };
    result: { commitId: string; status: GitStatus };
  };
  /** Pulls the current branch with a merge (never a rebase). */
  'git.pull': { payload: void; result: { outcome: PullOutcome; status: GitStatus } };
  /** Pushes the current branch, or publishes it when it has no upstream. Never forces. */
  'git.push': { payload: void; result: GitStatus };
  'git.conflict': { payload: { path: string }; result: GitConflict };
  /** Resolves one conflicted file; `edited` is refused while it still has conflict markers. */
  'git.resolve': { payload: { path: string; resolution: ConflictResolution }; result: GitStatus };
  /** Abandons the merge in progress (`git merge --abort`). */
  'git.abortMerge': { payload: void; result: GitStatus };
}

export type BridgeMessageType = keyof BridgeMessages;

export interface BridgeRequestEnvelope {
  id: string;
  type: BridgeMessageType;
  payload?: unknown;
}

export interface BridgeErrorShape {
  code: string;
  message: string;
}

export type BridgeResponseEnvelope =
  { id: string; ok: true; payload: unknown } | { id: string; ok: false; error: BridgeErrorShape };

/** Error codes the host can return. */
export const ERROR_CODES = {
  noWorkspace: 'no_workspace',
  notFound: 'not_found',
  invalidPath: 'invalid_path',
  invalidFile: 'invalid_file',
  invalidRequest: 'invalid_request',
  alreadyExists: 'already_exists',
  folderNotEmpty: 'folder_not_empty',
  io: 'io_error',
  unknownMessage: 'unknown_message',
  badPayload: 'bad_payload',
  internal: 'internal_error',
  noHost: 'no_host',
} as const;
