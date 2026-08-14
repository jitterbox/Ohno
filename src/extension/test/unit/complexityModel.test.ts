import { describe, expect, it } from 'vitest';
import type { FunctionComplexity } from '../../src/analysis/types';
import { buildPanelModel } from '../../src/ui/complexityModel';

const fn: FunctionComplexity = {
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
  dimensions: [{ variable: 'n', meaning: 'values.Length' }],
  evidence: {
    kind: 'loop',
    label: 'foreach (n)',
    cost: 'n log k',
    range: {
      startLine: 2, startCharacter: 0, endLine: 8, endCharacter: 1,
    },
    children: [{
      kind: 'call',
      label: 'Enqueue',
      cost: 'log k',
      range: {
        startLine: 3, startCharacter: 4, endLine: 3, endCharacter: 24,
      },
      children: [],
    }],
  },
  warnings: [{ message: 'Worst-case analysis used for branches.' }],
  boundingSuggestions: [],
  explanation: 'Linearithmic time',
  patterns: [],
  confidenceReasons: [],
  tier: 'fast',
};

describe('buildPanelModel', () => {
  it('puts analysis in the summary and evidence in the tree', () => {
    const model = buildPanelModel(fn, 'file:///a.cs', new Set(['root.0']));
    expect(model.summary.map((i) => i.label)).toEqual(expect.arrayContaining([
      'TopK',
      'O(n log k) · O(k)',
      'Linearithmic time',
      'Confidence: high',
      'Dimensions',
      'Why this is an estimate',
    ]));
    expect(model.derivation).toHaveLength(1);
    expect(model.derivation[0].label).toBe('foreach (n): n log k');
    expect(model.derivation[0].children[0].label).toBe('Enqueue: log k');
    expect(model.derivation[0].children[0].highlighted).toBe(true);
    expect(model.derivation[0].highlighted).toBe(false);
  });

  it('adds a deep-analysis node for an unchanged run', () => {
    const model = buildPanelModel(
      fn,
      'file:///a.cs',
      new Set(),
      {
        functionId: 'TopK',
        status: 'unchanged',
        summary: 'Nothing additional found',
        changes: [],
      },
    );
    expect(model.summary[0].kind).toBe('deep');
    expect(model.summary[0].label).toBe('Nothing additional found');
    expect(model.summary[0].icon).toBe('pass');
  });

  it('lists why confidence is not high', () => {
    const model = buildPanelModel(
      {
        ...fn,
        confidence: 'medium',
        confidenceReasons: [
          'Collection size is assumed bounded by a Count > k + Dequeue check.',
        ],
      },
      'file:///a.cs',
      new Set(),
    );
    const conf = model.summary.find((i) => i.id === 'confidence');
    expect(conf?.label).toBe('Confidence: medium');
    expect(conf?.children[0].label).toContain('Count > k');
    expect(conf?.children[0].italic).toBe(true);
  });

  it('lists deep-analysis changes under a zap node', () => {
    const model = buildPanelModel(
      fn,
      'file:///a.cs',
      new Set(),
      {
        functionId: 'TopK',
        status: 'changed',
        summary: 'Time: O(n) → O(n log k)',
        changes: [{ label: 'Time: O(n) → O(n log k)' }],
      },
    );
    expect(model.summary[0].icon).toBe('zap');
    expect(model.summary[0].children[0].label).toBe(
      'Time: O(n) → O(n log k)',
    );
  });
});
