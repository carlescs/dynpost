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

export interface ResponseDto {
  status: number;
  /** The reason phrase, e.g. `Not Found`; may be empty. */
  statusText: string;
  /** In the order received; names may repeat. */
  headers: { key: string; value: string }[];
  /** The media type without parameters, e.g. `application/json`. */
  contentType?: string | null;
  /** The body as text, or base64 when `bodyEncoding` is `base64` (binary content). */
  body: string;
  bodyEncoding: 'text' | 'base64';
  /** Size of the received body in bytes. */
  size: number;
  /** Whether the body was too large and was cut off. */
  truncated: boolean;
  durationMs: number;
}

export interface EnvironmentVariableDto {
  name: string;
  value: string;
  /** Whether the value is kept on this computer only (in `.missive/`) instead of in the file. */
  secret: boolean;
}

export interface EnvironmentDto {
  name: string;
  variables: EnvironmentVariableDto[];
}

export interface SavedEnvironmentDto {
  /** The environment's file name in the `environments` folder, e.g. `dev.yaml`. */
  id: string;
  environment: EnvironmentDto;
}

export interface EnvironmentSummaryDto {
  id: string;
  /** The environment's name, or its file name when the file can't be read. */
  name: string;
  /** Why the file can't be read, if it can't. */
  error?: string | null;
}

export interface EnvironmentListDto {
  /** The selected environment's id, or `null` when none is selected. */
  active: string | null;
  /** Sorted by name. */
  environments: EnvironmentSummaryDto[];
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

/**
 * A changed file as of the last commit (`original`) and in the working tree (`modified`). A text
 * is `null` when the file doesn't exist on that side, or when it is binary.
 */
export interface GitFileDiff {
  path: string;
  status: GitFileStatus;
  /** For a rename, the path the file had in the last commit. */
  originalPath?: string | null;
  binary: boolean;
  original: string | null;
  modified: string | null;
  /** Whether the file is a request file; its sides are then parsed into requests too. */
  isRequest: boolean;
  originalRequest?: RequestDto | null;
  modifiedRequest?: RequestDto | null;
  /** Why a side of a request file could not be read as a request, if it couldn't. */
  requestError?: string | null;
}

export interface GitRemote {
  name: string;
  fetchUrl: string;
  /** Equal to `fetchUrl` unless the remote has its own push URL. */
  pushUrl: string;
}

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
  /**
   * Sends a request (saved or not) from the host and waits for the response. `id` is chosen by
   * the UI and can be passed to `request.cancel`.
   */
  'request.send': { payload: { id: string; request: RequestDto }; result: ResponseDto };
  /** Cancels a request being sent; the result says whether it was still being sent. */
  'request.cancel': { payload: { id: string }; result: boolean };
  /** Creates a folder inside `parent` (`''` is the workspace root). */
  'folder.create': { payload: { parent: string; name: string }; result: FolderDto };
  /** Renames a folder within its parent; the result is its new path. */
  'folder.rename': { payload: { path: string; name: string }; result: FolderDto };
  /** Moves a folder into `parent` (`''` is the root), keeping its name. */
  'folder.move': { payload: { path: string; parent: string }; result: FolderDto };
  /** Deletes a folder with its requests; refused if it holds other files. */
  'folder.delete': { payload: { path: string }; result: null };
  /** The workspace's environments and the selected one. */
  'environment.list': { payload: void; result: EnvironmentListDto };
  /** Reads an environment, with the values of its secrets from this computer. */
  'environment.load': { payload: { id: string }; result: EnvironmentDto };
  /** Saves a new environment as a file named after it. */
  'environment.create': { payload: { environment: EnvironmentDto }; result: SavedEnvironmentDto };
  /** Saves an environment; the result id differs if the file was renamed. */
  'environment.save': {
    payload: { id: string; environment: EnvironmentDto };
    result: SavedEnvironmentDto;
  };
  /** Deletes an environment file and its stored secrets. */
  'environment.delete': { payload: { id: string }; result: null };
  /** Selects the environment requests are sent with; `null` selects none. */
  'environment.select': { payload: { id: string | null }; result: null };
  /** The open workspace's git status. */
  'git.status': { payload: void; result: GitStatus };
  /** Creates a git repository in the open workspace folder (`git init`). */
  'git.init': { payload: void; result: GitStatus };
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
  /** The committed and working tree versions of a changed file. */
  'git.diff': { payload: { path: string }; result: GitFileDiff };
  /** The repository's remotes, sorted by name. */
  'git.remote.list': { payload: void; result: GitRemote[] };
  /** Adds a remote; nothing is fetched or pushed. */
  'git.remote.add': { payload: { name: string; url: string }; result: GitRemote[] };
  /** Renames a remote and/or changes its URL (passing the current values keeps them). */
  'git.remote.update': {
    payload: { name: string; newName: string; url: string };
    result: GitRemote[];
  };
  /** Removes a remote and its remote-tracking branches. */
  'git.remote.remove': { payload: { name: string }; result: GitRemote[] };
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
  networkError: 'http.network_error',
  timeout: 'http.timeout',
  cancelled: 'http.cancelled',
  alreadySending: 'http.already_sending',
  unknownMessage: 'unknown_message',
  badPayload: 'bad_payload',
  internal: 'internal_error',
  noHost: 'no_host',
} as const;
