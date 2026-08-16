import type { AnalyzeDocumentRequest } from '../analyzer';
import type { AnalyzeResponse } from '../types';
import { analyzeDocument } from './analyze';

export type WorkerMethod = 'ping' | 'analyze' | 'cancel';

export interface WorkerRequest {
  id: number;
  method: WorkerMethod;
  params?: AnalyzeDocumentRequest;
  cancelId?: number;
}

export interface WorkerResponse {
  id: number;
  result?: PingResult | AnalyzeResponse;
  error?: string;
}

export interface PingResult {
  ok: true;
}

let abortFlag: Int32Array | undefined;

export function bindAbort(buffer: SharedArrayBuffer): void {
  abortFlag = new Int32Array(buffer);
}

export function requestAbort(id: number): void {
  if (abortFlag) Atomics.store(abortFlag, 0, id);
}

export function isAborted(id: number): boolean {
  return !!abortFlag && Atomics.load(abortFlag, 0) === id;
}

const latest = new Map<string, number>();

export function dispatch(request: WorkerRequest): WorkerResponse {
  if (request.method === 'ping') {
    return { id: request.id, result: { ok: true } };
  }
  if (request.method === 'cancel' && request.cancelId !== undefined) {
    requestAbort(request.cancelId);
    return { id: request.id, result: { ok: true } };
  }
  if (request.method === 'analyze' && request.params) {
    return runAnalyze(request, request.params);
  }
  return {
    id: request.id,
    error: `unknown method ${request.method}`,
  };
}

function runAnalyze(
  request: WorkerRequest,
  params: AnalyzeDocumentRequest,
): WorkerResponse {
  if (isAborted(request.id) || isStale(params)) {
    return { id: request.id, error: 'cancelled' };
  }
  latest.set(params.uri, params.version);
  try {
    return {
      id: request.id,
      result: analyzeDocument(params, () => isAborted(request.id)),
    };
  } catch (error) {
    const message = error instanceof Error
      ? error.message
      : String(error);
    return { id: request.id, error: message };
  }
}

function isStale(params: AnalyzeDocumentRequest): boolean {
  const seen = latest.get(params.uri);
  return seen !== undefined && params.version < seen;
}
