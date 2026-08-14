import * as fs from 'node:fs';
import * as path from 'node:path';
import { spawn } from 'node:child_process';
import { downloadAndUnzipVSCode } from '@vscode/test-electron';

const PORT = 9223;
const USER_DATA = path.resolve(__dirname, '../../../.vscode-ext-debug');

export default async function globalSetup(): Promise<void> {
  const executable = await downloadAndUnzipVSCode('stable');
  const extensionPath = path.resolve(__dirname, '../..');
  const workspace = path.resolve(__dirname, '../../../../test/fixtures');
  fs.mkdirSync(USER_DATA, { recursive: true });

  const child = spawn(executable, [
    `--extensionDevelopmentPath=${extensionPath}`,
    `--remote-debugging-port=${PORT}`,
    `--user-data-dir=${USER_DATA}`,
    '--disable-workspace-trust',
    '--disable-extensions',
    workspace,
  ], {
    env: { ...process.env, OHNO_TEST: '1' },
    stdio: 'ignore',
    detached: true,
  });
  child.unref();
  fs.writeFileSync(
    path.join(USER_DATA, 'pid'),
    String(child.pid ?? ''),
  );
  await waitForPort(PORT, 30_000);
}

async function waitForPort(port: number, timeoutMs: number): Promise<void> {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    try {
      const res = await fetch(`http://127.0.0.1:${port}/json/version`);
      if (res.ok) return;
    } catch {
      // not up yet
    }
    await new Promise((r) => setTimeout(r, 500));
  }
  throw new Error(`VS Code CDP port ${port} did not open`);
}
