import * as vscode from 'vscode';
import {
  BUILTIN_LANGUAGES,
  defaultLanguageEnabled,
} from './analysis/languages';

export type AnalysisTier = 'fast' | 'deep';
export type AnnotationMode = 'inline' | 'codelens' | 'off';

/**
 * How much of the accessor/operator surface gets an inline annotation.
 * They are always analyzed and always in the panel; this only controls
 * the editor decoration, so a class of plain properties does not fill
 * the margin with `O(1)`.
 */
export type AccessorAnnotations = 'nontrivial' | 'always' | 'off';

export interface OhnoConfig {
  enabled: boolean;
  languages: Readonly<Record<string, boolean>>;
  tier: AnalysisTier;
  mode: AnnotationMode;
  showInline: boolean;
  nestingDepth: number;
  showSpace: boolean;
  showConfidence: boolean;
  accessors: AccessorAnnotations;
  debounceMs: number;
  maxFileSizeKb: number;
  analyzerPath: string;
  logLevel: string;
}

export function readConfig(): OhnoConfig {
  const c = vscode.workspace.getConfiguration('ohno');
  return {
    enabled: c.get('enabled', true),
    languages: readLanguageFlags(c),
    tier: c.get('analysis.tier', 'fast'),
    mode: c.get('annotations.mode', 'inline'),
    showInline: c.get('annotations.showInline', true),
    nestingDepth: c.get('annotations.nestingDepth', 2),
    showSpace: c.get('annotations.showSpace', true),
    showConfidence: c.get('annotations.showConfidence', true),
    accessors: c.get('annotations.accessors', 'nontrivial'),
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
  if (!BUILTIN_LANGUAGES.some((item) => item.id === languageId)) {
    return false;
  }
  if (Object.hasOwn(config.languages, languageId)) {
    return config.languages[languageId];
  }
  return defaultLanguageEnabled(languageId);
}

function readLanguageFlags(
  c: vscode.WorkspaceConfiguration,
): Record<string, boolean> {
  const flags: Record<string, boolean> = {};
  for (const language of BUILTIN_LANGUAGES) {
    flags[language.id] = languageFlag(c, language.id);
  }
  return flags;
}

function languageFlag(
  c: vscode.WorkspaceConfiguration,
  id: string,
): boolean {
  const current = c.get<boolean | undefined>(`languages.${id}`);
  if (typeof current === 'boolean') return current;
  const legacy = c.get<boolean | undefined>(`languages.${id}.enabled`);
  if (typeof legacy === 'boolean') return legacy;
  return defaultLanguageEnabled(id);
}
