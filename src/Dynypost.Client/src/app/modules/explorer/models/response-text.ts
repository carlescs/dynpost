import { ResponseDto } from '../../../models/messages';
import { formatSize } from '../../../models/format-size';

/** Whether a text body is JSON, also when a server sends it as `text/plain` or untyped. */
export function looksLikeJson(r: ResponseDto): boolean {
  const type = r.contentType ?? '';
  if (type === 'application/json' || type.endsWith('+json')) return true;
  // Some servers send JSON as text/plain or without a content type.
  const start = r.body.trimStart()[0];
  return (!type || type === 'text/plain') && (start === '{' || start === '[');
}

/** The body re-indented when it is JSON; `null` when it isn't text JSON (or can't be parsed). */
export function prettyJson(r: ResponseDto): string | null {
  if (r.bodyEncoding !== 'text' || !looksLikeJson(r)) return null;
  try {
    return JSON.stringify(JSON.parse(r.body), null, 2);
  } catch {
    return null;
  }
}

/**
 * The body as compared in the history: pretty JSON when it is JSON, the text as received
 * otherwise, and a one-line description of a binary or missing body.
 */
export function comparableBody(r: ResponseDto, omitted = false): string {
  if (omitted) return `(The ${formatSize(r.size)} body was too large to keep in the history.)`;
  if (r.bodyEncoding !== 'text') {
    return `(Binary body: ${formatSize(r.size)}, ${r.contentType || 'unknown type'}.)`;
  }
  return prettyJson(r) ?? r.body;
}

/**
 * The status line and the headers of a response, one per line, as compared in the history.
 * Cookie values are hidden, as they are in kept responses, so they don't show up as changes.
 */
export function comparableHeaders(r: ResponseDto): string {
  const lines = [
    `${r.status} ${r.statusText}`.trim(),
    ...r.headers.map(
      (h) => `${h.key}: ${/^set-cookie$/i.test(h.key) ? hideCookie(h.value) : h.value}`,
    ),
  ];
  return lines.join('\n') + '\n';
}

/** A `Set-Cookie` value with the cookie's value hidden: `name=••••••; Path=/`. */
function hideCookie(value: string): string {
  const equals = value.indexOf('=');
  if (equals < 0) return '••••••';
  const semicolon = value.indexOf(';', equals);
  return `${value.slice(0, equals + 1)}••••••${semicolon < 0 ? '' : value.slice(semicolon)}`;
}
