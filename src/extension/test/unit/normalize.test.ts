import { describe, expect, it } from 'vitest';
import { normalizeAnalyzeResponse } from '../../src/analysis/normalize';

describe('normalizeAnalyzeResponse', () => {
  it('accepts PascalCase server payloads', () => {
    const result = normalizeAnalyzeResponse({
      Uri: 'file:///a.cs',
      Version: 3,
      Functions: [{
        Id: 'TopK',
        Name: 'TopK',
        Kind: 'method',
        Range: {
          StartLine: 1, StartCharacter: 0, EndLine: 10, EndCharacter: 1,
        },
        SignatureRange: {
          StartLine: 1, StartCharacter: 0, EndLine: 1, EndCharacter: 20,
        },
        Time: 'O(n log k)',
        Space: 'O(k)',
        Confidence: 'High',
        Dimensions: [{ Variable: 'n', Meaning: 'values.Length' }],
        Evidence: {
          Kind: 'loop',
          Label: 'foreach',
          Cost: 'n log k',
          Children: null,
        },
        Warnings: null,
        BoundingSuggestions: [],
        Tier: 'fast',
      }],
      Warnings: null,
    });

    expect(result.functions).toHaveLength(1);
    expect(result.functions[0].time).toBe('O(n log k)');
    expect(result.functions[0].confidence).toBe('high');
    expect(result.functions[0].evidence.children).toEqual([]);
    expect(result.warnings).toEqual([]);
  });

  it('turns a missing functions field into an empty array', () => {
    const result = normalizeAnalyzeResponse({});
    expect(result.functions).toEqual([]);
  });
});
