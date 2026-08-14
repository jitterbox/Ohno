import { describe, expect, it } from 'vitest';
import { buildMarkdown } from '../../src/ui/hoverProvider';
import { Uri } from './__mocks__/vscode';
import type { FunctionComplexity } from '../../src/analysis/types';

const fn: FunctionComplexity = {
  id: 'Walk',
  name: 'Walk',
  kind: 'method',
  range: { startLine: 0, startCharacter: 0, endLine: 5, endCharacter: 1 },
  signatureRange: {
    startLine: 0, startCharacter: 0, endLine: 0, endCharacter: 10,
  },
  time: 'O(n C(Process))',
  space: 'O(1)',
  confidence: 'low',
  dimensions: [{ variable: 'n', meaning: 'items.Length' }],
  evidence: {
    kind: 'loop',
    label: 'foreach',
    cost: 'n C(Process)',
    children: [{
      kind: 'call', label: 'Process', cost: 'C(Process)', children: [],
    }],
  },
  warnings: [{ message: 'unresolved call Process' }],
  boundingSuggestions: [{
    description: 'Bound the heap',
    condition: 'dequeue when Count > k',
    resultingTime: 'O(n log k)',
    resultingSpace: 'O(k)',
  }],
  tier: 'fast',
};

describe('buildMarkdown', () => {
  it('includes derivation, warnings, and command links', () => {
    const md = buildMarkdown(fn, Uri.parse('file:///a.cs') as never);
    expect(md.isTrusted).toBe(true);
    expect(md.value).toContain('O(n C(Process))');
    expect(md.value).toContain('items.Length');
    expect(md.value).toContain('unresolved call Process');
    expect(md.value).toContain('dequeue when Count > k');
    expect(md.value).toContain('command:ohno.runDeepAnalysis');
    expect(md.value).toContain('command:ohno.showDerivation');
  });
});
