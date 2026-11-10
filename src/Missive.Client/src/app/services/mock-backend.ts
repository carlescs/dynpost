import {
  Theme,
  AuthDto,
  BridgeRequestEnvelope,
  BridgeResponseEnvelope,
  CaptureDto,
  CaptureResultDto,
  CapturedValueDto,
  CookieDto,
  ERROR_CODES,
  EnvironmentDto,
  OAuth2TokenDto,
  RequestDto,
  ResponseDto,
  RunPlanDocumentDto,
  RunResultDto,
  RunStatsDto,
  WorkspaceDto,
  WorkspaceNodeDto,
} from '../models/messages';
import { VARIABLE_PATTERN } from '../models/variables';

const sample = (
  name: string,
  method: string,
  url: string,
  extra: Partial<RequestDto> = {},
): RequestDto => ({
  name,
  method,
  url,
  params: [],
  headers: [{ key: 'Accept', value: 'application/json', enabled: true }],
  auth: { type: 'none' },
  body: { type: 'none', content: '' },
  ...extra,
});

/**
 * In-memory stand-in for the .NET host, used when the UI runs in a plain browser
 * (`npm start`). It mimics the host's behavior closely enough for UI work — slug file names,
 * renames on save — but nothing is written to disk.
 */
export class MockBackend {
  private readonly root = '/browser-preview/my-api';
  private readonly recentWorkspaces = [{ name: 'my-api (browser preview)', path: this.root }];
  private readonly folders = new Set(['users', 'orders']);
  private readonly files = new Map<string, RequestDto>([
    ['users/get-user.yaml', sample('Get user', 'GET', '{{baseUrl}}/users/1')],
    [
      'users/create-user.yaml',
      sample('Create user', 'POST', '{{baseUrl}}/users', {
        headers: [{ key: 'Content-Type', value: 'application/json', enabled: true }],
        auth: { type: 'bearer', token: '{{apiToken}}' },
        body: { type: 'json', content: '{\n  "name": "Ada Lovelace"\n}\n' },
        description: 'Creates a user.\n\n- Needs a **bearer token** (`apiToken`).\n',
      }),
    ],
    ['orders/list-orders.yaml', sample('List orders', 'GET', '{{baseUrl}}/orders')],
    ['health.yaml', sample('Health check', 'GET', '{{baseUrl}}/health')],
  ]);
  private readonly environments = new Map<string, EnvironmentDto>([
    [
      'dev.yaml',
      {
        name: 'Development',
        cookieJar: true,
        variables: [
          { name: 'host', value: 'localhost:5000', secret: false },
          { name: 'baseUrl', value: 'http://{{host}}', secret: false },
          { name: 'apiToken', value: 'dev-token', secret: true },
        ],
      },
    ],
    [
      'prod.yaml',
      {
        name: 'Production',
        variables: [
          { name: 'baseUrl', value: 'https://api.example.com', secret: false },
          { name: 'apiToken', value: '', secret: true },
        ],
      },
    ],
  ]);
  private activeEnvironment: string | null = 'dev.yaml';
  /** Files kept on this computer only (git-ignored), by workspace path. */
  private readonly localOnly = new Set<string>();
  private open = false;
  private theme: Theme = 'light';
  /** Sends in progress, by id: calling the function cancels it. */
  private readonly sends = new Map<string, () => void>();
  /** The file names suggested for recent responses, by send id, for `response.saveBody`. */
  private readonly bodies = new Map<string, string>();
  /** Cookie jars by environment id. */
  private readonly jars = new Map<string, CookieDto[]>();
  /** Values requests captured, by environment id and variable name. */
  private readonly captured = new Map<string, Map<string, string>>();

  /** Run plan files by path. */
  private readonly runPlans = new Map<string, string>([
    [
      'perf/users-smoke.run.yaml',
      [
        'name: Users smoke load',
        'requests:',
        '  - users/get-user.yaml',
        '  - users/create-user.yaml',
        'load:',
        '  threads: 10',
        '  iterations: 20',
        '  rampUp: 2s',
        '  thinkTime: 100ms',
        'assert:',
        '  users/create-user.yaml:',
        '    status: 201',
        'thresholds:',
        '  p95: 500ms',
        '  errorRate: 1%',
        '',
      ].join('\n'),
    ],
  ]);
  /** Simulated runs by id: when they started and, once stopped, when they ended. */
  private readonly runs = new Map<
    string,
    { path: string; name: string; startedAt: number; endedAt: number | null }
  >();
  private static readonly RUN_MS = 6000;

