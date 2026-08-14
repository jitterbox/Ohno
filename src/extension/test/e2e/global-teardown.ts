import { spawn } from 'node:child_process';
import * as fs from 'node:fs';
import * as path from 'node:path';

export default async function globalTeardown(): Promise<void> {
  const pidFile = path.resolve(__dirname, '../../../.vscode-ext-debug/pid');
  if (!fs.existsSync(pidFile)) return;
  const pid = Number(fs.readFileSync(pidFile, 'utf8'));
  if (!pid) return;
  stopProcess(pid);
}

function stopProcess(pid: number): void {
  if (process.platform === 'win32') {
    spawn('taskkill', ['/pid', String(pid), '/T', '/F'], {
      stdio: 'ignore',
      windowsHide: true,
    });
    return;
  }
  try { process.kill(pid, 'SIGTERM'); } catch { /* already gone */ }
}
