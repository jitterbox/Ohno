import { describe, expect, it } from 'vitest';
import { headlineText } from '../../src/ui/decorationFactory';
import { toCssInjection } from '../../src/ui/cssInjection';
import type { FunctionComplexity } from '../../src/analysis/types';
import type { OhnoConfig } from '../../src/config';

const fn: FunctionComplexity = {
  id: 'TopK:1',
  name: 'TopK',
  kind: 'method',
  range: { startLine: 0, startCharacter: 0, endLine: 10, endCharacter: 1 },
  signatureRange: {
    startLine: 0, startCharacter: 0, endLine: 0, endCharacter: 20,
  },
  time: 'O(n log k)',
  space: 'O(k)',
  confidence: 'high',
  dimensions: [{ variable: 'n', meaning: 'values.Length' }],
  evidence: { kind: 'loop', label: 'foreach', cost: 'n log k', children: [] },
  warnings: [],
  boundingSuggestions: [],
  tier: 'fast',
};

const config: OhnoConfig = {
  enabled: true,
  csharpEnabled: true,
  typescriptEnabled: true,
  tier: 'fast',
  mode: 'inline',
  nestingDepth: 2,
  showSpace: true,
  showConfidence: true,
  debounceMs: 250,
  maxFileSizeKb: 500,
  analyzerPath: '',
  logLevel: 'warn',
};

describe('headlineText', () => {
  it('includes time, space, and confidence', () => {
    expect(headlineText(fn, config)).toContain('O(n log k)');
    expect(headlineText(fn, config)).toContain('O(k)');
    expect(headlineText(fn, config)).toContain('high');
  });

  it('hides space when configured', () => {
    const text = headlineText(fn, { ...config, showSpace: false });
    expect(text).not.toContain('O(k)');
  });
});

describe('toCssInjection', () => {
  it('emits text-decoration first', () => {
    const css = toCssInjection({
      'white-space': 'pre',
      'font-variant-numeric': 'tabular-nums',
    });
    expect(css.startsWith('text-decoration:none;')).toBe(true);
    expect(css).toContain('white-space:pre');
  });
});
