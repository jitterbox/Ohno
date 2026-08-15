import { describe, expect, it } from 'vitest';
import {
  annotationAfter,
  confidenceBackground,
  headlineRender,
  headlineText,
} from '../../src/ui/decorationFactory';
import { toCssInjection } from '../../src/ui/cssInjection';
import { ThemeColor } from 'vscode';
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
  explanation: 'Linearithmic time',
  patterns: [],
  confidenceReasons: [],
  approaches: [],
  selectionHint: '',
  tier: 'fast',
};

const config: OhnoConfig = {
  enabled: true,
  languages: { csharp: true },
  tier: 'fast',
  mode: 'inline',
  showInline: true,
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

describe('annotationAfter', () => {
  it('shades from one character before the text', () => {
    const after = annotationAfter(
      'O(n) · O(1)',
      'ohno.confidenceHigh',
      'ohno.inlineBackgroundHigh',
    );
    expect(after.contentText).toBe('O(n) · O(1)');
    expect(after.color).toEqual(new ThemeColor('ohno.confidenceHigh'));
    expect(after.backgroundColor).toEqual(
      new ThemeColor('ohno.inlineBackgroundHigh'),
    );
    expect(String(after.textDecoration)).toContain('padding-left:1ch');
  });
});

describe('headlineRender', () => {
  it('uses the confidence wash', () => {
    const after = headlineRender(fn, config);
    expect(after.backgroundColor).toEqual(
      new ThemeColor(confidenceBackground('high')),
    );
  });
});
