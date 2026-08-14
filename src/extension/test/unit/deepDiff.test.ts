import { describe, expect, it } from 'vitest';
import type { FunctionComplexity } from '../../src/analysis/types';
import { diffFunction, diffResponses } from '../../src/ui/deepDiff';

const base: FunctionComplexity = {
  id: 'TopK',
  name: 'TopK',
  kind: 'method',
  range: {
    startLine: 0, startCharacter: 0, endLine: 10, endCharacter: 1,
  },
  signatureRange: {
    startLine: 0, startCharacter: 0, endLine: 0, endCharacter: 10,
  },
  time: 'O(n log k)',
  space: 'O(k)',
  confidence: 'high',
  dimensions: [],
  evidence: {
    kind: 'loop',
    label: 'foreach (n)',
    cost: 'n log k',
    children: [{
      kind: 'call', label: 'Enqueue', cost: 'log k', children: [],
    }],
  },
  warnings: [],
  boundingSuggestions: [],
  explanation: 'Linearithmic time',
  patterns: [],
  confidenceReasons: [],
  tier: 'fast',
};

describe('diffFunction', () => {
  it('reports nothing additional when bounds match', () => {
    const run = diffFunction(base, { ...base, tier: 'deep' });
    expect(run.status).toBe('unchanged');
    expect(run.summary).toBe('Nothing additional found');
    expect(run.changes).toEqual([]);
  });

  it('lists bound and evidence changes', () => {
    const deep: FunctionComplexity = {
      ...base,
      time: 'O(n log n)',
      tier: 'deep',
      evidence: {
        ...base.evidence,
        cost: 'n log n',
        children: [
          ...base.evidence.children,
          { kind: 'call', label: 'Sort', cost: 'n log n', children: [] },
        ],
      },
    };
    const run = diffFunction(base, deep);
    expect(run.status).toBe('changed');
    expect(run.changes.map((c) => c.label)).toEqual(expect.arrayContaining([
      'Time: O(n log k) → O(n log n)',
      'foreach (n): n log k → n log n',
      'Added foreach (n) / Sort: n log n',
    ]));
  });
});

describe('diffResponses', () => {
  it('includes new file-level warnings as findings', () => {
    const runs = diffResponses(
      { uri: 'file:///a.cs', version: 1, functions: [base], warnings: [] },
      {
        uri: 'file:///a.cs',
        version: 2,
        functions: [{ ...base, tier: 'deep' }],
        warnings: [{ message: 'Deep analysis unavailable; used ad-hoc.' }],
      },
    );
    expect(runs[0].status).toBe('changed');
    expect(runs[0].changes[0].label).toContain('unavailable');
  });
});
