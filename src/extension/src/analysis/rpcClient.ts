import { spawn, type ChildProcess } from 'node:child_process';
import * as fs from 'node:fs';
import * as path from 'node:path';
import {
  createMessageConnection,
  StreamMessageReader,
  StreamMessageWriter,
  type MessageConnection,
} from 'vscode-jsonrpc/node';
import type { AnalyzeRequest, AnalyzeResponse } from './types';
import { ProtocolMethods } from './types';
import { normalizeAnalyzeResponse } from './normalize';

export class AnalyzerRpcClient {
  private process: ChildProcess | undefined;
  private connection: MessageConnection | undefined;
  private starting: Promise<MessageConnection> | undefined;

  constructor(
    private readonly serverPath: string,
    private readonly log: (message: string) => void,
  ) {}

  async analyze(request: AnalyzeRequest): Promise<AnalyzeResponse> {
    const connection = await this.ensure();
    const raw = await connection.sendRequest(
      ProtocolMethods.analyze,
      request,
    );
    return normalizeAnalyzeResponse(raw);
  }

  async analyzeDeep(request: AnalyzeRequest): Promise<AnalyzeResponse> {
    const connection = await this.ensure();
    const raw = await connection.sendRequest(
      ProtocolMethods.analyzeDeep,
      request,
    );
    return normalizeAnalyzeResponse(raw);
  }

  async setSolution(solutionPath: string): Promise<void> {
    const connection = await this.ensure();
    await connection.sendRequest(ProtocolMethods.setSolutionContext, {
      solutionPath,
    });
  }

  dispose(): void {
    try {
      this.connection?.sendNotification(ProtocolMethods.shutdown);
    } catch {
      // Process may already be gone.
    }
    this.connection?.dispose();
    this.process?.kill();
    this.connection = undefined;
    this.process = undefined;
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

      const child = spawn(this.serverPath, [], {
        stdio: ['pipe', 'pipe', 'pipe'],
      });
      this.process = child;
      child.stderr?.on('data', (chunk: Buffer) => {
        this.log(chunk.toString());
      });
      child.on('error', reject);
      child.on('exit', (code) => {
        this.log(`analyzer exited (${code ?? '?'})`);
        this.connection = undefined;
        this.process = undefined;
      });

      const connection = createMessageConnection(
        new StreamMessageReader(child.stdout!),
        new StreamMessageWriter(child.stdin!),
      );
      connection.listen();
      connection
        .sendRequest(ProtocolMethods.initialize)
        .then(() => resolve(connection))
        .catch(reject);
    });
  }
}

export function resolveServerPath(
  extensionPath: string,
  override: string,
): string {
  if (override) return override;
  const names = process.platform === 'win32'
    ? ['ComplexityAnalyzer.Server.exe']
    : ['ComplexityAnalyzer.Server'];
  const candidates = [
    path.join(extensionPath, 'server', names[0]),
    path.join(
      extensionPath,
      '..',
      'analyzer',
      'ComplexityAnalyzer.Server',
      'bin',
      'Debug',
      'net8.0',
      names[0],
    ),
  ];
  return candidates.find((c) => fs.existsSync(c)) ?? candidates[0];
}
