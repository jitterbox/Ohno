import { describe, expect, it } from 'vitest';
import type { EvidenceNode, FunctionComplexity } from '../../src/analysis/types';
import {
  matchEvidence,
  pickFunction,
  primaryHit,
} from '../../src/ui/evidenceMatch';

const loop: EvidenceNode = {
  kind: 'loop',
  label: 'foreach (n)',
  cost: 'n log k',
  range: {
    startLine: 2, startCharacter: 0, endLine: 8, endCharacter: 1,
  },
  children: [
    {
      kind: 'call',
      label: 'Enqueue',
      cost: 'log k',
      range: {
        startLine: 3, startCharacter: 4, endLine: 3, endCharacter: 24,
      },
      children: [],
    },
    {
      kind: 'conditional',
      label: 'worst-case branch',
      cost: 'log k',
      range: {
        startLine: 4, startCharacter: 4, endLine: 6, endCharacter: 22,
      },
      children: [{
        kind: 'call',
        label: 'Dequeue',
        cost: 'log k',
        range: {
          startLine: 5, startCharacter: 8, endLine: 5, endCharacter: 20,
        },
        children: [],
      }],
    },
  ],
};

const fn = (name: string, start: number, end: number): FunctionComplexity => ({
  id: name,
  name,
  kind: 'method',
  range: {
    startLine: start, startCharacter: 0, endLine: end, endCharacter: 1,
  },
  signatureRange: {
    startLine: start, startCharacter: 0, endLine: start, endCharacter: 10,
  },
  time: 'O(1)',
  space: 'O(1)',
  confidence: 'high',
  dimensions: [],
  evidence: { kind: 'sequence', label: name, cost: '1', children: [] },
  warnings: [],
  boundingSuggestions: [],
  explanation: '',
  patterns: [],
  confidenceReasons: [],
  approaches: [],
  selectionHint: '',
  tier: 'fast',
});

describe('matchEvidence', () => {
  it('highlights the innermost node for a caret', () => {
    const hits = matchEvidence(loop, {
      startLine: 3, startCharacter: 8, endLine: 3, endCharacter: 8,
    });
    const labels = hits.map((h) => h.node.label);
    expect(labels).toContain('Enqueue');
    expect(labels).toContain('foreach (n)');
    expect(primaryHit(hits)?.node.label).toBe('Enqueue');
  });

  it('highlights every overlapping node for a multi-line range', () => {
    const hits = matchEvidence(loop, {
      startLine: 3, startCharacter: 0, endLine: 5, endCharacter: 20,
    });
    const labels = hits.map((h) => h.node.label);
    expect(labels).toEqual(expect.arrayContaining([
      'foreach (n)',
      'Enqueue',
      'worst-case branch',
      'Dequeue',
    ]));
  });
});

describe('pickFunction', () => {
  it('prefers the function containing the active caret', () => {
    const chosen = pickFunction(
      [fn('A', 0, 10), fn('B', 12, 20)],
      { startLine: 8, startCharacter: 0, endLine: 14, endCharacter: 0 },
      { line: 13, character: 0 },
    );
    expect(chosen?.name).toBe('B');
  });
});
