import {
  Theme,
  AuthDto,
  BridgeRequestEnvelope,
  BridgeResponseEnvelope,
  CaptureDto,
  CaptureResultDto,
  CapturedValueDto,
  CookieDto,
  DateFormat,
  ERROR_CODES,
  EnvironmentDto,
  FolderSettingsDto,
  InheritedSettingsDto,
  HistoryEntryDetailsDto,
  HistorySettingsDto,
  McpStatusDto,
  OAuth2TokenDto,
  RequestDto,
  ResponseDto,
  RunPlanDocumentDto,
  RunResultDto,
  RunSampleDto,
  RunSamplesPageDto,
  RunStatsDto,
  WorkspaceDto,
  WorkspaceNodeDto,
} from '../models/messages';
import { VARIABLE_PATTERN } from '../models/variables';
import { PLANS_FOLDER, REQUESTS_FOLDER } from '../models/workspace';

const EMPTY_FOLDER_SETTINGS: FolderSettingsDto = { headers: [], auth: { type: 'inherit' } };

const US_MONTHS = [
  'January',
  'February',
  'March',
  'April',
  'May',
  'June',
  'July',
  'August',
  'September',
  'October',
  'November',
  'December',
];
const US_DAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

/** The date format the browser preview reports: .NET's for en-US. */
const US_DATE_FORMAT: DateFormat = {
  culture: 'en-US',
  shortDatePattern: 'M/d/yyyy',
  shortTimePattern: 'h:mm tt',
  longTimePattern: 'h:mm:ss tt',
  dateSeparator: '/',
  timeSeparator: ':',
  amDesignator: 'AM',
  pmDesignator: 'PM',
  monthNames: US_MONTHS,
  abbreviatedMonthNames: US_MONTHS.map((name) => name.slice(0, 3)),
  monthGenitiveNames: US_MONTHS,
  abbreviatedMonthGenitiveNames: US_MONTHS.map((name) => name.slice(0, 3)),
  dayNames: US_DAYS,
  abbreviatedDayNames: US_DAYS.map((name) => name.slice(0, 3)),
};

