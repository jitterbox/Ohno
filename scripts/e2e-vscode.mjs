#!/usr/bin/env node
/**
 * Launches a disposable VS Code with the Ohno extension, opens samples/TopK.cs,
 * and waits for OHNO_TEST_OUTPUT written by the extension.
 */
import { spawn } from 'node:child_process';
import * as fs from 'node:fs';
import * as path from 'node:path';

const root = path.resolve(import.meta.dirname, '..');
const output = path.join(root, '.ohno-last-result.json');
const userData = path.join(root, '.vscode-ext-debug');
const sample = path.join(root, 'samples', 'TopK.cs');
const extensionPath = path.join(root, 'src', 'extension');

fs.rmSync(output, { force: true });
fs.mkdirSync(userData, { recursive: true });

const executable = process.env.VSCODE_BIN ?? 'code';
const useShell = process.platform === 'win32'
  && !path.isAbsolute(executable);
const child = spawn(executable, [
  `--extensionDevelopmentPath=${extensionPath}`,
  `--user-data-dir=${userData}`,
  '--disable-workspace-trust',
  '--disable-extensions',
  '--new-window',
  sample,
], {
  env: {
    ...process.env,
    OHNO_TEST: '1',
    OHNO_TEST_OUTPUT: output,
  },
  stdio: 'ignore',
  shell: useShell,
  windowsHide: true,
});

const deadline = Date.now() + 60_000;
let passed = false;
try {
  while (Date.now() < deadline) {
    if (fs.existsSync(output)) {
      const raw = fs.readFileSync(output, 'utf8');
      if (raw.trim().length > 2) {
        const result = JSON.parse(raw);
        const names = (result.functions ?? []).map((fn) => `${fn.name}:${fn.time}`);
        console.log('OHNO_TEST_OUTPUT', names.join(', ') || '(no functions)');
        if (!result.functions?.some((fn) => fn.name === 'TopK' && fn.time.includes('n log k'))) {
          throw new Error(`Unexpected analysis: ${JSON.stringify(result.functions, null, 2)}`);
        }
        console.log('VS Code E2E passed');
        passed = true;
        break;
      }
    }
    await new Promise((r) => setTimeout(r, 500));
  }
  if (!passed) {
    throw new Error('Timed out waiting for OHNO_TEST_OUTPUT from VS Code');
  }
} finally {
  stopProcess(child.pid);
}

function stopProcess(pid) {
  if (!pid) return;
  if (process.platform === 'win32') {
    spawn('taskkill', ['/pid', String(pid), '/T', '/F'], {
      stdio: 'ignore',
      windowsHide: true,
    });
    return;
  }
  try { process.kill(pid, 'SIGTERM'); } catch { /* already gone */ }
}