  async handle(request: BridgeRequestEnvelope): Promise<BridgeResponseEnvelope> {
    const ok = (payload: unknown): BridgeResponseEnvelope => ({
      id: request.id,
      ok: true,
      payload,
    });
    const fail = (code: string, message: string): BridgeResponseEnvelope => ({
      id: request.id,
      ok: false,
      error: { code, message },
    });
    const payload = request.payload as Record<string, unknown> | undefined;

    // Sending works without a workspace, like in the host.
    if (request.type === 'request.send') {
      return this.send(request.id, payload?.['id'] as string, payload?.['request'] as RequestDto);
    }
    if (request.type === 'request.pickFile') return ok(null);
    if (request.type === 'response.saveBody') {
      // No save dialog in a browser: pretend the user picked a file in their downloads folder.
      const name = this.bodies.get(payload?.['id'] as string);
      return name
        ? ok({ path: `/browser-preview/Downloads/${name}` })
        : fail(ERROR_CODES.notFound, 'The response is no longer available; send it again.');
    }
    if (request.type === 'request.cancel') {
      const cancel = this.sends.get(payload?.['id'] as string);
      cancel?.();
      return ok(!!cancel);
    }
    if (request.type === 'workspace.openLast') return ok(null);
    if (request.type.startsWith('runPlan.') || request.type.startsWith('run.')) {
      return this.handleRuns(request, ok, fail);
    }
    if (request.type === 'workspace.recent') {
      return ok(this.recentWorkspaces);
    }
    if (request.type === 'workspace.recent.remove') {
      const path = payload?.['path'] as string;
      const index = this.recentWorkspaces.findIndex((recent) => recent.path === path);
      if (index >= 0) this.recentWorkspaces.splice(index, 1);
      return ok(null);
    }
    if (request.type === 'settings.get') return ok({ theme: this.theme });
    if (request.type === 'settings.setTheme') {
      this.theme = payload?.['theme'] as Theme;
      return ok({ theme: this.theme });
    }
    if (request.type === 'about.get') {
      return ok({
        version: 'browser preview',
        runtime: navigator.userAgent,
        operatingSystem: navigator.platform,
      });
    }
    // The browser preview is never installed, so it cannot update itself.
    if (request.type === 'update.check') return ok({ state: 'unsupported' });
    if (request.type === 'update.restart') {
      return fail(ERROR_CODES.updateFailed, 'The browser preview cannot update itself.');
    }
    if (request.type === 'workspace.pick' || request.type === 'workspace.open') {
      this.open = true;
      return ok(this.workspace());
    }
    if (!this.open) return fail(ERROR_CODES.noWorkspace, 'Open a workspace folder first.');

    switch (request.type) {
      case 'workspace.tree':
        return ok(this.workspace());
      case 'workspace.exportFormats':
        return ok([
          { id: 'postman', name: 'Postman collection v2.1', extension: '.postman_collection.json' },
          { id: 'insomnia', name: 'Insomnia v4', extension: '.insomnia.json' },
        ]);
      case 'workspace.export': {
        // No save dialog in a browser: pretend the user picked a file next to the workspace.
        const insomnia = payload?.['format'] === 'insomnia';
        const path = `${this.root}${insomnia ? '.insomnia.json' : '.postman_collection.json'}`;
        const envFiles =
          payload?.['includeEnvironments'] && !insomnia
            ? [...this.environments.keys()].map((id) =>
                id.replace(/\.yaml$/, '.postman_environment.json'),
              )
            : [];
        return ok({
          path,
          files: [path, ...envFiles],
          requests: this.files.size,
          environments: payload?.['includeEnvironments'] ? this.environments.size : 0,
          warnings: ['Browser preview: nothing was written to disk.'],
        });
      }
      case 'workspace.import': {
        // No open dialog in a browser: import a small sample collection.
        const parent = (payload?.['folder'] as string) ?? '';
        let name = 'Imported sample';
        for (let n = 2; this.exists(parent ? `${parent}/${name}` : name); n++) {
          name = `Imported sample ${n}`;
        }
        const folder = parent ? `${parent}/${name}` : name;
        this.folders.add(folder);
        this.files.set(
          `${folder}/list-products.yaml`,
          sample('List products', 'GET', '{{baseUrl}}/products', {
            params: [{ key: 'limit', value: '20', enabled: false }],
          }),
        );
        return ok({
          format: 'Postman collection v2.1',
          folder,
          requests: 1,
          folders: 1,
          environments: [],
          warnings: ['Browser preview: a sample collection was imported instead of a file.'],
        });
      }
      case 'environment.list':
        return ok({
          active: this.activeEnvironment,
          environments: [...this.environments]
            .map(([id, env]) => ({
              id,
              name: env.name,
              localOnly: this.localOnly.has(`environments/${id}`),
            }))
            .sort((a, b) => a.name.localeCompare(b.name)),
        });
      case 'environment.load': {
        const env = this.environments.get(payload?.['id'] as string);
        return env ? ok(structuredClone(env)) : fail(ERROR_CODES.notFound, 'Not found.');
      }
      case 'environment.create':
      case 'environment.save': {
        const env = structuredClone(payload?.['environment'] as EnvironmentDto);
        env.name = env.name.trim();
        env.variables = env.variables
          .filter((v) => v.name.trim() || v.value)
          .map((v) => ({ ...v, name: v.name.trim() }))
          .sort((a, b) => Number(a.secret) - Number(b.secret));
        if (!env.name) return fail(ERROR_CODES.invalidRequest, 'The environment needs a name.');
        const bad = env.variables.find((v) => !v.name || /[{}\s]/.test(v.name));
        if (bad) return fail(ERROR_CODES.invalidRequest, `'${bad.name}' is not a valid name.`);
        let id = payload?.['id'] as string | undefined;
        if (!id) {
          id = `${slug(env.name)}.yaml`;
          for (let n = 2; this.environments.has(id); n++) id = `${slug(env.name)}-${n}.yaml`;
        }
        env.cookieJar = !!env.cookieJar;
        if (!env.cookieJar) this.jars.delete(id);
        this.environments.set(id, env);
        return ok({ id, environment: structuredClone(env) });
      }
      case 'cookie.list':
        return ok(structuredClone(this.jars.get(payload?.['environment'] as string) ?? []));
      case 'cookie.delete': {
        const id = payload?.['environment'] as string;
        const left = (this.jars.get(id) ?? []).filter(
          (c) =>
            c.name !== payload?.['name'] ||
            c.domain !== payload?.['domain'] ||
            c.path !== payload?.['path'],
        );
        this.jars.set(id, left);
        return ok(structuredClone(left));
      }
      case 'cookie.clear':
        this.jars.delete(payload?.['environment'] as string);
        return ok(null);
      case 'capture.list':
        return ok(this.capturedList(payload?.['environment'] as string));
      case 'capture.clear': {
        const id = payload?.['environment'] as string;
        const name = payload?.['name'] as string | undefined;
        if (name) this.captured.get(id)?.delete(name);
        else this.captured.delete(id);
        return ok(this.capturedList(id));
      }
      case 'oauth2.token.status':
        return ok(this.oauth2Tokens.get(this.oauth2Key(payload?.['auth'] as AuthDto)) ?? null);
      case 'oauth2.token.get': {
        const auth = payload?.['auth'] as AuthDto;
        if (!auth.tokenUrl || !auth.clientId) {
          return fail(ERROR_CODES.invalidRequest, 'Enter the OAuth 2.0 token URL and client id.');
        }
        await new Promise((resolve) => setTimeout(resolve, 400));
        return ok(this.newOAuth2Token(auth));
      }
      case 'oauth2.token.clear':
        this.oauth2Tokens.delete(this.oauth2Key(payload?.['auth'] as AuthDto));
        return ok(null);
      case 'oauth2.signIn.cancel':
        return ok(false);
      case 'environment.delete': {
        const id = payload?.['id'] as string;
        this.environments.delete(id);
        this.jars.delete(id);
        this.captured.delete(id);
        if (this.activeEnvironment === id) this.activeEnvironment = null;
        return ok(null);
      }
      case 'environment.select':
        this.activeEnvironment = (payload?.['id'] as string | null) ?? null;
        return ok(null);
      case 'request.load': {
        const file = this.files.get(payload?.['path'] as string);
        return file ? ok(structuredClone(file)) : fail(ERROR_CODES.notFound, 'File not found.');
      }
      case 'request.create': {
        const folder = payload?.['folder'] as string;
        const dto = this.clean(payload?.['request'] as RequestDto);
        if (!dto.name) return fail(ERROR_CODES.invalidRequest, 'The request needs a name.');
        const path = this.freePath(folder, dto.name);
        this.files.set(path, dto);
        return ok({ path, request: structuredClone(dto) });
      }
      case 'request.save': {
        let path = payload?.['path'] as string;
        const dto = this.clean(payload?.['request'] as RequestDto);
        if (!dto.name) return fail(ERROR_CODES.invalidRequest, 'The request needs a name.');
        const previous = this.files.get(path);
        const folder = path.includes('/') ? path.slice(0, path.lastIndexOf('/')) : '';
        const stem = path.slice(path.lastIndexOf('/') + 1).replace(/\.ya?ml$/, '');
        this.files.set(path, dto);
        if (previous && previous.name !== dto.name && stem === slug(previous.name)) {
          const target = this.freePath(folder, dto.name, path);
          this.files.delete(path);
          this.files.set(target, dto);
          path = target;
        }
        return ok({ path, request: structuredClone(dto) });
      }
      case 'folder.create': {
        const parent = payload?.['parent'] as string;
        const name = ((payload?.['name'] as string) ?? '').trim();
        if (!name || /[\\/<>:"|?*]/.test(name) || name.startsWith('.')) {
          return fail(ERROR_CODES.invalidPath, `'${name}' is not a valid folder name.`);
        }
        const path = parent ? `${parent}/${name}` : name;
        if (this.exists(path)) return fail(ERROR_CODES.alreadyExists, `'${path}' already exists.`);
        this.folders.add(path);
        return ok({ path });
      }
      case 'folder.rename': {
        const path = payload?.['path'] as string;
        const name = ((payload?.['name'] as string) ?? '').trim();
        if (!name || /[\\/<>:"|?*]/.test(name) || name.startsWith('.')) {
          return fail(ERROR_CODES.invalidPath, `'${name}' is not a valid folder name.`);
        }
        const target = join(parentOf(path), name);
        if (target === path) return ok({ path });
        if (this.exists(target))
          return fail(ERROR_CODES.alreadyExists, `'${target}' already exists.`);
        this.relocateFolder(path, target);
        return ok({ path: target });
      }
      case 'folder.move': {
        const path = payload?.['path'] as string;
        const parent = payload?.['parent'] as string;
        if (parent === parentOf(path)) return ok({ path });
        if (isWithin(parent, path)) {
          return fail(ERROR_CODES.invalidPath, `'${path}' can't be moved into itself.`);
        }
        const target = join(parent, nameOf(path));
        if (this.exists(target))
          return fail(ERROR_CODES.alreadyExists, `'${target}' already exists.`);
        this.relocateFolder(path, target);
        return ok({ path: target });
      }
      case 'request.move': {
        const path = payload?.['path'] as string;
        const folder = payload?.['folder'] as string;
        const dto = this.files.get(path);
        if (!dto) return fail(ERROR_CODES.notFound, `The request file '${path}' does not exist.`);
        const target = join(folder, nameOf(path));
        if (target === path) return ok({ path });
        if (this.exists(target))
          return fail(ERROR_CODES.alreadyExists, `'${target}' already exists.`);
        this.files.delete(path);
        this.files.set(target, dto);
        return ok({ path: target });
      }
      case 'request.duplicate': {
        const path = payload?.['path'] as string;
        const dto = this.files.get(path);
        if (!dto) return fail(ERROR_CODES.notFound, `The request file '${path}' does not exist.`);
        const copy = { ...structuredClone(dto), name: `${dto.name} copy` };
        const target = this.freePath(parentOf(path), copy.name);
        this.files.set(target, copy);
        return ok({ path: target, request: structuredClone(copy) });
      }
      case 'folder.duplicate': {
        const path = payload?.['path'] as string;
        if (!this.folders.has(path))
          return fail(ERROR_CODES.notFound, `The folder '${path}' does not exist.`);
        let target = `${path} copy`;
        for (let n = 2; this.exists(target); n++) target = `${path} copy ${n}`;
        const copyPath = (p: string) => target + p.slice(path.length);
        for (const folder of [...this.folders].filter((f) => isWithin(f, path)))
          this.folders.add(copyPath(folder));
        for (const [file, dto] of [...this.files].filter(([f]) => isWithin(f, path)))
          this.files.set(copyPath(file), structuredClone(dto));
        return ok({ path: target });
      }
      case 'sharing.set': {
        const path = payload?.['path'] as string;
        const localOnly = payload?.['localOnly'] as boolean;
        const exists =
          this.files.has(path) ||
          this.runPlans.has(path) ||
          (path.startsWith('environments/') && this.environments.has(path.slice(13)));
        if (!exists) return fail(ERROR_CODES.notFound, `The file '${path}' does not exist.`);
        if (localOnly) this.localOnly.add(path);
        else this.localOnly.delete(path);
        return ok({ path, localOnly });
      }
      case 'request.delete': {
        const path = payload?.['path'] as string;
        if (!this.files.delete(path))
          return fail(ERROR_CODES.notFound, `The request file '${path}' does not exist.`);
        return ok(null);
      }
      case 'folder.delete': {
        const path = payload?.['path'] as string;
        for (const folder of [...this.folders])
          if (isWithin(folder, path)) this.folders.delete(folder);
        for (const file of [...this.files.keys()])
          if (isWithin(file, path)) this.files.delete(file);
        return ok(null);
      }
      default:
        return fail(ERROR_CODES.unknownMessage, `Unknown message type '${request.type}'.`);
    }
  }

  /** OAuth 2.0 tokens by environment and settings, like the host's cache (but in memory). */
  private readonly oauth2Tokens = new Map<string, OAuth2TokenDto>();

  private oauth2Key(auth: AuthDto): string {
    return [
      this.activeEnvironment ?? '',
      auth.grant,
      auth.tokenUrl,
      auth.clientId,
      auth.scope,
    ].join('\n');
  }

  private newOAuth2Token(auth: AuthDto): OAuth2TokenDto {
    const now = Date.now();
    const token: OAuth2TokenDto = {
      obtained: new Date(now).toISOString(),
      expires: new Date(now + 3600_000).toISOString(),
      expired: false,
      refreshable: auth.grant === 'authorizationCode',
      scope: auth.scope ?? '',
    };
    this.oauth2Tokens.set(this.oauth2Key(auth), token);
    return token;
  }

  /**
   * Pretends to send a request: after a short delay it answers with an echo of what would be
   * sent. Like the host, it resolves variables from the selected environment and refuses
   * requests with undefined ones.
   */
  private async send(
    envelopeId: string,
    id: string,
    dto: RequestDto,
  ): Promise<BridgeResponseEnvelope> {
    const fail = (code: string, message: string): BridgeResponseEnvelope => ({
      id: envelopeId,
      ok: false,
      error: { code, message },
    });

    const variables = this.variables();
    const resolve = (text: string | null | undefined) =>
      (text ?? '').replace(VARIABLE_PATTERN, (match, name: string) => variables.get(name) ?? match);
    dto = {
      ...dto,
      url: resolve(dto.url),
      params: dto.params.map((r) => ({ ...r, key: resolve(r.key), value: resolve(r.value) })),
      headers: dto.headers.map((r) => ({ ...r, key: resolve(r.key), value: resolve(r.value) })),
      auth: Object.fromEntries(
        Object.entries(dto.auth).map(([k, v]) => [k, k === 'type' ? v : resolve(v as string)]),
      ) as RequestDto['auth'],
      body: {
        ...dto.body,
        content: resolve(dto.body.content),
        variables: resolve(dto.body.variables),
      },
    };

    if (dto.auth.type === 'oauth2' && !this.oauth2Tokens.has(this.oauth2Key(dto.auth))) {
      if (dto.auth.grant === 'authorizationCode') {
        return fail(
          'oauth2.sign_in_required',
          "There is no OAuth 2.0 access token yet: use 'Get new token' on the Auth tab to sign in.",
        );
      }
      this.newOAuth2Token(dto.auth);
    }

    const enabled = (rows: RequestDto['params']) => rows.filter((r) => r.enabled && r.key);
    const text = [
      dto.url,
      ...[...dto.params, ...dto.headers].flatMap((r) => (r.enabled ? [r.key, r.value] : [])),
      dto.auth.token ?? '',
      dto.auth.username ?? '',
      dto.auth.password ?? '',
      dto.auth.key ?? '',
      dto.auth.value ?? '',
      dto.auth.tokenUrl ?? '',
      dto.auth.clientId ?? '',
      dto.auth.clientSecret ?? '',
      dto.body.content,
      dto.body.variables ?? '',
    ].join('\n');
    const missing = [...new Set([...text.matchAll(VARIABLE_PATTERN)].map((m) => m[1]))];
    if (missing.length) {
      return fail(
        ERROR_CODES.invalidRequest,
        `The request uses variables that aren't defined: ${missing.sort().join(', ')}. ` +
          (this.activeEnvironment ? 'Add them to the environment.' : 'No environment is selected.'),
      );
    }

    let url: URL;
    const raw = dto.url.trim();
    try {
      url = new URL(raw.includes('://') ? raw : `http://${raw}`);
      if (url.protocol !== 'http:' && url.protocol !== 'https:') throw new Error();
    } catch {
      return fail(ERROR_CODES.invalidRequest, `'${raw}' is not a valid http or https URL.`);
    }
    for (const param of enabled(dto.params)) url.searchParams.append(param.key, param.value);
    const auth = dto.auth;
    const apiKeyIn = auth.type === 'apiKey' && auth.key ? (auth.in ?? 'header') : null;
    if (apiKeyIn === 'query' && !url.searchParams.has(auth.key!)) {
      url.searchParams.append(auth.key!, auth.value ?? '');
    }

    const started = performance.now();
    const cancelled = await new Promise<boolean>((resolve) => {
      const timer = setTimeout(() => resolve(false), 300 + Math.random() * 500);
      this.sends.set(id, () => {
        clearTimeout(timer);
        resolve(true);
      });
    });
    this.sends.delete(id);
    if (cancelled) return fail(ERROR_CODES.cancelled, 'The request was cancelled.');

    const headers = enabled(dto.headers).map((h) => ({ key: h.key, value: h.value }));
    const named = (name: string) => (h: { key: string }) =>
      h.key.toLowerCase() === name.toLowerCase();
    // Like the host, auth only adds a header the request doesn't set itself.
    const authHeader =
      auth.type === 'basic' || auth.type === 'bearer' || auth.type === 'oauth2'
        ? { key: 'Authorization', value: `${auth.type === 'basic' ? 'Basic' : 'Bearer'} token` }
        : apiKeyIn === 'header'
          ? { key: auth.key!, value: auth.value ?? '' }
          : null;
    if (authHeader && !headers.some(named(authHeader.key))) headers.push(authHeader);
    const env = this.activeEnvironment ? this.environments.get(this.activeEnvironment) : null;
    const jarId = env?.cookieJar ? this.activeEnvironment : null;
    const jar = jarId ? (this.jars.get(jarId) ?? []) : [];

    // The request as the host would report it: Host first, the handler's additions merged into
    // the request's own headers, and credentials hidden wherever they appear.
    const mask = '••••••';
    const isApiKeyHeader = (key: string) => apiKeyIn === 'header' && named(auth.key!)({ key });
    const masked = (h: { key: string; value: string }) =>
      /^(proxy-)?authorization$/i.test(h.key)
        ? { ...h, value: h.value.includes(' ') ? `${h.value.split(' ')[0]} ${mask}` : mask }
        : isApiKeyHeader(h.key)
          ? { ...h, value: mask }
          : h;
    const encodings = ['gzip', 'deflate', 'br'];
    const ownEncoding = headers.find(named('Accept-Encoding'));
    const own = (ownEncoding?.value ?? '')
      .split(',')
      .map((e) => e.trim())
      .filter(Boolean);
    const acceptEncoding = [...own, ...encodings.filter((e) => !own.includes(e))].join(', ');
    const sentHeaders = [
      { key: 'Host', value: headers.find(named('Host'))?.value ?? url.host },
      ...headers
        .filter((h) => !named('Host')(h))
        .map((h) => (h === ownEncoding ? { ...h, value: acceptEncoding } : masked(h))),
      ...(headers.some(named('User-Agent')) ? [] : [{ key: 'User-Agent', value: 'Missive' }]),
      ...(ownEncoding ? [] : [{ key: 'Accept-Encoding', value: acceptEncoding }]),
      ...(jar.length
        ? [{ key: 'Cookie', value: jar.map((c) => `${c.name}=${c.value}`).join('; ') }]
        : []),
    ];
    const sentUrl = new URL(url);
    if (apiKeyIn === 'query') {
      const params = [...sentUrl.searchParams].map(
        ([k, v]) => [k, k === auth.key ? mask : v] as [string, string],
      );
      sentUrl.search = new URLSearchParams(params).toString();
    }
    const body = JSON.stringify({
      preview: 'Browser preview: nothing was sent. This is what the host would send.',
      method: dto.method,
      url: url.toString(),
      headers: Object.fromEntries(headers.map((h) => [h.key, h.value])),
      auth: dto.auth.type,
      body: dto.body.type === 'none' ? null : dto.body.content,
    });
    const setCookie = { key: 'Set-Cookie', value: 'preview-session=abc123; Path=/; HttpOnly' };
    if (jarId && !jar.some((c) => c.name === 'preview-session')) {
      this.jars.set(jarId, [
        ...jar,
        {
          name: 'preview-session',
          value: 'abc123',
          domain: url.hostname,
          path: '/',
          secure: false,
          httpOnly: true,
        },
      ]);
    }
    const responseHeaders = [
      { key: 'Content-Type', value: 'application/json; charset=utf-8' },
      { key: 'Content-Length', value: String(new TextEncoder().encode(body).length) },
      setCookie,
    ];
    const captures = dto.captures?.length
      ? dto.captures.map((c) => this.capture(c, responseHeaders, body))
      : null;
    const durationMs = Math.round(performance.now() - started);
    const segment = url.pathname.split('/').filter(Boolean).pop() ?? 'response';
    const fileName = segment.includes('.') ? segment : `${segment}.json`;
    this.bodies.set(id, fileName);
    return {
      id: envelopeId,
      ok: true,
      payload: {
        status: 200,
        statusText: 'OK',
        headers: responseHeaders,
        contentType: 'application/json',
        body,
        bodyEncoding: 'text',
        size: new TextEncoder().encode(body).length,
        truncated: false,
        durationMs,
        fileName,
        timings: {
          dns: 2.4,
          connect: 6.1,
          tls: url.protocol === 'https:' ? 21.7 : null,
          wait: Math.max(durationMs - 40, 1),
          download: 3.2,
        },
        redirects: [],
        sent: { method: dto.method, url: sentUrl.toString(), headers: sentHeaders },
        cookies: [
          {
            name: 'preview-session',
            value: 'abc123',
            path: '/',
            secure: false,
            httpOnly: true,
          },
        ],
        captures,
      } satisfies ResponseDto,
    };
  }

  private capturedList(id: string): CapturedValueDto[] {
    return [...(this.captured.get(id) ?? [])]
      .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
      .map(([name, value]) => ({ name, value }));
  }

  /** A simplified version of the host's capture rules: `header:Name` and `$.a.b[0]` paths. */
  private capture(
    rule: CaptureDto,
    headers: { key: string; value: string }[],
    body: string,
  ): CaptureResultDto {
    const result = (value: string | null, error?: string): CaptureResultDto => ({
      ...rule,
      value,
      error: error ?? null,
    });
    const expression = rule.expression.trim();
    let value: unknown;
    if (/^header:/i.test(expression)) {
      const name = expression.slice(7).trim().toLowerCase();
      value = headers.find((h) => h.key.toLowerCase() === name)?.value;
    } else if (expression.startsWith('$')) {
      value = JSON.parse(body);
      for (const [, key, index] of expression.slice(1).matchAll(/\.([^.[]+)|\[(\d+)\]/g)) {
        value = (value as Record<string, unknown> | null)?.[key ?? Number(index)];
      }
    } else {
      return result(null, `'${expression}' is neither a JSONPath nor a header.`);
    }
    if (value === undefined || value === null) {
      return result(null, `${expression} matches nothing in the response.`);
    }
    if (!this.activeEnvironment) {
      return result(null, 'Not stored: no environment is selected.');
    }
    const text = typeof value === 'string' ? value : JSON.stringify(value);
    const values = this.captured.get(this.activeEnvironment) ?? new Map<string, string>();
    values.set(rule.variable, text);
    this.captured.set(this.activeEnvironment, values);
    return result(text);
  }

  /** The selected environment's values, with references between variables expanded. */
  private variables(): Map<string, string> {
    const env = this.activeEnvironment ? this.environments.get(this.activeEnvironment) : null;
    const raw = new Map(
      (env?.variables ?? []).filter((v) => v.value).map((v) => [v.name, v.value]),
    );
    for (const [name, value] of this.captured.get(this.activeEnvironment ?? '') ?? []) {
      raw.set(name, value);
    }
    const resolved = new Map<string, string>();
    const resolve = (name: string, seen: Set<string>): string | undefined => {
      if (resolved.has(name)) return resolved.get(name);
      const value = raw.get(name);
      if (value === undefined || seen.has(name)) return undefined;
      const result = value.replace(
        VARIABLE_PATTERN,
        (match, inner: string) => resolve(inner, new Set([...seen, name])) ?? match,
      );
      resolved.set(name, result);
      return result;
    };
    for (const name of raw.keys()) resolve(name, new Set());
    return resolved;
  }

  private relocateFolder(path: string, target: string): void {
    const move = (p: string) => target + p.slice(path.length);
    for (const folder of [...this.folders].filter((f) => isWithin(f, path))) {
      this.folders.delete(folder);
      this.folders.add(move(folder));
    }
    for (const [file, dto] of [...this.files].filter(([f]) => isWithin(f, path))) {
      this.files.delete(file);
      this.files.set(move(file), dto);
    }
  }

  private exists(path: string): boolean {
    const lower = path.toLowerCase();
    return [...this.folders, ...this.files.keys()].some((p) => p.toLowerCase() === lower);
  }

  private clean(dto: RequestDto): RequestDto {
    const rows = (list: RequestDto['params']) => list.filter((r) => r.key || r.value);
    return {
      ...structuredClone(dto),
      name: dto.name.trim(),
      params: rows(dto.params),
      headers: rows(dto.headers),
    };
  }

  private freePath(folder: string, name: string, current?: string): string {
    for (let attempt = 1; ; attempt++) {
      const file = `${slug(name)}${attempt > 1 ? `-${attempt}` : ''}.yaml`;
      const path = folder ? `${folder}/${file}` : file;
      if (path === current || !this.files.has(path)) return path;
    }
  }

  private workspace(): WorkspaceDto {
    const nodes = (folder: string): WorkspaceNodeDto[] => {
      const prefix = folder ? `${folder}/` : '';
      const direct = (p: string) => p.startsWith(prefix) && !p.slice(prefix.length).includes('/');
      const byPath = (a: { path: string }, b: { path: string }) => a.path.localeCompare(b.path);
      const folders = [...this.folders]
        .filter(direct)
        .map((path) => ({
          path,
          name: path.slice(prefix.length),
          kind: 'folder' as const,
          children: nodes(path),
        }))
        .sort(byPath);
      const files = [...this.files]
        .filter(([path]) => direct(path))
        .map(([path, dto]) => ({
          path,
          name: dto.name,
          kind: 'request' as const,
          method: dto.method,
          localOnly: this.localOnly.has(path),
        }))
        .sort(byPath);
      return [...folders, ...files];
    };
    return { name: 'my-api (browser preview)', root: this.root, nodes: nodes('') };
  }

  private handleRuns(
    request: BridgeRequestEnvelope,
    ok: (payload: unknown) => BridgeResponseEnvelope,
    fail: (code: string, message: string) => BridgeResponseEnvelope,
  ): BridgeResponseEnvelope {
    const payload = request.payload as Record<string, unknown> | undefined;
    const path = payload?.['path'] as string;
    const document = (yaml: string): RunPlanDocumentDto => ({
      path,
      yaml,
      error: /^name:\s*\S/m.test(yaml) ? null : 'The plan needs a name.',
    });
    switch (request.type) {
      case 'runPlan.list':
        return ok(
          [...this.runPlans]
            .sort(([a], [b]) => a.localeCompare(b))
            .map(([planPath, yaml]) => ({
              path: planPath,
              name: /^name:\s*(.+)$/m.exec(yaml)?.[1] ?? null,
              error: null,
              localOnly: this.localOnly.has(planPath),
            })),
        );
      case 'runPlan.open': {
        const yaml = this.runPlans.get(path);
        return yaml === undefined
          ? fail(ERROR_CODES.notFound, 'The run plan does not exist.')
          : ok(document(yaml));
      }
      case 'runPlan.save': {
        const yaml = payload?.['yaml'] as string;
        const planName = (text: string | undefined) =>
          /^name:\s*(.+)$/m.exec(text ?? '')?.[1]?.trim();
        const previous = planName(this.runPlans.get(path));
        const renamed = planName(yaml);
        const slash = path.lastIndexOf('/');
        const folder = path.slice(0, slash + 1);
        const extension = /\.run\.ya?ml$/i.exec(path)?.[0] ?? '.run.yaml';
        const stem = path.slice(slash + 1, path.length - extension.length);
        let savedPath = path;
        if (
          previous &&
          renamed &&
          stem === slug(previous, 'plan') &&
          slug(renamed, 'plan') !== stem
        ) {
          savedPath = `${folder}${slug(renamed, 'plan')}${extension}`;
          for (let n = 2; this.runPlans.has(savedPath); n++) {
            savedPath = `${folder}${slug(renamed, 'plan')}-${n}${extension}`;
          }
          this.runPlans.delete(path);
          for (const run of this.runs.values()) if (run.path === path) run.path = savedPath;
        }
        this.runPlans.set(savedPath, yaml);
        return ok({ ...document(yaml), path: savedPath });
      }
      case 'runPlan.delete': {
        if (!this.runPlans.delete(path)) {
          return fail(ERROR_CODES.notFound, 'The run plan does not exist.');
        }
        for (const [runId, run] of this.runs) if (run.path === path) this.runs.delete(runId);
        return ok(null);
      }
      case 'run.start': {
        const yaml = this.runPlans.get(path);
        if (yaml === undefined) return fail(ERROR_CODES.notFound, 'The run plan does not exist.');
        const problem = document(yaml).error;
        if (problem) return fail(ERROR_CODES.invalidFile, problem);
        const threads = Number(/threads:\s*(\d+)/.exec(yaml)?.[1] ?? 1);
        if (threads > 200 && !payload?.['confirmLarge']) {
          return fail(
            ERROR_CODES.confirmationRequired,
            `This run starts ${threads} threads, which is a lot for a desktop app.`,
          );
        }
        const runId = `run-${this.runs.size + 1}`;
        const name = /^name:\s*(.+)$/m.exec(yaml)?.[1] ?? path;
        this.runs.set(runId, { path, name, startedAt: Date.now(), endedAt: null });
        return ok({ runId });
      }
      case 'run.cancel': {
        const run = this.runs.get(payload?.['runId'] as string);
        if (!run) return fail('run_not_found', 'There is no such run.');
        run.endedAt ??= Date.now();
        return ok(null);
      }
      case 'run.progress': {
        const run = this.runs.get(payload?.['runId'] as string);
        return run
          ? ok(this.simulateRun(payload?.['runId'] as string, run))
          : fail('run_not_found', 'There is no such run.');
      }
      case 'run.list':
        return ok(
          [...this.runs]
            .filter(
              ([, run]) => run.path === path && Date.now() - run.startedAt >= MockBackend.RUN_MS,
            )
            .reverse()
            .map(([id, run]) => {
              const result = this.simulateRun(id, run);
              return {
                runId: id,
                planPath: run.path,
                planName: run.name,
                state: result.state,
                startedAt: result.startedAt,
                elapsedMs: result.elapsedMs,
                passed: result.passed,
                samples: result.total.samples,
                errors: result.total.errors,
                errorPercent: result.total.errorPercent,
                p95: result.total.p95,
                throughput: result.total.throughput,
              };
            }),
        );
      default:
        return fail(ERROR_CODES.unknownMessage, `Unknown message type '${request.type}'.`);
    }
  }

  /** Fake numbers that grow while the simulated run lasts, so the dashboard can be tried out. */
  private simulateRun(
    runId: string,
    run: { path: string; name: string; startedAt: number; endedAt: number | null },
  ): RunResultDto {
    const elapsed = Math.min((run.endedAt ?? Date.now()) - run.startedAt, MockBackend.RUN_MS);
    const finished = run.endedAt !== null || elapsed >= MockBackend.RUN_MS;
    const seconds = Math.floor(elapsed / 1000);
    const samples = Math.floor((elapsed / 1000) * 40);
    const errors = Math.floor(samples * 0.02);
    /** `scale` is the share of the samples, `slow` stretches the latencies. */
    const stats = (scale: number, slow = 1): RunStatsDto => ({
      samples: Math.floor(samples * scale),
      errors: Math.floor(errors * scale),
      errorPercent: samples ? 2 : 0,
      min: 12 * slow,
      average: 85 * slow,
      median: 70 * slow,
      p90: 160 * slow,
      p95: 210 * slow,
      p99: 380 * slow,
      max: 450 * slow,
      throughput: 40 * scale,
      bytesSent: samples * 200 * scale,
      bytesReceived: samples * 1500 * scale,
      statusCodes: { '200': Math.floor(samples * 0.98 * scale), '500': Math.floor(errors * scale) },
      errorReasons: errors ? { 'HTTP 500': Math.floor(errors * scale) } : {},
    });
    const total = stats(1);
    const thresholds = [
      { name: 'p95', limit: '500ms', actual: '210ms', passed: true },
      { name: 'errorRate', limit: '1%', actual: '2%', passed: false },
    ];
    return {
      runId,
      planPath: run.path,
      planName: run.name,
      state: finished ? (run.endedAt !== null ? 'cancelled' : 'completed') : 'running',
      startedAt: new Date(run.startedAt).toISOString(),
      elapsedMs: elapsed,
      threads: 10,
      activeThreads: finished ? 0 : 10,
      total,
      requests: [
        { request: 'users/get-user.yaml', stats: stats(0.5, 0.6) },
        { request: 'users/create-user.yaml', stats: stats(0.5) },
      ],
      timeline: Array.from({ length: seconds + 1 }, (_, second) => ({
        second,
        samples: Math.min(40, 16 + second * 12),
        errors: second % 3 === 2 ? 2 : 0,
        averageMs: 70 + ((second * 17) % 40),
      })),
      thresholds,
      passed: finished && run.endedAt === null ? thresholds.every((t) => t.passed) : null,
      failedSamples: errors
        ? [
            {
              request: 'users/create-user.yaml',
              thread: 3,
              iteration: 4,
              atMs: 2500,
              latencyMs: 450,
              status: 500,
              error: 'HTTP 500',
              method: 'POST',
              url: 'https://api.example.com/users',
              dataRow: null,
              responsePreview: '{"error":"boom"}',
            },
          ]
        : [],
      slowestSamples: samples
        ? [
            {
              request: 'users/get-user.yaml',
              thread: 7,
              iteration: 2,
              atMs: 900,
              latencyMs: 270,
              status: 200,
              error: null,
              method: 'GET',
              url: 'https://api.example.com/users/42',
              dataRow: { userId: '42' },
              responsePreview: '{"id":42,"name":"Ada"}',
            },
          ]
        : [],
      message: null,
    };
  }
}

function parentOf(path: string): string {
  return path.includes('/') ? path.slice(0, path.lastIndexOf('/')) : '';
}

function nameOf(path: string): string {
  return path.slice(path.lastIndexOf('/') + 1);
}

function join(folder: string, name: string): string {
  return folder ? `${folder}/${name}` : name;
}

function isWithin(path: string, folder: string): boolean {
  return path === folder || path.startsWith(`${folder}/`);
}

function slug(name: string, fallback = 'request'): string {
  const value = name
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
  return value || fallback;
}
