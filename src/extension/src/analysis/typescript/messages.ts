import type { AnalyzeDocumentRequest } from '../analyzer';
import type { AnalyzeResponse } from '../types';
import { analyzeDocument } from './analyze';

export type WorkerMethod = 'ping' | 'analyze';

export interface WorkerRequest {
  id: number;
  method: WorkerMethod;
  params?: AnalyzeDocumentRequest;
}

export interface WorkerResponse {
  id: number;
  result?: PingResult | AnalyzeResponse;
  error?: string;
}

export interface PingResult {
  ok: true;
}

export function dispatch(request: WorkerRequest): WorkerResponse {
  if (request.method === 'ping') {
    return { id: request.id, result: { ok: true } };
  }
  if (request.method === 'analyze' && request.params) {
    try {
      return {
        id: request.id,
        result: analyzeDocument(request.params),
      };
    } catch (error) {
      return { id: request.id, error: String(error) };
    }
  }
  return {
    id: request.id,
    error: `unknown method ${request.method}`,
  };
}
