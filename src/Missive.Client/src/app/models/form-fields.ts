import { FormField, RequestContent } from './workspace';

/** The body types edited as a table of fields. */
export type FieldBodyType = 'form' | 'urlencoded';

export function isFieldBody(type: string): type is FieldBodyType {
  return type === 'form' || type === 'urlencoded';
}

export function emptyField(patch: Partial<FormField> = {}): FormField {
  return {
    name: '',
    value: '',
    kind: 'text',
    enabled: true,
    contentType: '',
    fileName: '',
    encode: false,
    ...patch,
  };
}

/** Content types offered for multipart parts. */
export const PART_CONTENT_TYPES = [
  'application/json',
  'application/xml',
  'application/octet-stream',
  'application/pdf',
  'text/plain',
  'text/csv',
  'text/html',
  'image/png',
  'image/jpeg',
  'image/gif',
  'image/svg+xml',
];

/** Whether a path is absolute (POSIX, Windows drive or UNC). */
export function isAbsolutePath(path: string): boolean {
  return /^(?:\/|[A-Za-z]:[\\/]|\\\\)/.test(path.trim());
}

/** The last segment of a path. */
export function fileNameOf(path: string): string {
  return path.trim().split(/[\\/]/).pop() ?? '';
}

/**
 * The fields of a `form` or `urlencoded` body: its fields, or those of the text an older file
 * kept (one `name=value` per line).
 */
export function fieldsOf(
  content: Pick<RequestContent, 'bodyType' | 'body' | 'bodyFields'>,
): FormField[] {
  if (!isFieldBody(content.bodyType)) return [];
  return content.bodyFields ?? parseBulk(content.body, content.bodyType);
}

/**
 * Parses the bulk-edit text, which is also how older files stored these bodies: one
 * `name=value` per line, `~` before a disabled field. In `form` bodies `name=@path` is a file,
 * optionally followed by `;type=<content type>` and `;filename=<name>`. In `urlencoded` bodies a
 * line may hold several pairs joined with `&`, and values are sent as written.
 */
export function parseBulk(text: string, type: FieldBodyType): FormField[] {
  let lines = text
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
  if (type === 'urlencoded') {
    lines = lines.flatMap((line) => {
      const disabled = line.startsWith('~');
      return line
        .slice(disabled ? 1 : 0)
        .split('&')
        .filter((pair) => pair.length > 0)
        .map((pair) => (disabled ? `~${pair}` : pair));
    });
  }

  return lines.map((line) => {
    const enabled = !line.startsWith('~');
    if (!enabled) line = line.slice(1);
    const eq = line.indexOf('=');
    const name = eq >= 0 ? line.slice(0, eq).trim() : line;
    const value = eq >= 0 ? line.slice(eq + 1).trim() : '';
    if (type === 'form' && value.startsWith('@')) {
      const { path, options } = splitFileOptions(value.slice(1));
      return emptyField({
        name,
        value: path,
        kind: 'file',
        enabled,
        contentType: options['type'] ?? '',
        fileName: options['filename'] ?? '',
      });
    }
    return emptyField({ name, value, enabled });
  });
}

/** Splits `path;type=…;filename=…` (options in any order, at the end) into its parts. */
function splitFileOptions(text: string): { path: string; options: Record<string, string> } {
  const options: Record<string, string> = {};
  let path = text;
  for (;;) {
    const match = /;\s*(type|filename)=([^;]*)$/i.exec(path);
    const key = match?.[1].toLowerCase();
    if (!match || !key || key in options) break;
    options[key] = match[2].trim();
    path = path.slice(0, match.index);
  }
  return { path: path.trim(), options };
}

/** The fields as bulk-edit text (see {@link parseBulk}). */
export function formatBulk(fields: FormField[], type: FieldBodyType): string {
  return fields
    .map((field) => {
      const prefix = field.enabled ? '' : '~';
      if (type === 'form' && field.kind === 'file') {
        const type = field.contentType ? `;type=${field.contentType}` : '';
        const name = field.fileName ? `;filename=${field.fileName}` : '';
        return `${prefix}${field.name}=@${field.value}${type}${name}`;
      }
      return `${prefix}${field.name}=${field.value}`;
    })
    .join('\n');
}

/**
 * Why the fields can't be shown as bulk-edit text without losing something, or `null` when they
 * can. Editing the text would drop what it can't hold, so the table stays in use.
 */
export function bulkProblem(fields: FormField[], type: FieldBodyType): string | null {
  for (const field of fields) {
    const label = field.name ? `'${field.name}'` : 'A field';
    const trimmed = (text: string) => text === text.trim();
    if (/[\r\n]/.test(field.name + field.value + field.contentType + field.fileName)) {
      return `${label} has line breaks.`;
    }
    if (!trimmed(field.name) || !trimmed(field.value)) {
      return `${label} starts or ends with spaces.`;
    }
    if (field.name.includes('=') || field.name.startsWith('~')) {
      return `${label} has a name with '=' or a leading '~'.`;
    }
    if (type === 'urlencoded') {
      if (field.encode) return `${label} is percent-encoded when sent.`;
      if ((field.name + field.value).includes('&')) return `${label} contains '&'.`;
    } else if (field.kind === 'file') {
      if (/;\s*(type|filename)=/i.test(field.value)) return `${label} has an unusual file path.`;
      if ((field.contentType + field.fileName).includes(';')) return `${label} has a ';'.`;
    } else {
      if (field.value.startsWith('@')) return `${label} is text starting with '@'.`;
      if (field.contentType) return `${label} has a content type.`;
    }
  }
  return null;
}

/** The body as text to compare two versions of a request: its fields one per line, or its text. */
export function bodyText(content: Pick<RequestContent, 'bodyType' | 'body' | 'bodyFields'>) {
  if (!isFieldBody(content.bodyType) || !content.bodyFields) return content.body;
  return content.bodyFields
    .map((f) => {
      const parts = [
        `${f.enabled ? '' : '~'}${f.name}=${f.kind === 'file' ? '@' : ''}${f.value.replace(/\n/g, '\\n')}`,
        f.contentType ? `type=${f.contentType}` : '',
        f.kind === 'file' && f.fileName ? `filename=${f.fileName}` : '',
        f.encode ? 'encoded' : '',
      ];
      return parts.filter((p) => p).join(' ; ');
    })
    .join('\n');
}
