import { languageForMediaType } from '../../../components/code-editor/monaco-loader';
import { BodyType } from '../../../models/workspace';

/**
 * The Monaco language of a request's JSON or raw text body. A raw body is XML when it starts with
 * `<` and JSON when it starts with `{` or `[` (but not a `{{variable}}`); otherwise its language
 * comes from the `Content-Type` header the request sets (an enabled one) or else one its folders
 * set.
 */
export function requestBodyLanguage(
  bodyType: BodyType,
  body: string,
  headers: { key: string; value: string; enabled?: boolean }[],
  inheritedHeaders: { key: string; value: string }[] = [],
): string {
  if (bodyType === 'json') return 'json';
  if (bodyType !== 'text') return 'plaintext';
  const start = body.trimStart();
  if (start.startsWith('<')) return 'xml';
  if (start.startsWith('[') || (start.startsWith('{') && !start.startsWith('{{'))) return 'json';
  const isContentType = (h: { key: string }) => h.key.trim().toLowerCase() === 'content-type';
  const header =
    headers.find((h) => h.enabled !== false && isContentType(h)) ??
    inheritedHeaders.find(isContentType);
  return languageForMediaType(header?.value.split(';')[0].trim());
}
