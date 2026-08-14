import * as vscode from 'vscode';

export type AnalysisTier = 'fast' | 'deep';
export type AnnotationMode = 'inline' | 'codelens' | 'off';

export interface OhnoConfig {
  enabled: boolean;
  csharpEnabled: boolean;
  typescriptEnabled: boolean;
  tier: AnalysisTier;
  mode: AnnotationMode;
  nestingDepth: number;
  showSpace: boolean;
  showConfidence: boolean;
  debounceMs: number;
  maxFileSizeKb: number;
  analyzerPath: string;
  logLevel: string;
}

export function readConfig(): OhnoConfig {
  const c = vscode.workspace.getConfiguration('ohno');
  return {
    enabled: c.get('enabled', true),
    csharpEnabled: c.get('languages.csharp.enabled', true),
    typescriptEnabled: c.get('languages.typescript.enabled', true),
    tier: c.get('analysis.tier', 'fast'),
    mode: c.get('annotations.mode', 'inline'),
    nestingDepth: c.get('annotations.nestingDepth', 2),
    showSpace: c.get('annotations.showSpace', true),
    showConfidence: c.get('annotations.showConfidence', true),
    debounceMs: c.get('performance.debounceMs', 250),
    maxFileSizeKb: c.get('performance.maxFileSizeKb', 500),
    analyzerPath: c.get('csharp.analyzerPath', ''),
    logLevel: c.get('server.logLevel', 'warn'),
  };
}

export function languageEnabled(
  languageId: string,
  config: OhnoConfig,
): boolean {
  if (!config.enabled) return false;
  if (languageId === 'csharp') return config.csharpEnabled;
  if (languageId === 'typescript') return config.typescriptEnabled;
  return false;
}
