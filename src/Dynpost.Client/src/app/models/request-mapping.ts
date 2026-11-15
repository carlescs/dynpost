import {
  AuthDto,
  FormFieldDto,
  RequestDto,
  RequestSettingsDto,
  WorkspaceNodeDto,
} from './messages';
import {
  AUTH_TYPES,
  BODY_TYPES,
  FormField,
  HTTP_METHODS,
  HttpMethod,
  KeyValue,
  RequestAuth,
  RequestContent,
  WorkspaceNode,
} from './workspace';

export const EMPTY_AUTH: RequestAuth = {
  type: 'inherit',
  username: '',
  password: '',
  token: '',
  key: '',
  value: '',
  in: 'header',
  grant: 'clientCredentials',
  authUrl: '',
  tokenUrl: '',
  redirectUri: '',
  clientId: '',
  clientSecret: '',
  scope: '',
  audience: '',
  credentials: 'header',
};

function toMethod(value: string | null | undefined): HttpMethod {
  const upper = (value ?? '').toUpperCase();
  return (HTTP_METHODS as readonly string[]).includes(upper) ? (upper as HttpMethod) : 'GET';
}

/**
 * A `:name` path variable segment, as the host finds them. Keep in sync with `PathParameters` in
 * `src/Dynpost.Domain/Requests/PathParameters.cs`.
 */
const PATH_VARIABLE = /(?<=\/):([A-Za-z_][A-Za-z0-9_-]*)(?=[/;.]|$)/g;

