/**
 * Re-indents JSON and XML text. Both formatters work on the text's own tokens instead of parsing
 * it into values, so what is written stays as written: number literals (`1.0`, ids too large for
 * a JavaScript number), duplicate keys, string escapes, entities and attribute quoting. Only the
 * whitespace between tokens changes.
 */

/** The languages `prettyPrint` can format. */
export type PrettyLanguage = 'json' | 'xml';

export interface PrettyOptions {
  /** Accept `{{variable}}` references where JSON expects a value, a key or an XML token. */
  variables?: boolean;
  /** The indentation of one level; two spaces by default. */
  indent?: string;
}

/** The text re-indented, or `null` when it isn't well-formed (or the language can't be formatted). */
export function prettyPrint(
  text: string,
  language: string,
  options: PrettyOptions = {},
): string | null {
  if (language === 'json') return prettyJsonText(text, options);
  if (language === 'xml') return prettyXmlText(text, options);
  return null;
}

/** Whether `prettyPrint` can format text of this Monaco language. */
export function canPrettyPrint(language: string): language is PrettyLanguage {
  return language === 'json' || language === 'xml';
}

// ---------------------------------------------------------------------------------------------
// JSON

const JSON_TOKEN =
  /\s*(?:("(?:[^"\\\u0000-\u001f]|\\(?:["\\/bfnrt]|u[0-9a-fA-F]{4}))*")|(-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?)|(true|false|null)|(\{\{[^{}"]*\}\})|([{}[\]:,]))/y;

type JsonToken = { kind: 'string' | 'literal' | 'variable' | 'punct'; text: string };

function jsonTokens(text: string, variables: boolean): JsonToken[] | null {
  const tokens: JsonToken[] = [];
  let index = 0;
  while (index < text.length) {
    JSON_TOKEN.lastIndex = index;
    const match = JSON_TOKEN.exec(text);
    // Only trailing whitespace may be left when no token matches.
    if (!match) return /^\s*$/.test(text.slice(index)) ? tokens : null;
    index = JSON_TOKEN.lastIndex;
    const [, string, number, keyword, variable, punct] = match;
    if (string !== undefined) tokens.push({ kind: 'string', text: string });
    else if (number !== undefined || keyword !== undefined) {
      tokens.push({ kind: 'literal', text: number ?? keyword });
      // A literal must end at a delimiter: `12ab` or `truex` aren't JSON.
      if (/[\w.$]/.test(text[index] ?? '')) return null;
    } else if (variable !== undefined) {
      if (!variables) return null;
      tokens.push({ kind: 'variable', text: variable });
    } else tokens.push({ kind: 'punct', text: punct });
  }
  return tokens;
}

function prettyJsonText(text: string, options: PrettyOptions): string | null {
  const tokens = jsonTokens(text, options.variables ?? false);
  if (!tokens?.length) return null;
  const indent = options.indent ?? '  ';
  let position = 0;
  const peek = () => tokens[position];
  const take = () => tokens[position++];
  const isPunct = (token: JsonToken | undefined, punct: string) =>
    token?.kind === 'punct' && token.text === punct;

  const value = (depth: number): string | null => {
    const token = take();
    if (!token) return null;
    if (token.kind !== 'punct') return token.text;
    if (token.text !== '{' && token.text !== '[') return null;
    const object = token.text === '{';
    const close = object ? '}' : ']';
    if (isPunct(peek(), close)) {
      take();
      return object ? '{}' : '[]';
    }
    const inner = indent.repeat(depth + 1);
    const items: string[] = [];
    for (;;) {
      let item = '';
      if (object) {
        const key = take();
        if (key?.kind !== 'string' && key?.kind !== 'variable') return null;
        if (!isPunct(take(), ':')) return null;
        item = `${key.text}: `;
      }
      const member = value(depth + 1);
      if (member === null) return null;
      items.push(inner + item + member);
      const next = take();
      if (isPunct(next, close)) break;
      if (!isPunct(next, ',')) return null;
    }
    return `${token.text}\n${items.join(',\n')}\n${indent.repeat(depth)}${close}`;
  };

  const result = value(0);
  return result !== null && position === tokens.length ? result : null;
}

// ---------------------------------------------------------------------------------------------
// XML

/**
 * Markup that is copied as it is: declarations, comments, CDATA and doctypes. A doctype may have
 * one internal subset in brackets; each part matches one way only, so no input backtracks badly.
 */
const XML_VERBATIM =
  /<\?[\s\S]*?\?>|<!--[\s\S]*?-->|<!\[CDATA\[[\s\S]*?\]\]>|<!DOCTYPE[^<>[]*(?:\[[^\]]*\][^<>[]*)?>/iy;
/** A start, end or empty-element tag; attribute values may contain `>`. */
const XML_TAG = /<(\/?)([^\s/>"'=<]+)((?:[^<>"']|"[^"]*"|'[^']*')*?)(\/?)>/y;

type XmlToken =
  | { kind: 'open' | 'close' | 'empty'; name: string; text: string }
  | { kind: 'verbatim' | 'text'; text: string };

function xmlTokens(text: string): XmlToken[] | null {
  const tokens: XmlToken[] = [];
  let index = 0;
  while (index < text.length) {
    if (text[index] !== '<') {
      const end = text.indexOf('<', index);
      const content = text.slice(index, end < 0 ? text.length : end);
      tokens.push({ kind: 'text', text: content });
      index += content.length;
      continue;
    }
    XML_VERBATIM.lastIndex = index;
    const verbatim = XML_VERBATIM.exec(text);
    if (verbatim) {
      tokens.push({ kind: 'verbatim', text: verbatim[0] });
      index = XML_VERBATIM.lastIndex;
      continue;
    }
    XML_TAG.lastIndex = index;
    const tag = XML_TAG.exec(text);
    if (!tag) return null;
    const [whole, slash, name, , selfClosing] = tag;
    if (slash && selfClosing) return null;
    const kind = slash ? 'close' : selfClosing ? 'empty' : 'open';
    tokens.push({ kind, name, text: whole });
    index = XML_TAG.lastIndex;
  }
  return tokens;
}

function prettyXmlText(text: string, options: PrettyOptions): string | null {
  const tokens = xmlTokens(text.trim());
  if (!tokens?.length) return null;
  const indent = options.indent ?? '  ';
  const lines: string[] = [];
  const open: string[] = [];
  let sawElement = false;

  for (let i = 0; i < tokens.length; i++) {
    const token = tokens[i];
    const pad = indent.repeat(open.length);
    switch (token.kind) {
      case 'open': {
        if (sawElement && open.length === 0) return null; // A second root element.
        sawElement = true;
        // An element holding only text stays on one line: <name>Ada</name>.
        const next = tokens[i + 1];
        const after = tokens[i + 2];
        if (next?.kind === 'close' && next.name === token.name) {
          lines.push(pad + token.text + next.text);
          i += 1;
          break;
        }
        if (next?.kind === 'text' && after?.kind === 'close' && after.name === token.name) {
          lines.push(pad + token.text + next.text.trim() + after.text);
          i += 2;
          break;
        }
        lines.push(pad + token.text);
        open.push(token.name);
        break;
      }
      case 'close':
        if (open.pop() !== token.name) return null;
        lines.push(indent.repeat(open.length) + token.text);
        break;
      case 'empty':
        if (sawElement && open.length === 0) return null;
        sawElement = true;
        lines.push(pad + token.text);
        break;
      case 'verbatim':
        lines.push(pad + token.text);
        break;
      case 'text': {
        const content = token.text.trim();
        if (!content) break;
        // Text outside the root element isn't XML, except a `{{variable}}` standing for markup.
        if (open.length === 0 && !(options.variables && /^(\{\{[^{}]*\}\}\s*)+$/.test(content))) {
          return null;
        }
        lines.push(pad + content);
        break;
      }
    }
  }
  return open.length === 0 && sawElement ? lines.join('\n') : null;
}
