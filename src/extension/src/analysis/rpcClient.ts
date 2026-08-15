import { spawn, type ChildProcess } from 'node:child_process';
import * as fs from 'node:fs';
import * as path from 'node:path';
import {
  createMessageConnection,
  StreamMessageReader,
  StreamMessageWriter,
  type CancellationToken,
  type MessageConnection,
} from 'vscode-jsonrpc/node';
import type { AnalyzeRequest, AnalyzeResponse } from './types';
import { ProtocolMethods } from './types';
import { normalizeAnalyzeResponse } from './normalize';

const INIT_TIMEOUT_MS = 15_000;

export class AnalyzerRpcClient {
  private process: ChildProcess | undefined;
  private connection: MessageConnection | undefined;
  private starting: Promise<MessageConnection> | undefined;

  constructor(
    private readonly serverPath: string,
    private readonly log: (message: string) => void,
  ) {}

  async analyze(
    request: AnalyzeRequest,
    token?: CancellationToken,
  ): Promise<AnalyzeResponse> {
    const connection = await this.ensure();
    const raw = await this.send(
      connection, ProtocolMethods.analyze, request, token);
    return normalizeAnalyzeResponse(raw);
  }

  async analyzeDeep(
    request: AnalyzeRequest,
    token?: CancellationToken,
  ): Promise<AnalyzeResponse> {
    const connection = await this.ensure();
    const raw = await this.send(
      connection, ProtocolMethods.analyzeDeep, request, token);
    return normalizeAnalyzeResponse(raw);
  }

  async setSolution(solutionPath: string): Promise<void> {
    const connection = await this.ensure();
    await connection.sendRequest(ProtocolMethods.setSolutionContext, {
      solutionPath,
    });
  }

  dispose(): void {
    this.announceShutdown();
    this.connection?.dispose();
    this.kill(this.process);
    this.connection = undefined;
    this.process = undefined;
  }

  /**
   * Ask the server to stop, and accept that the message may not land.
   *
   * The pipe is closing and the process is killed on the next line
   * either way, so losing that race is expected rather than
   * exceptional. `sendNotification` returns a promise, so a synchronous
   * catch alone leaves the EPIPE it rejects with unhandled — which
   * surfaces as an unhandled rejection well after dispose returned.
   */
  private announceShutdown(): void {
    try {
      const sent = this.connection?.sendNotification(
        ProtocolMethods.shutdown,
      );
      void Promise.resolve(sent).catch(() => undefined);
    } catch {
      // The connection was already disposed.
    }
  }

  private send(
    connection: MessageConnection,
    method: string,
    params: unknown,
    token?: CancellationToken,
  ): Promise<unknown> {
    return token
      ? connection.sendRequest(method, params, token)
      : connection.sendRequest(method, params);
  }

  private async ensure(): Promise<MessageConnection> {
    if (this.connection) return this.connection;
    if (this.starting) return this.starting;
    this.starting = this.start();
    try {
      this.connection = await this.starting;
      return this.connection;
    } finally {
      this.starting = undefined;
    }
  }

  private start(): Promise<MessageConnection> {
    return new Promise((resolve, reject) => {
      if (!fs.existsSync(this.serverPath)) {
        reject(new Error(`Analyzer server not found: ${this.serverPath}`));
        return;
      }

      let settled = false;
      const child = spawn(this.serverPath, [], {
        stdio: ['pipe', 'pipe', 'pipe'],
        windowsHide: true,
      });
      this.process = child;
      child.stderr?.on('data', (chunk: Buffer) => {
        this.log(chunk.toString());
      });

      const fail = (error: Error): void => {
        if (settled) return;
        settled = true;
        clearTimeout(timer);
        this.kill(child);
        reject(error);
      };

      child.on('error', (error) => fail(error));
      child.on('exit', (code) => {
        this.log(`analyzer exited (${code ?? '?'})`);
        if (this.process === child) {
          this.connection = undefined;
          this.process = undefined;
        }
        fail(new Error(`Analyzer exited (${code ?? '?'})`));
      });

      const connection = createMessageConnection(
        new StreamMessageReader(child.stdout!),
        new StreamMessageWriter(child.stdin!),
      );
      connection.listen();
      const timer = setTimeout(() => {
        fail(new Error('Analyzer initialize timed out'));
      }, INIT_TIMEOUT_MS);
      connection
        .sendRequest(ProtocolMethods.initialize)
        .then(() => {
          if (settled) return;
          settled = true;
          clearTimeout(timer);
          resolve(connection);
        })
        .catch((error: Error) => fail(error));
    });
  }

  private kill(child: ChildProcess | undefined): void {
    if (!child) return;
    try { child.kill(); } catch { /* already gone */ }
    if (this.process === child) this.process = undefined;
  }
}

export function serverExecutableNames(): string[] {
  return process.platform === 'win32'
    ? ['ComplexityAnalyzer.Server.exe', 'ComplexityAnalyzer.Server']
    : ['ComplexityAnalyzer.Server', 'ComplexityAnalyzer.Server.exe'];
}

export function resolveServerPath(
  extensionPath: string,
  override: string,
): string {
  if (override) return override;
  const names = serverExecutableNames();
  const dirs = [
    path.join(extensionPath, 'server'),
    path.join(
      extensionPath,
      '..',
      'analyzer',
      'ComplexityAnalyzer.Server',
      'bin',
      'Debug',
      'net10.0',
    ),
    path.join(
      extensionPath,
      '..',
      'analyzer',
      'ComplexityAnalyzer.Server',
      'bin',
      'Release',
      'net10.0',
    ),
  ];
  for (const dir of dirs) {
    for (const name of names) {
      const candidate = path.join(dir, name);
      if (fs.existsSync(candidate)) return candidate;
    }
  }
  return path.join(dirs[0], names[0]);
}