/** The `:name` path variables of a URL's path, in order, each once. */
export function pathVariableNames(url: string): string[] {
  const end = url.search(/[?#]/);
  const path = end < 0 ? url : url.slice(0, end);
  return [...new Set([...path.matchAll(PATH_VARIABLE)].map((m) => m[1]))];
}

/** One row per path variable of `url`, in its order, keeping the values `rows` already have. */
export function syncPathParams(url: string, rows: readonly KeyValue[]): KeyValue[] {
  const values = new Map<string, string>();
  for (const row of rows) if (!values.has(row.key)) values.set(row.key, row.value);
  return pathVariableNames(url).map((key) => ({
    key,
    value: values.get(key) ?? '',
    enabled: true,
  }));
}

/** The settings that are set, without the `null` (inherited) ones. */
export function compactSettings(
  settings: RequestSettingsDto | null | undefined,
): RequestSettingsDto {
  return Object.fromEntries(
    Object.entries(settings ?? {}).filter(([, value]) => value !== null && value !== undefined),
  );
}

export function fromRequestDto(dto: RequestDto): RequestContent {
  return {
    name: dto.name,
    method: toMethod(dto.method),
    url: dto.url,
    pathParams: syncPathParams(
      dto.url,
      (dto.pathParams ?? []).map((r) => ({ ...r, enabled: true })),
    ),
    params: dto.params.map((r) => ({ ...r })),
    headers: dto.headers.map((r) => ({ ...r })),
    auth: fromAuthDto(dto.auth),
    bodyType: BODY_TYPES.includes(dto.body.type) ? dto.body.type : 'none',
    body: dto.body.content,
    bodyVariables: dto.body.type === 'graphql' ? (dto.body.variables ?? '{}') : '',
    bodyFields: dto.body.fields ? dto.body.fields.map(fromFieldDto) : null,
    description: dto.description ?? '',
    captures: (dto.captures ?? []).map((c) => ({ ...c })),
    settings: compactSettings(dto.settings),
  };
}

/** The editable auth settings of a DTO; fields of other types are left empty. */
export function fromAuthDto(auth: AuthDto): RequestAuth {
  return {
    type: AUTH_TYPES.includes(auth.type) ? auth.type : 'none',
    username: auth.username ?? '',
    password: auth.password ?? '',
    token: auth.token ?? '',
    key: auth.key ?? '',
    value: auth.value ?? '',
    in: auth.in === 'query' ? 'query' : 'header',
    grant: auth.grant === 'authorizationCode' ? 'authorizationCode' : 'clientCredentials',
    authUrl: auth.authUrl ?? '',
    tokenUrl: auth.tokenUrl ?? '',
    redirectUri: auth.redirectUri ?? '',
    clientId: auth.clientId ?? '',
    clientSecret: auth.clientSecret ?? '',
    scope: auth.scope ?? '',
    audience: auth.audience ?? '',
    credentials: auth.credentials === 'body' ? 'body' : 'header',
  };
}

/** Builds the DTO the host saves. Only the fields of the chosen auth type are sent. */
export function toRequestDto(content: RequestContent): RequestDto {
  const { auth } = content;
  return {
    name: content.name,
    method: content.method,
    url: content.url,
    pathParams: content.pathParams.map((r) => ({ key: r.key, value: r.value, enabled: true })),
    params: content.params.map((r) => ({ ...r })),
    headers: content.headers.map((r) => ({ ...r })),
    auth: toAuthDto(auth),
    body: toBodyDto(content),
    description: content.description,
    captures: content.captures.map((c) => ({ ...c })),
    ...(Object.keys(content.settings).length ? { settings: { ...content.settings } } : {}),
  };
}

function toBodyDto(content: RequestContent): RequestDto['body'] {
  const fields =
    (content.bodyType === 'form' || content.bodyType === 'urlencoded') && content.bodyFields;
  if (fields) {
    return { type: content.bodyType, content: '', fields: fields.map(toFieldDto) };
  }
  return {
    type: content.bodyType,
    content: content.bodyType === 'none' ? '' : content.body,
    ...(content.bodyType === 'graphql' ? { variables: content.bodyVariables } : {}),
  };
}

function fromFieldDto(dto: FormFieldDto): FormField {
  return {
    name: dto.name ?? '',
    value: dto.value ?? '',
    kind: dto.kind === 'file' ? 'file' : 'text',
    enabled: dto.enabled ?? true,
    contentType: dto.contentType ?? '',
    fileName: dto.fileName ?? '',
    encode: dto.encode ?? false,
  };
}

/** Only what the field uses is sent, so the file stays small. */
function toFieldDto(field: FormField): FormFieldDto {
  return {
    name: field.name,
    value: field.value,
    kind: field.kind,
    enabled: field.enabled,
    ...(field.contentType ? { contentType: field.contentType } : {}),
    ...(field.kind === 'file' && field.fileName ? { fileName: field.fileName } : {}),
    ...(field.encode ? { encode: true } : {}),
  };
}

/** The DTO of the auth settings: only the fields of the chosen type (and grant) are sent. */
export function toAuthDto(auth: RequestAuth): AuthDto {
  switch (auth.type) {
    case 'basic':
      return { type: 'basic', username: auth.username, password: auth.password };
    case 'bearer':
      return { type: 'bearer', token: auth.token };
    case 'apiKey':
      return { type: 'apiKey', key: auth.key, value: auth.value, in: auth.in };
    case 'oauth2': {
      const code = auth.grant === 'authorizationCode';
      return {
        type: 'oauth2',
        grant: auth.grant,
        authUrl: code ? auth.authUrl : '',
        tokenUrl: auth.tokenUrl,
        redirectUri: code ? auth.redirectUri : '',
        clientId: auth.clientId,
        clientSecret: auth.clientSecret,
        scope: auth.scope,
        audience: auth.audience,
        credentials: auth.credentials,
      };
    }
    case 'none':
      return { type: 'none' };
    default:
      return { type: 'inherit' };
  }
}

export function fromNodeDto(node: WorkspaceNodeDto): WorkspaceNode {
  return {
    path: node.path,
    name: node.name,
    kind: node.kind,
    method: node.kind === 'request' && node.method ? toMethod(node.method) : undefined,
    error: node.error ?? undefined,
    localOnly: node.localOnly || undefined,
    children: node.children?.map(fromNodeDto),
  };
}
