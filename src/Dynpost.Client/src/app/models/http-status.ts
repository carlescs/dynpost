/** The name and a short explanation of an HTTP status code. */
export interface HttpStatusInfo {
  name: string;
  description: string;
}

/** Every status code registered with IANA, plus a few widely used unofficial ones. */
const STATUSES: Record<number, HttpStatusInfo> = {
  100: {
    name: 'Continue',
    description: 'The server got the request headers; the client should go on and send the body.',
  },
  101: {
    name: 'Switching Protocols',
    description: 'The server is switching to the protocol the client asked for in Upgrade.',
  },
  102: {
    name: 'Processing',
    description: 'The server accepted the request and is still working on it (WebDAV).',
  },
  103: {
    name: 'Early Hints',
    description: 'Preliminary headers, such as Link, sent before the final response.',
  },
  200: { name: 'OK', description: 'The request succeeded.' },
  201: {
    name: 'Created',
    description: 'The request succeeded and a new resource was created, usually at Location.',
  },
  202: {
    name: 'Accepted',
    description: 'The request was accepted for processing, but the processing has not finished.',
  },
  203: {
    name: 'Non-Authoritative Information',
    description: 'The request succeeded, but a proxy changed the response from the origin.',
  },
  204: { name: 'No Content', description: 'The request succeeded and there is no body to return.' },
  205: {
    name: 'Reset Content',
    description: 'The request succeeded; the client should reset the view that sent it.',
  },
  206: {
    name: 'Partial Content',
    description: 'The server is returning only the range of the resource asked for in Range.',
  },
  207: {
    name: 'Multi-Status',
    description: 'The body holds a separate status for each of several resources (WebDAV).',
  },
  208: {
    name: 'Already Reported',
    description: 'The members of this binding were already listed earlier in the reply (WebDAV).',
  },
  226: {
    name: 'IM Used',
    description: 'The response is the result of instance manipulations applied to the resource.',
  },
  300: {
    name: 'Multiple Choices',
    description: 'The resource has several representations; the client should pick one.',
  },
  301: {
    name: 'Moved Permanently',
    description: 'The resource has a new permanent URL, given in Location.',
  },
  302: {
    name: 'Found',
    description: 'The resource is temporarily at another URL, given in Location.',
  },
  303: {
    name: 'See Other',
    description: 'The result is at another URL, given in Location, to be fetched with GET.',
  },
  304: {
    name: 'Not Modified',
    description: 'The resource has not changed since the version the client has cached.',
  },
  305: { name: 'Use Proxy', description: 'Deprecated: the resource must be reached via a proxy.' },
  307: {
    name: 'Temporary Redirect',
    description:
      'The resource is temporarily at another URL; repeat the request with the same method.',
  },
  308: {
    name: 'Permanent Redirect',
    description: 'The resource has a new permanent URL; repeat the request with the same method.',
  },
  400: {
    name: 'Bad Request',
    description: 'The server cannot process the request because it is malformed or invalid.',
  },
  401: {
    name: 'Unauthorized',
    description: 'Authentication is required, or the credentials sent were not accepted.',
  },
  402: {
    name: 'Payment Required',
    description: 'Reserved for payment; some APIs use it for billing.',
  },
  403: {
    name: 'Forbidden',
    description: 'The server understood the request but refuses it; the client lacks permission.',
  },
  404: { name: 'Not Found', description: 'The server cannot find the requested resource.' },
  405: {
    name: 'Method Not Allowed',
    description: 'The resource does not support this method; Allow lists the ones it does.',
  },
  406: {
    name: 'Not Acceptable',
    description: 'No representation matches the Accept headers the client sent.',
  },
  407: {
    name: 'Proxy Authentication Required',
    description: 'The client must authenticate with the proxy first.',
  },
  408: {
    name: 'Request Timeout',
    description: 'The server timed out waiting for the client to finish the request.',
  },
  409: {
    name: 'Conflict',
    description: 'The request conflicts with the current state of the resource.',
  },
  410: {
    name: 'Gone',
    description: 'The resource was deliberately removed and will not come back.',
  },
  411: {
    name: 'Length Required',
    description: 'The server needs a Content-Length header for this request.',
  },
  412: {
    name: 'Precondition Failed',
    description: 'A condition in the request headers, such as If-Match, did not hold.',
  },
  413: {
    name: 'Content Too Large',
    description: 'The request body is larger than the server is willing to process.',
  },
  414: {
    name: 'URI Too Long',
    description: 'The URL is longer than the server is willing to interpret.',
  },
  415: {
    name: 'Unsupported Media Type',
    description: 'The server does not support the body format given in Content-Type.',
  },
  416: {
    name: 'Range Not Satisfiable',
    description: 'The range asked for in Range lies outside the resource.',
  },
  417: {
    name: 'Expectation Failed',
    description: 'The server cannot meet the requirement in the Expect header.',
  },
  418: {
    name: "I'm a teapot",
    description: 'An April Fools’ joke (RFC 2324): the server refuses to brew coffee.',
  },
  421: {
    name: 'Misdirected Request',
    description: 'The request reached a server that cannot answer for this host.',
  },
  422: {
    name: 'Unprocessable Content',
    description: 'The request is well formed, but its content fails validation.',
  },
  423: { name: 'Locked', description: 'The resource is locked (WebDAV).' },
  424: {
    name: 'Failed Dependency',
    description: 'The request failed because a request it depends on failed (WebDAV).',
  },
  425: {
    name: 'Too Early',
    description: 'The server will not process a request that might be replayed (TLS early data).',
  },
  426: {
    name: 'Upgrade Required',
    description: 'The client must switch to another protocol, given in Upgrade.',
  },
  428: {
    name: 'Precondition Required',
    description: 'The server requires a conditional request, such as one with If-Match.',
  },
  429: {
    name: 'Too Many Requests',
    description: 'The client sent too many requests; Retry-After may say when to try again.',
  },
  431: {
    name: 'Request Header Fields Too Large',
    description: 'The request headers, or a single header, are too large.',
  },
  451: {
    name: 'Unavailable For Legal Reasons',
    description: 'The resource cannot be served for legal reasons, such as censorship.',
  },
  500: {
    name: 'Internal Server Error',
    description: 'The server hit an unexpected error while handling the request.',
  },
  501: {
    name: 'Not Implemented',
    description: 'The server does not support the functionality the request needs.',
  },
  502: {
    name: 'Bad Gateway',
    description: 'A gateway or proxy got an invalid response from the server behind it.',
  },
  503: {
    name: 'Service Unavailable',
    description: 'The server is overloaded or down for maintenance; try again later.',
  },
  504: {
    name: 'Gateway Timeout',
    description: 'A gateway or proxy timed out waiting for the server behind it.',
  },
  505: {
    name: 'HTTP Version Not Supported',
    description: 'The server does not support the HTTP version of the request.',
  },
  506: {
    name: 'Variant Also Negotiates',
    description: 'The server’s content negotiation is misconfigured and loops.',
  },
  507: {
    name: 'Insufficient Storage',
    description: 'The server cannot store what is needed to complete the request (WebDAV).',
  },
  508: {
    name: 'Loop Detected',
    description: 'The server found an infinite loop while processing the request (WebDAV).',
  },
  510: {
    name: 'Not Extended',
    description: 'The request needs further extensions to be fulfilled.',
  },
  511: {
    name: 'Network Authentication Required',
    description: 'The client must sign in to the network, e.g. a captive portal.',
  },
};

