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

/** Every message type with its payload and result. */
export interface BridgeMessages {
  /** Shows a folder picker and opens the chosen folder; `null` when cancelled. */
  'workspace.pick': { payload: void; result: WorkspaceDto | null };
  'workspace.open': { payload: { path: string }; result: WorkspaceDto };
  /** Re-reads the open workspace from disk. */
  'workspace.tree': { payload: void; result: WorkspaceDto };
  'request.load': { payload: { path: string }; result: RequestDto };
  /** Saves a new request in a folder (`''` is the workspace root). */
  'request.create': { payload: { folder: string; request: RequestDto }; result: SavedRequestDto };
  /** Saves an existing request file; the result path differs if the file was renamed. */
  'request.save': { payload: { path: string; request: RequestDto }; result: SavedRequestDto };
  /** Creates a folder inside `parent` (`''` is the workspace root). */
  'folder.create': { payload: { parent: string; name: string }; result: FolderDto };
  /** Renames a folder within its parent; the result is its new path. */
  'folder.rename': { payload: { path: string; name: string }; result: FolderDto };
  /** Deletes a folder with its requests; refused if it holds other files. */
  'folder.delete': { payload: { path: string }; result: null };
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
