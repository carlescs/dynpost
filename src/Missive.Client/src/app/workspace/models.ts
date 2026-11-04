export const HTTP_METHODS = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'HEAD', 'OPTIONS'] as const;
export type HttpMethod = (typeof HTTP_METHODS)[number];

export const BODY_TYPES = ['none', 'json', 'text', 'form', 'urlencoded'] as const;
export type BodyType = (typeof BODY_TYPES)[number];

export const AUTH_TYPES = ['none', 'basic', 'bearer', 'apiKey'] as const;
export type AuthType = (typeof AUTH_TYPES)[number];

export interface KeyValue {
  key: string;
  value: string;
  enabled: boolean;
}

/** A request open in the editor. Mirrors one request YAML file in the workspace. */
export interface RequestDocument {
  /** Workspace-relative file path; `null` for a new request that has not been saved yet. */
  path: string | null;
  /** Stable id for the open document (the path, or a generated id for unsaved requests). */
  id: string;
  name: string;
  method: HttpMethod;
  url: string;
  params: KeyValue[];
  headers: KeyValue[];
  auth: AuthType;
  bodyType: BodyType;
  body: string;
  dirty: boolean;
}

export interface WorkspaceNode {
  /** Workspace-relative path. */
  path: string;
  name: string;
  kind: 'folder' | 'request';
  /** Only for requests: shown as a badge in the tree. */
  method?: HttpMethod;
  children?: WorkspaceNode[];
}

export type GitFileStatus = 'modified' | 'added' | 'deleted' | 'untracked' | 'conflicted';

export interface GitChange {
  path: string;
  status: GitFileStatus;
}

export interface GitState {
  branch: string;
  ahead: number;
  behind: number;
  changes: GitChange[];
}

export const METHOD_COLORS: Record<HttpMethod, string> = {
  GET: 'text-emerald-600',
  POST: 'text-amber-600',
  PUT: 'text-sky-600',
  PATCH: 'text-violet-600',
  DELETE: 'text-rose-600',
  HEAD: 'text-slate-500',
  OPTIONS: 'text-slate-500',
};
