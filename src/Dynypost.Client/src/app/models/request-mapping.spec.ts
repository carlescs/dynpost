import { RequestDto } from './messages';
import { EMPTY_AUTH, fromNodeDto, fromRequestDto, toRequestDto } from './request-mapping';
import { RequestContent } from './workspace';

const content: RequestContent = {
  name: 'Create user',
  method: 'POST',
  url: 'https://{{host}}/users',
  params: [{ key: 'page', value: '1', enabled: true }],
  headers: [{ key: 'Accept', value: 'application/json', enabled: false }],
  auth: { ...EMPTY_AUTH, type: 'bearer', token: 't0k', username: 'kept while editing' },
  bodyType: 'json',
  body: '{"name":"Ada"}',
  bodyVariables: '',
  bodyFields: null,
  description: '# Notes',
  captures: [{ variable: 'token', expression: '$.token' }],
};

describe('request mapping', () => {
  describe('toRequestDto', () => {
    it('sends only the fields of the chosen auth type', () => {
      expect(toRequestDto(content).auth).toEqual({ type: 'bearer', token: 't0k' });
      expect(
        toRequestDto({ ...content, auth: { ...content.auth, type: 'basic', password: 'p' } }).auth,
      ).toEqual({ type: 'basic', username: 'kept while editing', password: 'p' });
      expect(
        toRequestDto({
          ...content,
          auth: { ...content.auth, type: 'apiKey', key: 'X-Key', value: 'v', in: 'query' },
        }).auth,
      ).toEqual({ type: 'apiKey', key: 'X-Key', value: 'v', in: 'query' });
      expect(toRequestDto({ ...content, auth: EMPTY_AUTH }).auth).toEqual({ type: 'inherit' });
      expect(toRequestDto({ ...content, auth: { ...content.auth, type: 'none' } }).auth).toEqual({
        type: 'none',
      });
    });

    it('sends the OAuth 2.0 fields the grant uses', () => {
      const oauth = {
        ...EMPTY_AUTH,
        type: 'oauth2' as const,
        authUrl: 'https://auth.test/authorize',
        tokenUrl: 'https://auth.test/token',
        redirectUri: 'http://127.0.0.1:8765/cb',
        clientId: 'app',
        clientSecret: '{{secret}}',
        scope: 'read',
      };
      expect(toRequestDto({ ...content, auth: oauth }).auth).toEqual({
        type: 'oauth2',
        grant: 'clientCredentials',
        authUrl: '',
        tokenUrl: 'https://auth.test/token',
        redirectUri: '',
        clientId: 'app',
        clientSecret: '{{secret}}',
        scope: 'read',
        audience: '',
        credentials: 'header',
      });
      expect(
        toRequestDto({ ...content, auth: { ...oauth, grant: 'authorizationCode' } }).auth,
      ).toMatchObject({
        grant: 'authorizationCode',
        authUrl: 'https://auth.test/authorize',
        redirectUri: 'http://127.0.0.1:8765/cb',
      });
    });

    it('drops the body content when there is no body', () => {
      expect(toRequestDto({ ...content, bodyType: 'none' }).body).toEqual({
        type: 'none',
        content: '',
      });
    });

    it('copies the rows instead of sharing them', () => {
      const dto = toRequestDto(content);
      expect(dto.params).toEqual(content.params);
      expect(dto.params[0]).not.toBe(content.params[0]);
    });

    it('sends GraphQL query and variables separately', () => {
      expect(
        toRequestDto({
          ...content,
          bodyType: 'graphql',
          body: 'query { me }',
          bodyVariables: '{"id": 1}',
        }).body,
      ).toEqual({ type: 'graphql', content: 'query { me }', variables: '{"id": 1}' });
    });

    it('sends a binary body file path', () => {
      expect(
        toRequestDto({ ...content, bodyType: 'binary', body: './fixtures/data.bin' }).body,
      ).toEqual({ type: 'binary', content: './fixtures/data.bin' });
    });
  });

  describe('form fields', () => {
    const fields = [
      {
        name: 'meta',
        value: '{}',
        kind: 'text' as const,
        enabled: true,
        contentType: 'application/json',
        fileName: '',
        encode: false,
      },
      {
        name: 'file',
        value: 'a.png',
        kind: 'file' as const,
        enabled: false,
        contentType: '',
        fileName: 'b.png',
        encode: false,
      },
    ];

    it('sends the fields of a form body, and only what each field uses', () => {
      const dto = toRequestDto({ ...content, bodyType: 'form', body: 'old', bodyFields: fields });
      expect(dto.body).toEqual({
        type: 'form',
        content: '',
        fields: [
          {
            name: 'meta',
            value: '{}',
            kind: 'text',
            enabled: true,
            contentType: 'application/json',
          },
          { name: 'file', value: 'a.png', kind: 'file', enabled: false, fileName: 'b.png' },
        ],
      });
      expect(fromRequestDto(dto).bodyFields).toEqual(fields);
    });

    it('keeps the text of an older file until the fields are edited', () => {
      const dto = toRequestDto({ ...content, bodyType: 'urlencoded', body: 'a=1' });
      expect(dto.body).toEqual({ type: 'urlencoded', content: 'a=1' });
      expect(fromRequestDto(dto)).toMatchObject({ body: 'a=1', bodyFields: null });
    });

    it('sends the text of other body types even when fields were edited before', () => {
      expect(toRequestDto({ ...content, bodyFields: fields }).body).toEqual({
        type: 'json',
        content: '{"name":"Ada"}',
      });
    });
  });

  describe('fromRequestDto', () => {
    it('round-trips a request', () => {
      const { auth, ...rest } = fromRequestDto(toRequestDto(content));
      const { auth: original, ...expected } = content;
      expect(rest).toEqual(expected);
      expect(auth).toEqual({ ...EMPTY_AUTH, type: 'bearer', token: original.token });
    });

    it('normalizes unknown or missing values', () => {
      const dto = {
        name: 'odd',
        method: 'patch',
        url: '',
        params: [],
        headers: [],
        auth: { type: 'oauth', in: 'cookie' },
        body: { type: 'unknown', content: 'x' },
      } as unknown as RequestDto;
      const result = fromRequestDto(dto);
      expect(result.method).toBe('PATCH');
      expect(result.auth).toEqual({ ...EMPTY_AUTH, type: 'none' });
      expect(result.bodyType).toBe('none');
      expect(result.description).toBe('');
      expect(result.captures).toEqual([]);
      expect(fromRequestDto({ ...dto, method: 'BREW' }).method).toBe('GET');
    });

    it('reads GraphQL query and variables', () => {
      const dto: RequestDto = {
        name: 'GraphQL',
        method: 'POST',
        url: 'https://example.test/graphql',
        params: [],
        headers: [],
        auth: { type: 'none' },
        body: { type: 'graphql', content: 'query { me }', variables: '{"id": 1}' },
      };
      expect(fromRequestDto(dto)).toMatchObject({
        bodyType: 'graphql',
        body: 'query { me }',
        bodyVariables: '{"id": 1}',
      });
    });

    it('reads a binary body file path', () => {
      const dto: RequestDto = {
        name: 'Upload',
        method: 'POST',
        url: 'https://example.test/upload',
        params: [],
        headers: [],
        auth: { type: 'none' },
        body: { type: 'binary', content: './fixtures/data.bin' },
      };
      expect(fromRequestDto(dto)).toMatchObject({
        bodyType: 'binary',
        body: './fixtures/data.bin',
      });
    });
  });

  describe('fromNodeDto', () => {
    it('maps the tree, giving methods only to requests', () => {
      expect(
        fromNodeDto({
          path: 'users',
          name: 'users',
          kind: 'folder',
          method: 'post',
          children: [
            { path: 'users/get.yaml', name: 'Get', kind: 'request', method: 'get' },
            { path: 'users/bad.yaml', name: 'Bad', kind: 'request', error: 'Invalid YAML' },
          ],
        }),
      ).toEqual({
        path: 'users',
        name: 'users',
        kind: 'folder',
        method: undefined,
        error: undefined,
        children: [
          {
            path: 'users/get.yaml',
            name: 'Get',
            kind: 'request',
            method: 'GET',
            error: undefined,
            children: undefined,
          },
          {
            path: 'users/bad.yaml',
            name: 'Bad',
            kind: 'request',
            method: undefined,
            error: 'Invalid YAML',
            children: undefined,
          },
        ],
      });
    });
  });
});
