import {
  BridgeRequestEnvelope,
  BridgeResponseEnvelope,
  ERROR_CODES,
  RequestDto,
  WorkspaceDto,
  WorkspaceNodeDto,
} from './messages';

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
  private readonly folders = new Set(['users', 'orders']);
  private readonly files = new Map<string, RequestDto>([
    ['users/get-user.yaml', sample('Get user', 'GET', '{{baseUrl}}/users/1')],
    [
      'users/create-user.yaml',
      sample('Create user', 'POST', '{{baseUrl}}/users', {
        headers: [{ key: 'Content-Type', value: 'application/json', enabled: true }],
        auth: { type: 'bearer', token: '{{apiToken}}' },
        body: { type: 'json', content: '{\n  "name": "Ada Lovelace"\n}\n' },
      }),
    ],
    ['orders/list-orders.yaml', sample('List orders', 'GET', '{{baseUrl}}/orders')],
    ['health.yaml', sample('Health check', 'GET', '{{baseUrl}}/health')],
  ]);
  private open = false;
  /** Sends in progress, by id: calling the function cancels it. */
  private readonly sends = new Map<string, () => void>();

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
    if (request.type === 'request.cancel') {
      const cancel = this.sends.get(payload?.['id'] as string);
      cancel?.();
      return ok(!!cancel);
    }
    if (request.type === 'workspace.openLast') return ok(null);
    if (request.type === 'workspace.pick' || request.type === 'workspace.open') {
      this.open = true;
      return ok(this.workspace());
    }
    if (!this.open) return fail(ERROR_CODES.noWorkspace, 'Open a workspace folder first.');

    switch (request.type) {
      case 'workspace.tree':
        return ok(this.workspace());
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

  /**
   * Pretends to send a request: after a short delay it answers with an echo of what would be
   * sent. Like the host, it refuses requests with undefined variables (there are no environments).
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

    const enabled = (rows: RequestDto['params']) => rows.filter((r) => r.enabled && r.key);
    const text = [
      dto.url,
      ...[...dto.params, ...dto.headers].flatMap((r) => (r.enabled ? [r.key, r.value] : [])),
      dto.auth.token ?? '',
      dto.auth.username ?? '',
      dto.auth.password ?? '',
      dto.auth.key ?? '',
      dto.auth.value ?? '',
      dto.body.content,
    ].join('\n');
    const missing = [...new Set([...text.matchAll(/\{\{\s*([^{}\s]+)\s*\}\}/g)].map((m) => m[1]))];
    if (missing.length) {
      return fail(
        ERROR_CODES.invalidRequest,
        `The request uses variables that aren't defined: ${missing.sort().join(', ')}.`,
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

    const body = JSON.stringify({
      preview: 'Browser preview: nothing was sent. This is what the host would send.',
      method: dto.method,
      url: url.toString(),
      headers: Object.fromEntries(enabled(dto.headers).map((h) => [h.key, h.value])),
      auth: dto.auth.type,
      body: dto.body.type === 'none' ? null : dto.body.content,
    });
    return {
      id: envelopeId,
      ok: true,
      payload: {
        status: 200,
        statusText: 'OK',
        headers: [
          { key: 'Content-Type', value: 'application/json; charset=utf-8' },
          { key: 'Content-Length', value: String(new TextEncoder().encode(body).length) },
        ],
        contentType: 'application/json',
        body,
        bodyEncoding: 'text',
        size: new TextEncoder().encode(body).length,
        truncated: false,
        durationMs: Math.round(performance.now() - started),
      },
    };
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
        }))
        .sort(byPath);
      return [...folders, ...files];
    };
    return { name: 'my-api (browser preview)', root: this.root, nodes: nodes('') };
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

function slug(name: string): string {
  const value = name
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
  return value || 'request';
}
