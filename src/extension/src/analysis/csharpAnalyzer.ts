import type * as vscode from 'vscode';
import type { IComplexityAnalyzer, AnalyzeDocumentRequest } from './analyzer';
import { AnalyzerRpcClient } from './rpcClient';
import type { AnalyzeResponse } from './types';

export class CSharpAnalyzer implements IComplexityAnalyzer {
  readonly languageIds = ['csharp'] as const;
  readonly supportsDeepAnalysis = true;

  constructor(private readonly client: AnalyzerRpcClient) {}

  analyze(
    request: AnalyzeDocumentRequest,
    token: vscode.CancellationToken,
  ): Promise<AnalyzeResponse> {
    if (token.isCancellationRequested) {
      return Promise.reject(new Error('cancelled'));
    }
    const payload = {
      uri: request.uri,
      text: request.text,
      version: request.version,
      tier: request.tier,
    };
    return request.tier === 'deep'
      ? this.client.analyzeDeep(payload, token)
      : this.client.analyze(payload, token);
  }

  dispose(): void {
    this.client.dispose();
  }
}
