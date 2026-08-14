import { describe, expect, it } from 'vitest';
import { AnalyzerRegistry } from '../../src/analysis/registry';
import type { IComplexityAnalyzer } from '../../src/analysis/analyzer';
import { ResultStore } from '../../src/ui/resultStore';
import { Uri, Position } from './__mocks__/vscode';
import type { AnalyzeResponse } from '../../src/analysis/types';

const mockAnalyzer: IComplexityAnalyzer = {
  languageIds: ['csharp'],
  supportsDeepAnalysis: true,
  analyze: async (request) => ({
    uri: request.uri,
    version: request.version,
    functions: [],
    warnings: [],
  }),
};

describe('AnalyzerRegistry', () => {
  it('dispatches by language id', () => {
    const registry = new AnalyzerRegistry();
    registry.register(mockAnalyzer);
    expect(registry.get('csharp')).toBe(mockAnalyzer);
    expect(registry.get('python')).toBeUndefined();
  });
});

describe('ResultStore', () => {
  it('finds a function containing a position', () => {
    const store = new ResultStore();
    const response: AnalyzeResponse = {
      uri: 'file:///a.cs',
      version: 1,
      warnings: [],
      functions: [{
        id: 'Foo',
        name: 'Foo',
        kind: 'method',
        range: {
          startLine: 2, startCharacter: 0, endLine: 8, endCharacter: 1,
        },
        signatureRange: {
          startLine: 2, startCharacter: 0, endLine: 2, endCharacter: 10,
        },
        time: 'O(n)',
        space: 'O(1)',
        confidence: 'high',
        dimensions: [],
        evidence: { kind: 'sequence', label: 'body', cost: 'n', children: [] },
        warnings: [],
        boundingSuggestions: [],
        tier: 'fast',
      }],
    };
    store.set(response);
    const uri = Uri.parse('file:///a.cs');
    expect(store.functionAt(uri as never, new Position(4, 0) as never)?.name)
      .toBe('Foo');
    expect(store.functionAt(uri as never, new Position(0, 0) as never))
      .toBeUndefined();
  });
});
