import { copyFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
copyFileSync(
  resolve(root, 'CHANGELOG.md'),
  resolve(root, 'src/extension/CHANGELOG.md'),
);
