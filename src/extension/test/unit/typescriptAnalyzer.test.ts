import { describe, expect, it } from 'vitest';
import { TypeScriptAnalyzer } from '../../src/analysis/typescriptAnalyzer';

describe('TypeScriptAnalyzer', () => {
  it('estimates a linear for-of loop', async () => {
    const analyzer = new TypeScriptAnalyzer();
    const result = await analyzer.analyze({
      uri: 'file:///a.ts',
      text: `
        function contains(items: number[], value: number): boolean {
          for (const n of items) {
            if (n === value) return true;
          }
          return false;
        }
      `,
      version: 1,
      tier: 'fast',
    }, { isCancellationRequested: false } as never);
    const fn = result.functions.find((f) => f.name === 'contains');
    expect(fn).toBeDefined();
    expect(fn!.time).toMatch(/O\(/);
  });

  it('treats sort as n log n', async () => {
    const analyzer = new TypeScriptAnalyzer();
    const result = await analyzer.analyze({
      uri: 'file:///b.ts',
      text: `
        function sortNums(nums: number[]): number[] {
          return nums.toSorted();
        }
      `,
      version: 1,
      tier: 'fast',
    }, { isCancellationRequested: false } as never);
    const fn = result.functions.find((f) => f.name === 'sortNums');
    expect(fn!.time).toContain('n log n');
  });
});
