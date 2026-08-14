import type * as vscode from 'vscode';
import type {
  AnalysisTier,
  AnalyzeResponse,
} from './types';

export interface AnalyzeDocumentRequest {
  uri: string;
  text: string;
  version: number;
  tier: AnalysisTier;
}

export interface IComplexityAnalyzer {
  readonly languageIds: readonly string[];
  readonly supportsDeepAnalysis: boolean;
  analyze(
    request: AnalyzeDocumentRequest,
    token: vscode.CancellationToken,
  ): Promise<AnalyzeResponse>;
  dispose?(): void;
}
