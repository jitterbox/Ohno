import { describe, expect, it } from 'vitest';
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
  showInline: true,
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
  it('enables inline annotations by default', () => {
    expect(readConfig().showInline).toBe(true);
  });
});

describe('built-in languages', () => {
  it('defaults to C# and lists every built-in selector', () => {
    expect(DEFAULT_LANGUAGE_ID).toBe('csharp');
    expect(defaultLanguageEnabled('csharp')).toBe(true);
    expect(defaultLanguageEnabled('typescript')).toBe(false);
    expect(documentSelectors()).toEqual([
      { language: 'csharp' },
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

  it('ignores leftover TypeScript flags', () => {
    const flags = config({ csharp: true, typescript: true });
    expect(languageEnabled('typescript', flags)).toBe(false);
  });
});
