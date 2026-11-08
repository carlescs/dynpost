import { AuthDto, RequestDto, WorkspaceNodeDto } from './messages';
import {
  AUTH_TYPES,
  BODY_TYPES,
  HTTP_METHODS,
  HttpMethod,
  RequestAuth,
  RequestContent,
  WorkspaceNode,
} from './workspace';

export const EMPTY_AUTH: RequestAuth = {
  type: 'none',
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

export function fromRequestDto(dto: RequestDto): RequestContent {
  return {
    name: dto.name,
    method: toMethod(dto.method),
    url: dto.url,
    params: dto.params.map((r) => ({ ...r })),
    headers: dto.headers.map((r) => ({ ...r })),
    auth: {
      type: AUTH_TYPES.includes(dto.auth.type) ? dto.auth.type : 'none',
      username: dto.auth.username ?? '',
      password: dto.auth.password ?? '',
      token: dto.auth.token ?? '',
      key: dto.auth.key ?? '',
      value: dto.auth.value ?? '',
      in: dto.auth.in === 'query' ? 'query' : 'header',
      grant: dto.auth.grant === 'authorizationCode' ? 'authorizationCode' : 'clientCredentials',
      authUrl: dto.auth.authUrl ?? '',
      tokenUrl: dto.auth.tokenUrl ?? '',
      redirectUri: dto.auth.redirectUri ?? '',
      clientId: dto.auth.clientId ?? '',
      clientSecret: dto.auth.clientSecret ?? '',
      scope: dto.auth.scope ?? '',
      audience: dto.auth.audience ?? '',
      credentials: dto.auth.credentials === 'body' ? 'body' : 'header',
    },
    bodyType: BODY_TYPES.includes(dto.body.type) ? dto.body.type : 'none',
    body: dto.body.content,
    description: dto.description ?? '',
    captures: (dto.captures ?? []).map((c) => ({ ...c })),
  };
}

/** Builds the DTO the host saves. Only the fields of the chosen auth type are sent. */
export function toRequestDto(content: RequestContent): RequestDto {
  const { auth } = content;
  return {
    name: content.name,
    method: content.method,
    url: content.url,
    params: content.params.map((r) => ({ ...r })),
    headers: content.headers.map((r) => ({ ...r })),
    auth: toAuthDto(auth),
    body: { type: content.bodyType, content: content.bodyType === 'none' ? '' : content.body },
    description: content.description,
    captures: content.captures.map((c) => ({ ...c })),
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
    default:
      return { type: 'none' };
  }
}

export function fromNodeDto(node: WorkspaceNodeDto): WorkspaceNode {
  return {
    path: node.path,
    name: node.name,
    kind: node.kind,
    method: node.kind === 'request' && node.method ? toMethod(node.method) : undefined,
    error: node.error ?? undefined,
    children: node.children?.map(fromNodeDto),
  };
}
