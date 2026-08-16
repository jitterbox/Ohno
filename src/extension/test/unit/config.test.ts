import { describe, expect, it, vi } from 'vitest';
import * as vscode from 'vscode';
import { languageEnabled, readConfig, type OhnoConfig } from '../../src/config';
import {
  DEFAULT_LANGUAGE_ID,
  defaultLanguageEnabled,
  documentSelectors,
} from '../../src/analysis/languages';

const config = (languages: Record<string, boolean>): OhnoConfig => ({
  enabled: true,
  languages,
  tier: 'fast',
  mode: 'inline',
  nestingDepth: 2,
  showSpace: true,
  showConfidence: true,
  accessors: 'nontrivial',
  debounceMs: 250,
  maxFileSizeKb: 500,
  analyzerPath: '',
  logLevel: 'warn',
});

describe('readConfig', () => {
  it('defaults annotation mode to inline', () => {
    expect(readConfig().mode).toBe('inline');
  });

  it('treats leftover showInline false as mode off', () => {
    vi.spyOn(vscode.workspace, 'getConfiguration').mockReturnValue({
      get: (key: string, fallback: unknown) =>
        key === 'annotations.showInline' ? false : fallback,
      update: async () => undefined,
    } as vscode.WorkspaceConfiguration);
    expect(readConfig().mode).toBe('off');
    vi.restoreAllMocks();
  });

  it('does not override an explicit codelens mode', () => {
    vi.spyOn(vscode.workspace, 'getConfiguration').mockReturnValue({
      get: (key: string, fallback: unknown) => {
        if (key === 'annotations.mode') return 'codelens';
        if (key === 'annotations.showInline') return false;
        return fallback;
      },
      update: async () => undefined,
    } as vscode.WorkspaceConfiguration);
    expect(readConfig().mode).toBe('codelens');
    vi.restoreAllMocks();
  });
});

describe('built-in languages', () => {
  it('defaults to C# and lists every built-in selector', () => {
    expect(DEFAULT_LANGUAGE_ID).toBe('csharp');
    expect(defaultLanguageEnabled('csharp')).toBe(true);
    expect(defaultLanguageEnabled('typescript')).toBe(false);
    expect(documentSelectors()).toEqual([
      { language: 'csharp' },
      { language: 'typescript' },
      { language: 'javascript' },
      { language: 'typescriptreact' },
      { language: 'javascriptreact' },
    ]);
  });
});

describe('languageEnabled', () => {
  it('honors per-language toggles', () => {
    const flags = config({ csharp: true });
    expect(languageEnabled('csharp', flags)).toBe(true);
    expect(languageEnabled('typescript', flags)).toBe(false);
  });

  it('disables every language when Ohno is off', () => {
    const flags = { ...config({ csharp: true }), enabled: false };
    expect(languageEnabled('csharp', flags)).toBe(false);
  });

  it('leaves unknown languages off until an analyzer is registered', () => {
    expect(languageEnabled('python', config({ csharp: true }))).toBe(false);
  });

  it('honors an explicit TypeScript opt-in', () => {
    const flags = config({ csharp: true, typescript: true });
    expect(languageEnabled('typescript', flags)).toBe(true);
  });

  it('keeps TypeScript off unless the user opts in', () => {
    const flags = config({ csharp: true });
    expect(languageEnabled('typescript', flags)).toBe(false);
    expect(languageEnabled('javascript', flags)).toBe(false);
  });
});
