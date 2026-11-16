import {
  VariableLookup,
  describeVariable,
  splitVariables,
  variableClass,
  withFolderVariables,
} from './variables';

describe('variables', () => {
  const lookup: VariableLookup = new Map([
    ['host', { value: 'example.com', secret: false }],
    ['token', { value: 'abc', secret: true }],
  ]);

  describe('splitVariables', () => {
    it('splits plain text and references, allowing spaces inside the braces', () => {
      expect(splitVariables('https://{{host}}/users/{{ id }}')).toEqual([
        { text: 'https://' },
        { text: '{{host}}', variable: 'host' },
        { text: '/users/' },
        { text: '{{ id }}', variable: 'id' },
      ]);
    });

    it('returns a single plain segment without references', () => {
      expect(splitVariables('plain')).toEqual([{ text: 'plain' }]);
      expect(splitVariables('')).toEqual([]);
    });

    it('ignores incomplete or nested braces', () => {
      expect(splitVariables('{{a b}} {{}}')).toEqual([{ text: '{{a b}} {{}}' }]);
    });
  });

  describe('variableClass', () => {
    it('highlights every reference when the environment is unknown', () => {
      expect(variableClass('missing', null)).toBe('variable-highlight');
    });

    it('marks references the environment does not define', () => {
      expect(variableClass('host', lookup)).toBe('variable-highlight');
      expect(variableClass('missing', lookup)).toBe('variable-undefined');
    });
  });

  describe('describeVariable', () => {
    it('describes values, secrets and undefined references', () => {
      expect(describeVariable('host', null)).toBe('Variable host');
      expect(describeVariable('host', lookup)).toBe('host = example.com');
      expect(describeVariable('token', lookup)).toBe('token: secret value');
      const captured = new Map([['jwt', { value: 'x', secret: false, captured: true }]]);
      expect(describeVariable('jwt', captured)).toBe('jwt = x (captured)');
      expect(describeVariable('missing', lookup)).toBe(
        'missing is not defined in the selected environment or the globals',
      );
      const folder = new Map([['page', { value: '2', secret: false, folder: 'requests/users' }]]);
      expect(describeVariable('page', folder)).toBe('page = 2 (set by the folder requests/users)');
    });
  });

  describe('withFolderVariables', () => {
    it('adds the folder variables the environment does not define', () => {
      const combined = withFolderVariables(lookup, [
        { name: 'host', value: 'folder.test', folder: 'requests' },
        { name: 'page', value: '{{size}}', folder: 'requests/users' },
      ]);
      expect(combined.get('host')).toEqual({ value: 'example.com', secret: false });
      expect(combined.get('page')).toEqual({
        value: '{{size}}',
        secret: false,
        folder: 'requests/users',
      });
      expect(combined.get('token')?.secret).toBe(true);
    });

    it('returns the environment itself when no folder variable is added', () => {
      expect(withFolderVariables(lookup, [])).toBe(lookup);
    });
  });

  describe('built-in variables', () => {
    it('splits references with arguments only for built-in variables', () => {
      expect(splitVariables('{{$randomInt 1 6}}')).toEqual([
        { text: '{{$randomInt 1 6}}', variable: '$randomInt 1 6' },
      ]);
    });

    it('highlights supported built-in variables apart from environment variables', () => {
      expect(variableClass('$uuid', lookup)).toBe('variable-builtin');
      expect(variableClass('$randomInt 1 6', null)).toBe('variable-builtin');
      expect(variableClass('$nope', lookup)).toBe('variable-undefined');
      expect(variableClass('$randomInt 6 1', lookup)).toBe('variable-undefined');
    });

    it('describes what a built-in variable holds', () => {
      expect(describeVariable('$uuid', lookup)).toContain('built-in');
      expect(describeVariable('$randomInt 1 6', lookup)).toContain('from 1 to 6');
      expect(describeVariable('$nope', lookup)).toContain('not a built-in variable');
    });
  });

  describe('workspace globals', () => {
    const withGlobals: VariableLookup = new Map([
      ['host', { value: 'global.test', secret: false, global: true }],
      ['env', { value: 'dev', secret: false }],
    ]);

    it('lets folder variables win over globals but not over the environment', () => {
      const combined = withFolderVariables(withGlobals, [
        { name: 'host', value: 'folder.test', folder: 'requests' },
        { name: 'env', value: 'folder', folder: 'requests' },
      ]);
      expect(combined.get('host')?.value).toBe('folder.test');
      expect(combined.get('env')?.value).toBe('dev');
    });

    it('says a value comes from the globals', () => {
      expect(describeVariable('host', withGlobals)).toBe('host = global.test (workspace global)');
    });
  });
});
