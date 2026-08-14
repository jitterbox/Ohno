import * as fs from 'node:fs';
import * as path from 'node:path';

export default async function globalTeardown(): Promise<void> {
  const pidFile = path.resolve(__dirname, '../../../.vscode-ext-debug/pid');
  if (!fs.existsSync(pidFile)) return;
  const pid = Number(fs.readFileSync(pidFile, 'utf8'));
  if (pid) {
    try {
      process.kill(pid);
    } catch {
      // already gone
    }
  }
}
