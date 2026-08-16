import { execSync } from 'node:child_process';
import { dirname, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

await import(pathToFileURL(resolve(
  dirname(fileURLToPath(import.meta.url)),
  'sync-extension-docs.mjs',
)).href);

const ext = resolve(
  dirname(fileURLToPath(import.meta.url)),
  '../src/extension',
);
const listed = execSync('npx --yes @vscode/vsce ls', {
  cwd: ext,
  encoding: 'utf8',
});
const need = [
  'dist/extension.js',
  'dist/ohno-ts-worker.js',
  'node_modules/typescript/lib/typescript.js',
  'node_modules/typescript/lib/lib.esnext.d.ts',
  'CHANGELOG.md',
];
const missing = need.filter((file) => !listed.includes(file));
if (missing.length > 0) {
  console.error('VSIX is missing:\n' + missing.join('\n'));
  process.exit(1);
}
console.log('VSIX contents include the TS worker and lib.');
