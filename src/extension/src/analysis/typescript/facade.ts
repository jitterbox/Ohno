import { Worker } from 'node:worker_threads';
import * as path from 'node:path';
import type * as vscode from 'vscode';
import type {
  AnalyzeDocumentRequest,
  IComplexityAnalyzer,
} from '../analyzer';
import type { AnalyzeResponse } from '../types';
import {
  dispatch,
  type PingResult,
  type WorkerRequest,
  type WorkerResponse,
} from './messages';

export const TS_LANGUAGE_IDS = [
  'typescript',
  'javascript',
  'typescriptreact',
  'javascriptreact',
] as const;

/**
 * Host-side facade. Analysis runs on a worker thread so typecheck
 * stays off the UI thread. The worker is started on first analyze
 * and is not respawned after dispose.
 */
export class TypeScriptAnalyzer implements IComplexityAnalyzer {
  readonly languageIds = TS_LANGUAGE_IDS;
  readonly supportsDeepAnalysis = true;

  private worker: Worker | undefined;
  private starting: Promise<Worker> | undefined;
  private disposed = false;
  private nextId = 1;
  private readonly pending = new Map<
    number,
    {
      resolve: (value: WorkerResponse) => void;
      reject: (error: Error) => void;
    }
  >();

  constructor(private readonly workerFile?: string) {}

  async ping(): Promise<PingResult> {
    const response = await this.post({ method: 'ping' });
    return (response.result as PingResult) ?? { ok: true };
  }

  async analyze(
    request: AnalyzeDocumentRequest,
    token: vscode.CancellationToken,
  ): Promise<AnalyzeResponse> {
    if (token.isCancellationRequested) {
      return Promise.reject(new Error('cancelled'));
    }
    if (this.disposed) {
      return failedAnalyze(request, 'TypeScript analyzer disposed');
    }
    try {
      const response = await this.post({
        method: 'analyze',
        params: request,
      });
      if (response.error) {
        return failedAnalyze(request, response.error);
      }
      return (response.result as AnalyzeResponse)
        ?? failedAnalyze(request, 'empty worker result');
    } catch (error) {
      return failedAnalyze(request, String(error));
    }
  }

  dispose(): void {
    this.disposed = true;
    for (const wait of this.pending.values()) {
      wait.reject(new Error('TypeScript analyzer disposed'));
    }
    this.pending.clear();
    void this.worker?.terminate();
    this.worker = undefined;
    this.starting = undefined;
  }

  private async post(
    body: Omit<WorkerRequest, 'id'>,
  ): Promise<WorkerResponse> {
    const worker = await this.ensure();
    const id = this.nextId++;
    const request: WorkerRequest = { id, ...body };
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      worker.postMessage(request);
    });
  }

  private async ensure(): Promise<Worker> {
    if (this.disposed) {
      throw new Error('TypeScript analyzer disposed');
    }
    if (this.worker) return this.worker;
    if (this.starting) return this.starting;
    this.starting = this.start();
    try {
      this.worker = await this.starting;
      return this.worker;
    } finally {
      this.starting = undefined;
    }
  }

  private start(): Promise<Worker> {
    const file = this.workerFile ?? defaultWorkerFile();
    const worker = new Worker(file);
    worker.on('message', (message: WorkerResponse) => {
      const wait = this.pending.get(message.id);
      if (!wait) return;
      this.pending.delete(message.id);
      wait.resolve(message);
    });
    worker.on('error', (error) => {
      this.failAll(error);
    });
    worker.on('exit', (code) => {
      if (this.disposed) return;
      this.failAll(new Error(`TS worker exited (${code})`));
      this.worker = undefined;
    });
    return Promise.resolve(worker);
  }

  private failAll(error: Error): void {
    for (const wait of this.pending.values()) wait.reject(error);
    this.pending.clear();
  }
}

export function pingInline(): PingResult {
  return dispatch({ id: 0, method: 'ping' }).result as PingResult;
}

function defaultWorkerFile(): string {
  return path.join(__dirname, 'ohno-ts-worker.js');
}

function failedAnalyze(
  request: AnalyzeDocumentRequest,
  message: string,
): AnalyzeResponse {
  return {
    uri: request.uri,
    version: request.version,
    functions: [],
    warnings: [{ message }],
  };
}