/** A one-page PDF, the body of the simulated runs' saved PDF responses. */
const MOCK_PDF =
  'JVBERi0xLjQKMSAwIG9iago8PCAvVHlwZSAvQ2F0YWxvZyAvUGFnZXMgMiAwIFIgPj4KZW5kb2JqCjIg' +
  'MCBvYmoKPDwgL1R5cGUgL1BhZ2VzIC9LaWRzIFszIDAgUl0gL0NvdW50IDEgPj4KZW5kb2JqCjMgMCBv' +
  'YmoKPDwgL1R5cGUgL1BhZ2UgL1BhcmVudCAyIDAgUiAvTWVkaWFCb3ggWzAgMCAzMDAgMTQ0XSAvQ29u' +
  'dGVudHMgNCAwIFIgL1Jlc291cmNlcyA8PCAvRm9udCA8PCAvRjEgNSAwIFIgPj4gPj4gPj4KZW5kb2Jq' +
  'CjQgMCBvYmoKPDwgL0xlbmd0aCA0NiA+PgpzdHJlYW0KQlQgL0YxIDE4IFRmIDI0IDcyIFRkIChNb2Nr' +
  'IHVzZXIgcmVwb3J0KSBUaiBFVAplbmRzdHJlYW0KZW5kb2JqCjUgMCBvYmoKPDwgL1R5cGUgL0ZvbnQg' +
  'L1N1YnR5cGUgL1R5cGUxIC9CYXNlRm9udCAvSGVsdmV0aWNhID4+CmVuZG9iagp4cmVmCjAgNgowMDAw' +
  'MDAwMDAwIDY1NTM1IGYgCjAwMDAwMDAwMDkgMDAwMDAgbiAKMDAwMDAwMDA1OCAwMDAwMCBuIAowMDAw' +
  'MDAwMTE1IDAwMDAwIG4gCjAwMDAwMDAyNDEgMDAwMDAgbiAKMDAwMDAwMDMzNyAwMDAwMCBuIAp0cmFp' +
  'bGVyCjw8IC9TaXplIDYgL1Jvb3QgMSAwIFIgPj4Kc3RhcnR4cmVmCjQwNwolJUVPRgo=';

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
  auth: { type: 'inherit' },
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
  private readonly folders = new Set(['requests/users', 'requests/orders', 'plans/perf']);
  private readonly files = new Map<string, RequestDto>([
    ['requests/users/get-user.yaml', sample('Get user', 'GET', '{{baseUrl}}/users/1')],
    [
      'requests/users/create-user.yaml',
      sample('Create user', 'POST', '{{baseUrl}}/users', {
        headers: [{ key: 'Content-Type', value: 'application/json', enabled: true }],
        auth: { type: 'bearer', token: '{{apiToken}}' },
        body: { type: 'json', content: '{\n  "name": "Ada Lovelace"\n}\n' },
        description: 'Creates a user.\n\n- Needs a **bearer token** (`apiToken`).\n',
      }),
    ],
    [
      'requests/users/upload-avatar.yaml',
      sample('Upload avatar', 'POST', '{{baseUrl}}/users/1/avatar', {
        body: {
          type: 'form',
          content: '',
          fields: [
            {
              name: 'metadata',
              value: '{"title": "Profile picture"}',
              kind: 'text',
              enabled: true,
              contentType: 'application/json',
            },
            { name: 'avatar', value: 'fixtures/avatar.png', kind: 'file', enabled: true },
            {
              name: 'avatar',
              value: 'fixtures/missing.png',
              kind: 'file',
              enabled: true,
              fileName: 'backup.png',
            },
            { name: 'debug', value: 'true', kind: 'text', enabled: false },
          ],
        },
      }),
    ],
    ['requests/orders/list-orders.yaml', sample('List orders', 'GET', '{{baseUrl}}/orders')],
    ['requests/health.yaml', sample('Health check', 'GET', '{{baseUrl}}/health')],
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
  /** The headers and auth folders set (their `_folder.yaml`), by folder path. */
  private readonly folderSettings = new Map<string, FolderSettingsDto>([
    [
      'requests/users',
      {
        headers: [{ key: 'Accept', value: 'application/json', enabled: true }],
        auth: { type: 'inherit' },
      },
    ],
  ]);
  private open = false;
  private theme: Theme = 'light';
  private activityOrder: string[] = [];
  /** The MCP server's settings; it never runs in the browser preview. */
  private mcp = { enabled: false, port: 47823, token: null as string | null };
  private historySettings: HistorySettingsDto = {
    enabled: true,
    maxEntries: 20,
    maxBodyBytes: 1024 * 1024,
  };
  /** Responses kept per request path, newest first. */
  private readonly history = new Map<string, HistoryEntryDetailsDto[]>();
  private historyCount = 0;
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
      'plans/perf/users-smoke.run.yaml',
      [
        'name: Users smoke load',
        'requests:',
        '  - requests/users/get-user.yaml',
        '  - requests/users/create-user.yaml',
        'load:',
        '  threads: 10',
        '  iterations: 20',
        '  rampUp: 2s',
        '  thinkTime: 100ms',
        'bodies:',
        '  requests/users/create-user.yaml:',
        '    folder: perf/bodies/create-user',
        "    pattern: '*.json'",
        'assert:',
        '  requests/users/create-user.yaml:',
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
      return this.send(
        request.id,
        payload?.['id'] as string,
        payload?.['request'] as RequestDto,
        (payload?.['path'] as string | null | undefined) ?? null,
        (payload?.['folder'] as string | null | undefined) ?? null,
      );
    }
    if (request.type === 'request.pickFile') return ok(null);
    // No file dialog in a browser: pretend the user picked files from the workspace.
    if (request.type === 'request.pickFiles') {
      const picked = [{ path: 'fixtures/avatar.png', isAbsolute: false }];
      if (payload?.['multiple']) picked.push({ path: 'fixtures/report.pdf', isAbsolute: false });
      return ok(picked);
    }
    if (request.type === 'request.inspectFiles') {
      return ok(
        ((payload?.['paths'] as string[]) ?? []).map((path) => {
          const isAbsolute = /^(?:\/|[A-Za-z]:[\\/]|\\\\)/.test(path);
          const outside = !isAbsolute && path.split(/[\\/]/).includes('..');
          return {
            path,
            isAbsolute,
            exists: !outside && !path.includes('missing'),
            size: outside || path.includes('missing') ? null : 2048 + path.length * 97,
            error: outside
              ? `The file path '${path}' must stay inside the workspace, or be an absolute path.`
              : null,
          };
        }),
      );
    }
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
    if (request.type.startsWith('runFolder.')) {
      const path = payload?.['path'] as string;
      if (request.type === 'runFolder.list') {
        return ok(
          [...this.folders]
            .filter((f) => f.startsWith(`${PLANS_FOLDER}/`))
            .sort((a, b) => a.localeCompare(b)),
        );
      }
      return this.handle({
        ...request,
        type: request.type.replace('runFolder.', 'folder.') as typeof request.type,
      });
    }
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
    const settings = () => ({
      theme: this.theme,
      history: { ...this.historySettings },
      activityOrder: [...this.activityOrder],
    });
    if (request.type === 'settings.get') return ok(settings());
    if (request.type === 'settings.setTheme') {
      this.theme = payload?.['theme'] as Theme;
      return ok(settings());
    }
    if (request.type === 'settings.setHistory') {
      const history = payload as unknown as HistorySettingsDto;
      if (history.maxEntries < 1 || history.maxEntries > 500) {
        return fail('settings.invalid_history', 'Keep between 1 and 500 responses per request.');
      }
      if (history.maxBodyBytes < 0 || history.maxBodyBytes > 50 * 1024 * 1024) {
        return fail('settings.invalid_history', 'The body size limit must be between 0 and 50 MB.');
      }
      this.historySettings = { ...history };
      return ok(settings());
    }
    if (request.type === 'settings.setActivityOrder') {
      const order = (payload?.['order'] as string[] | undefined) ?? [];
      if (new Set(order).size !== order.length || order.some((id) => !id.trim())) {
        return fail(
          'settings.invalid_activity_order',
          'The activity order must list distinct, non-empty view ids.',
        );
      }
      this.activityOrder = [...order];
      return ok(settings());
    }
    const mcpStatus = (): McpStatusDto => ({
      ...this.mcp,
      running: false,
      url: `http://localhost:${this.mcp.port}/mcp`,
      error: this.mcp.enabled ? 'The MCP server runs only in the desktop app.' : null,
    });
    if (request.type === 'mcp.status') return ok(mcpStatus());
    if (request.type === 'mcp.set') {
      const port = (payload?.['port'] as number | undefined) ?? this.mcp.port;
      if (!Number.isInteger(port) || port < 1024 || port > 65535) {
        return fail('mcp.invalid_port', 'Choose a port between 1024 and 65535.');
      }
      const enabled = payload?.['enabled'] === true;
      this.mcp = { enabled, port, token: this.mcp.token ?? (enabled ? mockToken() : null) };
      return ok(mcpStatus());
    }
    if (request.type === 'mcp.regenerateToken') {
      this.mcp = { ...this.mcp, token: mockToken() };
      return ok(mcpStatus());
    }
    if (request.type === 'about.get') {
      return ok({
        version: 'browser preview',
        runtime: navigator.userAgent,
        operatingSystem: navigator.platform,
      });
    }
    // The browser preview has no regional settings to read; it formats dates as en-US does.
    if (request.type === 'locale.dateFormat') return ok(US_DATE_FORMAT);
    // The browser preview is never installed, so it cannot update itself.
    if (request.type === 'update.check') return ok({ state: 'unsupported' });
    if (request.type === 'update.restart') {
      return fail(ERROR_CODES.updateFailed, 'The browser preview cannot update itself.');
    }
    // The browser preview's tab is the browser's to close.
    if (request.type === 'app.quit') return ok(null);
    if (request.type === 'workspace.pick' || request.type === 'workspace.open') {
      this.open = true;
      return ok(this.workspace());
    }
    if (!this.open) return fail(ERROR_CODES.noWorkspace, 'Open a workspace folder first.');

    switch (request.type) {
      // A browser has no file manager to open; pretend it was shown.
      case 'workspace.reveal':
        return ok(null);
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
        const parent = (payload?.['folder'] as string) ?? REQUESTS_FOLDER;
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
          this.moveHistory(path, target);
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
        this.moveHistory(path, target);
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
        for (const [plan, yaml] of [...this.runPlans].filter(([p]) => isWithin(p, path)))
          this.runPlans.set(copyPath(plan), yaml);
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
        this.history.delete(path);
        return ok(null);
      }
      case 'history.list':
        return ok((this.history.get(payload?.['path'] as string) ?? []).map((h) => h.entry));
      case 'history.get': {
        const found = this.history
          .get(payload?.['path'] as string)
          ?.find((h) => h.entry.id === payload?.['id']);
        return found
          ? ok(structuredClone(found))
          : fail(ERROR_CODES.notFound, 'That response is no longer kept in the history.');
      }
      case 'history.delete': {
        const path = payload?.['path'] as string;
        const id = payload?.['id'] as string | null | undefined;
        const left = id ? (this.history.get(path) ?? []).filter((h) => h.entry.id !== id) : [];
        if (left.length) this.history.set(path, left);
        else this.history.delete(path);
        return ok(left.map((h) => h.entry));
      }
      case 'folder.settings.load': {
        const path = payload?.['path'] as string;
        if (path !== REQUESTS_FOLDER && !this.folders.has(path))
          return fail(ERROR_CODES.notFound, `The folder '${path}' does not exist.`);
        return ok(structuredClone(this.folderSettings.get(path) ?? EMPTY_FOLDER_SETTINGS));
      }
      case 'folder.settings.save': {
        const path = payload?.['path'] as string;
        if (path !== REQUESTS_FOLDER && !this.folders.has(path))
          return fail(ERROR_CODES.notFound, `The folder '${path}' does not exist.`);
        const given = payload?.['settings'] as FolderSettingsDto;
        const settings: FolderSettingsDto = {
          headers: given.headers
            .filter((h) => h.key || h.value)
            .map((h) => ({ ...h, key: h.key.trim() })),
          auth: given.auth,
        };
        const names = settings.headers.map((h) => h.key.toLowerCase());
        if (names.some((name, i) => names.indexOf(name) !== i))
          return fail(ERROR_CODES.invalidRequest, 'A header is defined more than once.');
        if (!settings.headers.length && settings.auth.type === 'inherit')
          this.folderSettings.delete(path);
        else this.folderSettings.set(path, structuredClone(settings));
        return ok(settings);
      }
      case 'folder.settings.inherited':
        return ok(this.inherited(payload?.['path'] as string));
      case 'folder.delete': {
        const path = payload?.['path'] as string;
        for (const folder of [...this.folderSettings.keys()])
          if (isWithin(folder, path)) this.folderSettings.delete(folder);
        for (const folder of [...this.folders])
          if (isWithin(folder, path)) this.folders.delete(folder);
        for (const file of [...this.files.keys()])
          if (isWithin(file, path)) this.files.delete(file);
        for (const file of [...this.history.keys()])
          if (isWithin(file, path)) this.history.delete(file);
        for (const plan of [...this.runPlans.keys()].filter((p) => isWithin(p, path))) {
          this.runPlans.delete(plan);
          for (const [runId, run] of this.runs) if (run.path === plan) this.runs.delete(runId);
        }
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
    path: string | null,
    folder: string | null,
  ): Promise<BridgeResponseEnvelope> {
    const fail = (code: string, message: string): BridgeResponseEnvelope => ({
      id: envelopeId,
      ok: false,
      error: { code, message },
    });
    // Like the host: the folders' headers come first unless the request sets them, and their
    // auth replaces `inherit`.
    const inherited = this.inherited(path ? parentOf(path) : (folder ?? ''));
    const ownHeaders = new Set(
      dto.headers.filter((h) => h.enabled).map((h) => h.key.toLowerCase()),
    );
    dto = {
      ...dto,
      headers: [
        ...inherited.headers
          .filter((h) => !ownHeaders.has(h.key.toLowerCase()))
          .map((h) => ({ key: h.key, value: h.value, enabled: true })),
        ...dto.headers,
      ],
      auth: dto.auth.type === 'inherit' ? inherited.auth : dto.auth,
    };

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
        fields: dto.body.fields?.map((f) => ({
          ...f,
          name: resolve(f.name),
          value: resolve(f.value),
          contentType: resolve(f.contentType ?? ''),
          fileName: resolve(f.fileName ?? ''),
        })),
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
      ...(dto.body.fields ?? []).flatMap((f) =>
        f.enabled ? [f.name, f.value, f.contentType ?? '', f.fileName ?? ''] : [],
      ),
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
      ...(headers.some(named('User-Agent')) ? [] : [{ key: 'User-Agent', value: 'Dynpost' }]),
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
      body: dto.body.type === 'none' ? null : (dto.body.fields ?? dto.body.content),
    });
    const parts =
      dto.body.type === 'form' && dto.body.fields
        ? dto.body.fields
            .filter((f) => f.enabled && f.name)
            .map((f) => ({
              name: f.name,
              fileName:
                f.kind === 'file' ? f.fileName || (f.value.split(/[\\/]/).pop() ?? '') : null,
              contentType: f.contentType || (f.kind === 'file' ? 'application/octet-stream' : null),
              size: f.kind === 'file' ? 2048 : new TextEncoder().encode(f.value).length,
            }))
        : null;
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
    const response = {
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
      sent: { method: dto.method, url: sentUrl.toString(), headers: sentHeaders, parts },
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
    } satisfies ResponseDto;
    return { id: envelopeId, ok: true, payload: this.recordHistory(path, dto.method, response) };
  }

  /** Keeps a response in a request's history, like the host; returns it with its entry id. */
  private recordHistory(path: string | null, method: string, response: ResponseDto): ResponseDto {
    if (!path || !this.files.has(path) || !this.historySettings.enabled) return response;
    const id = `preview-${++this.historyCount}`;
    const kept: ResponseDto = {
      ...structuredClone(response),
      headers: response.headers.map((h) =>
        /^set-cookie$/i.test(h.key) ? { ...h, value: h.value.replace(/=[^;]*/, '=••••••') } : h,
      ),
      cookies: response.cookies?.map((c) => ({ ...c, value: '••••••' })),
      historyId: null,
    };
    const entry = {
      id,
      sentAt: new Date().toISOString(),
      method,
      url: response.sent?.url ?? '',
      status: response.status,
      statusText: response.statusText,
      durationMs: response.durationMs,
      size: response.size,
      contentType: response.contentType,
    };
    const entries = [{ entry, response: kept }, ...(this.history.get(path) ?? [])];
    this.history.set(path, entries.slice(0, this.historySettings.maxEntries));
    return { ...response, historyId: id };
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

  private moveHistory(path: string, target: string): void {
    for (const [file, entries] of [...this.history].filter(([f]) => isWithin(f, path))) {
      this.history.delete(file);
      this.history.set(target + file.slice(path.length), entries);
    }
  }

  /** What everything directly inside `folder` inherits from it and the folders above. */
  private inherited(folder: string): InheritedSettingsDto {
    const result: InheritedSettingsDto = { headers: [], auth: { type: 'none' }, authFolder: null };
    if (!isWithin(folder, REQUESTS_FOLDER)) return result;
    const segments = folder.split('/');
    for (let i = 1; i <= segments.length; i++) {
      const path = segments.slice(0, i).join('/');
      const settings = this.folderSettings.get(path);
      if (!settings) continue;
      for (const header of settings.headers.filter((h) => h.enabled && h.key)) {
        result.headers = result.headers.filter(
          (h) => h.key.toLowerCase() !== header.key.toLowerCase(),
        );
        result.headers.push({ key: header.key, value: header.value, folder: path });
      }
      if (settings.auth.type !== 'inherit') {
        result.auth = structuredClone(settings.auth);
        result.authFolder = path;
      }
    }
    return result;
  }

  private relocateFolder(path: string, target: string): void {
    const move = (p: string) => target + p.slice(path.length);
    for (const [folder, settings] of [...this.folderSettings].filter(([f]) => isWithin(f, path))) {
      this.folderSettings.delete(folder);
      this.folderSettings.set(move(folder), settings);
    }
    this.moveHistory(path, target);
    for (const folder of [...this.folders].filter((f) => isWithin(f, path))) {
      this.folders.delete(folder);
      this.folders.add(move(folder));
    }
    for (const [file, dto] of [...this.files].filter(([f]) => isWithin(f, path))) {
      this.files.delete(file);
      this.files.set(move(file), dto);
    }
    for (const [plan, yaml] of [...this.runPlans].filter(([p]) => isWithin(p, path))) {
      this.runPlans.delete(plan);
      this.runPlans.set(move(plan), yaml);
      for (const run of this.runs.values()) if (run.path === plan) run.path = move(plan);
    }
  }

  private exists(path: string): boolean {
    const lower = path.toLowerCase();
    return [...this.folders, ...this.files.keys(), ...this.runPlans.keys()].some(
      (p) => p.toLowerCase() === lower,
    );
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
    return { name: 'my-api (browser preview)', root: this.root, nodes: nodes(REQUESTS_FOLDER) };
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
      case 'runPlan.move': {
        const yaml = this.runPlans.get(path);
        if (yaml === undefined) return fail(ERROR_CODES.notFound, 'The run plan does not exist.');
        const target = join(payload?.['folder'] as string, nameOf(path));
        if (target === path) return ok({ path });
        if (this.exists(target))
          return fail(ERROR_CODES.alreadyExists, `'${target}' already exists.`);
        this.runPlans.delete(path);
        this.runPlans.set(target, yaml);
        for (const run of this.runs.values()) if (run.path === path) run.path = target;
        if (this.localOnly.delete(path)) this.localOnly.add(target);
        return ok({ path: target });
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
      case 'run.samples': {
        const runId = payload?.['runId'] as string;
        const run = this.runs.get(runId);
        if (!run) return fail('run_not_found', 'There is no such run.');
        const sent = this.simulateRun(runId, run).total.samples;
        const log = Array.from({ length: Math.min(sent, 10_000) }, (_, i) =>
          this.simulateSample(i),
        );
        const matching = payload?.['failedOnly'] ? log.filter((s) => s.error) : log;
        const offset = Math.min(Math.max(0, payload?.['offset'] as number), matching.length);
        const limit = Math.min(Math.max(1, payload?.['limit'] as number), 500);
        return ok({
          offset,
          matching: matching.length,
          logged: log.length,
          sent,
          samples: matching.slice(offset, offset + limit),
        } satisfies RunSamplesPageDto);
      }
      case 'run.response':
        return /^\d+\.pdf$/.test(payload?.['file'] as string)
          ? ok({ body: MOCK_PDF })
          : fail('response_not_found', 'The response was not saved.');
      case 'run.delete': {
        const runIds = (payload?.['runIds'] as string[] | undefined) ?? [];
        if (!runIds.length) return fail('invalid_request', 'Choose the runs to delete.');
        for (const runId of runIds) {
          const run = this.runs.get(runId);
          if (run && this.simulateRun(runId, run).state === 'running')
            return fail('invalid_request', 'Stop the run before deleting it.');
        }
        const missing = runIds.filter((runId) => !this.runs.delete(runId));
        return missing.length ? fail('run_not_found', 'There is no such run.') : ok(null);
      }
      case 'run.clear': {
        const runs = [...this.runs].filter(([, run]) => run.path === path);
        if (runs.some(([id, run]) => this.simulateRun(id, run).state === 'running'))
          return fail('invalid_request', 'Stop the run of this plan before clearing its history.');
        for (const [id] of runs) this.runs.delete(id);
        return ok(null);
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

  /**
   * The `index`th fake request of a simulated run: one in fifty fails, and one in ten is a PDF
   * report whose response was saved, as if the plan had `saveResponses`.
   */
  private simulateSample(index: number): RunSampleDto {
    const create = index % 2 === 1;
    const failed = index % 50 === 49;
    if (index % 10 === 4) {
      return {
        request: 'requests/users/get-report.yaml',
        thread: (index % 10) + 1,
        iteration: Math.floor(index / 10) + 1,
        atMs: index * 25,
        latencyMs: 120 + ((index * 37) % 200),
        status: 200,
        error: null,
        method: 'GET',
        url: `https://api.example.com/users/${index}/report`,
        dataRow: null,
        responsePreview: null,
        contentType: 'application/pdf',
        responseFile: `${index + 1}.pdf`,
      };
    }
    return {
      request: create ? 'requests/users/create-user.yaml' : 'requests/users/get-user.yaml',
      thread: (index % 10) + 1,
      iteration: Math.floor(index / 10) + 1,
      atMs: index * 25,
      latencyMs: 40 + ((index * 37) % 200),
      status: failed ? 500 : create ? 201 : 200,
      error: failed ? 'HTTP 500' : null,
      method: create ? 'POST' : 'GET',
      url: create ? 'https://api.example.com/users' : `https://api.example.com/users/${index}`,
      dataRow: null,
      bodyFile: create
        ? `perf/bodies/create-user/${String((index >> 1) % 5).padStart(3, '0')}.json`
        : null,
      responsePreview: failed
        ? '{"error":"boom"}'
        : JSON.stringify({ id: index, name: `User ${index}` }, null, 2),
      contentType: 'application/json',
    };
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
        { request: 'requests/users/get-user.yaml', stats: stats(0.5, 0.6) },
        { request: 'requests/users/create-user.yaml', stats: stats(0.5) },
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
              request: 'requests/users/create-user.yaml',
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
              bodyFile: 'perf/bodies/create-user/002.json',
            },
          ]
        : [],
      slowestSamples: samples
        ? [
            {
              request: 'requests/users/get-user.yaml',
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

/** A random token like the host's: 32 bytes as lower-case hex. */
function mockToken(): string {
  return Array.from(crypto.getRandomValues(new Uint8Array(32)), (b) =>
    b.toString(16).padStart(2, '0'),
  ).join('');
}
