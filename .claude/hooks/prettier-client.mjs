// PostToolUse hook: formats a file under src/Dynpost.Client with the client's own Prettier
// after Claude edits or writes it, so `npm run format:check` (enforced in CI) stays green.
// Written in Node rather than shell so it runs the same on Windows, macOS and Linux.
// It never blocks: any problem (no node_modules yet, unsupported file) is silently skipped.
import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';

const EXTENSIONS = new Set(['.ts', '.html', '.scss', '.css', '.json', '.mjs', '.js', '.md']);

try {
  const input = JSON.parse(readFileSync(0, 'utf8'));
  const file = input?.tool_input?.file_path;
  const projectDir = process.env.CLAUDE_PROJECT_DIR || input?.cwd;
  if (!file || !projectDir) process.exit(0);

  const clientDir = path.resolve(projectDir, 'src', 'Dynpost.Client');
  const target = path.resolve(file);
  const relative = path.relative(clientDir, target);
  const insideClient = relative && !relative.startsWith('..') && !path.isAbsolute(relative);
  if (!insideClient || relative.split(path.sep).includes('node_modules')) process.exit(0);
  if (!EXTENSIONS.has(path.extname(target).toLowerCase()) || !existsSync(target)) process.exit(0);

  const prettier = path.join(clientDir, 'node_modules', 'prettier', 'bin', 'prettier.cjs');
  if (!existsSync(prettier)) process.exit(0); // run `npm ci` in src/Dynpost.Client first

  spawnSync(process.execPath, [prettier, '--write', '--ignore-unknown', '--log-level', 'warn', target], {
    cwd: clientDir,
    stdio: ['ignore', 'ignore', 'inherit'],
    timeout: 20000,
  });
} catch {
  // Never fail the edit because of formatting.
}
process.exit(0);