/** What a code that is not in the table means, by its class. */
const CLASSES: Record<string, HttpStatusInfo> = {
  '1': { name: 'Informational', description: 'An interim response; the request is in progress.' },
  '2': { name: 'Success', description: 'The request was received, understood and accepted.' },
  '3': {
    name: 'Redirection',
    description: 'Further action, usually a redirect, is needed to complete the request.',
  },
  '4': { name: 'Client Error', description: 'The request is wrong or cannot be fulfilled.' },
  '5': { name: 'Server Error', description: 'The server failed to fulfil a valid request.' },
};

/**
 * The name and explanation of a status code, falling back to its class (2xx, 4xx, ...) for codes
 * that are not registered; `null` when the code is not a valid status.
 */
export function httpStatusInfo(code: string | number | null | undefined): HttpStatusInfo | null {
  const value = Number(code);
  if (!Number.isInteger(value) || value < 100 || value > 599) return null;
  return STATUSES[value] ?? CLASSES[String(value)[0]];
}

/** The standard name of a status code (`Not Found`), or `''` when it has none. */
export function httpStatusName(code: string | number | null | undefined): string {
  const value = Number(code);
  return STATUSES[value]?.name ?? '';
}

/**
 * A tooltip for a status code: `404 Not Found — The server cannot find the requested resource.`
 * Unregistered codes are described by their class; anything else gets just the value.
 */
export function httpStatusTooltip(code: string | number | null | undefined): string {
  const info = httpStatusInfo(code);
  if (!info) return String(code ?? '');
  const value = Number(code);
  const name = STATUSES[value] ? info.name : `${info.name} (${String(value)[0]}xx)`;
  return `${value} ${name} — ${info.description}`;
}
